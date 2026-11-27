using System.Windows.Forms;
using Gtk.Windows.Forms.Automation;

namespace GtkWinFormsApp.UITests.Infrastructure;

/// <summary>
/// Runs a test body against a real sample form through Gtk.Windows.Forms.Automation. The form is shown
/// non-modally and the GTK loop is pumped on the test thread, so an assertion failure is a normal xUnit
/// failure rather than an exception inside a GTK signal callback.
/// </summary>
internal static class UiDriver
{
    /// <summary>
    /// Constructs and shows <paramref name="create"/>, runs <paramref name="script"/> against it, then always
    /// closes the form and any window it opened. Exceptions from the script propagate.
    /// </summary>
    internal static void Run<TForm>(Func<TForm> create, Action<UiContext> script) where TForm : Form
    {
        UiApp.EnsureInitialized();
        using var driver = UiAutomation.Show(create);
        script(new UiContext(driver));
    }

    /// <summary>Pumps the GTK main loop <paramref name="iterations"/> times so pending layout/paint/signals settle.</summary>
    internal static void Pump(int iterations = 12) => UiAutomation.Pump(iterations);
}
