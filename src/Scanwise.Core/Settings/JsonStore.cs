using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scanwise.Core.Settings;

/// <summary>Loads and atomically saves JSON documents; a missing or corrupt file yields defaults.</summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T Load<T>(string path, Func<T> fallback)
    {
        try
        {
            if (!File.Exists(path))
            {
                return fallback();
            }
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, Options) ?? fallback();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Keep the broken file for inspection instead of silently overwriting it on the next save.
            TryMove(path, path + ".corrupt");
            return fallback();
        }
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, value, Options);
        }
        File.Move(temp, path, overwrite: true);
    }

    private static void TryMove(string from, string to)
    {
        try
        {
            if (File.Exists(from))
            {
                File.Move(from, to, overwrite: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
