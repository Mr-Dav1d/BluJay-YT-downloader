using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BluJay_YT_downloader.ViewModels;
using BluJay_YT_downloader.Views;

namespace BluJay_YT_downloader;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private MainViewModel? _mainViewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainViewModel = new MainViewModel();
            _mainWindow = new MainWindow
            {
                DataContext = _mainViewModel
            };

            desktop.MainWindow = _mainWindow;
            
            // Standard startup: show the window
            _mainWindow.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void TrayIcon_OnClicked(object? sender, EventArgs e)
    {
        ShowWindow();
    }

    private void MenuShow_OnClick(object? sender, EventArgs e)
    {
        ShowWindow();
    }

    private void MenuExit_OnClick(object? sender, EventArgs e)
    {
        ExitApplication();
    }

    private void ShowWindow()
    {
        if (_mainWindow != null)
        {
            _mainWindow.Show();
            _mainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
            _mainWindow.Activate();
        }
    }

    private void ExitApplication()
    {
        if (_mainViewModel != null)
        {
            _mainViewModel.StopServices();
        }

        if (_mainWindow != null)
        {
            _mainWindow.IsReallyClosing = true;
            _mainWindow.Close();
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}