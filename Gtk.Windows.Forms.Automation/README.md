# Gtk.Windows.Forms.Automation

Drive the forms of a `Gtk.Windows.Forms.Base` application from code, and gate how they look against
committed screenshots. Everything runs **in-process** at the WinForms API level: input runs your real event
handlers, and an exception a handler throws reaches your test as a normal exception.

```csharp
using Gtk.Windows.Forms.Automation;

UiAutomation.Initialize(new UiAutomationOptions { Culture = CultureInfo.GetCultureInfo("en-US") });

using var ui = UiAutomation.Show(() => new MainForm());

ui.Type(ui.Find<TextBox>("nameTextBox"), "Ada");
ui.Select(ui.Find<ComboBox>("roleCombo"), "Admin");
ui.Check(ui.Find<CheckBox>("activeCheckBox"));

UiAutomation.MessageBoxes.Enqueue(DialogResult.Yes);   // answer the "Save changes?" box
ui.Click("saveButton");

Assert.Equal("Save changes?", UiAutomation.MessageBoxes.Last!.Text);
ui.WaitUntil(() => ui.Find<Label>("statusLabel").Text == "Saved");

BaselineStore.InProjectDirectory().Verify(ui.Form, "MainForm/saved");
```

## What it gives you

| Area | API |
|---|---|
| Session | `UiAutomation.Initialize`, `Show`, `Attach`, `WaitForForm<T>`, `Pump`, `WaitUntil` |
| Finding | `FormDriver.Find<T>("name")`, paths like `"panel1/okButton"`, `FindAll<T>()`, `FindItem("menuName")` |
| Input | `Click`, `DoubleClick`, `Type`, `PressKey`, `Select` (index or text), `Check`, `RaiseEvent` (any `OnXxx`) |
| Dialogs | `UiAutomation.MessageBoxes`: records each `MessageBox.Show` and answers from a queue, a `Responder`, or the default button |
| Pixels | `UiCapture.Capture(control)` → Gtk.Drawing `Bitmap`; `ImageComparer.Compare` with an `ImageTolerance` |
| Baselines | `BaselineStore.Verify(control, "Form/checkpoint")`; `UPDATE_UI_BASELINES=1` rewrites them |

## Rules it enforces

- **One thread.** GTK is single-threaded; every call from a thread other than the one that ran
  `Initialize` throws. With xUnit, add `Gtk.Windows.Forms.Automation.Xunit`, which runs all tests on one
  STA thread.
- **A user's reach.** Input verbs refuse a control that is disabled or hidden (itself or through a parent),
  and `Type` refuses a read-only text box. Turn off with `UiAutomationOptions.RequireInteractable = false`.
- **No blocking dialogs.** `MessageBox` is intercepted by default, so a handler that asks a question gets
  an answer at once instead of waiting for a click.

## Screenshots

Captures are rendered from the widget's GTK 4 render nodes by GTK's software renderer, so they need no
window focus and do not depend on the GPU. Fonts and theme still come from the machine: keep baselines
per platform and generate them where they are verified. On a mismatch the actual and diff images are
written to `TestResults/Screenshots`. A missing baseline is seeded and passes, except when the `CI`
environment variable is set, where it fails.

## Limits

- Input is raised at the WinForms level, not as OS mouse or keyboard events: `PressKey` raises
  `KeyDown`/`KeyPress`/`KeyUp`, but GTK does not insert the character into an entry (use `Type`).
- A `Form.ShowDialog()` called from a handler runs a nested loop that blocks the test until that form closes.
  Only `MessageBox` is intercepted.
- The library's static `MessageBox.Show` overloads do not yet pass `defaultButton` (or `options`) on to an
  installed message box, so an unanswered box falls back to its **first** button even when the
  application's code sets `MessageBoxDefaultButton.Button2`. Queue explicit answers for any box whose answer
  matters, such as a delete confirmation.
