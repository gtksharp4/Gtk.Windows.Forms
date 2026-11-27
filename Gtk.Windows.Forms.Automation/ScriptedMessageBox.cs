namespace Gtk.Windows.Forms.Automation;

// Inside the namespace on purpose: types of the enclosing GtkSharp "Gtk" namespace (Gtk.ComboBox,
// Gtk.Application, ...) would otherwise win over System.Windows.Forms in name lookup.
using System.Windows.Forms;

/// <summary>One <see cref="MessageBox"/> call the application made while automation was intercepting them.</summary>
public sealed class MessageBoxCall
{
    internal MessageBoxCall(IWin32Window? owner, string? text, string caption, MessageBoxButtons buttons,
        MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options)
    {
        Owner = owner;
        Text = text;
        Caption = caption;
        Buttons = buttons;
        Icon = icon;
        DefaultButton = defaultButton;
        Options = options;
    }

    /// <summary>Gets the owner window passed to <c>Show</c>, if any.</summary>
    public IWin32Window? Owner { get; }

    /// <summary>Gets the message text.</summary>
    public string? Text { get; }

    /// <summary>Gets the caption.</summary>
    public string Caption { get; }

    /// <summary>Gets the buttons the box offered.</summary>
    public MessageBoxButtons Buttons { get; }

    /// <summary>Gets the icon.</summary>
    public MessageBoxIcon Icon { get; }

    /// <summary>Gets the default button.</summary>
    public MessageBoxDefaultButton DefaultButton { get; }

    /// <summary>Gets the options.</summary>
    public MessageBoxOptions Options { get; }

    /// <summary>Gets the answer returned to the application.</summary>
    public DialogResult Result { get; internal set; }

    /// <inheritdoc />
    public override string ToString() => $"[{Caption}] {Text} ({Buttons}) -> {Result}";
}

/// <summary>
/// An <see cref="IMessageBox"/> that shows nothing: it records every call and answers from a queue, or
/// with the box's default button when the queue is empty. Installed by
/// <see cref="UiAutomation.Initialize(UiAutomationOptions?)"/> unless
/// <see cref="UiAutomationOptions.InterceptMessageBoxes"/> is off; reach it through
/// <see cref="UiAutomation.MessageBoxes"/>.
/// </summary>
public sealed class ScriptedMessageBox : IMessageBox
{
    private readonly List<MessageBoxCall> _calls = new();
    private readonly Queue<DialogResult> _answers = new();

    /// <summary>Not raised: no dialog is ever created.</summary>
    public event EventHandler<DialogEventArgs>? DialogAvailable
    {
        add { }
        remove { }
    }

    /// <summary>Gets every call recorded since the last <see cref="Clear"/>, oldest first.</summary>
    public IReadOnlyList<MessageBoxCall> Calls => _calls;

    /// <summary>Gets the most recent call, or <see langword="null"/> when none was made.</summary>
    public MessageBoxCall? Last => _calls.Count == 0 ? null : _calls[_calls.Count - 1];

    /// <summary>
    /// Gets or sets a function that decides the answer for calls the queue does not cover. When
    /// <see langword="null"/>, the box's default button is the answer.
    /// </summary>
    public Func<MessageBoxCall, DialogResult>? Responder { get; set; }

    /// <summary>Queues the answers for the next message boxes, in order. Queued answers win over <see cref="Responder"/>.</summary>
    /// <param name="answers">The answers.</param>
    public void Enqueue(params DialogResult[] answers)
    {
        foreach (var answer in answers)
        {
            _answers.Enqueue(answer);
        }
    }

    /// <summary>Forgets recorded calls, queued answers and the <see cref="Responder"/>.</summary>
    public void Clear()
    {
        _calls.Clear();
        _answers.Clear();
        Responder = null;
    }

    /// <summary>Returns the answer a user pressing Enter would give: the box's default button.</summary>
    /// <param name="buttons">The buttons offered.</param>
    /// <param name="defaultButton">The default button.</param>
    /// <returns>The default answer.</returns>
    public static DialogResult DefaultAnswer(MessageBoxButtons buttons, MessageBoxDefaultButton defaultButton)
    {
        var results = buttons switch
        {
            MessageBoxButtons.OKCancel => new[] { DialogResult.OK, DialogResult.Cancel },
            MessageBoxButtons.AbortRetryIgnore => new[] { DialogResult.Abort, DialogResult.Retry, DialogResult.Ignore },
            MessageBoxButtons.YesNoCancel => new[] { DialogResult.Yes, DialogResult.No, DialogResult.Cancel },
            MessageBoxButtons.YesNo => new[] { DialogResult.Yes, DialogResult.No },
            MessageBoxButtons.RetryCancel => new[] { DialogResult.Retry, DialogResult.Cancel },
            _ => new[] { DialogResult.OK },
        };
        var index = defaultButton switch
        {
            MessageBoxDefaultButton.Button2 => 1,
            MessageBoxDefaultButton.Button3 => 2,
            _ => 0,
        };
        return results[Math.Min(index, results.Length - 1)];
    }

    private DialogResult Record(IWin32Window? owner, string? text, string? caption,
        MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1, MessageBoxOptions options = 0)
    {
        var call = new MessageBoxCall(owner, text, caption ?? string.Empty, buttons, icon, defaultButton, options);
        _calls.Add(call);
        call.Result = _answers.Count > 0
            ? _answers.Dequeue()
            : Responder?.Invoke(call) ?? DefaultAnswer(buttons, defaultButton);
        return call.Result;
    }

#pragma warning disable CS1591 // The IMessageBox overloads are documented on the interface.
    public DialogResult Show(string? text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, bool displayHelpButton)
        => Record(null, text, caption, buttons, icon, defaultButton, options);

    public DialogResult Show(string? text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath)
        => Record(null, text, caption, buttons, icon, defaultButton, options);

    public DialogResult Show(IWin32Window? owner, string? text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath)
        => Record(owner, text, caption, buttons, icon, defaultButton, options);

    public DialogResult Show(string? text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath, string keyword)
        => Record(null, text, caption, buttons, icon, defaultButton, options);

    public DialogResult Show(IWin32Window? owner, string? text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath, string keyword)
        => Record(owner, text, caption, buttons, icon, defaultButton, options);

    public DialogResult Show(string? text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath, HelpNavigator navigator)
        => Record(null, text, caption, buttons, icon, defaultButton, options);

    public DialogResult Show(IWin32Window? owner, string? text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath, HelpNavigator navigator)
        => Record(owner, text, caption, buttons, icon, defaultButton, options);

    public DialogResult Show(string? text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath, HelpNavigator navigator, object param)
        => Record(null, text, caption, buttons, icon, defaultButton, options);

    public DialogResult Show(IWin32Window? owner, string? text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options, string helpFilePath, HelpNavigator navigator, object param)
        => Record(owner, text, caption, buttons, icon, defaultButton, options);

    public DialogResult Show(string? text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options)
        => Record(null, text, caption, buttons, icon, defaultButton, options);

    public DialogResult Show(string? text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        => Record(null, text, caption, buttons, icon, defaultButton);

    public DialogResult Show(string? text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        => Record(null, text, caption, buttons, icon);

    public DialogResult Show(string? text, string caption, MessageBoxButtons buttons)
        => Record(null, text, caption, buttons);

    public DialogResult Show(string? text, string caption)
        => Record(null, text, caption);

    public DialogResult Show(string? text)
        => Record(null, text, null);

    public DialogResult Show(IWin32Window? owner, string? text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options)
        => Record(owner, text, caption, buttons, icon, defaultButton, options);

    public DialogResult Show(IWin32Window? owner, string? text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        => Record(owner, text, caption, buttons, icon, defaultButton);

    public DialogResult Show(IWin32Window? owner, string? text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        => Record(owner, text, caption, buttons, icon);

    public DialogResult Show(IWin32Window? owner, string? text, string caption, MessageBoxButtons buttons)
        => Record(owner, text, caption, buttons);

    public DialogResult Show(IWin32Window? owner, string? text, string caption)
        => Record(owner, text, caption);

    public DialogResult Show(IWin32Window? owner, string? text)
        => Record(owner, text, null);
#pragma warning restore CS1591
}
