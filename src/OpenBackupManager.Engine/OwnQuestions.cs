using OpenBackupManager.Core.Protocol;
using OpenBackupManager.Rclone;
using SetupChoice = OpenBackupManager.Core.Protocol.SetupChoice;
using SetupQuestion = OpenBackupManager.Core.Protocol.SetupQuestion;

namespace OpenBackupManager.Engine;

// One of the questions Open Backup Manager asks itself, after rclone's, once the account can be reached. Clients word
// each by its name, as rclone's own questions come with rclone's words
internal abstract class OwnQuestion
{
    public abstract SetupQuestion Ask();

    public abstract Task<Answered> AnswerAsync(string answer, CancellationToken cancellationToken);
}

// Where an answer leads: on by Move questions, so 0 asks it again and -1 the one before, with Why shown on it
internal readonly record struct Answered(int Move, string? Why = null)
{
    public static Answered Next => new(1);

    public static Answered Again(string why) => new(0, why);

    public static Answered Back(string? why = null) => new(-1, why);
}

// What our own questions chose, for adding the remote to config.toml. A remote set up again starts with its own root
internal sealed class SetupChoices
{
    public string? Root { get; set; }

    // What's at the root, once it's chosen
    public FoundFolder? Found { get; set; }

    // Once it's proved it can hold files
    public bool Written { get; set; }
}

// Empty is the remote's top, such as a whole OneDrive
internal sealed class RootQuestion(RemoteFolders folders, SetupChoices choices) : OwnQuestion
{
    public override SetupQuestion Ask() =>
        new(SetupQuestion.Root, "", "", [], Required: false, IsPassword: false, Exclusive: false, "string");

    // Asked again with rclone's reason when it can't be looked at, as a wrong folder needs no new sign-in. A backup's
    // folder, or one inside it, is refused, as two backups never share files
    public override async Task<Answered> AnswerAsync(string answer, CancellationToken cancellationToken)
    {
        FoundFolder found;
        try
        {
            found = await folders.FindAsync(answer, cancellationToken);
        }
        catch (Exception e) when (e is RcloneException or HttpRequestException or IOException)
        {
            return Answered.Again(e.Message);
        }

        if (found.BackupAbove is { } above)
        {
            return Answered.Again($"There's an Open Backup Manager backup in {Named(above)}, which {Named(found.Path)} is inside. Joining is not yet available");
        }

        if (found.HasBackup)
        {
            return Answered.Again($"There's already an Open Backup Manager backup in {Named(found.Path)}. Joining is not yet available");
        }

        choices.Root = found.Path;
        choices.Found = found;
        return Answered.Next;
    }

    internal static string Named(string path) => path.Length > 0 ? path : "the remote's top";
}

// What's at the root, to say yes or no to. Yes proves it can hold files, which makes the folder; no, or a folder that
// can't hold files, goes back to the root
internal sealed class RootConfirmQuestion(RemoteFolders folders, SetupChoices choices) : OwnQuestion
{
    public override SetupQuestion Ask()
    {
        var found = choices.Found!;
        var state = found.Folders is null ? RootFolderState.Missing : found.Folders.Count + found.Files == 0 ? RootFolderState.Empty : RootFolderState.HasFiles;
        return new SetupQuestion(
            SetupQuestion.RootConfirm,
            "",
            // Only as asked, where it's already someone else's
            state == RootFolderState.HasFiles ? "no" : "yes",
            [new SetupChoice("yes", ""), new SetupChoice("no", "")],
            Required: true,
            IsPassword: false,
            Exclusive: true,
            "string",
            Folder: new RootFolder(found.Path, state, found.Folders?.Count ?? 0, found.Files, found.NearMiss));
    }

    public override async Task<Answered> AnswerAsync(string answer, CancellationToken cancellationToken)
    {
        if (answer == "no")
        {
            return Answered.Back();
        }

        if (answer != "yes")
        {
            return Answered.Again("Answer yes or no");
        }

        try
        {
            await folders.WriteTestAsync(choices.Root!, cancellationToken);
        }
        catch (Exception e) when (e is RcloneException or HttpRequestException or IOException)
        {
            return Answered.Back($"{RootQuestion.Named(choices.Root!)} can't hold files: {e.Message}");
        }

        choices.Written = true;
        return Answered.Next;
    }
}
