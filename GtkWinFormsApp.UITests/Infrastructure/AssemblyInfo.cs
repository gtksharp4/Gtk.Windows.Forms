using Gtk.Windows.Forms.Automation.Xunit;

// GTK is single-threaded, and on Windows GDK 4 needs that thread to be an STA: run every test on the one
// thread the automation adapter owns, and never two at once.
[assembly: TestFramework(GtkTestFramework.TypeName, GtkTestFramework.AssemblyName)]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
