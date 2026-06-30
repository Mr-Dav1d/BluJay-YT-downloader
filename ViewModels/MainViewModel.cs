using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media.Imaging;
using BluJay_YT_downloader.Models;
using BluJay_YT_downloader.Services;

namespace BluJay_YT_downloader.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ClipboardService _clipboardService;

    [ObservableProperty]
    private ObservableCollection<VideoItem> _downloads = new();

    [ObservableProperty]
    private string _statusMessage = "Listening for YouTube links...";

    public MainViewModel()
    {
        _clipboardService = new ClipboardService();
        _clipboardService.UrlDetected += OnUrlDetected;
        _clipboardService.Start();
    }

    private void OnUrlDetected(string url)
    {
        // Thread-safe check and modification
        Dispatcher.UIThread.Post(() =>
        {
            // Deduplicate against the current collection
            if (Downloads.Any(item => item.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var newItem = new VideoItem
            {
                Url = url,
                DownloadStatus = "Loading",
                Title = "Loading details...",
                TargetFormat = "MP4 (Best)"
            };

            Downloads.Add(newItem);

            // Fetch metadata in background
            Task.Run(() => FetchMetadataAsync(newItem));
        });
    }

    private async Task FetchMetadataAsync(VideoItem item)
    {
        try
        {
            var metadata = await DownloadService.FetchMetadataAsync(item.Url);
            if (metadata != null)
            {
                Bitmap? thumbnail = null;
                if (!string.IsNullOrEmpty(metadata.ThumbnailUrl))
                {
                    thumbnail = await DownloadService.DownloadThumbnailAsync(metadata.ThumbnailUrl);
                }

                Dispatcher.UIThread.Post(() =>
                {
                    item.Title = metadata.Title;
                    item.Duration = metadata.Duration;
                    item.Thumbnail = thumbnail;
                    item.DownloadStatus = "Queued";
                });
            }
            else
            {
                throw new Exception("Metadata response was empty.");
            }
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                item.Title = "Failed to load details";
                item.DownloadStatus = "Failed";
                item.ErrorMessage = ex.Message;
            });
        }
    }

    [RelayCommand]
    private async Task DownloadAsync(VideoItem item)
    {
        if (item.IsProcessing || item.DownloadStatus == "Downloading" || item.DownloadStatus == "Loading")
            return;

        Dispatcher.UIThread.Post(() =>
        {
            item.IsProcessing = true;
            item.DownloadStatus = "Downloading";
            item.Progress = 0;
            item.ErrorMessage = string.Empty;
        });

        try
        {
            await Task.Run(async () =>
            {
                await DownloadService.DownloadAsync(item.Url, item.TargetFormat, (progress) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        item.Progress = progress;
                    });
                });
            });

            Dispatcher.UIThread.Post(() =>
            {
                item.DownloadStatus = "Completed";
                item.Progress = 100;
                item.IsProcessing = false;
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                item.DownloadStatus = "Failed";
                item.Progress = 0;
                item.IsProcessing = false;
                item.ErrorMessage = ex.Message;
            });
        }
    }

    [RelayCommand]
    private async Task DownloadAllQueueAsync()
    {
        var itemsToDownload = Downloads
            .Where(i => i.DownloadStatus == "Queued" || i.DownloadStatus == "Failed")
            .ToList();

        if (!itemsToDownload.Any()) return;

        var tasks = itemsToDownload.Select(DownloadAsync);
        await Task.WhenAll(tasks);
    }

    [RelayCommand]
    private void Delete(VideoItem item)
    {
        Downloads.Remove(item);
    }

    public void StopServices()
    {
        _clipboardService.Stop();
    }
}
