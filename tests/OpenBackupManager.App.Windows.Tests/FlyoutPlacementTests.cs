using Windows.Graphics;

namespace OpenBackupManager.App.Windows.Tests;

public class FlyoutPlacementTests
{
    private const int Gap = 12;
    private const int Slide = 25;

    // A 1920 x 1080 display with a 48-pixel taskbar on each edge in turn
    private static readonly RectInt32 Screen = new(0, 0, 1920, 1080);
    private static readonly RectInt32 TaskbarBottom = new(0, 0, 1920, 1032);
    private static readonly RectInt32 TaskbarTop = new(0, 48, 1920, 1032);
    private static readonly RectInt32 TaskbarLeft = new(48, 0, 1872, 1080);
    private static readonly RectInt32 TaskbarRight = new(0, 0, 1872, 1080);
    private static readonly SizeInt32 Size = new(320, 140);

    [Test]
    public void BottomTaskbar_CentresOnTheIconJustAboveTheTaskbar()
    {
        var (position, slideFrom) = FlyoutPlacement.Place(Screen, TaskbarBottom, Icon(1500, 1040), Size, Gap, Slide);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(position, Is.EqualTo(new PointInt32(1500 + 16 - 160, 1032 - Gap - 140)));
            Assert.That(slideFrom, Is.EqualTo(new PointInt32(0, Slide)));
        }
    }

    [Test]
    public void BottomTaskbar_StaysOnScreenNearTheRightEdge()
    {
        var (position, _) = FlyoutPlacement.Place(Screen, TaskbarBottom, Icon(1880, 1040), Size, Gap, Slide);

        Assert.That(position.X, Is.EqualTo(1920 - 320 - Gap));
    }

    [Test]
    public void BottomTaskbar_StaysOnScreenNearTheLeftEdge()
    {
        var (position, _) = FlyoutPlacement.Place(Screen, TaskbarBottom, Icon(10, 1040), Size, Gap, Slide);

        Assert.That(position.X, Is.EqualTo(Gap));
    }

    [Test]
    public void BottomTaskbar_FromTheHiddenIconsPopup_SitsJustAboveTheIcon()
    {
        var (position, slideFrom) = FlyoutPlacement.Place(Screen, TaskbarBottom, Icon(1700, 900), Size, Gap, Slide);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(position, Is.EqualTo(new PointInt32(1700 + 16 - 160, 900 - Gap - 140)));
            Assert.That(slideFrom, Is.EqualTo(new PointInt32(0, Slide)));
        }
    }

    [Test]
    public void TopTaskbar_CentresOnTheIconJustBelowTheTaskbar()
    {
        var (position, slideFrom) = FlyoutPlacement.Place(Screen, TaskbarTop, Icon(1500, 8), Size, Gap, Slide);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(position, Is.EqualTo(new PointInt32(1500 + 16 - 160, 48 + Gap)));
            Assert.That(slideFrom, Is.EqualTo(new PointInt32(0, -Slide)));
        }
    }

    [Test]
    public void LeftTaskbar_CentresOnTheIconJustRightOfTheTaskbar()
    {
        var (position, slideFrom) = FlyoutPlacement.Place(Screen, TaskbarLeft, Icon(8, 700), Size, Gap, Slide);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(position, Is.EqualTo(new PointInt32(48 + Gap, 700 + 16 - 70)));
            Assert.That(slideFrom, Is.EqualTo(new PointInt32(-Slide, 0)));
        }
    }

    [Test]
    public void RightTaskbar_CentresOnTheIconJustLeftOfTheTaskbar()
    {
        var (position, slideFrom) = FlyoutPlacement.Place(Screen, TaskbarRight, Icon(1880, 700), Size, Gap, Slide);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(position, Is.EqualTo(new PointInt32(1872 - 320 - Gap, 700 + 16 - 70)));
            Assert.That(slideFrom, Is.EqualTo(new PointInt32(Slide, 0)));
        }
    }

    [Test]
    public void RightTaskbar_StaysOnScreenNearTheBottomEdge()
    {
        var (position, _) = FlyoutPlacement.Place(Screen, TaskbarRight, Icon(1880, 1060), Size, Gap, Slide);

        Assert.That(position.Y, Is.EqualTo(1080 - 140 - Gap));
    }

    [Test]
    public void NotificationAreaCorner_IsAtTheEndOfTheTaskbar()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Corner(TaskbarBottom), Is.EqualTo(new PointInt32(1919, 1079)));
            Assert.That(Corner(TaskbarTop), Is.EqualTo(new PointInt32(1919, 0)));
            Assert.That(Corner(TaskbarLeft), Is.EqualTo(new PointInt32(0, 1079)));
            Assert.That(Corner(TaskbarRight), Is.EqualTo(new PointInt32(1919, 1079)));
        }
    }

    [Test]
    public void NotificationAreaCorner_PlacesTheFlyoutInTheCorner()
    {
        var corner = FlyoutPlacement.NotificationAreaCorner(Screen, TaskbarBottom);

        var (position, _) = FlyoutPlacement.Place(Screen, TaskbarBottom, corner, Size, Gap, Slide);

        Assert.That(position, Is.EqualTo(new PointInt32(1920 - 320 - Gap, 1032 - Gap - 140)));
    }

    [Test]
    public void At150Percent_ScalesTheSizeGapAndSlide()
    {
        // A 3840 x 2160 display with a 72-pixel taskbar, and a 48-pixel icon
        var screen = new RectInt32(0, 0, 3840, 2160);
        var work = new RectInt32(0, 0, 3840, 2088);

        var (size, position, slideFrom) = FlyoutPlacement.Place(screen, work, new RectInt32(3000, 2100, 48, 48), 1.5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(size, Is.EqualTo(new SizeInt32(480, 210)));
            Assert.That(position, Is.EqualTo(new PointInt32(3000 + 24 - 240, 2088 - 18 - 210)));
            Assert.That(slideFrom, Is.EqualTo(new PointInt32(0, 37)));
        }
    }

    // A 32 x 32 icon with its top-left corner at the point
    private static RectInt32 Icon(int x, int y) => new(x, y, 32, 32);

    private static PointInt32 Corner(RectInt32 work) => FlyoutPlacement.Centre(FlyoutPlacement.NotificationAreaCorner(Screen, work));
}
