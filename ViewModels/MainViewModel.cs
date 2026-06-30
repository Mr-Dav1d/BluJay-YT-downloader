using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
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

    [ObservableProperty]
    private string _manualUrl = string.Empty;

    [ObservableProperty]
    private string _manualInputIconState = "Idle"; // Idle, Parsing, Success, Error

    [ObservableProperty]
    private bool _hasManualUrlText = false;

    partial void OnManualUrlChanged(string value)
    {
        HasManualUrlText = !string.IsNullOrEmpty(value);
    }

    public MainViewModel()
    {
        _clipboardService = new ClipboardService();
        _clipboardService.UrlDetected += OnUrlDetected;
        _clipboardService.Start();
    }

    [RelayCommand]
    private void ClearManualUrl()
    {
        ManualUrl = string.Empty;
    }

    [RelayCommand]
    private async Task AddUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(ManualUrl)) return;
        string url = ManualUrl.Trim();
        ManualUrl = string.Empty;
        
        ManualInputIconState = "Parsing";
        OnUrlDetected(url);
    }

    private void OnUrlDetected(string url)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Downloads.Any(item => item.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
            {
                ManualInputIconState = "Error";
                ResetManualInputIconStateAfterDelay(2000);
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
            
            Task.Run(async () =>
            {
                bool success = await FetchMetadataAsync(newItem);
                Dispatcher.UIThread.Post(() =>
                {
                    if (success)
                    {
                        ManualInputIconState = "Success";
                    }
                    else
                    {
                        ManualInputIconState = "Error";
                    }
                    ResetManualInputIconStateAfterDelay(2000);
                });
            });
        });
    }

    private async void ResetManualInputIconStateAfterDelay(int delayMs)
    {
        await Task.Delay(delayMs);
        Dispatcher.UIThread.Post(() =>
        {
            if (ManualInputIconState != "Parsing")
            {
                ManualInputIconState = "Idle";
            }
        });
    }

    private async Task<bool> FetchMetadataAsync(VideoItem item)
    {
        try
        {
            var metadata = await DownloadService.FetchMetadataAsync(item.Url);
            if (metadata != null)
            {
                if (metadata.IsPlaylist)
                {
                    var childItems = new ObservableCollection<VideoItem>();
                    foreach (var child in metadata.PlaylistItems)
                    {
                        childItems.Add(new VideoItem
                        {
                            Title = child.Title,
                            Url = child.Url,
                            Duration = child.Duration,
                            ThumbnailUrl = child.ThumbnailUrl,
                            DownloadStatus = "Queued",
                            TargetFormat = "MP4 (Best)"
                        });
                    }

                    // Download thumbnail for the playlist (first item)
                    Bitmap? pThumb = null;
                    if (metadata.PlaylistItems.Any() && !string.IsNullOrEmpty(metadata.PlaylistItems.First().ThumbnailUrl))
                    {
                        pThumb = await DownloadService.DownloadThumbnailAsync(metadata.PlaylistItems.First().ThumbnailUrl);
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        item.Title = metadata.Title;
                        item.IsPlaylist = true;
                        item.PlaylistItems = childItems;
                        item.TotalCount = childItems.Count;
                        item.DownloadedCount = 0;
                        item.FailedCount = 0;
                        item.Thumbnail = pThumb;
                        item.DownloadStatus = "Queued";
                    });

                    // Download individual thumbnails for children in background
                    _ = Task.Run(async () =>
                    {
                        foreach (var child in childItems)
                        {
                            if (!string.IsNullOrEmpty(child.ThumbnailUrl))
                            {
                                var childThumb = await DownloadService.DownloadThumbnailAsync(child.ThumbnailUrl);
                                if (childThumb != null)
                                {
                                    Dispatcher.UIThread.Post(() => child.Thumbnail = childThumb);
                                }
                            }
                        }
                    });
                }
                else
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
                        item.IsPlaylist = false;
                        item.DownloadStatus = "Queued";
                    });
                }
                return true;
            }
            throw new Exception("Metadata response was empty.");
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                item.Title = "Failed to load details";
                item.DownloadStatus = "Failed";
                item.ErrorMessage = ex.Message;
            });
            return false;
        }
    }

    [RelayCommand]
    private async Task DownloadAsync(VideoItem item)
    {
        if (item.IsProcessing || item.DownloadStatus == "Downloading" || item.DownloadStatus == "Loading")
            return;

        var cts = new CancellationTokenSource();
        Dispatcher.UIThread.Post(() =>
        {
            item.IsProcessing = true;
            item.DownloadStatus = "Downloading";
            item.Progress = 0;
            item.ErrorMessage = string.Empty;
            item.Cts = cts;
        });

        try
        {
            if (item.IsPlaylist)
            {
                int total = item.PlaylistItems.Count;
                Dispatcher.UIThread.Post(() =>
                {
                    item.DownloadedCount = item.PlaylistItems.Count(i => i.DownloadStatus == "Completed");
                    item.FailedCount = item.PlaylistItems.Count(i => i.DownloadStatus == "Failed");
                    item.TotalCount = total;
                });

                var itemsToDownload = item.PlaylistItems
                    .Where(i => i.DownloadStatus == "Queued" || i.DownloadStatus == "Failed")
                    .ToList();

                foreach (var child in itemsToDownload)
                {
                    if (cts.IsCancellationRequested)
                        break;

                    string targetFormat = child.TargetFormat;
                    if (child.TargetFormat == "MP4 (Best)" && item.TargetFormat != "MP4 (Best)")
                    {
                        targetFormat = item.TargetFormat;
                        Dispatcher.UIThread.Post(() => child.TargetFormat = item.TargetFormat);
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        child.IsProcessing = true;
                        child.DownloadStatus = "Downloading";
                        child.Progress = 0;
                        child.ErrorMessage = string.Empty;
                        
                        item.DownloadStatus = $"Downloading ({item.DownloadedCount}/{total} done, {item.FailedCount} failed)";
                    });

                    try
                    {
                        await DownloadService.DownloadAsync(child.Url, targetFormat, child.Title, (progress) =>
                        {
                            Dispatcher.UIThread.Post(() => child.Progress = progress);
                        }, cts.Token);

                        Dispatcher.UIThread.Post(() =>
                        {
                            child.DownloadStatus = "Completed";
                            child.Progress = 100;
                            child.IsProcessing = false;
                            item.DownloadedCount++;
                            item.Progress = ((double)(item.DownloadedCount + item.FailedCount) / total) * 100;
                            item.DownloadStatus = $"Downloading ({item.DownloadedCount}/{total} done, {item.FailedCount} failed)";
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            child.DownloadStatus = ex is OperationCanceledException ? "Cancelled" : "Failed";
                            child.Progress = 0;
                            child.IsProcessing = false;
                            child.ErrorMessage = ex.Message;
                            if (ex is not OperationCanceledException)
                            {
                                item.FailedCount++;
                            }
                            item.Progress = ((double)(item.DownloadedCount + item.FailedCount) / total) * 100;
                            item.DownloadStatus = $"Downloading ({item.DownloadedCount}/{total} done, {item.FailedCount} failed)";
                        });
                    }
                }

                Dispatcher.UIThread.Post(() =>
                {
                    item.IsProcessing = false;
                    if (cts.IsCancellationRequested)
                    {
                        item.DownloadStatus = "Cancelled";
                    }
                    else if (item.FailedCount > 0)
                    {
                        item.DownloadStatus = $"Failed ({item.DownloadedCount}/{total} completed)";
                    }
                    else
                    {
                        item.DownloadStatus = "Completed";
                        item.Progress = 100;
                    }
                });
            }
            else
            {
                await Task.Run(async () =>
                {
                    await DownloadService.DownloadAsync(item.Url, item.TargetFormat, item.Title, (progress) =>
                    {
                        Dispatcher.UIThread.Post(() => item.Progress = progress);
                    }, cts.Token);
                });

                Dispatcher.UIThread.Post(() =>
                {
                    item.DownloadStatus = "Completed";
                    item.Progress = 100;
                    item.IsProcessing = false;
                });
            }
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                item.DownloadStatus = ex is OperationCanceledException ? "Cancelled" : "Failed";
                item.Progress = 0;
                item.IsProcessing = false;
                item.ErrorMessage = ex.Message;
            });
        }
    }

    [RelayCommand]
    private void Cancel(VideoItem item)
    {
        if (item.Cts != null)
        {
            item.Cts.Cancel();
            Dispatcher.UIThread.Post(() =>
            {
                item.DownloadStatus = "Cancelled";
                item.IsProcessing = false;
                item.Progress = 0;
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
        Cancel(item);
        Downloads.Remove(item);
    }

    [RelayCommand]
    private void ToggleExpand(VideoItem item)
    {
        if (item.IsPlaylist)
        {
            item.IsExpanded = !item.IsExpanded;
        }
    }

    public void StopServices()
    {
        _clipboardService.Stop();
        foreach (var item in Downloads)
        {
            Cancel(item);
        }
    }
}
