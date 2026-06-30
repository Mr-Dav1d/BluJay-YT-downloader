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
    private string _downloadStatus = "Queued"; // Queued, Loading, Downloading, Completed, Failed

    [ObservableProperty]
    private double _progress = 0; // 0 to 100

    [ObservableProperty]
    private string _targetFormat = "MP4 (Best)"; // MP4 (Best), MP4 (1080p), MP4 (720p), MP3 Audio (320kbps)

    [ObservableProperty]
    private bool _isProcessing = false;

    [ObservableProperty]
    private string _errorMessage = string.Empty;
}
