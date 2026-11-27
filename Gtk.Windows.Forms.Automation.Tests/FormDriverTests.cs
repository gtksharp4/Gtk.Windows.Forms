using System.Drawing;
using System.Windows.Forms;
using Gtk.Windows.Forms.Automation;

namespace GtkWinForms.Automation.Tests;

public class FormDriverTests
{
    [Fact]
    public void Find_ReturnsANestedControlByName()
    {
        using var ui = TestUi.Show(f =>
        {
            var panel = new Panel { Name = "outer" };
            panel.Controls.Add(new Button { Name = "deep", Text = "Deep" });
            f.Controls.Add(panel);
        });

        Assert.Equal("Deep", ui.Find<Button>("deep").Text);
    }

    [Fact]
    public void Find_WithAPath_PicksTheControlInsideTheNamedContainer()
    {
        using var ui = TestUi.Show(f =>
        {
            var left = new Panel { Name = "left", Location = new Point(0, 0), Size = new Size(140, 100) };
            left.Controls.Add(new Button { Name = "ok", Text = "Left OK" });
            var right = new Panel { Name = "right", Location = new Point(150, 0), Size = new Size(140, 100) };
            right.Controls.Add(new Button { Name = "ok", Text = "Right OK" });
            f.Controls.Add(left);
            f.Controls.Add(right);
        });

        Assert.Equal("Right OK", ui.Find<Button>("right/ok").Text);
        Assert.Equal("Left OK", ui.Find<Button>("left/ok").Text);
    }

    [Fact]
    public void Find_AnUnknownName_ThrowsListingTheNamesThatExist()
    {
        using var ui = TestUi.Show(f => f.Controls.Add(new Button { Name = "saveButton" }));

        var ex = Assert.Throws<ControlNotFoundException>(() => ui.Find<Button>("savebutton"));

        Assert.Contains("saveButton (Button)", ex.Message);
        Assert.Null(ui.TryFind<Button>("savebutton"));
    }

    [Fact]
    public void Find_WithTheWrongType_DoesNotMatch()
    {
        using var ui = TestUi.Show(f => f.Controls.Add(new Label { Name = "caption" }));

        Assert.Throws<ControlNotFoundException>(() => ui.Find<Button>("caption"));
    }

    [Fact]
    public void Click_RunsTheWiredHandler()
    {
        var clicks = 0;
        using var ui = TestUi.Show(f =>
        {
            var button = new Button { Name = "go" };
            button.Click += (_, _) => clicks++;
            f.Controls.Add(button);
        });

        ui.Click("go");

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Click_OnADisabledControl_IsRefusedAndRunsNoHandler()
    {
        var clicks = 0;
        using var ui = TestUi.Show(f =>
        {
            var button = new Button { Name = "go", Enabled = false };
            button.Click += (_, _) => clicks++;
            f.Controls.Add(button);
        });

        var ex = Assert.Throws<InvalidOperationException>(() => ui.Click("go"));

        Assert.Contains("disabled", ex.Message);
        Assert.Equal(0, clicks);
    }

    [Fact]
    public void Click_OnAControlInADisabledContainer_IsRefused()
    {
        using var ui = TestUi.Show(f =>
        {
            var panel = new Panel { Name = "frame" };
            panel.Controls.Add(new Button { Name = "go" });
            f.Controls.Add(panel);
            panel.Enabled = false;
        });

        var ex = Assert.Throws<InvalidOperationException>(() => ui.Click("go"));

        Assert.Contains("parent 'frame' is disabled", ex.Message);
    }

    [Fact]
    public void Click_OnAHiddenControl_IsRefused()
    {
        using var ui = TestUi.Show(f => f.Controls.Add(new Button { Name = "go", Visible = false }));

        var ex = Assert.Throws<InvalidOperationException>(() => ui.Click("go"));

        Assert.Contains("hidden", ex.Message);
    }

    [Fact]
    public void Click_OnADisabledControl_IsAllowedWhenInteractabilityIsNotRequired()
    {
        var clicks = 0;
        using var ui = TestUi.Show(f =>
        {
            var button = new Button { Name = "go", Enabled = false };
            button.Click += (_, _) => clicks++;
            f.Controls.Add(button);
        });

        UiAutomation.Options.RequireInteractable = false;
        try
        {
            ui.Click("go");
        }
        finally
        {
            UiAutomation.Options.RequireInteractable = true;
        }

        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Type_ReplacesTheTextAndRaisesTextChanged()
    {
        var changes = new List<string>();
        using var ui = TestUi.Show(f =>
        {
            var box = new TextBox { Name = "name", Text = "old" };
            box.TextChanged += (s, _) => changes.Add(((TextBox)s!).Text);
            f.Controls.Add(box);
        });

        ui.Type(ui.Find("name"), "Ada");

        Assert.Equal("Ada", ui.Find<TextBox>("name").Text);
        Assert.Contains("Ada", changes);
    }

    [Fact]
    public void Type_IntoAReadOnlyTextBox_IsRefused()
    {
        using var ui = TestUi.Show(f => f.Controls.Add(new TextBox { Name = "name", Text = "fixed", ReadOnly = true }));

        Assert.Throws<InvalidOperationException>(() => ui.Type(ui.Find("name"), "changed"));
        Assert.Equal("fixed", ui.Find<TextBox>("name").Text);
    }

    [Fact]
    public void Select_ByText_SelectsTheMatchingComboItem()
    {
        using var ui = TestUi.Show(f =>
        {
            var combo = new ComboBox { Name = "role", DropDownStyle = ComboBoxStyle.DropDownList };
            combo.Items.AddRange(new object[] { "Guest", "User", "Admin" });
            f.Controls.Add(combo);
        });
        var role = ui.Find<ComboBox>("role");

        ui.Select(role, "Admin");

        Assert.Equal(2, role.SelectedIndex);
        Assert.Throws<ArgumentException>(() => ui.Select(role, "admin"));
    }

    [Fact]
    public void Select_ByIndex_SelectsTheListBoxItem()
    {
        using var ui = TestUi.Show(f =>
        {
            var list = new ListBox { Name = "items", Size = new Size(120, 80) };
            list.Items.AddRange(new object[] { "one", "two", "three" });
            f.Controls.Add(list);
        });
        var items = ui.Find<ListBox>("items");

        ui.Select(items, 1);

        Assert.Equal("two", items.SelectedItem);
    }

    [Fact]
    public void Check_RaisesCheckedChanged()
    {
        var states = new List<bool>();
        using var ui = TestUi.Show(f =>
        {
            var box = new CheckBox { Name = "active" };
            box.CheckedChanged += (s, _) => states.Add(((CheckBox)s!).Checked);
            f.Controls.Add(box);
        });

        ui.Check(ui.Find<CheckBox>("active"));
        ui.Check(ui.Find<CheckBox>("active"), false);

        Assert.Equal(new[] { true, false }, states);
    }

    [Fact]
    public void PressKey_RaisesDownPressUpInOrder()
    {
        var events = new List<string>();
        using var ui = TestUi.Show(f =>
        {
            var box = new TextBox { Name = "input" };
            box.KeyDown += (_, e) => events.Add("down:" + e.KeyCode);
            box.KeyPress += (_, e) => events.Add("press:" + e.KeyChar);
            box.KeyUp += (_, e) => events.Add("up:" + e.KeyCode);
            f.Controls.Add(box);
        });

        ui.PressKey(ui.Find("input"), Keys.A, 'a');

        Assert.Equal(new[] { "down:A", "press:a", "up:A" }, events);
    }

    [Fact]
    public void PressKey_WithKeyPreview_LetsTheFormHandleItFirst()
    {
        var events = new List<string>();
        using var ui = TestUi.Show(f =>
        {
            f.KeyPreview = true;
            f.KeyDown += (_, e) =>
            {
                events.Add("form:" + e.KeyData);
                e.Handled = true;
            };
            var box = new TextBox { Name = "input" };
            box.KeyDown += (_, e) => events.Add("box:" + e.KeyData);
            f.Controls.Add(box);
        });

        ui.PressKey(ui.Find("input"), Keys.Control | Keys.S);

        Assert.Equal(new[] { "form:" + (Keys.Control | Keys.S) }, events);
    }

    [Fact]
    public void PressKey_SuppressedInKeyDown_SkipsKeyPress()
    {
        var events = new List<string>();
        using var ui = TestUi.Show(f =>
        {
            var box = new TextBox { Name = "input" };
            box.KeyDown += (_, e) =>
            {
                events.Add("down");
                e.SuppressKeyPress = true;
            };
            box.KeyPress += (_, _) => events.Add("press");
            f.Controls.Add(box);
        });

        ui.PressKey(ui.Find("input"), Keys.Enter, '\r');

        Assert.Equal(new[] { "down" }, events);
    }

    [Fact]
    public void AnExceptionThrownByAHandler_ReachesTheTestUnwrapped()
    {
        using var ui = TestUi.Show(f =>
        {
            var box = new TextBox { Name = "input" };
            box.KeyDown += (_, _) => throw new FormatException("bad key");
            f.Controls.Add(box);
        });

        var ex = Assert.Throws<FormatException>(() => ui.PressKey(ui.Find("input"), Keys.X));

        Assert.Equal("bad key", ex.Message);
    }

    [Fact]
    public void FindItem_AndClick_RunTheMenuItemHandler()
    {
        var opened = false;
        using var ui = TestUi.Show(f =>
        {
            var menu = new MenuStrip { Name = "mainMenu" };
            var file = new ToolStripMenuItem("File") { Name = "fileMenu" };
            var open = new ToolStripMenuItem("Open") { Name = "openItem" };
            open.Click += (_, _) => opened = true;
            file.DropDownItems.Add(open);
            menu.Items.Add(file);
            f.Controls.Add(menu);
            f.MainMenuStrip = menu;
        });

        ui.Click(ui.FindItem("openItem"));

        Assert.True(opened);
    }

    [Fact]
    public void WaitUntil_PumpsTheLoopSoATimerCanFire()
    {
        using var ui = TestUi.Show(_ => { });
        using var timer = new System.Windows.Forms.Timer { Interval = 50 };
        var ticked = false;
        timer.Tick += (_, _) =>
        {
            ticked = true;
            timer.Stop();
        };
        timer.Start();

        ui.WaitUntil(() => ticked, TimeSpan.FromSeconds(5), "the timer to tick");

        Assert.True(ticked);
    }

    [Fact]
    public void WaitUntil_ThrowsTimeoutExceptionNamingTheCondition()
    {
        using var ui = TestUi.Show(_ => { });

        var ex = Assert.Throws<TimeoutException>(
            () => ui.WaitUntil(() => false, TimeSpan.FromMilliseconds(100), "a miracle"));

        Assert.Contains("a miracle", ex.Message);
    }

    [Fact]
    public void Dispose_ClosesTheFormAndTheWindowsItOpened()
    {
        TestUi.Init();
        var before = Gtk.Window.ListToplevels().Count(w => w.IsVisible);
        var ui = TestUi.Show(f =>
        {
            var button = new Button { Name = "open" };
            button.Click += (_, _) => new Form { Text = "child" }.Show();
            f.Controls.Add(button);
        });
        ui.Click("open");
        Assert.True(Gtk.Window.ListToplevels().Count(w => w.IsVisible) >= before + 2);

        ui.Dispose();

        Assert.Equal(before, Gtk.Window.ListToplevels().Count(w => w.IsVisible));
    }

    [Fact]
    public async Task Calls_FromAnotherThread_AreRefused()
    {
        TestUi.Init();

        var ex = await Task.Run(() => Record.Exception(() => UiAutomation.Pump()));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("single-threaded", ex!.Message);
    }
}
