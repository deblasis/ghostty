using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Ghostty.Core.Clipboard;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;
using WinClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

namespace Ghostty.Clipboard;

/// <summary>
/// Production IClipboardBackend backed by Windows.ApplicationModel.
/// DataTransfer.Clipboard. Must be called from the UI thread; the
/// bridge dispatches all calls before invoking us.
/// </summary>
internal sealed class WinUiClipboardBackend : IClipboardBackend
{
    // CO_E_NOTINITIALIZED is the WinUI 3 startup race when SetContent is
    // called before the window's clipboard broker is fully ready.
    // See memory/reference_winui3_quirks.md.
    private const int CO_E_NOTINITIALIZED = unchecked((int)0x800401F0);

    private readonly DispatcherQueue _dispatcher;

    public WinUiClipboardBackend(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async ValueTask<string?> ReadTextAsync()
    {
        try
        {
            var view = WinClipboard.GetContent();
            if (!view.Contains(StandardDataFormats.Text))
                return null;
            return await view.GetTextAsync();
        }
        catch (COMException ex)
        {
            // Clipboard locked by another process. Treated as "no text".
            Debug.WriteLine($"[clipboard] read failed: 0x{ex.HResult:X8}");
            return null;
        }
    }

    public ValueTask WriteAsync(IReadOnlyList<ClipboardPayload> payloads)
    {
        var package = new DataPackage();
        foreach (var payload in payloads)
        {
            switch (WindowsClipboardFormatMap.FromMime(payload.Mime))
            {
                case WindowsClipboardFormat.Text:
                    package.SetText(payload.Data);
                    break;
                case WindowsClipboardFormat.Html:
                    // CreateHtmlFormat wraps the body in the CF_HTML
                    // header that Word and Outlook understand.
                    package.SetHtmlFormat(HtmlFormatHelper.CreateHtmlFormat(payload.Data));
                    break;
                default:
                    // Unknown MIME: already filtered by the service, but
                    // be defensive in case the contract drifts.
                    break;
            }
        }

        try
        {
            WinClipboard.SetContent(package);
        }
        catch (COMException ex) when (ex.HResult == CO_E_NOTINITIALIZED)
        {
            // Window not ready yet. Retry once on the next dispatcher tick.
            _dispatcher.TryEnqueue(() =>
            {
                try { WinClipboard.SetContent(package); }
                catch (COMException retryEx)
                {
                    Debug.WriteLine($"[clipboard] write retry failed: 0x{retryEx.HResult:X8}");
                }
            });
        }

        return ValueTask.CompletedTask;
    }
}
