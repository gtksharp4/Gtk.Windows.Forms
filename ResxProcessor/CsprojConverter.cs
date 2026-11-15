using System.Text;
using System.Text.RegularExpressions;

namespace ResxProcessor;

/// <summary>
/// Converts a consumer WinForms project to build on the GTK-backed compatibility layer: strips the
/// Windows platform moniker from the target framework(s) (<c>net10.0-windows</c> becomes
/// <c>net10.0</c>) and removes the real Windows Desktop WinForms opt-in
/// (<c>&lt;UseWindowsForms&gt;true&lt;/UseWindowsForms&gt;</c>).
/// </summary>
/// <remarks>
/// The edits are textual and minimal so the rest of the project file is left byte-for-byte unchanged,
/// and they are idempotent: a project that is already cross-platform is not modified. A build target
/// cannot change the framework of the build already in progress, so the rewrite takes effect from the
/// consumer's next evaluation.
/// </remarks>
internal static class CsprojConverter
{
    // A <TargetFramework>/<TargetFrameworks> element (optional attributes), capturing the element name
    // and its text value. A backreference ties the closing tag to the same name.
    private static readonly Regex TargetFrameworkElement = new(
        @"<(TargetFrameworks?)(?:\s[^>]*)?>(?<value>[^<]*)</\1>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // The platform moniker suffix of a TFM, e.g. "-windows" or "-windows10.0.19041.0".
    private static readonly Regex WindowsMoniker = new(
        @"-windows[0-9.]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // A <UseWindowsForms>true</UseWindowsForms> element, with its leading indentation and trailing
    // newline so the whole line is removed cleanly. [^\S\r\n] is horizontal whitespace only.
    private static readonly Regex UseWindowsFormsTrue = new(
        @"[^\S\r\n]*<UseWindowsForms>\s*true\s*</UseWindowsForms>[^\S\r\n]*(\r?\n)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Applies the conversion to project-file text and reports whether anything changed.
    /// </summary>
    /// <param name="content">The raw <c>.csproj</c> text.</param>
    /// <param name="changed">Set to <see langword="true"/> when the result differs from the input.</param>
    /// <returns>The converted text (equal to <paramref name="content"/> when nothing matched).</returns>
    internal static string Transform(string content, out bool changed)
    {
        var result = TargetFrameworkElement.Replace(content, match =>
        {
            var value = match.Groups["value"].Value;
            if (!WindowsMoniker.IsMatch(value))
            {
                // No Windows moniker to strip (singular or multi-target, already cross-platform):
                // leave the element's text exactly as written.
                return match.Value;
            }

            // Both <TargetFramework> and <TargetFrameworks> are handled: split on ';' (a no-op for the
            // singular form), strip the moniker from each entry, then drop blanks and de-duplicate so a
            // list like "net10.0-windows;net10.0" collapses to "net10.0" instead of a duplicate TFM.
            var name = match.Groups[1].Value;
            var frameworks = string.Join(";", value
                .Split(';')
                .Select(tfm => WindowsMoniker.Replace(tfm.Trim(), string.Empty))
                .Where(tfm => tfm.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase));
            return $"<{name}>{frameworks}</{name}>";
        });

        result = UseWindowsFormsTrue.Replace(result, string.Empty);

        changed = !string.Equals(result, content, StringComparison.Ordinal);
        return result;
    }

    /// <summary>
    /// Converts the project file in place, preserving its original UTF-8 byte-order-mark state.
    /// </summary>
    /// <param name="csprojPath">The absolute path to the <c>.csproj</c>.</param>
    /// <returns><see langword="true"/> when the file was rewritten; otherwise <see langword="false"/>.</returns>
    internal static bool TryConvertFile(string csprojPath)
    {
        var bytes = File.ReadAllBytes(csprojPath);
        var hadBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

        var content = File.ReadAllText(csprojPath);
        var result = Transform(content, out var changed);
        if (changed)
        {
            File.WriteAllText(csprojPath, result, new UTF8Encoding(encoderShouldEmitUTF8Identifier: hadBom));
        }

        return changed;
    }
}
