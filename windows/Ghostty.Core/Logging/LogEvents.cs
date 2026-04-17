namespace Ghostty.Core.Logging;

/// <summary>
/// EventId constants for <c>Ghostty.Core</c>-resident components.
/// Id ranges are disjoint per component. Each id appears in exactly two
/// places: the definition here and one <c>[LoggerMessage(EventId = ...)]</c>
/// attribute - `grep` for the constant name should return exactly two hits.
/// </summary>
internal static class LogEvents
{
    // 1000-1099: Config
    internal static class Config
    {
        // Populated in Task 2.3 / 2.4.
    }

    // 1100-1199: Frecency / command history
    internal static class Frecency
    {
        // Populated in Task 2.2.
    }
}
