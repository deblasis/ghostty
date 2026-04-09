using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Ghostty.Core.Clipboard;

/// <summary>
/// Pure-logic clipboard service. Mediates between libghostty's three
/// clipboard callbacks and the platform backend/confirmer. Knows nothing
/// about WinUI 3 and is fully unit-tested in Ghostty.Tests.
///
/// Key rules baked in:
///   * Selection clipboard is a no-op on Windows (no PRIMARY-style buffer).
///   * Backend exceptions on read are swallowed and surface as null so
///     paste keybinds fall through to the terminal.
///   * Writes never call the backend with an empty payload list, and
///     never call the backend if no payload has a known MIME (don't
///     clear the clipboard with an empty package).
/// </summary>
public sealed class ClipboardService
{
    private readonly IClipboardBackend _backend;
    private readonly IClipboardConfirmer _confirmer;

    public ClipboardService(IClipboardBackend backend, IClipboardConfirmer confirmer)
    {
        _backend = backend;
        _confirmer = confirmer;
    }

    public async ValueTask<string?> HandleReadAsync(ClipboardKind kind)
    {
        if (kind == ClipboardKind.Selection)
            return null;

        try
        {
            return await _backend.ReadTextAsync();
        }
        catch
        {
            // Clipboard read can throw when another process holds the
            // clipboard open. Surfacing null lets the keybind fall
            // through to the terminal, matching macOS.
            return null;
        }
    }
}
