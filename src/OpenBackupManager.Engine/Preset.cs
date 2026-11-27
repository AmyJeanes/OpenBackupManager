using System.Text.Json;
using System.Text.Json.Serialization;
using SetupQuestion = OpenBackupManager.Core.Protocol.SetupQuestion;

namespace OpenBackupManager.Engine;

// A backend's answers to rclone's questions that Open Backup Manager knows, and plainer words for those left, from a
// file in Presets, so every client sets it up the same way and none has code for any backend
internal sealed record Preset(IReadOnlyDictionary<string, string> Answers, IReadOnlyDictionary<string, PresetWords> Questions)
{
    // Null for a backend without one
    public static Preset? For(string type)
    {
        using var file = typeof(Preset).Assembly.GetManifestResourceStream($"Presets/{type}.jsonc");
        return file is null ? null : JsonSerializer.Deserialize(file, PresetJson.Default.Preset);
    }

    // A client's own answers win, such as its own client ID for its own app registration
    public static Dictionary<string, string> Answer(Preset? preset, IReadOnlyDictionary<string, string>? given)
    {
        var answers = new Dictionary<string, string>(preset?.Answers ?? new Dictionary<string, string>());
        foreach (var (name, answer) in given ?? new Dictionary<string, string>())
        {
            answers[name] = answer;
        }

        return answers;
    }

    public SetupQuestion Word(SetupQuestion question) =>
        Questions.TryGetValue(question.Name, out var words)
            ? question with
            {
                Help = words.Help,
                Choices = [.. question.Choices.Select(choice => words.Choices?.GetValueOrDefault(choice.Value) is { } help ? choice with { Help = help } : choice)],
            }
            : question;
}

// Choices are by their value
internal sealed record PresetWords(string Help, IReadOnlyDictionary<string, string>? Choices = null);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Preset))]
internal sealed partial class PresetJson : JsonSerializerContext;
