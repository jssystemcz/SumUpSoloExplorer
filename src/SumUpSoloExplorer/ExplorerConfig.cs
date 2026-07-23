using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SumUpSoloExplorer;

internal sealed class ExplorerConfig
{
    public List<CommandDefinition> Commands { get; set; } = [];
    public Dictionary<string, TagDefinition> Tags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> StatusCodes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class CommandDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Frame { get; set; } = "";
    public int TimeoutMs { get; set; } = 3000;
    public int ReadTimeoutMs { get; set; } = 150;
    public int QuietPeriodMs { get; set; } = 350;
    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public byte[] FrameBytes => HexCodec.Parse(Frame);
}

internal sealed class TagDefinition
{
    public string Name { get; set; } = "";
    public string Format { get; set; } = "hex";
}

internal static class ConfigLoader
{
    public static string ConfigDirectory =>
        Path.Combine(AppContext.BaseDirectory, "config");

    public static ExplorerConfig Load()
    {
        Directory.CreateDirectory(ConfigDirectory);

        var result = new ExplorerConfig
        {
            Commands = Read<List<CommandDefinition>>("commands.json") ?? [],
            Tags = Read<Dictionary<string, TagDefinition>>("tags.json")
                ?? new(StringComparer.OrdinalIgnoreCase),
            StatusCodes = Read<Dictionary<string, string>>("statuses.json")
                ?? new(StringComparer.OrdinalIgnoreCase)
        };

        result.Tags = new Dictionary<string, TagDefinition>(
            result.Tags, StringComparer.OrdinalIgnoreCase);
        result.StatusCodes = new Dictionary<string, string>(
            result.StatusCodes, StringComparer.OrdinalIgnoreCase);

        foreach (CommandDefinition command in result.Commands)
        {
            if (string.IsNullOrWhiteSpace(command.Id))
                throw new InvalidDataException("Každý příkaz musí mít neprázdné 'id'.");
            if (string.IsNullOrWhiteSpace(command.Name))
                throw new InvalidDataException($"Příkaz '{command.Id}' nemá 'name'.");
            _ = command.FrameBytes;
        }

        return result;
    }

    private static T? Read<T>(string fileName)
    {
        string path = Path.Combine(ConfigDirectory, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Chybí konfigurační soubor: {path}");

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
    }
}

internal static class HexCodec
{
    public static byte[] Parse(string text)
    {
        string compact = new(text.Where(Uri.IsHexDigit).ToArray());
        if (compact.Length == 0 || compact.Length % 2 != 0)
            throw new FormatException("HEX rámec musí obsahovat sudý počet číslic.");

        return Convert.FromHexString(compact);
    }

    public static string Format(IEnumerable<byte> data) =>
        string.Join(' ', data.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
}
