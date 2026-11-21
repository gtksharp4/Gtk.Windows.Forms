using System.Diagnostics;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Shared helpers that let the anchor baseline functional tests run on every GTK backend, including the
/// headless ones used in CI (xvfb on Linux, no window manager on the macOS runner).
/// </summary>
/// <remarks>
/// Two backend differences break an exact-pixel GUI test that passes against a real desktop:
/// <list type="bullet">
/// <item>No window manager honours <see cref="FormWindowState.Maximized" />, so a test that maximises to
/// produce a resize never grows. <see cref="Enlarge" /> maximises first (what the tests document) and
/// falls back to growing the form directly when nothing honours it.</item>
/// <item>Window chrome and allocation rounding differ by a couple of pixels, so an anchored child lands a
/// pixel or two off its designed edge. <see cref="Approx" /> / <see cref="ApproxBounds" /> tolerate that
/// while still catching a real anchor bug, which is wrong by tens to hundreds of pixels.</item>
/// </list>
/// </remarks>
internal static class AnchorBaselineTestSupport
{
    /// <summary>Pixels of slack allowed for window-chrome and allocation-rounding differences between backends.</summary>
    internal const int PixelTolerance = 4;

    /// <summary>True when <paramref name="actual" /> is within <paramref name="tolerance" /> of <paramref name="expected" />.</summary>
    internal static bool Approx(int expected, int actual, int tolerance = PixelTolerance) =>
        Math.Abs(expected - actual) <= tolerance;

    /// <summary>True when every edge of <paramref name="actual" /> is within <paramref name="tolerance" /> of <paramref name="expected" />.</summary>
    internal static bool ApproxBounds(Rectangle expected, Rectangle actual, int tolerance = PixelTolerance) =>
        Approx(expected.X, actual.X, tolerance) &&
        Approx(expected.Y, actual.Y, tolerance) &&
        Approx(expected.Width, actual.Width, tolerance) &&
        Approx(expected.Height, actual.Height, tolerance);

    /// <summary>True when the control's GTK allocation matches <paramref name="width" />x<paramref name="height" /> within tolerance.</summary>
    internal static bool AllocatedApprox(Control control, int width, int height, int tolerance = PixelTolerance) =>
        control.Widget is { } widget &&
        Approx(widget.AllocatedWidth, width, tolerance) &&
        Approx(widget.AllocatedHeight, height, tolerance);

    /// <summary>
    /// Enlarges <paramref name="form" /> so its anchored children see a resize, preferring a real maximise
    /// and falling back to a direct size increase when no window manager honours it.
    /// </summary>
    /// <returns>The form's display width after the enlargement.</returns>
    internal static int Enlarge(Form form, int minGrowth = 150, int timeoutMs = 3000)
    {
        var width0 = form.DisplayRectangle.Width;

        form.WindowState = FormWindowState.Maximized;
        if (PumpUntil(() => form.DisplayRectangle.Width >= width0 + minGrowth, timeoutMs))
        {
            return form.DisplayRectangle.Width;
        }

        // Headless: no window manager maximised the window. Grow it explicitly - well past minGrowth so the
        // anchored children clearly move - and let the anchor pass follow it.
        form.WindowState = FormWindowState.Normal;
        form.Size = new Size(form.Width + minGrowth + 250, form.Height + 200);
        PumpUntil(() => form.DisplayRectangle.Width >= width0 + minGrowth, timeoutMs);
        return form.DisplayRectangle.Width;
    }

    /// <summary>Pumps the main loop until <paramref name="condition" /> holds or the timeout passes.</summary>
    internal static bool PumpUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            Application.DoEvents();
            if (condition())
            {
                return true;
            }

            Threading.Thread.Sleep(15);
        }

        return condition();
    }
}
