namespace FamilyHub.Infrastructure;

/// <summary>
/// Resolves a data file, preferring a local override over the copy shipped with the build.
/// </summary>
/// <remarks>
/// The dashboard auto-updates from git, and every update replaces the published output directory.
/// Anything hand-edited there — a corrected school calendar, adjusted bell times — would be lost.
/// So an editable copy in the user's data directory always wins, and the shipped file is only the
/// default used until someone overrides it.
/// </remarks>
public static class DataFile
{
    public static string UserDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FamilyHub");

    /// <summary>Full path to use for <paramref name="fileName"/>: the user's copy if present, else the shipped one.</summary>
    public static string Resolve(string fileName)
    {
        var overridePath = Path.Combine(UserDirectory, fileName);
        return File.Exists(overridePath) ? overridePath : Path.Combine(AppContext.BaseDirectory, fileName);
    }

    /// <summary>True when the file in use is a local override rather than the shipped default.</summary>
    public static bool IsOverridden(string fileName) => File.Exists(Path.Combine(UserDirectory, fileName));
}
