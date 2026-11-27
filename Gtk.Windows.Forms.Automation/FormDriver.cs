namespace Gtk.Windows.Forms.Automation;

// Inside the namespace on purpose: types of the enclosing GtkSharp "Gtk" namespace (Gtk.ComboBox,
// Gtk.Application, ...) would otherwise win over System.Windows.Forms in name lookup.
using System.Collections.Concurrent;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

/// <summary>
/// Drives one shown form at the WinForms API level: finds controls by name and performs the input a user
/// would, then pumps the GTK loop so the result is laid out and painted before the next step. Input runs
/// the application's real event handlers on the calling (UI) thread, so an exception thrown by a handler
/// propagates to the caller as a normal exception. Obtain one from
/// <see cref="UiAutomation.Show{TForm}(Func{TForm})"/> or <see cref="UiAutomation.Attach{TForm}(TForm)"/>.
/// </summary>
public class FormDriver : IDisposable
{
    private static readonly ConcurrentDictionary<(Type, string, Type), MethodInfo?> Raisers = new();

    private readonly HashSet<IntPtr>? _toplevelsBefore;
    private readonly bool _ownsForm;
    private bool _disposed;

    internal FormDriver(Form form, HashSet<IntPtr>? toplevelsBefore, bool ownsForm)
    {
        Form = form;
        _toplevelsBefore = toplevelsBefore;
        _ownsForm = ownsForm;
    }

    /// <summary>Gets the form under test.</summary>
    public Form Form { get; }

    /// <summary>
    /// Finds a control by <see cref="Control.Name"/>, searching the whole control tree depth-first. A path
    /// such as <c>"panel1/okButton"</c> resolves each segment inside the previous one, to disambiguate
    /// controls with the same name in different containers.
    /// </summary>
    /// <param name="name">The control name or a <c>/</c>-separated path of names.</param>
    /// <returns>The control.</returns>
    /// <exception cref="ControlNotFoundException">No control has that name.</exception>
    public Control Find(string name) => Find<Control>(name);

    /// <summary>Finds a control of type <typeparamref name="T"/> by name or path (see <see cref="Find(string)"/>).</summary>
    /// <typeparam name="T">The expected control type.</typeparam>
    /// <param name="name">The control name or a <c>/</c>-separated path of names.</param>
    /// <returns>The control.</returns>
    /// <exception cref="ControlNotFoundException">No control of that type has that name.</exception>
    public T Find<T>(string name) where T : Control
        => TryFind<T>(name) ?? throw new ControlNotFoundException(name, typeof(T), Form);

    /// <summary>Like <see cref="Find{T}(string)"/>, but returns <see langword="null"/> when nothing matches.</summary>
    /// <typeparam name="T">The expected control type.</typeparam>
    /// <param name="name">The control name or a <c>/</c>-separated path of names.</param>
    /// <returns>The control, or <see langword="null"/>.</returns>
    public T? TryFind<T>(string name) where T : Control
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("A control name is required.", nameof(name));
        }

        UiAutomation.EnsureUiThread();
        var segments = name.Split('/');
        Control scope = Form;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var next = Descendants(scope).FirstOrDefault(c => c.Name == segments[i]);
            if (next == null)
            {
                return null;
            }

            scope = next;
        }

        var last = segments[segments.Length - 1];
        return Descendants(scope).OfType<T>().FirstOrDefault(c => c.Name == last);
    }

    /// <summary>Returns every control of type <typeparamref name="T"/> in the form, depth-first.</summary>
    /// <typeparam name="T">The control type.</typeparam>
    /// <returns>The matching controls.</returns>
    public IReadOnlyList<T> FindAll<T>() where T : Control
    {
        UiAutomation.EnsureUiThread();
        return Descendants(Form).OfType<T>().ToList();
    }

    /// <summary>
    /// Finds a menu or tool strip item by <see cref="ToolStripItem.Name"/> in every strip of the form,
    /// including drop-down items.
    /// </summary>
    /// <param name="name">The item name.</param>
    /// <returns>The item.</returns>
    /// <exception cref="ControlNotFoundException">No item has that name.</exception>
    public ToolStripItem FindItem(string name)
    {
        UiAutomation.EnsureUiThread();
        return AllItems().FirstOrDefault(i => i.Name == name)
               ?? throw new ControlNotFoundException(name, typeof(ToolStripItem), Form);
    }

    /// <summary>Clicks a control: raises its <c>Click</c> event through <see cref="Control.PerformClick"/>, then pumps.</summary>
    /// <param name="control">The control to click.</param>
    /// <remarks>
    /// This runs the click handlers; it does not toggle a <see cref="CheckBox"/> or <see cref="RadioButton"/>,
    /// whose state GTK changes itself on a real click - use <see cref="Check(CheckBox, bool)"/> for those.
    /// </remarks>
    public void Click(Control control)
    {
        EnsureInteractable(control, "click");
        control.PerformClick();
        Settle();
    }

    /// <summary>Clicks the control with the given name (see <see cref="Find(string)"/>).</summary>
    /// <param name="name">The control name or path.</param>
    public void Click(string name) => Click(Find(name));

    /// <summary>Clicks a menu or tool strip item, then pumps.</summary>
    /// <param name="item">The item to click.</param>
    public void Click(ToolStripItem item)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        UiAutomation.EnsureUiThread();
        if (UiAutomation.Options.RequireInteractable && !item.Enabled)
        {
            throw new InvalidOperationException($"Cannot click item '{item.Name}': it is disabled.");
        }

        item.PerformClick();
        Settle();
    }

    /// <summary>Double-clicks a control: raises <c>DoubleClick</c>, then pumps.</summary>
    /// <param name="control">The control.</param>
    public void DoubleClick(Control control)
    {
        EnsureInteractable(control, "double-click");
        RaiseEvent(control, "OnDoubleClick", EventArgs.Empty);
    }

    /// <summary>
    /// Replaces a control's text, as typing over a selection would, raising <c>TextChanged</c>; then pumps.
    /// A read-only text box refuses it, as it would refuse a user.
    /// </summary>
    /// <param name="control">The text control.</param>
    /// <param name="text">The new text.</param>
    public void Type(Control control, string text)
    {
        EnsureInteractable(control, "type into");
        if (UiAutomation.Options.RequireInteractable && control is TextBoxBase { ReadOnly: true })
        {
            throw new InvalidOperationException($"Cannot type into '{control.Name}': it is read-only.");
        }

        control.Text = text;
        Settle();
    }

    /// <summary>
    /// Presses a key on a control: raises <c>KeyDown</c>, then <c>KeyPress</c> when <paramref name="keyChar"/>
    /// is given and the key was not suppressed, then <c>KeyUp</c>. When the form has
    /// <see cref="Form.KeyPreview"/> set, the form sees each event first, as in WinForms.
    /// </summary>
    /// <param name="control">The focused control receiving the key.</param>
    /// <param name="keyData">The key and modifiers, e.g. <c>Keys.Control | Keys.S</c>.</param>
    /// <param name="keyChar">The character the key produces, if any.</param>
    /// <remarks>Events are raised at the WinForms level; GTK does not insert the character into an entry. Use <see cref="Type"/> to change text.</remarks>
    public void PressKey(Control control, Keys keyData, char? keyChar = null)
    {
        EnsureInteractable(control, "press a key on");
        var targets = Form.KeyPreview && !ReferenceEquals(control, Form) ? new Control[] { Form, control } : new[] { control };

        var down = new KeyEventArgs(keyData);
        foreach (var target in targets)
        {
            InvokeRaiser(target, "OnKeyDown", down);
            if (down.Handled)
            {
                break;
            }
        }

        if (keyChar.HasValue && !down.SuppressKeyPress)
        {
            var press = new KeyPressEventArgs(keyChar.Value);
            foreach (var target in targets)
            {
                InvokeRaiser(target, "OnKeyPress", press);
                if (press.Handled)
                {
                    break;
                }
            }
        }

        var up = new KeyEventArgs(keyData);
        foreach (var target in targets)
        {
            InvokeRaiser(target, "OnKeyUp", up);
            if (up.Handled)
            {
                break;
            }
        }

        Settle();
    }

    /// <summary>Selects the item at <paramref name="index"/> in a combo box, then pumps.</summary>
    /// <param name="combo">The combo box.</param>
    /// <param name="index">The item index.</param>
    public void Select(ComboBox combo, int index)
    {
        EnsureInteractable(combo, "select in");
        combo.SelectedIndex = index;
        Settle();
    }

    /// <summary>Selects the item whose display text is exactly <paramref name="itemText"/> in a combo box, then pumps.</summary>
    /// <param name="combo">The combo box.</param>
    /// <param name="itemText">The display text of the item.</param>
    /// <exception cref="ArgumentException">No item has that text.</exception>
    public void Select(ComboBox combo, string itemText)
    {
        EnsureInteractable(combo, "select in");
        combo.SelectedIndex = IndexOfText(combo.Items.Cast<object>(), combo.GetItemText, itemText, combo.Name);
        Settle();
    }

    /// <summary>Selects the item at <paramref name="index"/> in a list box, then pumps.</summary>
    /// <param name="list">The list box.</param>
    /// <param name="index">The item index.</param>
    public void Select(ListBox list, int index)
    {
        EnsureInteractable(list, "select in");
        list.SelectedIndex = index;
        Settle();
    }

    /// <summary>Selects the item whose display text is exactly <paramref name="itemText"/> in a list box, then pumps.</summary>
    /// <param name="list">The list box.</param>
    /// <param name="itemText">The display text of the item.</param>
    /// <exception cref="ArgumentException">No item has that text.</exception>
    public void Select(ListBox list, string itemText)
    {
        EnsureInteractable(list, "select in");
        list.SelectedIndex = IndexOfText(list.Items.Cast<object>(), list.GetItemText, itemText, list.Name);
        Settle();
    }

    /// <summary>Sets a check box's state, raising <c>CheckedChanged</c> when it changes, then pumps.</summary>
    /// <param name="checkBox">The check box.</param>
    /// <param name="isChecked">The state to set.</param>
    public void Check(CheckBox checkBox, bool isChecked = true)
    {
        EnsureInteractable(checkBox, "check");
        checkBox.Checked = isChecked;
        Settle();
    }

    /// <summary>Checks a radio button, which unchecks the others in its group, then pumps.</summary>
    /// <param name="radioButton">The radio button.</param>
    public void Check(RadioButton radioButton)
    {
        EnsureInteractable(radioButton, "check");
        radioButton.Checked = true;
        Settle();
    }

    /// <summary>
    /// Raises any event of a control through its protected <c>OnXxx</c> method - the escape hatch for input
    /// the other verbs do not cover, e.g. <c>RaiseEvent(c, "OnMouseDown", new MouseEventArgs(...))</c>. Pumps after.
    /// Does not check that the control is interactable.
    /// </summary>
    /// <param name="control">The control.</param>
    /// <param name="onMethodName">The raiser's name, e.g. <c>OnMouseDown</c>.</param>
    /// <param name="args">The event arguments; their runtime type selects the overload.</param>
    /// <exception cref="MissingMethodException">The control has no such raiser.</exception>
    public void RaiseEvent(Control control, string onMethodName, EventArgs args)
    {
        if (control == null)
        {
            throw new ArgumentNullException(nameof(control));
        }

        UiAutomation.EnsureUiThread();
        InvokeRaiser(control, onMethodName, args);
        Settle();
    }

    /// <summary>Pumps the GTK loop (see <see cref="UiAutomation.Pump(int?)"/>).</summary>
    /// <param name="iterations">Number of drain passes; <see langword="null"/> uses the configured default.</param>
    public void Pump(int? iterations = null) => UiAutomation.Pump(iterations);

    /// <summary>Waits until <paramref name="condition"/> holds (see <see cref="UiAutomation.WaitUntil"/>).</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="description">What is being waited for.</param>
    public void WaitUntil(Func<bool> condition, TimeSpan? timeout = null, string? description = null)
        => UiAutomation.WaitUntil(condition, timeout, description);

    /// <summary>Captures the form's client area as a bitmap (see <see cref="UiCapture.Capture(Control)"/>).</summary>
    /// <returns>A new bitmap; dispose it when done.</returns>
    public Bitmap Capture() => UiCapture.Capture(Form);

    /// <summary>Captures the whole form window, including the title bar (see <see cref="UiCapture.CaptureWindow(Form)"/>).</summary>
    /// <returns>A new bitmap; dispose it when done.</returns>
    public Bitmap CaptureWindow() => UiCapture.CaptureWindow(Form);

    /// <summary>Captures one control as a bitmap (see <see cref="UiCapture.Capture(Control)"/>).</summary>
    /// <param name="control">The control.</param>
    /// <returns>A new bitmap; dispose it when done.</returns>
    public Bitmap Capture(Control control) => UiCapture.Capture(control);

    /// <summary>
    /// Closes the form if this driver owns it, and destroys every window opened since it was shown, so the
    /// next test starts from a clean desktop. Safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsForm)
        {
            WindowTracker.TearDown(Form, _toplevelsBefore);
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        var stack = new Stack<Control>();
        PushChildren(root, stack);
        while (stack.Count > 0)
        {
            var control = stack.Pop();
            yield return control;
            PushChildren(control, stack);
        }
    }

    private static void PushChildren(Control parent, Stack<Control> stack)
    {
        var children = parent.Controls;
        if (children == null)
        {
            return;
        }

        for (var i = children.Count - 1; i >= 0; i--)
        {
            stack.Push(children[i]);
        }
    }

    private IEnumerable<ToolStripItem> AllItems()
    {
        var pending = new Stack<ToolStripItem>();
        foreach (var strip in new[] { Form }.Concat(Descendants(Form)).OfType<ToolStrip>())
        {
            foreach (ToolStripItem item in strip.Items)
            {
                pending.Push(item);
            }
        }

        while (pending.Count > 0)
        {
            var item = pending.Pop();
            yield return item;
            if (item is ToolStripDropDownItem dropDown)
            {
                foreach (ToolStripItem child in dropDown.DropDownItems)
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static int IndexOfText(IEnumerable<object> items, Func<object, string?> textOf, string itemText, string owner)
    {
        var index = 0;
        foreach (var item in items)
        {
            if (string.Equals(textOf(item), itemText, StringComparison.Ordinal))
            {
                return index;
            }

            index++;
        }

        throw new ArgumentException($"'{owner}' has no item with the text '{itemText}'.", nameof(itemText));
    }

    private static void EnsureInteractable(Control control, string verb)
    {
        if (control == null)
        {
            throw new ArgumentNullException(nameof(control));
        }

        UiAutomation.EnsureUiThread();
        if (control.IsDisposed)
        {
            throw new ObjectDisposedException(control.Name, $"Cannot {verb} '{control.Name}': it is disposed.");
        }

        if (!UiAutomation.Options.RequireInteractable)
        {
            return;
        }

        // Enabled/Visible here report the control's own GTK flag, so walk the parents: a control inside a
        // disabled panel or a hidden tab page cannot be reached by a user either.
        for (var c = control; c != null; c = c.Parent)
        {
            if (!c.Enabled)
            {
                throw new InvalidOperationException(
                    $"Cannot {verb} '{control.Name}': " + (ReferenceEquals(c, control) ? "it is disabled." : $"its parent '{c.Name}' is disabled."));
            }

            if (!c.Visible)
            {
                throw new InvalidOperationException(
                    $"Cannot {verb} '{control.Name}': " + (ReferenceEquals(c, control) ? "it is hidden." : $"its parent '{c.Name}' is hidden."));
            }
        }
    }

    private static void InvokeRaiser(Control target, string methodName, EventArgs args)
    {
        var key = (target.GetType(), methodName, args.GetType());
        var method = Raisers.GetOrAdd(key, k => FindRaiser(k.Item1, k.Item2, k.Item3))
                     ?? throw new MissingMethodException(
                         $"{target.GetType().Name} has no {methodName} method accepting {args.GetType().Name}.");
        try
        {
            method.Invoke(target, new object[] { args });
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            // Surface the handler's own exception, not the reflection wrapper.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static MethodInfo? FindRaiser(Type? type, string name, Type argsType)
    {
        for (; type != null; type = type.BaseType)
        {
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (method.Name != name)
                {
                    continue;
                }

                var parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(argsType))
                {
                    return method;
                }
            }
        }

        return null;
    }

    private static void Settle() => UiAutomation.Pump();
}

/// <summary>A <see cref="FormDriver"/> whose <see cref="Form"/> is typed as <typeparamref name="TForm"/>.</summary>
/// <typeparam name="TForm">The form type.</typeparam>
public sealed class FormDriver<TForm> : FormDriver where TForm : Form
{
    internal FormDriver(TForm form, HashSet<IntPtr>? toplevelsBefore, bool ownsForm)
        : base(form, toplevelsBefore, ownsForm)
    {
    }

    /// <summary>Gets the form under test.</summary>
    public new TForm Form => (TForm)base.Form;
}

/// <summary>Tracks GTK toplevel windows so a driver can destroy exactly the windows its test opened.</summary>
internal static class WindowTracker
{
    /// <summary>Returns the native handles of the toplevel windows that exist now.</summary>
    internal static HashSet<IntPtr> Snapshot()
        => new(Gtk.Window.ListToplevels().Select(w => w.Handle));

    /// <summary>
    /// Closes <paramref name="form"/>, then destroys every toplevel not in <paramref name="before"/> (all of
    /// them when it is <see langword="null"/>). Best effort: teardown never throws over the test's own failure.
    /// </summary>
    internal static void TearDown(Form? form, HashSet<IntPtr>? before)
    {
        if (form != null && !form.IsDisposed)
        {
            try
            {
                form.Close();
            }
            catch
            {
                // best-effort teardown
            }
        }

        PumpQuietly(8);

        foreach (var window in Gtk.Window.ListToplevels())
        {
            if (before != null && before.Contains(window.Handle))
            {
                continue;
            }

            try
            {
                if (window.IsRealized)
                {
                    window.Destroy();
                }
            }
            catch
            {
                // best-effort teardown
            }
        }

        PumpQuietly(4);
    }

    private static void PumpQuietly(int iterations)
    {
        for (var i = 0; i < iterations; i++)
        {
            try
            {
                Application.DoEvents();
            }
            catch
            {
                // a handler failing during teardown must not mask the test result
            }
        }
    }
}
