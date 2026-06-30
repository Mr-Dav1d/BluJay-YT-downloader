using Avalonia.Controls;

namespace BluJay_YT_downloader.Views;

public partial class MainWindow : Window
{
    public bool IsReallyClosing { get; set; } = false;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!IsReallyClosing)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            base.OnClosing(e);
        }
    }
}