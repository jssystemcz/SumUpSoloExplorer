using System.Globalization;
using System.Text;

namespace SumUpSoloExplorer;

internal static class DeviceInfoParser
{
    private static readonly Dictionary<string, string> TagNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["C1"] = "Verze komponenty C1",
        ["C2"] = "Verze komponenty C2",
        ["C3"] = "Verze komponenty C3",
        ["C4"] = "Identifikátor aplikace A",
        ["C5"] = "Identifikátor zařízení / binární data",
        ["C6"] = "Identifikátor aplikace B",
        ["C7"] = "Identifikátor zařízení",
        ["C8"] = "Jazyk a oblast",
        ["C9"] = "Parametr C9",
        ["CA"] = "Parametr CA",
        ["CB"] = "Parametr CB",
        ["CC"] = "Parametr CC",
        ["CD"] = "Parametr CD",
        ["CE"] = "Parametr CE",
        ["CF"] = "Parametr CF",
        ["D0"] = "Parametr D0",
        ["DF1F"] = "Datum terminálu",
        ["DF20"] = "Čas terminálu"
    };

    public static bool TryFormat(byte[] frame, out string text)
    {
        text = string.Empty;

        if (frame.Length < 11 || frame[0] != 0x02 || frame[^1] != 0x03)
            return false;

        if (frame.Length <= 8 || frame[4] != 0x01 || frame[5] != 0x01)
            return false;

        int end = frame.Length - 3;
        int offset = 8;
        var sb = new StringBuilder();

        sb.AppendLine("SumUp Solo – Device Info");
        sb.AppendLine(new string('=', 34));

        while (offset < end)
        {
            string tag;
            byte first = frame[offset++];

            if (first == 0xDF)
            {
                if (offset >= end)
                    break;

                tag = $"DF{frame[offset++]:X2}";
            }
            else
            {
                tag = first.ToString("X2", CultureInfo.InvariantCulture);
            }

            if (offset >= end)
                break;

            int length = frame[offset++];
            if (offset + length > end)
                break;

            byte[] value = frame.AsSpan(offset, length).ToArray();
            offset += length;

            string name = TagNames.TryGetValue(tag, out string? knownName)
                ? knownName
                : $"Neznámý tag {tag}";

            sb.AppendLine($"{name,-34}: {FormatValue(tag, value)}");
        }

        sb.AppendLine();
        sb.AppendLine("Poznámka: názvy privátních tagů C1–D0 jsou zatím pracovní; raw hodnoty zůstávají v logu.");
        text = sb.ToString();
        return true;
    }

    private static string FormatValue(string tag, byte[] value)
    {
        if (tag == "DF1F" && value.Length == 3)
            return $"20{Bcd(value[0]):00}-{Bcd(value[1]):00}-{Bcd(value[2]):00}";

        if (tag == "DF20" && value.Length == 3)
            return $"{Bcd(value[0]):00}:{Bcd(value[1]):00}:{Bcd(value[2]):00}";

        if (tag is "C1" or "C2" or "C3")
            return string.Join('.', value.Select(Bcd)) + $"  [{Hex(value)}]";

        string? ascii = ExtractReadableAscii(value);
        if (!string.IsNullOrWhiteSpace(ascii))
            return $"{ascii}  [{Hex(value)}]";

        if (value.Length == 1)
            return $"{value[0]} (0x{value[0]:X2})";

        if (value.Length == 2)
        {
            ushort number = (ushort)((value[0] << 8) | value[1]);
            return $"{number} (0x{number:X4})";
        }

        if (value.Length == 4)
        {
            uint number = ((uint)value[0] << 24) | ((uint)value[1] << 16) | ((uint)value[2] << 8) | value[3];
            return $"{number} (0x{number:X8})";
        }

        return Hex(value);
    }

    private static string? ExtractReadableAscii(byte[] value)
    {
        var chars = value
            .Where(b => b is >= 0x20 and <= 0x7E)
            .Select(b => (char)b)
            .ToArray();

        return chars.Length < 4 ? null : new string(chars).TrimEnd('\0');
    }

    private static int Bcd(byte value) => ((value >> 4) * 10) + (value & 0x0F);

    private static string Hex(byte[] data) =>
        string.Join(' ', data.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
}
