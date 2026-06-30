using System;
using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media.Imaging;

namespace BluJay_YT_downloader.Models;

public partial class VideoItem : ObservableObject
{
    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private string _title = "Loading details...";

    [ObservableProperty]
    private string _duration = "--:--";

    [ObservableProperty]
    private string _thumbnailUrl = string.Empty;

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    private string _downloadStatus = "Queued"; // Queued, Loading, Downloading, Completed, Failed, Cancelled

    public string StatusColor => DownloadStatus switch
    {
        "Loading" => "#3B82F6",
        var s when s.StartsWith("Downloading") => "#10B981",
        "Completed" => "#10B981",
        var s when s.StartsWith("Failed") => "#EF4444",
        "Cancelled" => "#9CA3AF",
        _ => "#EAB308"
    };

    // Manual progress property to throttle updates < 0.5%
    private double _progress = 0;
    public double Progress
    {
        get => _progress;
        set
        {
            if (Math.Abs(value - _progress) >= 0.5 || value == 0 || value == 100)
            {
                SetProperty(ref _progress, value);
            }
        }
    }

    [ObservableProperty]
    private string _targetFormat = "MP4 (Best)"; // MP4 (Best), MP4 (1080p), MP4 (720p), MP3 Audio (320kbps)

    [ObservableProperty]
    private bool _isProcessing = false;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    // Playlist structures
    [ObservableProperty]
    private bool _isPlaylist = false;

    [ObservableProperty]
    private ObservableCollection<VideoItem> _playlistItems = new();

    [ObservableProperty]
    private bool _isExpanded = false;

    [ObservableProperty]
    private int _downloadedCount = 0;

    [ObservableProperty]
    private int _failedCount = 0;

    [ObservableProperty]
    private int _totalCount = 0;

    // Cancellation support
    public CancellationTokenSource? Cts { get; set; }
}
