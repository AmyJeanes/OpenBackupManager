using OpenBackupManager.Rclone;
using SetupQuestion = OpenBackupManager.Core.Protocol.SetupQuestion;

namespace OpenBackupManager.Engine;

// One of the questions Open Backup Manager asks itself, after rclone's, once the account can be reached. Clients word
// each by its name, as rclone's own questions come with rclone's words
internal abstract class OwnQuestion
{
    public abstract SetupQuestion Ask();

    // Null to go on, or why it's asked again
    public abstract Task<string?> AnswerAsync(string answer, CancellationToken cancellationToken);
}

// What our own questions chose, for adding the remote to config.toml
internal sealed class SetupChoices
{
    public string? Root { get; set; }
}

// Empty is the remote's top, such as a whole OneDrive
internal sealed class RootQuestion(RemoteFolders folders, SetupChoices choices) : OwnQuestion
{
    public override SetupQuestion Ask() =>
        new(SetupQuestion.Root, "", "", [], Required: false, IsPassword: false, Exclusive: false, "string");

    // Asked again with rclone's reason when it can't be looked at, as a wrong folder needs no new sign-in
    public override async Task<string?> AnswerAsync(string answer, CancellationToken cancellationToken)
    {
        try
        {
            choices.Root = (await folders.FindAsync(answer, cancellationToken)).Path;
            return null;
        }
        catch (Exception e) when (e is RcloneException or HttpRequestException or IOException)
        {
            return e.Message;
        }
    }
}
