using System.Xml.Linq;

namespace OpenBackupManager.App.Windows;

// The XML Windows shows a notification from. Clicking the notification or a button with arguments passes them to the
// app's activator. https://learn.microsoft.com/uwp/schemas/tiles/toastschema/schema-root
internal sealed class Toast
{
    private readonly XElement _toast = new("toast");
    private readonly XElement _binding = new("binding", new XAttribute("template", "ToastGeneric"));
    private readonly XElement _actions = new("actions");

    public Toast(string? arguments = null)
    {
        if (arguments is not null)
        {
            _toast.Add(new XAttribute("launch", arguments));
        }

        _toast.Add(new XElement("visual", _binding));
    }

    public Toast Text(string text)
    {
        _binding.Add(new XElement("text", text));
        return this;
    }

    public Toast Button(string content, string arguments) => Action(content, arguments, "foreground");

    // Opens the link without starting the app
    public Toast Link(string content, Uri uri) => Action(content, uri.AbsoluteUri, "protocol");

    // Closes the notification without starting the app
    public Toast DismissButton(string content) => Action(content, "dismiss", "system");

    public override string ToString()
    {
        var toast = new XElement(_toast);
        if (_actions.HasElements)
        {
            toast.Add(_actions);
        }

        return toast.ToString(SaveOptions.DisableFormatting);
    }

    private Toast Action(string content, string arguments, string activationType)
    {
        _actions.Add(new XElement(
            "action",
            new XAttribute("content", content),
            new XAttribute("arguments", arguments),
            new XAttribute("activationType", activationType)));
        return this;
    }
}
