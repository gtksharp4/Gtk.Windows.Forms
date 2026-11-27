namespace Gtk.Windows.Forms.Automation;

// Inside the namespace on purpose: types of the enclosing GtkSharp "Gtk" namespace (Gtk.ComboBox,
// Gtk.Application, ...) would otherwise win over System.Windows.Forms in name lookup.
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

/// <summary>
/// Gates screenshots against committed baseline images: <c>&lt;BaselineDirectory&gt;/&lt;name&gt;.png</c>.
/// <list type="bullet">
/// <item><description>Verify (default): compare within <see cref="Tolerance"/>; on a mismatch write
/// <c>.actual.png</c> and <c>.diff.png</c> to <see cref="ArtifactsDirectory"/> and throw
/// <see cref="BaselineMismatchException"/>.</description></item>
/// <item><description>Update (<see cref="Update"/>, or env <c>UPDATE_UI_BASELINES=1</c>): overwrite the baseline.</description></item>
/// <item><description>Missing baseline: seeded and passed when <see cref="SeedMissing"/> is set (the default
/// except under CI, where a missing baseline fails so an unreviewed image never passes silently).</description></item>
/// </list>
/// </summary>
public sealed class BaselineStore
{
    /// <summary>The environment variable that switches every store to update mode when set to <c>1</c> or <c>true</c>.</summary>
    public const string UpdateEnvironmentVariable = "UPDATE_UI_BASELINES";

    /// <summary>Initializes a new instance of the <see cref="BaselineStore"/> class.</summary>
    /// <param name="baselineDirectory">Where the committed baselines live (normally in the source tree).</param>
    /// <param name="artifactsDirectory">Where actual/diff images go on a mismatch; defaults to <c>TestResults/Screenshots</c> under the output directory.</param>
    public BaselineStore(string baselineDirectory, string? artifactsDirectory = null)
    {
        if (string.IsNullOrEmpty(baselineDirectory))
        {
            throw new ArgumentException("A baseline directory is required.", nameof(baselineDirectory));
        }

        BaselineDirectory = Path.GetFullPath(baselineDirectory);
        ArtifactsDirectory = Path.GetFullPath(
            artifactsDirectory ?? Path.Combine(AppContext.BaseDirectory, "TestResults", "Screenshots"));
        Update = IsSet(Environment.GetEnvironmentVariable(UpdateEnvironmentVariable));
        SeedMissing = !IsSet(Environment.GetEnvironmentVariable("CI"));
    }

    /// <summary>Gets the directory holding the baselines.</summary>
    public string BaselineDirectory { get; }

    /// <summary>Gets the directory mismatch artifacts are written to.</summary>
    public string ArtifactsDirectory { get; }

    /// <summary>Gets or sets the comparison tolerance.</summary>
    public ImageTolerance Tolerance { get; set; } = ImageTolerance.Default;

    /// <summary>Gets or sets a value indicating whether <see cref="Verify(Bitmap, string)"/> overwrites baselines instead of comparing.</summary>
    public bool Update { get; set; }

    /// <summary>Gets or sets a value indicating whether a missing baseline is written and passes, instead of failing.</summary>
    public bool SeedMissing { get; set; }

    /// <summary>
    /// Returns a store for <paramref name="relativeDirectory"/> inside the test project's source directory,
    /// found by walking up from the output directory to the first folder that holds a <c>.csproj</c>.
    /// </summary>
    /// <param name="relativeDirectory">The baseline folder relative to the project directory.</param>
    /// <returns>The store.</returns>
    /// <exception cref="DirectoryNotFoundException">No project directory above the output directory.</exception>
    public static BaselineStore InProjectDirectory(string relativeDirectory = "Baselines")
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (dir.EnumerateFiles("*.csproj").Any())
            {
                return new BaselineStore(Path.Combine(dir.FullName, relativeDirectory));
            }
        }

        throw new DirectoryNotFoundException(
            $"No directory containing a .csproj above '{AppContext.BaseDirectory}'; pass the baseline directory explicitly.");
    }

    /// <summary>Captures <paramref name="control"/> and verifies it against the baseline <paramref name="name"/>.</summary>
    /// <param name="control">The control to capture.</param>
    /// <param name="name">The baseline name, e.g. <c>"MainForm/startup"</c> (no extension).</param>
    public void Verify(Control control, string name)
    {
        using var actual = UiCapture.Capture(control);
        Verify(actual, name);
    }

    /// <summary>Verifies <paramref name="actual"/> against the baseline <paramref name="name"/> (see the class remarks).</summary>
    /// <param name="actual">The captured image.</param>
    /// <param name="name">The baseline name, e.g. <c>"MainForm/startup"</c> (no extension).</param>
    /// <exception cref="BaselineMismatchException">The image differs, or the baseline is missing and seeding is off.</exception>
    public void Verify(Bitmap actual, string name)
    {
        if (actual == null)
        {
            throw new ArgumentNullException(nameof(actual));
        }

        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("A baseline name is required.", nameof(name));
        }

        var relative = name.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar) + ".png";
        var baselinePath = Path.Combine(BaselineDirectory, relative);

        if (Update || (SeedMissing && !File.Exists(baselinePath)))
        {
            Save(actual, baselinePath);
            return;
        }

        var actualPath = Path.Combine(ArtifactsDirectory, Path.ChangeExtension(relative, ".actual.png"));
        if (!File.Exists(baselinePath))
        {
            Save(actual, actualPath);
            throw new BaselineMismatchException(name, baselinePath, actualPath, null,
                $"No baseline '{name}' at {baselinePath}. The capture was written to {actualPath}; review it and " +
                $"run with {UpdateEnvironmentVariable}=1 to accept it.");
        }

        using var expected = new Bitmap(baselinePath);
        using var result = ImageComparer.Compare(expected, actual, Tolerance);
        if (result.Match)
        {
            return;
        }

        Save(actual, actualPath);
        string? diffPath = null;
        if (result.Diff != null)
        {
            diffPath = Path.Combine(ArtifactsDirectory, Path.ChangeExtension(relative, ".diff.png"));
            Save(result.Diff, diffPath);
        }

        throw new BaselineMismatchException(name, baselinePath, actualPath, diffPath,
            $"Screenshot '{name}' differs from its baseline: {result.Message}. Actual{(diffPath != null ? " and diff" : string.Empty)} " +
            $"written to {Path.GetDirectoryName(actualPath)}. If the new look is intended, re-run with " +
            $"{UpdateEnvironmentVariable}=1 and commit the updated baseline.");
    }

    private static void Save(Bitmap image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        image.Save(file, ImageFormat.Png);
    }

    private static bool IsSet(string? value)
        => value != null && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Thrown by <see cref="BaselineStore.Verify(Bitmap, string)"/> when a capture does not match its baseline.</summary>
public sealed class BaselineMismatchException : Exception
{
    internal BaselineMismatchException(string name, string baselinePath, string actualPath, string? diffPath, string message)
        : base(message)
    {
        BaselineName = name;
        BaselinePath = baselinePath;
        ActualPath = actualPath;
        DiffPath = diffPath;
    }

    /// <summary>Gets the baseline name.</summary>
    public string BaselineName { get; }

    /// <summary>Gets the path of the committed baseline.</summary>
    public string BaselinePath { get; }

    /// <summary>Gets the path the actual capture was written to.</summary>
    public string ActualPath { get; }

    /// <summary>Gets the path of the diff image, or <see langword="null"/> when none was produced (missing baseline or size change).</summary>
    public string? DiffPath { get; }
}
