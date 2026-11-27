namespace Gtk.Windows.Forms.Automation;

// Inside the namespace on purpose: types of the enclosing GtkSharp "Gtk" namespace (Gtk.ComboBox,
// Gtk.Application, ...) would otherwise win over System.Windows.Forms in name lookup.
using System.Windows.Forms;

/// <summary>Thrown when a control or tool strip item looked up by name does not exist in the form.</summary>
public sealed class ControlNotFoundException : Exception
{
    private const int MaxListedNames = 40;

    internal ControlNotFoundException(string name, Type expectedType, Form form)
        : base(BuildMessage(name, expectedType, form))
    {
        Name = name;
        ExpectedType = expectedType;
    }

    /// <summary>Gets the name or path that was looked up.</summary>
    public string Name { get; }

    /// <summary>Gets the type the caller asked for.</summary>
    public Type ExpectedType { get; }

    private static string BuildMessage(string name, Type expectedType, Form form)
    {
        var names = new List<string>();
        var stack = new Stack<Control>();
        stack.Push(form);
        while (stack.Count > 0 && names.Count <= MaxListedNames)
        {
            var control = stack.Pop();
            if (!ReferenceEquals(control, form) && !string.IsNullOrEmpty(control.Name))
            {
                names.Add($"{control.Name} ({control.GetType().Name})");
            }

            foreach (Control child in (IEnumerable<Control>?)control.Controls?.Cast<Control>() ?? Array.Empty<Control>())
            {
                stack.Push(child);
            }
        }

        var listed = names.Count == 0
            ? "the form has no named controls"
            : "named controls include: " + string.Join(", ", names.Take(MaxListedNames)) + (names.Count > MaxListedNames ? ", ..." : string.Empty);
        return $"No {expectedType.Name} named '{name}' in {form.GetType().Name}; {listed}.";
    }
}
