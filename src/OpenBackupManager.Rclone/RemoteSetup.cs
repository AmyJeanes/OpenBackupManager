using System.Text.Json;

namespace OpenBackupManager.Rclone;

/// <summary>
/// Sets one remote up in an rcd's config through rclone's own questions, a step at a time: each call returns the next
/// question, or null once it's set up. rclone saves the config after every step, so one stopped part way leaves it half
/// written
/// </summary>
public sealed class RemoteSetup
{
    private readonly RcClient _client;
    private readonly string _name;
    private readonly Dictionary<string, string> _parameters;
    private string? _state;
    private SetupQuestion? _asked;

    // Parameters answer any question by its name, such as a preset's, so it's never asked
    public RemoteSetup(RcClient client, string name, IReadOnlyDictionary<string, string> parameters)
    {
        _client = client;
        _name = name;
        // Advanced options are hidden until other backends are offered, and each backend has some. rclone doesn't open
        // a browser to sign in, as the client does, where the person is, which rclone may not be, such as in a service
        _parameters = new Dictionary<string, string>(parameters) { ["config_fs_advanced"] = "false", ["config_auth_no_browser"] = "true" };
    }

    public async Task<SetupQuestion?> StartAsync(string type, CancellationToken cancellationToken) =>
        await NextAsync(await _client.CreateConfigAsync(_name, type, _parameters, cancellationToken), cancellationToken);

    // An empty password stays empty, as obscuring makes it something
    public async Task<SetupQuestion?> AnswerAsync(string answer, CancellationToken cancellationToken)
    {
        var asked = _asked ?? throw new InvalidOperationException("There's no question waiting for an answer");
        if (asked.IsPassword && answer.Length > 0)
        {
            answer = await _client.ObscureAsync(answer, cancellationToken);
        }

        return await NextAsync(await _client.ContinueConfigAsync(_name, _parameters, _state!, answer, cancellationToken), cancellationToken);
    }

    // A step with no question goes straight on, as rclone's own prompt does, and its error comes with the next. One
    // that's followed by another error rather than a question stops it, as nothing new would be asked to fix it
    private async Task<SetupQuestion?> NextAsync(ConfigStep step, CancellationToken cancellationToken)
    {
        _asked = null;
        string? error = null;
        while (true)
        {
            if (step.Error is { Length: > 0 } stepError)
            {
                if (error is not null || step.State is not { Length: > 0 })
                {
                    throw new RcloneException($"rclone couldn't set {_name} up: {stepError.Trim()}");
                }

                error = stepError.Trim();
            }

            if (step.State is not { Length: > 0 } state)
            {
                return null;
            }

            if (step.Option is { } option)
            {
                _state = state;
                _asked = Question(option, error);
                return _asked;
            }

            step = await _client.ContinueConfigAsync(_name, _parameters, state, step.Result ?? "", cancellationToken);
        }
    }

    private static SetupQuestion Question(ConfigOption option, string? error) => new(
        option.Name,
        option.Help ?? "",
        option.Default.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? "" : option.DefaultStr ?? "",
        [.. (option.Examples ?? []).Select(example => new SetupChoice(example.Value, example.Help ?? ""))],
        option.Required,
        option.IsPassword,
        option.Exclusive,
        option.Type ?? "string",
        error);
}

/// <summary>
/// One of rclone's setup questions, in its own words, which are only in English. Choices are the answers it suggests,
/// and Exclusive says it has to be one of them. Error is what rclone said about the step before, if anything
/// </summary>
public sealed record SetupQuestion(
    string Name,
    string Help,
    string Default,
    IReadOnlyList<SetupChoice> Choices,
    bool Required,
    bool IsPassword,
    bool Exclusive,
    string Type,
    string? Error = null);

public sealed record SetupChoice(string Value, string Help);
