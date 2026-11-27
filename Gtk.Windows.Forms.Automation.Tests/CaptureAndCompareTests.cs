using System.Drawing;
using System.Windows.Forms;
using Gtk.Windows.Forms.Automation;

namespace GtkWinForms.Automation.Tests;

public class CaptureAndCompareTests
{
    [Fact]
    public void Capture_RendersTheControlsRealPixels()
    {
        using var ui = TestUi.Show(f => f.Controls.Add(new Panel
        {
            Name = "swatch",
            BackColor = Color.FromArgb(255, 0, 0),
            Location = new Point(10, 10),
            Size = new Size(80, 60),
        }));
        var swatch = ui.Find<Panel>("swatch");

        using var shot = ui.Capture(swatch);

        Assert.Equal(80, shot.Width);
        Assert.Equal(60, shot.Height);
        var centre = shot.GetPixel(40, 30);
        Assert.True(centre.R > 200 && centre.G < 60 && centre.B < 60 && centre.A == 255, $"centre pixel is {centre}");
    }

    [Fact]
    public void Capture_OfAForm_IsItsClientArea_AndNotBlank()
    {
        using var ui = TestUi.Show(f => f.Controls.Add(new Label { Text = "Hello", Location = new Point(10, 10) }));

        using var shot = ui.Capture();

        Assert.Equal(new Size(300, 200), new Size(shot.Width, shot.Height));
        var opaque = 0;
        for (var y = 0; y < shot.Height; y += 4)
        {
            for (var x = 0; x < shot.Width; x += 4)
            {
                if (shot.GetPixel(x, y).A > 0)
                {
                    opaque++;
                }
            }
        }

        Assert.True(opaque > 0, "every sampled pixel of the form capture is transparent");
    }

    [Fact]
    public void CaptureWindow_AddsTheTitleBarAboveTheClientArea()
    {
        using var ui = TestUi.Show(f => f.Text = "Titled");

        using var client = ui.Capture();
        using var window = ui.CaptureWindow();

        Assert.True(window.Height > client.Height, $"window {window.Width}x{window.Height}, client {client.Width}x{client.Height}");
        Assert.True(window.Width >= client.Width);
    }

    [Fact]
    public void Capture_StaysReliableAcrossManyForms()
    {
        // Regression: snapshotting outside a frame's after-paint phase raced with redraws and returned a
        // blank image for roughly a third of freshly shown forms.
        for (var i = 0; i < 15; i++)
        {
            using var ui = TestUi.Show(f => f.Controls.Add(new Panel { BackColor = Color.Black, Size = new Size(300, 200) }));
            using var shot = ui.Capture();
            Assert.Equal(Color.FromArgb(255, 0, 0, 0), shot.GetPixel(150, 100));
        }
    }

    [Fact]
    public void Capture_IsRepeatable()
    {
        using var ui = TestUi.Show(f => f.Controls.Add(new Button { Text = "Same", Location = new Point(20, 20) }));

        using var first = ui.Capture();
        using var second = ui.Capture();
        using var result = ImageComparer.Compare(first, second, ImageTolerance.Exact);

        Assert.True(result.Match, result.Message);
    }

    [Fact]
    public void Capture_SeesAChangeTheUserWouldSee()
    {
        using var ui = TestUi.Show(f => f.Controls.Add(new Panel
        {
            Name = "swatch",
            BackColor = Color.White,
            Size = new Size(50, 50),
        }));
        var swatch = ui.Find<Panel>("swatch");
        using var before = ui.Capture(swatch);

        swatch.BackColor = Color.Blue;
        ui.Pump();
        using var after = ui.Capture(swatch);
        using var result = ImageComparer.Compare(before, after);

        Assert.False(result.Match, "the capture did not change when the panel turned blue");
    }

    [Fact]
    public void Compare_IdenticalImages_Match()
    {
        TestUi.Init();
        using var a = TestUi.Solid(20, 10, Color.Teal);
        using var b = TestUi.Solid(20, 10, Color.Teal);

        using var result = ImageComparer.Compare(a, b, ImageTolerance.Exact);

        Assert.True(result.Match);
        Assert.Equal(0, result.DifferentPixels);
        Assert.Equal(200, result.TotalPixels);
        Assert.Null(result.Diff);
    }

    [Fact]
    public void Compare_NoiseWithinTheChannelTolerance_Matches()
    {
        TestUi.Init();
        using var a = TestUi.Solid(10, 10, Color.FromArgb(100, 100, 100));
        using var b = TestUi.Solid(10, 10, Color.FromArgb(110, 95, 100));

        using var lenient = ImageComparer.Compare(a, b);
        using var exact = ImageComparer.Compare(a, b, ImageTolerance.Exact);

        Assert.True(lenient.Match, lenient.Message);
        Assert.Equal(10, lenient.MaxChannelDelta);
        Assert.False(exact.Match);
    }

    [Fact]
    public void Compare_HonoursTheDifferentPixelRatio()
    {
        TestUi.Init();
        using var expected = TestUi.Solid(100, 100, Color.White);
        using var oneOff = TestUi.Solid(100, 100, Color.White);
        oneOff.SetPixel(5, 5, Color.Black);
        using var tenOff = TestUi.Solid(10, 10, Color.White);
        using var tenExpected = TestUi.Solid(10, 10, Color.White);
        tenOff.SetPixel(1, 1, Color.Black);

        // 1 of 10,000 pixels is within the default 0.5%; 1 of 100 (1%) is not.
        using var small = ImageComparer.Compare(expected, oneOff);
        using var large = ImageComparer.Compare(tenExpected, tenOff);

        Assert.True(small.Match, small.Message);
        Assert.False(large.Match);
        Assert.Equal(1, large.DifferentPixels);
    }

    [Fact]
    public void Compare_MarksTheDifferingPixelsRedInTheDiff()
    {
        TestUi.Init();
        using var expected = TestUi.Solid(8, 8, Color.White);
        using var actual = TestUi.Solid(8, 8, Color.White);
        actual.SetPixel(3, 4, Color.Black);

        using var result = ImageComparer.Compare(expected, actual, ImageTolerance.Exact);

        Assert.NotNull(result.Diff);
        Assert.Equal(Color.FromArgb(255, 255, 0, 0), result.Diff!.GetPixel(3, 4));
        Assert.NotEqual(Color.FromArgb(255, 255, 0, 0), result.Diff.GetPixel(0, 0));
    }

    [Fact]
    public void Compare_DifferentSizes_DoNotMatch()
    {
        TestUi.Init();
        using var a = TestUi.Solid(10, 10, Color.White);
        using var b = TestUi.Solid(10, 11, Color.White);

        using var result = ImageComparer.Compare(a, b);

        Assert.False(result.Match);
        Assert.Contains("10x10", result.Message);
        Assert.Contains("10x11", result.Message);
    }

    [Fact]
    public void Compare_TreatsFullyTransparentPixelsAsEqual()
    {
        TestUi.Init();
        using var a = TestUi.Solid(4, 4, Color.FromArgb(0, 255, 0, 0));
        using var b = TestUi.Solid(4, 4, Color.FromArgb(0, 0, 0, 255));

        using var result = ImageComparer.Compare(a, b, ImageTolerance.Exact);

        Assert.True(result.Match, result.Message);
    }
}
