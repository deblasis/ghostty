using Microsoft.UI.Xaml.Navigation;
namespace Ghostty;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    public App()
    {
        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs e)
    {
        MainWindow ??= new Window
        {
            Title = "Ghostty"
        };

        if (MainWindow.Content is not Frame rootFrame)
        {
            rootFrame = new Frame();
            rootFrame.NavigationFailed += OnNavigationFailed;
            MainWindow.Content = rootFrame;
        }

        rootFrame.Navigate(typeof(Views.MainPage), e.Arguments);

        MainWindow.Activate();
    }

    private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new InvalidOperationException($"Failed to load page {e.SourcePageType.FullName}");
    }
}
