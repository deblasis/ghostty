using System;
using System.Threading.Tasks;
using Ghostty.Core.Version;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using WinClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

namespace Ghostty.Dialogs;

internal sealed partial class VersionDialog : ContentDialog
{
    private readonly string _output;

    public VersionDialog()
    {
        InitializeComponent();

        var info = VersionRenderer.Build();
        _output = VersionRenderer.RenderPlain(info);
        VersionText.Text = _output;
        Title = $"Wintty {info.WinttyVersionString}";

        PrimaryButtonClick += OnCopy;
    }

    private async void OnCopy(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Don't dismiss the dialog when Copy is clicked.
        args.Cancel = true;

        var data = new DataPackage();
        data.SetText(_output);
        WinClipboard.SetContent(data);

        var originalText = PrimaryButtonText;
        PrimaryButtonText = "Copied";
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1500));
        }
        finally
        {
            PrimaryButtonText = originalText;
        }
    }
}
