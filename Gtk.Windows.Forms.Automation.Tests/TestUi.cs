using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Gtk.Windows.Forms.Automation;

namespace GtkWinForms.Automation.Tests;

/// <summary>Shared setup: one automation session per test run, plus small form and bitmap builders.</summary>
internal static class TestUi
{
    internal static void Init()
    {
        UiAutomation.Initialize(new UiAutomationOptions { Culture = CultureInfo.GetCultureInfo("en-US") });
        UiAutomation.MessageBoxes.Clear();
    }

    /// <summary>Shows a 300x200 form after letting <paramref name="build"/> add its controls.</summary>
    internal static FormDriver<Form> Show(Action<Form> build)
    {
        Init();
        return UiAutomation.Show(() =>
        {
            var form = new Form { Name = "testForm", ClientSize = new Size(300, 200) };
            build(form);
            return form;
        });
    }

    internal static Bitmap Solid(int width, int height, Color color)
    {
        var bitmap = new Bitmap(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, color);
            }
        }

        return bitmap;
    }
}
