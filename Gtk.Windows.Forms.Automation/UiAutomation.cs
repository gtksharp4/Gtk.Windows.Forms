namespace Gtk.Windows.Forms.Automation;

// Inside the namespace on purpose: types of the enclosing GtkSharp "Gtk" namespace (Gtk.ComboBox,
// Gtk.Application, ...) would otherwise win over System.Windows.Forms in name lookup.
using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;

/// <summary>
/// Entry point for driving Gtk.Windows.Forms forms from code. Everything runs in-process on the thread that
/// called <see cref="Initialize(UiAutomationOptions?)"/>: GTK is single-threaded, so every call made from
/// another thread throws instead of corrupting GTK state. Under xUnit, the
/// <c>Gtk.Windows.Forms.Automation.Xunit</c> package runs all tests on one such thread.
/// </summary>
public static class UiAutomation
{
    private static readonly object Gate = new();
    private static Thread? _uiThread;
    private static UiAutomationOptions _options = new();
    private static ScriptedMessageBox? _messageBoxes;

    /// <summary>Gets a value indicating whether <see cref="Initialize(UiAutomationOptions?)"/> has run.</summary>
    public static bool IsInitialized => _uiThread != null;

    /// <summary>Gets the options the session was initialized with.</summary>
    public static UiAutomationOptions Options => _options;

    /// <summary>
    /// Gets the scripted message box installed by <see cref="UiAutomationOptions.InterceptMessageBoxes"/>,
    /// to queue answers and inspect what was shown. Throws if interception is off.
    /// </summary>
    public static ScriptedMessageBox MessageBoxes
    {
        get
        {
            EnsureUiThread();
            return _messageBoxes ?? throw new InvalidOperationException(
                "MessageBox interception is disabled (UiAutomationOptions.InterceptMessageBoxes = false).");
        }
    }

    /// <summary>
    /// Initializes GTK and the WinForms layer on the calling thread, which becomes the UI thread. Idempotent
    /// on that thread: later calls are no-ops and their <paramref name="options"/> are ignored.
    /// </summary>
    /// <param name="options">Settings for the session; <see langword="null"/> uses the defaults.</param>
    /// <exception cref="InvalidOperationException">Called from a thread other than the UI thread.</exception>
    public static void Initialize(UiAutomationOptions? options = null)
    {
        lock (Gate)
        {
            if (_uiThread != null)
            {
                EnsureUiThread();
                return;
            }

            _options = options ?? new UiAutomationOptions();

            if (_options.Culture != null)
            {
                CultureInfo.DefaultThreadCurrentCulture = _options.Culture;
                CultureInfo.DefaultThreadCurrentUICulture = _options.Culture;
                Thread.CurrentThread.CurrentCulture = _options.Culture;
                Thread.CurrentThread.CurrentUICulture = _options.Culture;
            }

            if (_options.InterceptMessageBoxes)
            {
                _messageBoxes = new ScriptedMessageBox();
                MessageBox.ReplaceInstance(_messageBoxes);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Init();

            _uiThread = Thread.CurrentThread;
        }
    }

    /// <summary>
    /// Shows <typeparamref name="TForm"/> non-modally and returns a driver for it. Dispose the driver to
    /// close the form and any window it opened. Initializes the session with default options if needed.
    /// </summary>
    /// <typeparam name="TForm">The form type.</typeparam>
    /// <param name="create">Creates the form; runs on the UI thread.</param>
    /// <returns>A driver owning the shown form.</returns>
    public static FormDriver<TForm> Show<TForm>(Func<TForm> create) where TForm : Form
    {
        if (create == null)
        {
            throw new ArgumentNullException(nameof(create));
        }

        Initialize();
        EnsureUiThread();

        var existing = WindowTracker.Snapshot();
        TForm? form = null;
        try
        {
            form = create();
            form.Show();
            Pump(Math.Max(20, _options.SettleIterations));
            return new FormDriver<TForm>(form, existing, ownsForm: true);
        }
        catch
        {
            WindowTracker.TearDown(form, existing);
            throw;
        }
    }

    /// <summary>
    /// Returns a driver for a form that is already open (for example one a click opened). The driver does
    /// not own it: disposing the driver leaves the form open.
    /// </summary>
    /// <typeparam name="TForm">The form type.</typeparam>
    /// <param name="form">The open form.</param>
    /// <returns>A non-owning driver.</returns>
    public static FormDriver<TForm> Attach<TForm>(TForm form) where TForm : Form
    {
        if (form == null)
        {
            throw new ArgumentNullException(nameof(form));
        }

        EnsureUiThread();
        return new FormDriver<TForm>(form, null, ownsForm: false);
    }

    /// <summary>
    /// Waits until an open form of type <typeparamref name="TForm"/> exists (optionally matching
    /// <paramref name="predicate"/>) and returns a non-owning driver for it.
    /// </summary>
    /// <typeparam name="TForm">The form type.</typeparam>
    /// <param name="predicate">Optional extra filter.</param>
    /// <param name="timeout">How long to wait; <see langword="null"/> uses <see cref="UiAutomationOptions.DefaultWaitTimeout"/>.</param>
    /// <returns>A driver for the first matching form.</returns>
    /// <exception cref="TimeoutException">No matching form opened in time.</exception>
    public static FormDriver<TForm> WaitForForm<TForm>(Func<TForm, bool>? predicate = null, TimeSpan? timeout = null)
        where TForm : Form
    {
        TForm? found = null;
        WaitUntil(
            () =>
            {
                found = Application.OpenForms.OfType<TForm>().FirstOrDefault(f => predicate == null || predicate(f));
                return found != null;
            },
            timeout,
            $"a {typeof(TForm).Name} to open");
        return Attach(found!);
    }

    /// <summary>
    /// Runs the GTK main loop until it is idle, <paramref name="iterations"/> times, so pending layout, paint
    /// and signal work settles. Several passes are needed because GTK schedules layout on frame ticks.
    /// </summary>
    /// <param name="iterations">Number of drain passes; <see langword="null"/> uses <see cref="UiAutomationOptions.SettleIterations"/>.</param>
    public static void Pump(int? iterations = null)
    {
        EnsureUiThread();
        var count = iterations ?? _options.SettleIterations;
        for (var i = 0; i < count; i++)
        {
            Application.DoEvents();
        }
    }

    /// <summary>
    /// Pumps the main loop until <paramref name="condition"/> is true, so timers, idle handlers and async
    /// continuations posted to the UI thread get to run.
    /// </summary>
    /// <param name="condition">The condition to wait for; evaluated on the UI thread.</param>
    /// <param name="timeout">How long to wait; <see langword="null"/> uses <see cref="UiAutomationOptions.DefaultWaitTimeout"/>.</param>
    /// <param name="description">What is being waited for, used in the timeout message.</param>
    /// <exception cref="TimeoutException">The condition did not become true in time.</exception>
    public static void WaitUntil(Func<bool> condition, TimeSpan? timeout = null, string? description = null)
    {
        if (condition == null)
        {
            throw new ArgumentNullException(nameof(condition));
        }

        EnsureUiThread();
        var limit = timeout ?? _options.DefaultWaitTimeout;
        var clock = Stopwatch.StartNew();
        while (true)
        {
            Application.DoEvents();
            if (condition())
            {
                return;
            }

            if (clock.Elapsed >= limit)
            {
                throw new TimeoutException(
                    $"Timed out after {limit.TotalMilliseconds:0} ms waiting for {description ?? "the condition"}.");
            }

            // Yield briefly so GLib timeouts become due without spinning a core.
            Thread.Sleep(5);
        }
    }

    /// <summary>Throws unless called on the UI thread of an initialized session.</summary>
    /// <exception cref="InvalidOperationException">Not initialized, or called from another thread.</exception>
    public static void EnsureUiThread()
    {
        var owner = _uiThread;
        if (owner == null)
        {
            throw new InvalidOperationException("UiAutomation.Initialize() has not been called.");
        }

        if (owner != Thread.CurrentThread)
        {
            throw new InvalidOperationException(
                $"GTK is single-threaded: UI automation must run on the thread that initialized it " +
                $"('{owner.Name ?? owner.ManagedThreadId.ToString(CultureInfo.InvariantCulture)}'), not on " +
                $"'{Thread.CurrentThread.Name ?? Thread.CurrentThread.ManagedThreadId.ToString(CultureInfo.InvariantCulture)}'. " +
                "Under xUnit, use the Gtk.Windows.Forms.Automation.Xunit test framework.");
        }
    }
}
