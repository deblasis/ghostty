using System;
using System.Collections.Generic;

namespace Ghostty.Commands;

internal sealed class ConfigCommandSource : ICommandSource
{
    private readonly Func<string, Action<CommandItem>> _bindingActionFactory;

    public ConfigCommandSource(Func<string, Action<CommandItem>> bindingActionFactory)
    {
        _bindingActionFactory = bindingActionFactory;
    }

    public IReadOnlyList<CommandItem> GetCommands() => [];

    public void Refresh()
    {
        // Will call ghostty_config_command_list when interop lands
    }
}
