namespace OpenBackupManager.Core.Platform;

// Clicking the notification passes Action, if it has one, to INotifier.Clicked
public sealed record Notification(string Title, IReadOnlyList<string> Lines, string? Action = null)
{
    public IReadOnlyList<NotificationButton> Buttons { get; init; } = [];
}

public abstract record NotificationButton(string Label);

// Passes Action to INotifier.Clicked, starting the app if it isn't running
public sealed record ActionButton(string Label, string Action) : NotificationButton(Label);

// Opens the link without starting the app
public sealed record LinkButton(string Label, Uri Link) : NotificationButton(Label);

// Closes the notification without starting the app
public sealed record DismissButton(string Label) : NotificationButton(Label);
