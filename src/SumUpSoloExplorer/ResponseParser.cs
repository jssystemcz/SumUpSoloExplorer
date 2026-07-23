using System.Globalization;
using System.Text;

namespace SumUpSoloExplorer;

internal static class ResponseParser
{
    public static string FormatSession(
        CommandDefinition command,
        IReadOnlyList<byte[]> packets,
        ExplorerConfig config)
    {
        var sb = new StringBuilder();
        sb.AppendLine(command.Name);
        sb.AppendLine(new string('=', Math.Max(20, command.Name.Length)));
        sb.AppendLine($"Přijaté pakety: {packets.Count}");
        sb.AppendLine();

        for (int i = 0; i < packets.Count; i++)
        {
            byte[] packet = packets[i];
            sb.AppendLine($"Paket #{i + 1} ({packet.Length} B)");
            sb.AppendLine(new string('-', 30));

            if (TryParseAck(packet, config, out string ack))
            {
                sb.AppendLine(ack);
            }
            else if (TryParseTlvResponse(packet, config, out string tlv))
            {
                sb.AppendLine(tlv);
            }
            else
            {
                sb.AppendLine("Typ: neznámý rámec");
                sb.AppendLine($"HEX: {HexCodec.Format(packet)}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static bool TryParseAck(
        byte[] frame,
        ExplorerConfig config,
        out string text)
    {
        text = "";
        if (frame.Length != 11 ||
            frame[0] != 0x02 ||
            frame[^1] != 0x03 ||
            frame[4] != 0x80 ||
            frame[5] != 0x01)
        {
            return false;
        }

        string code = $"{frame[6]:X2}{frame[7]:X2}";
        string description = config.StatusCodes.TryGetValue(code, out string? known)
            ? known
            : "Neznámý stav";

        text =
            $"Typ: ACK / stavová odpověď{Environment.NewLine}" +
            $"Stav: {code} – {description}{Environment.NewLine}" +
            $"CRC/ocas: {frame[8]:X2} {frame[9]:X2}{Environment.NewLine}" +
            $"HEX: {HexCodec.Format(frame)}";
        return true;
    }

    private static bool TryParseTlvResponse(
        byte[] frame,
        ExplorerConfig config,
        out string text)
    {
        text = "";
        if (frame.Length < 11 || frame[0] != 0x02 || frame[^1] != 0x03)
            return false;

        // DeviceInfo response observed as:
        // STX + length(2) + flags + command(2) + status(2) + TLV + CRC(2) + ETX
        if (frame.Length < 9 || frame[4] != 0x01 || frame[5] != 0x01)
            return false;

        int offset = 8;
        int end = frame.Length - 3;
        if (offset >= end)
            return false;

        var sb = new StringBuilder();
        sb.AppendLine("Typ: TLV odpověď");
        sb.AppendLine($"Hlavička: {HexCodec.Format(frame.Take(8))}");

        int parsedCount = 0;
        while (offset < end)
        {
            int start = offset;
            if (!TryReadTag(frame, ref offset, end, out string tag))
                break;

            if (offset >= end)
                break;

            int length = frame[offset++];
            if (length < 0 || offset + length > end)
            {
                sb.AppendLine($"Neúplný TLV od offsetu {start}: {tag}, délka {length}");
                break;
            }

            byte[] value = frame.AsSpan(offset, length).ToArray();
            offset += length;
            parsedCount++;

            TagDefinition definition = config.Tags.TryGetValue(tag, out TagDefinition? known)
                ? known
                : new TagDefinition { Name = $"Neznámý tag {tag}", Format = "hex" };

            string formatted = FormatValue(definition.Format, value);
            sb.AppendLine($"{definition.Name} [{tag}]: {formatted}");
        }

        if (parsedCount == 0)
            return false;

        sb.AppendLine($"CRC: {frame[^3]:X2} {frame[^2]:X2}");
        text = sb.ToString().TrimEnd();
        return true;
    }

    private static bool TryReadTag(
        byte[] data,
        ref int offset,
        int end,
        out string tag)
    {
        tag = "";
        if (offset >= end)
            return false;

        byte first = data[offset++];
        tag = first.ToString("X2", CultureInfo.InvariantCulture);

        // BER-TLV: pokud dolních 5 bitů prvního bajtu = 0x1F,
        // pokračují další bajty tagu, dokud není bit 7 nulový.
        if ((first & 0x1F) == 0x1F)
        {
            do
            {
                if (offset >= end)
                    return false;

                byte next = data[offset++];
                tag += next.ToString("X2", CultureInfo.InvariantCulture);
                if ((next & 0x80) == 0)
                    break;
            }
            while (true);
        }

        return true;
    }

    private static string FormatValue(string format, byte[] value)
    {
        string normalized = (format ?? "hex").Trim().ToLowerInvariant();

        return normalized switch
        {
            "ascii" => FormatAscii(value),
            "version" => string.Join('.', value.Select(Bcd)) +
                         $"  [{HexCodec.Format(value)}]",
            "bcd-date" when value.Length == 3 =>
                $"20{Bcd(value[0]):00}-{Bcd(value[1]):00}-{Bcd(value[2]):00}",
            "bcd-time" when value.Length == 3 =>
                $"{Bcd(value[0]):00}:{Bcd(value[1]):00}:{Bcd(value[2]):00}",
            "uint8" when value.Length == 1 =>
                $"{value[0]} (0x{value[0]:X2})",
            "uint16-be" when value.Length == 2 =>
                $"{(value[0] << 8) | value[1]} (0x{value[0]:X2}{value[1]:X2})",
            "uint32-be" when value.Length == 4 =>
                $"{ReadUInt32Be(value)} (0x{HexCodec.Format(value).Replace(" ", "")})",
            "auto" => FormatAuto(value),
            _ => HexCodec.Format(value)
        };
    }

    private static string FormatAuto(byte[] value)
    {
        string ascii = new(value
            .Where(b => b is >= 0x20 and <= 0x7E)
            .Select(b => (char)b)
            .ToArray());

        return ascii.Length >= 4
            ? $"{ascii.TrimEnd('\0')}  [{HexCodec.Format(value)}]"
            : HexCodec.Format(value);
    }

    private static string FormatAscii(byte[] value)
    {
        string ascii = new(value
            .Where(b => b is >= 0x20 and <= 0x7E)
            .Select(b => (char)b)
            .ToArray());

        return $"{ascii.TrimEnd('\0')}  [{HexCodec.Format(value)}]";
    }

    private static int Bcd(byte value) =>
        ((value >> 4) * 10) + (value & 0x0F);

    private static uint ReadUInt32Be(byte[] value) =>
        ((uint)value[0] << 24) |
        ((uint)value[1] << 16) |
        ((uint)value[2] << 8) |
        value[3];
}
