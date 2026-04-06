using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Ghostty;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Mica backdrop. Keep the default Windows title bar for the first
        // shell pass; custom title bar with ExtendsContentIntoTitleBar
        // comes in a follow-up PR so we can focus on the terminal plumbing
        // here.
        SystemBackdrop = new MicaBackdrop();
    }
}
