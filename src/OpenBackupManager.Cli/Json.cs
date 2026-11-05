using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using OpenBackupManager.Core;

namespace OpenBackupManager.Cli;

// --json: the records the engine sends, and obm's own, whose field names are a contract for scripts, kept as the
// protocol's are
internal static class Json
{
    // The default escapes characters such as + and accented letters, for JSON put inside HTML
    public static string Format<T>(T record, JsonTypeInfo<T> type)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            JsonSerializer.Serialize(writer, record, type);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IReadOnlyList<SavedReport>))]
internal sealed partial class CliJson : JsonSerializerContext;
