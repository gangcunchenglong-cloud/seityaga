using UrlInsight.Core.Storage;
using UrlInsight.Mac.Platform;
using UrlInsight.Mac.UI;

namespace UrlInsight.Mac.Tests;

public class PlacementTests
{
    private static readonly ScreenBox Work = new(0, 25, 1440, 875);

    [Fact]
    public void PlacesOnTheRightAwayFromTheCursor()
    {
        var (x, y) = CardPlacement.NearAnchor(300, 400, Work, 428, 500, CardSide.Right, Array.Empty<ScreenBox>(), 1);
        Assert.Equal(1440 - 428 - 8, x);
        Assert.Equal(400 - 125, y);
    }

    [Fact]
    public void MovesToTheOtherSideOfTheCursorWhenTheyWouldOverlap()
    {
        var (x, _) = CardPlacement.NearAnchor(1300, 400, Work, 428, 500, CardSide.Right, Array.Empty<ScreenBox>(), 1);
        Assert.Equal(1300 - 428 - 28, x);
    }

    [Fact]
    public void StaysInsideTheScreenVertically()
    {
        var (_, y) = CardPlacement.NearAnchor(300, 880, Work, 428, 500, CardSide.Right, Array.Empty<ScreenBox>(), 1);
        Assert.Equal(25 + 875 - 500 - 8, y);
    }

    [Fact]
    public void AvoidsPinnedCards()
    {
        var pinned = new[] { new ScreenBox(1004, 100, 428, 600) };
        var (x, _) = CardPlacement.NearAnchor(300, 400, Work, 428, 500, CardSide.Right, pinned, 1);
        Assert.Equal(1004 - 428 - 8, x);
    }

    [Fact]
    public void ClampsRestoredCardsIntoTheScreen()
    {
        Assert.Equal((1440 - 428 - 8, 25 + 8), CardPlacement.ClampInto(3000, -50, Work, 428, 300, 1));
        Assert.Equal((200, 300), CardPlacement.ClampInto(200, 300, Work, 428, 300, 1));
    }

    [Fact]
    public void LoginItemPlistIsValidXmlAndEscapesThePath()
    {
        var xml = LoginItem.BuildPlist("/Applications/URL Insight & Co.app/Contents/MacOS/URLInsight");
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        var strings = doc.Descendants("string").Select(e => e.Value).ToList();
        Assert.Contains("/Applications/URL Insight & Co.app/Contents/MacOS/URLInsight", strings);
        Assert.Contains("--minimized", strings);
        Assert.Contains(LoginItem.Label, strings);
    }
}
