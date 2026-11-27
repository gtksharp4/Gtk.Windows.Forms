namespace Gtk.Windows.Forms.Automation;

using System.Globalization;

/// <summary>
/// Process-wide settings applied once by <see cref="UiAutomation.Initialize(UiAutomationOptions?)"/>.
/// </summary>
public sealed class UiAutomationOptions
{
    /// <summary>
    /// Culture pinned for the UI thread and as the default for new threads, so text, number and date
    /// formatting (and therefore screenshots) are the same on every machine. <see langword="null"/>
    /// leaves the process culture untouched. Default: <see langword="null"/>.
    /// </summary>
    public CultureInfo? Culture { get; set; }

    /// <summary>
    /// Replaces the library's <see cref="System.Windows.Forms.MessageBox"/> with a
    /// <see cref="ScriptedMessageBox"/>, so a handler that pops a modal message box returns a scripted
    /// answer immediately instead of blocking the test on a dialog nobody will click. Default:
    /// <see langword="true"/>.
    /// </summary>
    public bool InterceptMessageBoxes { get; set; } = true;

    /// <summary>
    /// Main-loop iterations run after showing a form and after every input verb, so GTK has realized,
    /// laid out and painted before the next assertion. Default: 12.
    /// </summary>
    public int SettleIterations { get; set; } = 12;

    /// <summary>
    /// When <see langword="true"/> (the default) input verbs refuse a control that is disabled or hidden,
    /// itself or through a parent, by throwing <see cref="InvalidOperationException"/> - the way a real user
    /// cannot click it. Set to <see langword="false"/> to raise events regardless.
    /// </summary>
    public bool RequireInteractable { get; set; } = true;

    /// <summary>Timeout used by <see cref="UiAutomation.WaitUntil"/> when none is passed. Default: 5 seconds.</summary>
    public TimeSpan DefaultWaitTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
