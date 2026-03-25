using Ghostty.Interop;

namespace Ghostty.Views;

public partial class MainPage : Page
{
    private GhosttyApp? _ghosttyApp;

    public MainPage()
    {
        // TODO: Replace with actual terminal surface view
        this.InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Validate the DLL loads by calling ghostty_info (no global state needed).
        // ghostty_init currently crashes due to a Zig bug with global mutable
        // state in Windows DLLs — the state variable ends up at address 0.
        try
        {
            var info = NativeMethods.Info();
            var version = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(
                info.Version, (int)info.VersionLen);
            StatusText.Text = $"libghostty {version} ({info.BuildMode})";
        }
        catch (System.Exception ex)
        {
            StatusText.Text = $"Failed to load libghostty: {ex.Message}";
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _ghosttyApp?.Dispose();
        _ghosttyApp = null;
    }
}
