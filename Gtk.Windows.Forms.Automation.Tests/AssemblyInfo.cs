using Gtk.Windows.Forms.Automation.Xunit;

// Every test runs on the one GTK thread the adapter owns, and serially: GTK is single-threaded.
[assembly: TestFramework(GtkTestFramework.TypeName, GtkTestFramework.AssemblyName)]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
