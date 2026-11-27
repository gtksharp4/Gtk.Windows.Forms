using System.Windows.Forms;
using Gtk.Windows.Forms.Automation;

namespace GtkWinFormsApp.UITests.Infrastructure;

/// <summary>
/// The scripting surface handed to a test body: the form under test, the automation driver's input verbs,
/// and the screenshot checkpoint gated against <c>Baselines/&lt;Form&gt;/&lt;checkpoint&gt;.png</c>.
/// </summary>
internal sealed class UiContext
{
    private static readonly Lazy<BaselineStore> Baselines = new(() => BaselineStore.InProjectDirectory("Baselines"));

    internal UiContext(FormDriver driver) => Driver = driver;

    /// <summary>The automation driver, for verbs this wrapper does not repeat (Find, PressKey, Check, ...).</summary>
    internal FormDriver Driver { get; }

    /// <summary>The form under test.</summary>
    internal Form Form => Driver.Form;

    /// <summary>Pumps the GTK main loop so pending work settles.</summary>
    internal void Pump(int iterations = 12) => Driver.Pump(iterations);

    /// <summary>Clicks a control as a user would (refused if it is disabled or hidden), then pumps.</summary>
    internal void Click(Control control) => Driver.Click(control);

    /// <summary>Replaces a text control's value (raising <c>TextChanged</c>), then pumps.</summary>
    internal void Type(Control textControl, string text) => Driver.Type(textControl, text);

    /// <summary>Selects an item in a combo box by index, then pumps.</summary>
    internal void Select(ComboBox combo, int index) => Driver.Select(combo, index);

    /// <summary>Selects an item in a list box by index, then pumps.</summary>
    internal void Select(ListBox list, int index) => Driver.Select(list, index);

    /// <summary>
    /// Captures the form's client area and compares it with its committed baseline (or rewrites the baseline
    /// when <c>UPDATE_UI_BASELINES=1</c>). <paramref name="checkpoint"/> names the visual state.
    /// </summary>
    internal void Shot(string checkpoint) => Baselines.Value.Verify(Form, $"{Form.GetType().Name}/{checkpoint}");
}
