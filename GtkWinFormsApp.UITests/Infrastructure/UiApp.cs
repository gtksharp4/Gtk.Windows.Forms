using System.Globalization;
using Gtk.Windows.Forms.Automation;

namespace GtkWinFormsApp.UITests.Infrastructure;

/// <summary>
/// One-time session startup shared by every UI test, mirroring the sample's own <c>Program.Main</c>
/// bootstrap. Culture is pinned so on-screen text and number/date formatting - which screenshots and text
/// assertions depend on - are the same on every machine.
/// </summary>
internal static class UiApp
{
    /// <summary>Starts the automation session on first call; a no-op afterwards.</summary>
    internal static void EnsureInitialized()
    {
        if (UiAutomation.IsInitialized)
        {
            return;
        }

        System.AppContext.SetSwitch("System.Drawing.EnableUnixSupport", true);

        // MessageBox interception (on by default) answers the boxes sample handlers pop, e.g. Form2's
        // FormClosing, with their default button - the automated equivalent of the tester clicking OK.
        UiAutomation.Initialize(new UiAutomationOptions { Culture = CultureInfo.GetCultureInfo("en-US") });
    }
}
