namespace Gtk.Windows.Forms.Automation;

// Inside the namespace on purpose: types of the enclosing GtkSharp "Gtk" namespace (Gtk.ComboBox,
// Gtk.Application, ...) would otherwise win over System.Windows.Forms in name lookup.
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;

/// <summary>How different two images may be and still count as the same.</summary>
public sealed class ImageTolerance
{
    /// <summary>Pixel-exact comparison.</summary>
    public static ImageTolerance Exact { get; } = new() { MaxChannelDelta = 0, MaxDifferentPixelRatio = 0 };

    /// <summary>
    /// The default: a channel may differ by up to 12/255 and up to 0.5% of pixels may differ beyond that,
    /// which absorbs anti-aliasing noise but not a moved, resized or recolored control.
    /// </summary>
    public static ImageTolerance Default { get; } = new();

    /// <summary>Gets or sets the largest per-channel (R, G, B or A) difference, 0-255, that still counts as the same pixel.</summary>
    public int MaxChannelDelta { get; set; } = 12;

    /// <summary>Gets or sets the largest fraction (0-1) of pixels that may differ by more than <see cref="MaxChannelDelta"/>.</summary>
    public double MaxDifferentPixelRatio { get; set; } = 0.005;
}

/// <summary>The outcome of <see cref="ImageComparer.Compare(Bitmap, Bitmap, ImageTolerance?)"/>.</summary>
public sealed class ImageComparisonResult : IDisposable
{
    internal ImageComparisonResult(bool match, int differentPixels, int totalPixels, int maxChannelDelta, Bitmap? diff, string message)
    {
        Match = match;
        DifferentPixels = differentPixels;
        TotalPixels = totalPixels;
        MaxChannelDelta = maxChannelDelta;
        Diff = diff;
        Message = message;
    }

    /// <summary>Gets a value indicating whether the images are the same within the tolerance.</summary>
    public bool Match { get; }

    /// <summary>Gets the number of pixels that differ by more than the tolerance.</summary>
    public int DifferentPixels { get; }

    /// <summary>Gets the number of pixels compared (0 when the sizes differ).</summary>
    public int TotalPixels { get; }

    /// <summary>Gets the largest per-channel difference found.</summary>
    public int MaxChannelDelta { get; }

    /// <summary>
    /// Gets an image the size of the inputs with differing pixels in red over a faded copy of the actual
    /// image; <see langword="null"/> when the images match or their sizes differ.
    /// </summary>
    public Bitmap? Diff { get; }

    /// <summary>Gets a one-line description of the outcome.</summary>
    public string Message { get; }

    /// <summary>Disposes <see cref="Diff"/>.</summary>
    public void Dispose() => Diff?.Dispose();
}

/// <summary>Compares two bitmaps pixel by pixel within an <see cref="ImageTolerance"/>.</summary>
public static class ImageComparer
{
    /// <summary>Compares <paramref name="actual"/> with <paramref name="expected"/>.</summary>
    /// <param name="expected">The reference image.</param>
    /// <param name="actual">The image under test.</param>
    /// <param name="tolerance">The tolerance; <see langword="null"/> uses <see cref="ImageTolerance.Default"/>.</param>
    /// <returns>The result; dispose it to release its diff image.</returns>
    public static ImageComparisonResult Compare(Bitmap expected, Bitmap actual, ImageTolerance? tolerance = null)
    {
        if (expected == null)
        {
            throw new ArgumentNullException(nameof(expected));
        }

        if (actual == null)
        {
            throw new ArgumentNullException(nameof(actual));
        }

        tolerance ??= ImageTolerance.Default;

        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            return new ImageComparisonResult(false, 0, 0, 255, null,
                $"size differs: expected {expected.Width}x{expected.Height}, actual {actual.Width}x{actual.Height}");
        }

        var width = expected.Width;
        var height = expected.Height;
        var expectedPixels = ReadRgba(expected);
        var actualPixels = ReadRgba(actual);
        var diff = new byte[expectedPixels.Length];
        var different = 0;
        var maxDelta = 0;

        for (var i = 0; i < expectedPixels.Length; i += 4)
        {
            var delta = PixelDelta(expectedPixels, actualPixels, i);
            maxDelta = Math.Max(maxDelta, delta);
            if (delta > tolerance.MaxChannelDelta)
            {
                different++;
                diff[i] = 255;
                diff[i + 1] = 0;
                diff[i + 2] = 0;
                diff[i + 3] = 255;
            }
            else
            {
                // Faded grey of the actual pixel, so the red stands out but the layout stays recognizable.
                var grey = (byte)(((actualPixels[i] + actualPixels[i + 1] + actualPixels[i + 2]) / 3 / 4) + 191);
                diff[i] = grey;
                diff[i + 1] = grey;
                diff[i + 2] = grey;
                diff[i + 3] = 255;
            }
        }

        var total = width * height;
        var ratio = total == 0 ? 0 : (double)different / total;
        var match = ratio <= tolerance.MaxDifferentPixelRatio;
        var message = string.Format(
            CultureInfo.InvariantCulture,
            "{0} of {1} pixels ({2:P2}) differ by more than {3}/255 (largest channel difference {4}); allowed {5:P2}",
            different, total, ratio, tolerance.MaxChannelDelta, maxDelta, tolerance.MaxDifferentPixelRatio);

        return new ImageComparisonResult(match, different, total, maxDelta,
            match ? null : ToBitmap(diff, width, height), message);
    }

    /// <summary>Reads a bitmap's pixels as tightly packed, non-premultiplied RGBA bytes.</summary>
    internal static byte[] ReadRgba(Bitmap bitmap)
    {
        var pixbuf = bitmap.Pixbuf ?? throw new InvalidOperationException("The bitmap has no pixel data.");
        var width = pixbuf.Width;
        var height = pixbuf.Height;
        var channels = pixbuf.NChannels;
        var stride = pixbuf.Rowstride;
        var rowBytes = width * channels;
        var row = new byte[rowBytes];
        var rgba = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            // Copy only the row's pixel bytes: the last row of a pixbuf may be shorter than the rowstride.
            Marshal.Copy(IntPtr.Add(pixbuf.Pixels, y * stride), row, 0, rowBytes);
            for (int x = 0, src = 0, dst = y * width * 4; x < width; x++, src += channels, dst += 4)
            {
                rgba[dst] = row[src];
                rgba[dst + 1] = row[src + 1];
                rgba[dst + 2] = row[src + 2];
                rgba[dst + 3] = channels >= 4 ? row[src + 3] : (byte)255;
            }
        }

        return rgba;
    }

    private static int PixelDelta(byte[] a, byte[] b, int i)
    {
        // Two fully transparent pixels look identical whatever their colour channels hold.
        if (a[i + 3] == 0 && b[i + 3] == 0)
        {
            return 0;
        }

        var delta = Math.Abs(a[i] - b[i]);
        delta = Math.Max(delta, Math.Abs(a[i + 1] - b[i + 1]));
        delta = Math.Max(delta, Math.Abs(a[i + 2] - b[i + 2]));
        return Math.Max(delta, Math.Abs(a[i + 3] - b[i + 3]));
    }

    private static Bitmap ToBitmap(byte[] rgba, int width, int height)
    {
        // Through GLib.Bytes, which copies: the byte[] overload may wrap the managed array, which the GC can move.
        using var bytes = new GLib.Bytes(rgba);
        using var pixbuf = new Gdk.Pixbuf(bytes, Gdk.Colorspace.Rgb, true, 8, width, height, width * 4);
        return new Bitmap(new MemoryStream(pixbuf.SaveToBuffer("png")));
    }
}
