using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Gtk.Windows.Forms.Automation.Xunit;

/// <summary>
/// xUnit v2 test framework that runs every test case of the assembly on one dedicated thread, with GTK
/// initialized before the first test. GTK must only be touched from one thread, and on Windows GDK 4 calls
/// <c>OleInitialize</c>, which needs that thread to be an STA; the default runner uses MTA thread-pool
/// threads and the host aborts with "OleInitialize failed". Activate it once per test assembly:
/// <code>[assembly: TestFramework(GtkTestFramework.TypeName, GtkTestFramework.AssemblyName)]</code>
/// Async tests stay on that thread: their continuations are queued back to it.
/// </summary>
public sealed class GtkTestFramework : XunitTestFramework
{
    /// <summary>The full type name to pass to <c>[assembly: TestFramework]</c>.</summary>
    public const string TypeName = "Gtk.Windows.Forms.Automation.Xunit.GtkTestFramework";

    /// <summary>The assembly name to pass to <c>[assembly: TestFramework]</c>.</summary>
    public const string AssemblyName = "Gtk.Windows.Forms.Automation.Xunit";

    /// <summary>Initializes a new instance of the <see cref="GtkTestFramework"/> class.</summary>
    /// <param name="messageSink">The diagnostic message sink supplied by xUnit.</param>
    public GtkTestFramework(IMessageSink messageSink)
        : base(messageSink)
    {
    }

    /// <inheritdoc />
    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName)
        => new GtkTestFrameworkExecutor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);
}

/// <summary>Runs the assembly's test cases on the single GTK thread (see <see cref="GtkTestFramework"/>).</summary>
public sealed class GtkTestFrameworkExecutor : XunitTestFrameworkExecutor
{
    /// <summary>Initializes a new instance of the <see cref="GtkTestFrameworkExecutor"/> class.</summary>
    /// <param name="assemblyName">The test assembly name.</param>
    /// <param name="sourceInformationProvider">The source information provider.</param>
    /// <param name="diagnosticMessageSink">The diagnostic message sink.</param>
    public GtkTestFrameworkExecutor(AssemblyName assemblyName, ISourceInformationProvider sourceInformationProvider,
        IMessageSink diagnosticMessageSink)
        : base(assemblyName, sourceInformationProvider, diagnosticMessageSink)
    {
    }

    /// <inheritdoc />
    protected override void RunTestCases(IEnumerable<IXunitTestCase> testCases, IMessageSink executionMessageSink,
        ITestFrameworkExecutionOptions executionOptions)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var previous = SynchronizationContext.Current;
            var context = new SingleThreadSyncContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                // Initialize GTK as the first queued action, before any test: constructing a widget needs the
                // GTK type system, and a test that builds a control without calling UiAutomation.Initialize
                // would otherwise crash the host natively. It is queued rather than called here because
                // Application.Init waits on GApplication registration, which completes only while the queue
                // is being drained. Idempotent, so UiAutomation.Initialize later only applies its options.
                context.Post(_ => InitializeGtk(), null);

                using var runner = new XunitTestAssemblyRunner(TestAssembly, testCases, DiagnosticMessageSink,
                    executionMessageSink, executionOptions);
                var task = runner.RunAsync();
                task.ContinueWith(_ => context.Complete(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                context.Run();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        });

        thread.Name = "GTK-Test-Thread";
        thread.IsBackground = false;

        // STA is a Windows-only COM concept; elsewhere SetApartmentState throws, and one thread is enough.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            thread.SetApartmentState(ApartmentState.STA);
        }

        thread.Start();
        thread.Join();

        if (failure != null)
        {
            throw failure;
        }
    }

    private void InitializeGtk()
    {
        try
        {
            global::System.Windows.Forms.Application.Init();
        }
        catch (Exception ex)
        {
            // Report, do not throw: each test that needs GTK then fails with its own clear error instead of
            // the whole run aborting with no results.
            DiagnosticMessageSink.OnMessage(new DiagnosticMessage("GTK initialization failed: " + ex));
        }
    }

    /// <summary>Queues posted callbacks and runs them on the thread that calls <see cref="Run"/>.</summary>
    private sealed class SingleThreadSyncContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        // Only ever called on the owning thread in practice; running inline avoids a self-deadlock.
        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public void Run()
        {
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        }

        public void Complete() => _queue.CompleteAdding();
    }
}
