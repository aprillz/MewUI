using System.Text.Encodings.Web;
using System.Text.Json;

namespace Aprillz.MewUI.MarkupAudit;

/// <summary>Writes the full audit result as indented UTF-8 JSON with LF line endings.</summary>
public static class JsonAuditWriter
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Write(AuditResult result, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(result, _options) + "\n");
    }

    public static string Serialize(AuditResult result) => JsonSerializer.Serialize(result, _options);
}
