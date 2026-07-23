using System.Text.Json;

namespace SumUpSoloExplorer;

internal static class CaptureExporter
{
    public static string Save(
        CommandDefinition command,
        byte[] sent,
        IReadOnlyList<ReceivedPacket> received)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "captures");
        Directory.CreateDirectory(directory);

        string safeId = string.Concat(command.Id.Select(ch =>
            Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

        string path = Path.Combine(
            directory,
            $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}_{safeId}.json");

        var capture = new
        {
            createdAt = DateTimeOffset.Now,
            command = new
            {
                command.Id,
                command.Name,
                command.TimeoutMs,
                command.ReadTimeoutMs,
                command.QuietPeriodMs
            },
            sent = HexCodec.Format(sent),
            responses = received.Select((packet, index) => new
            {
                index = index + 1,
                timestamp = packet.Timestamp,
                length = packet.Data.Length,
                raw = HexCodec.Format(packet.Data)
            })
        };

        File.WriteAllText(path, JsonSerializer.Serialize(capture, new JsonSerializerOptions
        {
            WriteIndented = true
        }));

        return path;
    }
}

internal sealed record ReceivedPacket(DateTimeOffset Timestamp, byte[] Data);
