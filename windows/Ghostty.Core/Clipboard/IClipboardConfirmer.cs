using System.Threading.Tasks;

namespace Ghostty.Core.Clipboard;

/// <summary>
/// Renders the clipboard confirmation dialog libghostty asks for via
/// confirm_read_clipboard_cb (paste, OSC 52 read, OSC 52 write). The
/// production implementation is DialogClipboardConfirmer in the WinUI
/// project.
/// </summary>
public interface IClipboardConfirmer
{
    /// <summary>
    /// Show a dialog with the supplied preview as the body and return
    /// true if the user accepts. Implementations must default to Cancel
    /// (return false) for safety and must be safe to call concurrently.
    /// </summary>
    ValueTask<bool> ConfirmAsync(string preview, ClipboardConfirmRequest request);
}
