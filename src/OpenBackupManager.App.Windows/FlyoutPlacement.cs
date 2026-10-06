using Windows.Graphics;

namespace OpenBackupManager.App.Windows;

// Where the status flyout sits, in physical pixels. The work area shows which edge of the display the taskbar is on
internal static class FlyoutPlacement
{
    // In device-independent pixels
    public const int Width = 320;
    public const int Height = 140;
    public const int Gap = 12;
    public const int SlideDistance = 25;

    // At the display's scaling, where 1 is 100%
    public static (SizeInt32 Size, PointInt32 Position, PointInt32 SlideFrom) Place(RectInt32 screen, RectInt32 work, RectInt32 icon, double scale)
    {
        var size = new SizeInt32(Scaled(Width), Scaled(Height));
        var (position, slideFrom) = Place(screen, work, icon, size, Scaled(Gap), Scaled(SlideDistance));
        return (size, position, slideFrom);

        int Scaled(int dips) => (int)(dips * scale);
    }

    // Centred on the tray icon. From the taskbar it sits just clear of the taskbar, and from the hidden icons popup
    // just clear of the icon. It slides in from the taskbar's side
    public static (PointInt32 Position, PointInt32 SlideFrom) Place(
        RectInt32 screen, RectInt32 work, RectInt32 icon, SizeInt32 size, int gap, int slide)
    {
        var centre = Centre(icon);
        var left = work.X + gap;
        var top = work.Y + gap;
        var right = work.X + work.Width - size.Width - gap;
        var bottom = work.Y + work.Height - size.Height - gap;
        var inPopup = centre.X >= work.X && centre.X < work.X + work.Width && centre.Y >= work.Y && centre.Y < work.Y + work.Height;
        var alongX = Math.Clamp(centre.X - (size.Width / 2), left, right);
        var alongY = Math.Clamp(centre.Y - (size.Height / 2), top, bottom);
        return (work.Y > screen.Y, work.X > screen.X, work.Width < screen.Width) switch
        {
            (true, _, _) => (new PointInt32(alongX, inPopup ? Math.Min(icon.Y + icon.Height + gap, bottom) : top), new PointInt32(0, -slide)),
            (_, true, _) => (new PointInt32(inPopup ? Math.Min(icon.X + icon.Width + gap, right) : left, alongY), new PointInt32(-slide, 0)),
            (_, _, true) => (new PointInt32(inPopup ? Math.Max(icon.X - gap - size.Width, left) : right, alongY), new PointInt32(slide, 0)),
            _ => (new PointInt32(alongX, inPopup ? Math.Max(icon.Y - gap - size.Height, top) : bottom), new PointInt32(0, slide)),
        };
    }

    // The end of the taskbar, where the notification area is, for when the icon can't be found
    public static RectInt32 NotificationAreaCorner(RectInt32 screen, RectInt32 work)
    {
        var x = work.X > screen.X ? screen.X : screen.X + screen.Width - 1;
        var y = work.Y > screen.Y ? screen.Y : screen.Y + screen.Height - 1;
        return new(x, y, 0, 0);
    }

    public static PointInt32 Centre(RectInt32 icon) => new(icon.X + (icon.Width / 2), icon.Y + (icon.Height / 2));
}
