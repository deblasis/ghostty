namespace Ghostty.Core.Shell;

/// <summary>
/// Output of <see cref="ShellDetector.Detect(string)"/>. Structured so
/// callers do not have to switch on the enum just to decide whether to
/// log an unrecognized binary: <see cref="IsKnown"/> is the signal.
/// </summary>
/// <param name="Capability">VT/Console-API verdict.</param>
/// <param name="NormalizedFileName">
/// Lowercase leaf filename (e.g. <c>"pwsh.exe"</c>). Empty string when
/// the input has no file component. Kept as a stable value so log lines
/// do not drift with casing (<c>PWSH.EXE</c> and <c>pwsh.exe</c> both
/// log as <c>pwsh.exe</c>).
/// </param>
public readonly record struct ShellDetectionResult(
    ShellCapability Capability,
    string NormalizedFileName)
{
    /// <summary>
    /// True if and only if the filename matched the known-shell table
    /// (equivalently, <see cref="Capability"/> is not <see cref="ShellCapability.Unknown"/>).
    /// Computed from <see cref="Capability"/> so the two signals cannot drift.
    /// </summary>
    public bool IsKnown => Capability != ShellCapability.Unknown;
}
