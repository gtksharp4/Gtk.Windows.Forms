namespace Gtk.Windows.Forms.Automation;

// Inside the namespace on purpose: types of the enclosing GtkSharp "Gtk" namespace (Gtk.ComboBox,
// Gtk.Application, ...) would otherwise win over System.Windows.Forms in name lookup.
using System.Drawing;
using System.Windows.Forms;

/// <summary>
/// Captures what a control looks like on screen as a Gtk.Drawing <see cref="Bitmap"/>. The capture is
/// rendered in-process from the widget's GTK 4 render nodes, so it needs no window focus and is not
/// affected by other windows overlapping it.
/// </summary>
/// <remarks>
/// Rendering uses GTK's software (Cairo) renderer rather than the window's GPU renderer, so the same widget
/// tree gives the same pixels on any machine. Fonts and theme still come from the host, so baselines are
/// only comparable between machines with the same GTK theme and fonts.
/// </remarks>
public static class UiCapture
{
    private const int AllocationWaitPasses = 20;
    private const int MaxEmptyPaints = 3;

    private static Gsk.Renderer? _renderer;

    /// <summary>
    /// Captures <paramref name="control"/> at its allocated size, in logical pixels. A <see cref="Form"/> is
    /// captured as its client area: the title bar is left out, because GTK draws it differently depending on
    /// whether the window has focus, which a test cannot rely on. Use <see cref="CaptureWindow(Form)"/> to
    /// include it.
    /// </summary>
    /// <param name="control">A control that is shown.</param>
    /// <returns>A new 32-bit ARGB bitmap; dispose it when done.</returns>
    /// <exception cref="InvalidOperationException">The control has no GTK widget, never gets a size, or is never painted.</exception>
    public static Bitmap Capture(Control control)
    {
        var widget = WidgetOf(control);
        // FormBase is a Gtk.Dialog (deprecated in GTK 4.10); its content area is the form's client area.
#pragma warning disable CS0612
        var clientArea = control is Form && widget is Gtk.Dialog dialog ? dialog.ContentArea : null;
#pragma warning restore CS0612
        return Capture(widget, clientArea, NameOf(control));
    }

    /// <summary>Captures the whole window of <paramref name="form"/>, including the title bar GTK draws.</summary>
    /// <param name="form">A shown form.</param>
    /// <returns>A new 32-bit ARGB bitmap; dispose it when done.</returns>
    public static Bitmap CaptureWindow(Form form) => Capture(WidgetOf(form), null, NameOf(form));

    private static Gtk.Widget WidgetOf(Control control)
    {
        if (control == null)
        {
            throw new ArgumentNullException(nameof(control));
        }

        UiAutomation.EnsureUiThread();
        return control.Widget ?? throw new InvalidOperationException($"'{NameOf(control)}' has no GTK widget to capture.");
    }

    private static string NameOf(Control control)
        => string.IsNullOrEmpty(control.Name) ? control.GetType().Name : control.Name;

    /// <summary>Renders <paramref name="widget"/>, cropped to <paramref name="region"/> (a descendant) when given.</summary>
    private static Bitmap Capture(Gtk.Widget widget, Gtk.Widget? region, string what)
    {
        // A freshly shown widget is allocated on a later frame tick; give layout a chance to run.
        UiAutomation.Pump();
        for (var i = 0; (widget.Width <= 0 || widget.Height <= 0) && i < AllocationWaitPasses; i++)
        {
            UiAutomation.Pump(5);
        }

        if (widget.Width <= 0 || widget.Height <= 0)
        {
            throw new InvalidOperationException(
                $"'{what}' has no allocated size ({widget.Width}x{widget.Height}); is it shown and visible?");
        }

        var frame = SnapshotAfterNextPaint(widget, region, what);
        var viewport = frame.Viewport;
        var width = (int)Math.Round(viewport.Width);
        var height = (int)Math.Round(viewport.Height);
        if (width <= 0 || height <= 0)
        {
            frame.Node?.Dispose();
            throw new InvalidOperationException($"'{what}' has an empty client area ({width}x{height}).");
        }

        if (frame.Node == null)
        {
            // Nothing drew (an empty, transparent container): a valid, fully transparent image.
            return new Bitmap(width, height);
        }

        using (frame.Node)
        {
            var bounds = Graphene.Rect.Alloc().Init(viewport.X, viewport.Y, width, height);
            using var texture = Renderer().RenderTexture(frame.Node, bounds);
            return new Bitmap(new MemoryStream(texture.SaveToPngBytes().Data));
        }
    }

    /// <summary>
    /// Takes the widget's render node in the after-paint phase of a frame, with the region to keep. A widget
    /// paintable does not draw the widget: it reuses the node GTK cached when the window last painted, and
    /// any queued redraw discards that node until the next frame. Snapshotting at an arbitrary moment
    /// therefore races with redraws and intermittently yields nothing; right after a paint the cached node
    /// exists and is current. The crop is measured in the same callback, so node and region match.
    /// </summary>
    private static (Gsk.RenderNode? Node, RectangleF Viewport) SnapshotAfterNextPaint(Gtk.Widget widget, Gtk.Widget? region, string what)
    {
        UiAutomation.WaitUntil(() => widget.IsMapped, null, $"'{what}' to be mapped");
        var clock = widget.FrameClock
                    ?? throw new InvalidOperationException($"'{what}' has no frame clock; is its window shown?");

        Gsk.RenderNode? node = null;
        var viewport = RectangleF.Empty;
        Exception? failure = null;
        var emptyPaints = 0;
        var done = false;

        void OnAfterPaint(object? sender, EventArgs e)
        {
            if (done)
            {
                return;
            }

            // Runs inside a GTK signal: never let an exception escape into native code.
            try
            {
                viewport = region == null ? new RectangleF(0, 0, widget.Width, widget.Height) : BoundsIn(region, widget);
                using var paintable = new Gtk.WidgetPaintable(widget);
                using var snapshot = new Gtk.Snapshot();
                paintable.Snapshot(snapshot, widget.Width, widget.Height);
                node = snapshot.ToNode();

                // Still empty right after a paint means the widget really draws nothing (an empty,
                // transparent container); a few frames in a row rule out a redraw queued mid-paint.
                done = node != null || ++emptyPaints >= MaxEmptyPaints;
                if (!done)
                {
                    widget.QueueDraw();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
                done = true;
            }
        }

        clock.AfterPaint += OnAfterPaint;
        try
        {
            widget.QueueDraw();
            UiAutomation.WaitUntil(() => done, null, $"a frame of '{what}' to be painted");
        }
        finally
        {
            clock.AfterPaint -= OnAfterPaint;
        }

        if (failure != null)
        {
            node?.Dispose();
            throw new InvalidOperationException($"Capturing '{what}' failed.", failure);
        }

        return (node, viewport);
    }

    private static RectangleF BoundsIn(Gtk.Widget region, Gtk.Widget ancestor)
    {
        // Not ComputeBounds/ComputePoint: the binding marshals their 'out Graphene.Rect/Point' incorrectly
        // and reading the result faults (AccessViolationException). TranslateCoordinates returns plain
        // doubles; GTK deprecated it in 4.12 but still ships it throughout GTK 4.
#pragma warning disable CS0612, CS0618
        if (!region.TranslateCoordinates(ancestor, 0, 0, out var x, out var y))
#pragma warning restore CS0612, CS0618
        {
            throw new InvalidOperationException("Could not locate the client area inside the window.");
        }

        return new RectangleF((float)x, (float)y, region.Width, region.Height);
    }

    private static Gsk.Renderer Renderer()
    {
        if (_renderer != null)
        {
            return _renderer;
        }

        var renderer = new Gsk.CairoRenderer();
        if (!renderer.RealizeForDisplay(Gdk.Display.Default))
        {
            renderer.Dispose();
            throw new InvalidOperationException("Could not realize GTK's Cairo renderer for the default display.");
        }

        // Kept for the process lifetime: the binding exposes no Unrealize, and realizing per capture is wasteful.
        return _renderer = renderer;
    }
}
