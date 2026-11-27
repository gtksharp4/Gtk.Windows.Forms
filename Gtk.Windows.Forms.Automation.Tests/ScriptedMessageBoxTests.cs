using System.Windows.Forms;
using Gtk.Windows.Forms.Automation;

namespace GtkWinForms.Automation.Tests;

public class ScriptedMessageBoxTests
{
    [Fact]
    public void AHandlersMessageBox_GetsTheQueuedAnswer_AndIsRecorded()
    {
        DialogResult? answer = null;
        using var ui = TestUi.Show(f =>
        {
            var button = new Button { Name = "delete" };
            button.Click += (_, _) => answer = MessageBox.Show("Delete it?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            f.Controls.Add(button);
        });
        UiAutomation.MessageBoxes.Enqueue(DialogResult.No);

        ui.Click("delete");

        Assert.Equal(DialogResult.No, answer);
        var call = Assert.Single(UiAutomation.MessageBoxes.Calls);
        Assert.Equal("Delete it?", call.Text);
        Assert.Equal("Confirm", call.Caption);
        Assert.Equal(MessageBoxButtons.YesNo, call.Buttons);
        Assert.Equal(MessageBoxIcon.Question, call.Icon);
        Assert.Equal(DialogResult.No, call.Result);
    }

    [Fact]
    public void WithNothingQueued_TheDefaultButtonAnswers()
    {
        TestUi.Init();

        // Through the interface: the library's static MessageBox.Show overloads do not yet forward the
        // default button to an installed IMessageBox, so via MessageBox.Show this would answer Yes.
        IMessageBox box = UiAutomation.MessageBoxes;
        var result = box.Show("Save?", "App", MessageBoxButtons.YesNoCancel, MessageBoxIcon.None, MessageBoxDefaultButton.Button2);

        Assert.Equal(DialogResult.No, result);
        Assert.Equal(DialogResult.OK, MessageBox.Show("Done"));
        Assert.Equal(string.Empty, UiAutomation.MessageBoxes.Last!.Caption);
    }

    [Fact]
    public void TheResponder_DecidesWhenTheQueueIsEmpty()
    {
        TestUi.Init();
        UiAutomation.MessageBoxes.Enqueue(DialogResult.Abort);
        UiAutomation.MessageBoxes.Responder = call => call.Text!.Contains("retry") ? DialogResult.Retry : DialogResult.Cancel;

        Assert.Equal(DialogResult.Abort, MessageBox.Show("first", "x", MessageBoxButtons.AbortRetryIgnore));
        Assert.Equal(DialogResult.Retry, MessageBox.Show("please retry", "x", MessageBoxButtons.RetryCancel));
        Assert.Equal(DialogResult.Cancel, MessageBox.Show("other", "x", MessageBoxButtons.RetryCancel));
    }

    [Theory]
    [InlineData(MessageBoxButtons.OK, MessageBoxDefaultButton.Button1, DialogResult.OK)]
    [InlineData(MessageBoxButtons.OK, MessageBoxDefaultButton.Button3, DialogResult.OK)]
    [InlineData(MessageBoxButtons.OKCancel, MessageBoxDefaultButton.Button2, DialogResult.Cancel)]
    [InlineData(MessageBoxButtons.AbortRetryIgnore, MessageBoxDefaultButton.Button3, DialogResult.Ignore)]
    [InlineData(MessageBoxButtons.YesNo, MessageBoxDefaultButton.Button1, DialogResult.Yes)]
    [InlineData(MessageBoxButtons.RetryCancel, MessageBoxDefaultButton.Button2, DialogResult.Cancel)]
    public void DefaultAnswer_IsTheDefaultButtonsResult(MessageBoxButtons buttons, MessageBoxDefaultButton defaultButton, DialogResult expected)
        => Assert.Equal(expected, ScriptedMessageBox.DefaultAnswer(buttons, defaultButton));
}
