using System.Drawing;
using Gtk.Windows.Forms.Automation;

namespace GtkWinForms.Automation.Tests;

public sealed class BaselineStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "baseline-tests-" + Guid.NewGuid().ToString("N"));

    private string Baselines => Path.Combine(_root, "Baselines");

    private string Artifacts => Path.Combine(_root, "Artifacts");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public void AMissingBaseline_IsSeeded_WhenSeedingIsOn()
    {
        TestUi.Init();
        var store = new BaselineStore(Baselines, Artifacts) { SeedMissing = true, Update = false };
        using var image = TestUi.Solid(6, 6, Color.Green);

        store.Verify(image, "Main/startup");

        Assert.True(File.Exists(Path.Combine(Baselines, "Main", "startup.png")));
    }

    [Fact]
    public void AMissingBaseline_Fails_WhenSeedingIsOff_AndKeepsTheCapture()
    {
        TestUi.Init();
        var store = new BaselineStore(Baselines, Artifacts) { SeedMissing = false, Update = false };
        using var image = TestUi.Solid(6, 6, Color.Green);

        var ex = Assert.Throws<BaselineMismatchException>(() => store.Verify(image, "Main/startup"));

        Assert.False(File.Exists(Path.Combine(Baselines, "Main", "startup.png")));
        Assert.True(File.Exists(ex.ActualPath));
        Assert.Null(ex.DiffPath);
    }

    [Fact]
    public void AMatchingImage_Passes()
    {
        TestUi.Init();
        var store = new BaselineStore(Baselines, Artifacts) { SeedMissing = true, Update = false };
        using var image = TestUi.Solid(6, 6, Color.Green);
        store.Verify(image, "same");
        store.SeedMissing = false;

        using var again = TestUi.Solid(6, 6, Color.Green);
        store.Verify(again, "same");
    }

    [Fact]
    public void AChangedImage_Fails_AndWritesActualAndDiff()
    {
        TestUi.Init();
        var store = new BaselineStore(Baselines, Artifacts) { SeedMissing = true, Update = false };
        using var original = TestUi.Solid(6, 6, Color.Green);
        store.Verify(original, "Main/startup");

        using var changed = TestUi.Solid(6, 6, Color.Red);
        var ex = Assert.Throws<BaselineMismatchException>(() => store.Verify(changed, "Main/startup"));

        Assert.Equal(Path.Combine(Artifacts, "Main", "startup.actual.png"), ex.ActualPath);
        Assert.Equal(Path.Combine(Artifacts, "Main", "startup.diff.png"), ex.DiffPath);
        Assert.True(File.Exists(ex.ActualPath));
        Assert.True(File.Exists(ex.DiffPath));
        Assert.Contains("UPDATE_UI_BASELINES=1", ex.Message);
    }

    [Fact]
    public void UpdateMode_OverwritesTheBaseline()
    {
        TestUi.Init();
        var store = new BaselineStore(Baselines, Artifacts) { SeedMissing = true, Update = false };
        using var original = TestUi.Solid(6, 6, Color.Green);
        store.Verify(original, "x");
        using var changed = TestUi.Solid(6, 6, Color.Red);

        store.Update = true;
        store.Verify(changed, "x");
        store.Update = false;
        store.SeedMissing = false;

        store.Verify(changed, "x");
        using var stored = new Bitmap(Path.Combine(Baselines, "x.png"));
        Assert.Equal(Color.FromArgb(255, 255, 0, 0), stored.GetPixel(0, 0));
    }
}
