using Microsoft.Extensions.Logging;

namespace Ghostty.Core.Logging;

/// <summary>
/// Holds the current <see cref="LoggerFilterOptions"/> behind a
/// <c>volatile</c> field so <see cref="LoggingBootstrap.ApplyFilters"/>
/// can atomically swap in a rebuilt rules set on config reload without
/// tearing down the factory or providers.
/// </summary>
internal sealed class FilterState
{
    // Reference writes are atomic in .NET. `volatile` additionally
    // prevents the filter delegate from observing a stale reference.
    private volatile LoggerFilterOptions _options;

    public FilterState(LoggerFilterOptions initial) => _options = initial;

    public LoggerFilterOptions Options => _options;

    public void Replace(LoggerFilterOptions next) => _options = next;
}
