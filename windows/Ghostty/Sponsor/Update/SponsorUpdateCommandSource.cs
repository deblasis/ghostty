#if DEBUG
using System.Collections.Generic;
using Ghostty.Commands;
using Ghostty.Core.Sponsor.Update;

namespace Ghostty.Sponsor.Update;

/// <summary>
/// Debug-only command source that exposes every simulator transition as
/// a palette entry under the "Custom" category. Exists only so manual
/// QA can drive the update UI without keyboard shortcuts. Production
/// sponsor builds compile this out via <c>#if DEBUG</c>.
/// </summary>
internal sealed class SponsorUpdateCommandSource : ICommandSource
{
    private readonly UpdateSimulator _simulator;
    private readonly List<CommandItem> _items;

    public SponsorUpdateCommandSource(UpdateSimulator simulator)
    {
        _simulator = simulator;
        _items = Build();
    }

    public IReadOnlyList<CommandItem> GetCommands() => _items;
    public void Refresh() { /* static set */ }

    private List<CommandItem> Build()
    {
        var list = new List<CommandItem>();
        void Add(string id, string title, UpdateState state, string? version = null, double? progress = null, string? error = null)
        {
            list.Add(new CommandItem
            {
                Id = id,
                Title = title,
                Description = "Drive the update simulator for manual QA.",
                Category = CommandCategory.Custom,
                Execute = _ => _simulator.Simulate(state, version, progress, error),
            });
        }

        Add("sponsor.sim.idle",            "Simulate: Idle",             UpdateState.Idle);
        Add("sponsor.sim.no-updates",      "Simulate: No Updates",       UpdateState.NoUpdatesFound);
        Add("sponsor.sim.available",       "Simulate: Update Available", UpdateState.UpdateAvailable, version: "1.4.2");
        Add("sponsor.sim.downloading",     "Simulate: Downloading 42%",  UpdateState.Downloading, progress: 0.42);
        Add("sponsor.sim.extracting",      "Simulate: Extracting",       UpdateState.Extracting);
        Add("sponsor.sim.installing",      "Simulate: Installing",       UpdateState.Installing);
        Add("sponsor.sim.restart-pending", "Simulate: Restart Pending",  UpdateState.RestartPending);
        Add("sponsor.sim.error",           "Simulate: Error",            UpdateState.Error, error: "Simulated failure");
        return list;
    }
}
#endif
