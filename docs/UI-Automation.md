# UI automation

How to test the forms of an application built on `Gtk.Windows.Forms` (package `Gtk.Windows.Forms.Base`)
with the **`Gtk.Windows.Forms.Automation`** and **`Gtk.Windows.Forms.Automation.Xunit`** packages: driving
forms from code, answering message boxes, waiting for asynchronous work, and gating how forms look against
committed screenshots.

The package READMEs ([Automation](../Gtk.Windows.Forms.Automation/README.md),
[Automation.Xunit](../Gtk.Windows.Forms.Automation.Xunit/README.md)) are the short overview shipped on
NuGet; this page is the full guide.

## Contents

- [The model](#the-model)
- [Setting up a test project](#setting-up-a-test-project)
- [A first test](#a-first-test)
- [Session and form lifetime](#session-and-form-lifetime)
- [Finding controls](#finding-controls)
- [Input](#input)
- [Message boxes](#message-boxes)
- [Waiting](#waiting)
- [Forms that open other forms](#forms-that-open-other-forms)
- [Screenshots and baselines](#screenshots-and-baselines)
- [Running in CI](#running-in-ci)
- [Troubleshooting](#troubleshooting)
- [Limits](#limits)
- [Examples in this repository](#examples-in-this-repository)

## The model

Automation runs **in-process**, at the **WinForms API level**, on **one thread**.

- **In-process.** The test constructs the real form and calls into it. There is no separate driver
  process, no accessibility tree and no OS input injection.
- **WinForms API level.** A click calls `PerformClick()`, typing sets `Text`, a selection sets
  `SelectedIndex`. The application's own event handlers run, synchronously, on the test thread, so an
  exception a handler throws reaches the test as an ordinary exception with its own stack trace.
- **One thread.** GTK is single-threaded, and on Windows GDK 4 needs that thread to be an STA. The thread
  that calls `UiAutomation.Initialize` becomes the UI thread; every automation call from any other thread
  throws `InvalidOperationException` instead of corrupting GTK state.

After every input verb the driver pumps the GTK main loop (`SettleIterations`, 12 by default) so layout,
painting and signals have run before the next line of the test.

What this gives you: fast, deterministic tests that need no window focus and cannot be disturbed by other
windows. What it does not: proof that GTK delivers a real mouse click or keystroke to your control. See
[Limits](#limits).

## Setting up a test project

The packages are framework-neutral (`netstandard2.0`). `Gtk.Windows.Forms.Automation` works with any test
framework; `Gtk.Windows.Forms.Automation.Xunit` adds the single-thread runner for **xUnit v2**.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
    <PackageReference Include="Gtk.Windows.Forms.Automation" Version="..." />
    <PackageReference Include="Gtk.Windows.Forms.Automation.Xunit" Version="..." />
  </ItemGroup>

  <ItemGroup>
    <!-- The application whose forms are under test. -->
    <ProjectReference Include="..\MyApp\MyApp.csproj" />
  </ItemGroup>
</Project>
```

Then activate the framework once, in any `.cs` file of the test project:

```csharp
using Gtk.Windows.Forms.Automation.Xunit;

[assembly: TestFramework(GtkTestFramework.TypeName, GtkTestFramework.AssemblyName)]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

`GtkTestFramework` runs every test of the assembly on one dedicated thread (`GTK-Test-Thread`, an STA on
Windows) and initializes GTK before the first test. `async` tests stay on that thread: their continuations
are queued back to it. Disabling parallelization is strongly recommended: without it, test collections
interleave at `await` points on the shared thread.

Without the framework, xUnit runs tests on MTA thread-pool threads, and on Windows the host aborts with
`OleInitialize failed ... incompatible apartment model`.

With another test framework (NUnit, MSTest, ...), run all UI tests on one thread you own (an STA on
Windows) and call `UiAutomation.Initialize` on it first.

## A first test

```csharp
using System.Globalization;
using System.Windows.Forms;
using Gtk.Windows.Forms.Automation;

public class LoginFormTests
{
    private static void Init()
    {
        UiAutomation.Initialize(new UiAutomationOptions { Culture = CultureInfo.GetCultureInfo("en-US") });
        UiAutomation.MessageBoxes.Clear();
    }

    [Fact]
    public void SigningIn_WithAnEmptyPassword_ShowsAnError()
    {
        Init();
        using var ui = UiAutomation.Show(() => new LoginForm());

        ui.Type(ui.Find<TextBox>("userTextBox"), "ada");
        ui.Click("signInButton");

        Assert.Equal("Password is required.", UiAutomation.MessageBoxes.Last!.Text);
        Assert.False(ui.Form.IsDisposed);
    }
}
```

`Initialize` is idempotent on the UI thread: the first call applies its options, later calls are no-ops
(and their options are ignored), so calling it at the start of every test is fine. Call it from the test
body, which is guaranteed to run on the GTK thread. In a larger suite, put it in one shared helper, as
[GtkWinFormsApp.UITests/Infrastructure/UiApp.cs](../GtkWinFormsApp.UITests/Infrastructure/UiApp.cs) does.

## Session and form lifetime

### `UiAutomationOptions`

Set once, by the first `Initialize` call.

| Option | Default | Effect |
|---|---|---|
| `Culture` | `null` | Pins `CurrentCulture`/`CurrentUICulture` of the UI thread and the default for new threads. Pin it: text, number and date formatting change screenshots and text assertions. `null` leaves the process culture alone. |
| `InterceptMessageBoxes` | `true` | Installs a `ScriptedMessageBox` through `MessageBox.ReplaceInstance`, so `MessageBox.Show` never blocks. See [Message boxes](#message-boxes). |
| `SettleIterations` | `12` | Main-loop passes run after showing a form and after each input verb. |
| `RequireInteractable` | `true` | Input verbs refuse a disabled or hidden control (itself or through a parent), and `Type` refuses a read-only text box. |
| `DefaultWaitTimeout` | 5 s | Timeout for `WaitUntil` / `WaitForForm` when none is passed. |

`Initialize` also runs `Application.EnableVisualStyles()`,
`Application.SetCompatibleTextRenderingDefault(false)` and `Application.Init()` — the usual `Program.Main`
bootstrap — so tests do not repeat it. Anything else your `Program.Main` does before `Application.Run`
(settings, services, `AppContext` switches) the test must do itself.

### Showing a form

| Call | Owns the form | Use for |
|---|---|---|
| `UiAutomation.Show(() => new MyForm())` | yes | The form under test. Shows it **non-modally**, pumps, returns a `FormDriver<MyForm>`. |
| `UiAutomation.Attach(form)` | no | A form you already have a reference to. |
| `UiAutomation.WaitForForm<T>(predicate?, timeout?)` | no | A form the application opened by itself (see [below](#forms-that-open-other-forms)). |

The `create` delegate runs on the UI thread. If it or `Show()` throws, any window it opened is torn down
before the exception propagates.

**Always dispose the driver** (`using var ui = ...`). Disposing an owning driver closes the form and then
destroys **every GTK toplevel window opened since it was shown** — child forms, dialogs, tool windows — so
the next test starts from a clean desktop even when this one failed halfway. Teardown is best-effort: an
exception from a `FormClosing` handler during teardown is swallowed so it cannot mask the test's own result.
Disposing a non-owning driver does nothing.

`FormDriver<TForm>.Form` is typed as your form, so its members are directly reachable:
`ui.Form.CurrentUser`.

## Finding controls

| Call | Result |
|---|---|
| `ui.Find("name")` / `ui.Find<T>("name")` | First control with that `Name` (and type), depth-first over the whole tree. Throws `ControlNotFoundException`. |
| `ui.Find<T>("panel1/okButton")` | Path: each segment is looked up inside the previous one. Use it when two containers hold controls with the same name. |
| `ui.TryFind<T>("name")` | As `Find<T>`, but returns `null`. Use it to assert absence. |
| `ui.FindAll<T>()` | Every control of type `T`, depth-first. |
| `ui.FindItem("name")` | A `ToolStripItem` (menu item, tool button, status label) in any strip of the form, drop-down items included. |

Names are matched **ordinally** (case-sensitive) against `Control.Name`, which the designer sets to the
field name. A failed lookup lists the names that do exist, which usually points straight at a typo:

```
No Button named 'savebutton' in MainForm; named controls include: saveButton (Button), nameTextBox (TextBox), ...
```

A type mismatch is also "not found": `Find<Button>("caption")` on a `Label` throws.

### Find by name, or reach the designer field?

Lookup by name works against any application unchanged and is the default. If you prefer compile-checked
access, change the designer fields from `private` to `internal` and grant the test assembly
`InternalsVisibleTo`; the test can then write `ui.Form.saveButton`. `GtkWinFormsApp.UITests` does this (see
its [README](../GtkWinFormsApp.UITests/README.md#how-the-forms-are-reached)). If the test assembly is
strong-name signed, the grant must carry its public key.

## Input

All verbs check the UI thread, then (with `RequireInteractable`) check that a user could reach the
control, perform the action, and pump.

| Verb | What it does | Notes |
|---|---|---|
| `Click(control)` / `Click("name")` | `control.PerformClick()` | Runs `Click` handlers. Does **not** toggle a `CheckBox` or `RadioButton` — use `Check`. |
| `Click(toolStripItem)` | `item.PerformClick()` | Refused if the item is disabled. Visibility of items is not checked. |
| `DoubleClick(control)` | raises `OnDoubleClick` | |
| `Type(control, text)` | sets `control.Text` | Replaces the whole text, as typing over a selection would; raises `TextChanged`. Refused on a read-only `TextBoxBase`. |
| `PressKey(control, keys, keyChar?)` | raises `KeyDown`, `KeyPress` (only if `keyChar` is given and the key was not suppressed), `KeyUp` | With `Form.KeyPreview`, the form sees each event first and can stop it with `Handled`, as in WinForms. Does not insert text. |
| `Select(comboBox, index)` / `Select(comboBox, "text")` | sets `SelectedIndex` | Text matches `GetItemText(item)` exactly; throws `ArgumentException` if no item matches. |
| `Select(listBox, index)` / `Select(listBox, "text")` | sets `SelectedIndex` | As above. |
| `Check(checkBox, isChecked = true)` | sets `Checked` | Raises `CheckedChanged` when the state changes. |
| `Check(radioButton)` | sets `Checked = true` | Unchecks the others in the group. |
| `RaiseEvent(control, "OnXxx", args)` | calls the protected `OnXxx` raiser by reflection | The escape hatch for everything else (`OnMouseDown`, `OnMouseWheel`, `OnValidating`, ...). The runtime type of `args` selects the overload. **No interactability check.** Throws `MissingMethodException` if there is no such raiser. |

Examples:

```csharp
ui.PressKey(ui.Find<TextBox>("searchBox"), Keys.Enter);
ui.PressKey(ui.Form, Keys.Control | Keys.S);                       // a form-level shortcut (KeyPreview)
ui.Click(ui.FindItem("fileSaveMenuItem"));
ui.RaiseEvent(grid, "OnMouseDown", new MouseEventArgs(MouseButtons.Right, 1, 20, 30, 0));
```

Exceptions thrown inside a handler are rethrown as themselves (not wrapped in
`TargetInvocationException`), including for `PressKey` and `RaiseEvent`.

### The interactability rule

A user cannot click a button inside a disabled panel or on a hidden tab page, so neither can a test. The
driver walks from the control up through its parents and refuses the verb with an
`InvalidOperationException` naming the culprit:

```
Cannot click 'deleteButton': its parent 'adminPanel' is disabled.
```

This catches a real class of bug — "the handler works, but the user can never reach it". Set
`RequireInteractable = false` only for a suite that deliberately exercises handlers in isolation.

## Message boxes

With `InterceptMessageBoxes` on (the default), every `MessageBox.Show` call is **recorded** and **answered
immediately**; no dialog is created. The answer is, in order of precedence:

1. the next answer queued with `UiAutomation.MessageBoxes.Enqueue(...)`;
2. otherwise `Responder(call)`, if set;
3. otherwise the box's default button — see the caveat in [Limits](#limits): today this is effectively
   always the **first** button.

```csharp
var boxes = UiAutomation.MessageBoxes;
boxes.Clear();                                    // forget earlier calls, answers and Responder

boxes.Enqueue(DialogResult.No);                   // "Save changes?" -> No
ui.Click("closeButton");

Assert.Single(boxes.Calls);
Assert.Equal(MessageBoxButtons.YesNoCancel, boxes.Last!.Buttons);
Assert.Equal(DialogResult.No, boxes.Last.Result);

// A rule instead of a sequence:
boxes.Responder = call => call.Caption == "Delete" ? DialogResult.Cancel : DialogResult.OK;
```

Each `MessageBoxCall` exposes `Owner`, `Text`, `Caption`, `Buttons`, `Icon`, `DefaultButton`, `Options`
and `Result`.

The recorder is **process-wide**, not per form or per test: call `Clear()` at the start of each test that
inspects calls, or answers queued by one test leak into the next.

## Waiting

Input verbs return after a fixed number of pump passes. Anything that completes later — a `Timer` tick, an
`await` continuation, a `BeginInvoke`, a resize that GTK applies on a later frame — needs an explicit wait:

```csharp
ui.Click("loadButton");
ui.WaitUntil(() => ui.Find<DataGridView>("ordersGrid").Rows.Count > 0,
             timeout: TimeSpan.FromSeconds(10),
             description: "orders to load");
```

`WaitUntil` pumps the main loop, evaluates the condition on the UI thread, and sleeps 5 ms between passes
so GLib timeouts become due without spinning a core. On timeout it throws `TimeoutException` with the
description.

**Wait for the condition, never for a count of pumps.** `ui.Pump(40)` followed by an assertion is a flaky
test: GTK schedules layout on frame ticks, and how many passes that takes varies by machine and load.
`GtkWinFormsApp.UITests` had exactly this bug (a resize test that failed 2 runs in 3) until it was
rewritten to `WaitUntil`. Use `Pump()` only to let already-queued work drain, such as before reading a
property GTK updates asynchronously.

Do not block the UI thread: `Thread.Sleep`, `Task.Wait()` or `.Result` in a test starve the loop that
would complete the work.

## Forms that open other forms

A handler that calls `new DetailsForm().Show()` opens a non-modal window. Get a driver for it with
`WaitForForm`:

```csharp
using var ui = UiAutomation.Show(() => new MainForm());
ui.Click("openDetailsButton");

var details = UiAutomation.WaitForForm<DetailsForm>(f => f.OrderId == 42);
details.Type(details.Find<TextBox>("noteTextBox"), "Rush");
details.Click("okButton");
```

`WaitForForm` searches `Application.OpenForms` and returns a **non-owning** driver; the owning driver of
`MainForm` destroys the window when it is disposed.

A handler that calls `ShowDialog()` runs a nested modal loop, and the test's next line does not run until
that dialog closes — which, in a test, is never. Only `MessageBox` is intercepted. Options:

- make the dialog injectable (a factory or interface the test replaces), or
- test the dialog form on its own with `UiAutomation.Show`, and test the caller's handling of each
  `DialogResult` separately.

## Screenshots and baselines

### Capturing

| Call | Captures |
|---|---|
| `UiCapture.Capture(control)` / `ui.Capture(control)` | The control at its allocated size, in logical pixels. |
| `UiCapture.Capture(form)` / `ui.Capture()` | The form's **client area**. The title bar is left out because GTK draws it differently depending on window focus. |
| `UiCapture.CaptureWindow(form)` / `ui.CaptureWindow()` | The whole window, title bar included. |

Each returns a new 32-bit ARGB Gtk.Drawing `Bitmap`; dispose it.

Captures are rendered **in-process from the widget's GTK 4 render nodes** with GTK's software (Cairo)
renderer, taken right after a paint. They therefore need no window focus, are not affected by overlapping
windows, and do not depend on the GPU. They **do** depend on the machine's fonts and GTK theme.

### Comparing

`ImageComparer.Compare(expected, actual, tolerance)` returns an `ImageComparisonResult` (dispose it) with
`Match`, `DifferentPixels`, `TotalPixels`, `MaxChannelDelta`, a one-line `Message`, and a `Diff` image
(differing pixels in red over a faded copy of the actual image; `null` when the images match or differ in
size). A size difference is always a mismatch.

| Tolerance | Per-channel delta | Pixels allowed to differ |
|---|---|---|
| `ImageTolerance.Default` | ≤ 12/255 | ≤ 0.5 % |
| `ImageTolerance.Exact` | 0 | 0 |
| `new ImageTolerance { MaxChannelDelta = ..., MaxDifferentPixelRatio = ... }` | custom | custom |

The default absorbs anti-aliasing noise but not a moved, resized or recolored control.

### Gating against committed baselines

```csharp
private static readonly BaselineStore Baselines = BaselineStore.InProjectDirectory();   // <project>/Baselines

[Fact]
public void MainForm_LooksRight()
{
    using var ui = UiAutomation.Show(() => new MainForm());
    Baselines.Verify(ui.Form, "MainForm/startup");        // Baselines/MainForm/startup.png
}
```

`InProjectDirectory(relative = "Baselines")` walks up from the test output directory to the first folder
containing a `.csproj`, so baselines live in the source tree and are committed. Use
`new BaselineStore(baselineDir, artifactsDir?)` for any other layout.

`Verify` behaves as follows:

| Situation | Result |
|---|---|
| Baseline exists and matches within `Tolerance` | passes |
| Baseline exists and differs | writes `<name>.actual.png` and `<name>.diff.png` to `ArtifactsDirectory` (default `<output>/TestResults/Screenshots`) and throws `BaselineMismatchException` (`BaselinePath`, `ActualPath`, `DiffPath`) |
| Baseline missing, `CI` not set | writes the capture as the baseline and **passes** (seeding) |
| Baseline missing, `CI=1`/`true` | writes `.actual.png` and **fails** — an unreviewed image never passes in CI |
| `UPDATE_UI_BASELINES=1`/`true` | overwrites the baseline and passes |

`Update`, `SeedMissing` and `Tolerance` can also be set per store in code.

### The baseline workflow

1. Write the test and run it locally: the baseline is seeded.
2. **Open the PNG and look at it** before committing. A seeded baseline is only as good as the review it
   got. (The sample suite once committed blank PNGs from an unfinished capture path, and every screenshot
   check passed whatever was drawn.)
3. After an intentional visual change:

   ```sh
   UPDATE_UI_BASELINES=1 dotnet test MyApp.UITests
   git diff --stat -- MyApp.UITests/Baselines     # review every changed image, then commit
   ```

4. On an unexpected failure, open the `.diff.png` in `TestResults/Screenshots` first.

### Making screenshots deterministic

- **Pin the culture** in `UiAutomationOptions`.
- **Pin time-dependent state** before the shot: a `DateTimePicker` defaulting to `DateTime.Now`, a clock
  label, a grid seeded from the time. If a form cannot be made static, gate it with state assertions
  instead of a screenshot.
- **Ship the same theme and resources** the application loads next to the test binaries.
- **Keep baselines per platform** (for example `BaselineStore.InProjectDirectory($"Baselines/{os}")`), and
  generate them on the kind of machine that verifies them. Fonts and theme differ between Windows, Linux
  distributions and macOS; a baseline from one will not match another.
- **Capture the smallest thing that proves the point.** `Baselines.Verify(ui.Find("chartPanel"), ...)` is
  less brittle than the whole form.

## Running in CI

GTK needs a display, even though capture never reads the screen.

| OS | How |
|---|---|
| Windows | An interactive desktop session (hosted GitHub runners have one). |
| Linux | A virtual X server: `GDK_BACKEND=x11 xvfb-run -a dotnet test ...`. GTK 4 on headless runners has no Wayland, hence `GDK_BACKEND=x11`. |
| macOS | **Not supported yet.** GTK must run on the process main thread, which `dotnet test` keeps for its own host, and `GtkTestFramework` does not cover that case. |

Set `CI=true` (GitHub Actions does this already) so a missing baseline fails instead of being seeded. Upload
`**/TestResults/Screenshots/**` as a build artifact on failure so the actual and diff images can be
inspected.

This repository's [build-and-test workflow](../.github/workflows/build-and-test.yml) runs
`Gtk.Windows.Forms.Automation.Tests` this way on Windows and Linux and skips it on macOS with a notice.
`GtkWinFormsApp.UITests` is not run in CI: its baselines were generated on a Windows desktop.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| Host aborts with `OleInitialize failed ... incompatible apartment model` | Tests run on MTA thread-pool threads. Add the `[assembly: TestFramework(...)]` line. |
| `InvalidOperationException: GTK is single-threaded: UI automation must run on the thread that initialized it` | A call came from another thread: `Task.Run`, `ConfigureAwait(false)`, a parallel test collection, a `System.Threading.Timer` callback. Stay on the test thread; disable parallelization. |
| `UiAutomation.Initialize() has not been called.` | Call it (or `UiAutomation.Show`, which initializes with defaults) before `Find`, `Pump`, `MessageBoxes`, ... |
| `MessageBox interception is disabled` | `InterceptMessageBoxes = false` was set on the first `Initialize`; later options are ignored. |
| A test hangs | A modal `ShowDialog()` from a handler, or a blocking wait on the UI thread. See [Forms that open other forms](#forms-that-open-other-forms) and [Waiting](#waiting). |
| `ControlNotFoundException` | Read the listed names: wrong case, wrong type parameter, the control is created lazily (wait for it), or it lives in another form. |
| `Cannot click 'x': its parent 'y' is disabled.` | Working as intended — the user could not click it either. Fix the test's preconditions or the application. |
| `CheckBox` state unchanged after `Click` | `Click` raises the click handlers only; use `Check`. |
| `PressKey` did not change the text | By design: it raises key events only. Use `Type`. |
| `'x' has no allocated size (0x0); is it shown and visible?` | Capturing a control that is hidden, on an unselected tab page, or not yet laid out. Make it visible and `WaitUntil` it has a size. |
| Screenshot fails on another machine only | Different fonts or theme. Regenerate baselines per platform where they are verified. |
| Screenshot fails intermittently | Time-dependent content, or a capture taken before an asynchronous update; `WaitUntil` the state, then capture. |
| Messages from one test show up in the next | The message-box recorder is process-wide: `UiAutomation.MessageBoxes.Clear()` at the start of the test. |

## Limits

- **No OS-level input.** Events are raised at the WinForms level. Hit-testing, focus traversal, GTK
  gesture handling and text insertion by keystrokes are not exercised. `PressKey` raises
  `KeyDown`/`KeyPress`/`KeyUp`, but GTK does not insert the character into an entry.
- **`Click` does not toggle check boxes or radio buttons**; GTK does that on a real click. Use `Check`.
- **Only `MessageBox` is intercepted.** `Form.ShowDialog()` and the common dialogs (`OpenFileDialog`,
  `ColorDialog`, ...) still block.
- **Default button of an unanswered box.** The library's static `MessageBox.Show` overloads do not yet pass
  `defaultButton` (or `options`) on to an installed message box, so an unanswered box falls back to its
  **first** button even when the application sets `MessageBoxDefaultButton.Button2`. Queue explicit answers
  for any box whose answer matters, such as a delete confirmation.
- **Tool strip item visibility** is not checked by `Click(ToolStripItem)`; only `Enabled` is.
- **No macOS runner** in `Gtk.Windows.Forms.Automation.Xunit` yet.
- **Baselines are machine-dependent** (fonts, theme), though not GPU- or focus-dependent.

## Examples in this repository

- [Gtk.Windows.Forms.Automation.Tests](../Gtk.Windows.Forms.Automation.Tests) — the packages' own tests:
  one small behaviour per test (lookup, interactability, key routing, message boxes, capture, baselines).
  [TestUi.cs](../Gtk.Windows.Forms.Automation.Tests/TestUi.cs) shows a minimal shared setup.
- [GtkWinFormsApp.UITests](../GtkWinFormsApp.UITests/README.md) — an end-to-end suite over the real sample
  application: a thin `UiDriver.Run(create, script)` / `UiContext` wrapper, designer fields reached through
  `InternalsVisibleTo`, and committed baselines per form and checkpoint.
