# Gtk.Windows.Forms.Automation.Xunit

An xUnit v2 test framework for `Gtk.Windows.Forms.Automation`: every test of the assembly runs on **one
dedicated thread** (an STA on Windows, which GDK 4 requires), and GTK is initialized before the first test.

Add it to the test project, then activate it once (any `.cs` file):

```csharp
using Gtk.Windows.Forms.Automation.Xunit;

[assembly: TestFramework(GtkTestFramework.TypeName, GtkTestFramework.AssemblyName)]
// Recommended: test collections otherwise interleave at await points on the shared GTK thread.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

Without it, xUnit runs tests on MTA thread-pool threads: GTK calls then come from several threads and, on
Windows, the host aborts with `OleInitialize failed ... incompatible apartment model`.

On macOS, GTK must run on the process main thread, which `dotnet test` gives to its own host. This
framework does not cover that case yet: run GTK tests on Windows and Linux (under `xvfb-run` on a
headless machine).
