using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace BluJay_YT_downloader.Services;

public class DownloadService
{
    private static readonly string BaseBinariesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Binaries");
    private static readonly string YtDlpPath = Path.Combine(BaseBinariesPath, "yt-dlp.exe");
    private static readonly string FfmpegPath = Path.Combine(BaseBinariesPath, "ffmpeg.exe");

    private static readonly Regex ProgressRegex = new(@"\[download\]\s+(\d+(?:\.\d+)?)%", RegexOptions.Compiled);

    public static bool AreBinariesPresent()
    {
        return File.Exists(YtDlpPath) && File.Exists(FfmpegPath);
    }

    public static async Task<VideoMetadata?> FetchMetadataAsync(string url)
    {
        if (!AreBinariesPresent())
        {
            throw new FileNotFoundException("yt-dlp.exe or ffmpeg.exe is missing from Assets/Binaries folder.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = YtDlpPath,
            Arguments = $"--dump-json --no-playlist \"{url}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = BaseBinariesPath
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            throw new Exception($"Failed to fetch metadata: {error}");
        }

        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            string title = "Unknown Title";
            if (root.TryGetProperty("title", out var titleProp))
            {
                title = titleProp.GetString() ?? "Unknown Title";
            }

            double durationSec = 0;
            if (root.TryGetProperty("duration", out var durProp) && !durProp.ValueKind.Equals(JsonValueKind.Null))
            {
                durationSec = durProp.GetDouble();
            }

            string thumbnailUrl = string.Empty;
            if (root.TryGetProperty("thumbnail", out var thumbProp))
            {
                thumbnailUrl = thumbProp.GetString() ?? string.Empty;
            }

            bool has1080p = false;
            bool has720p = false;
            if (root.TryGetProperty("formats", out var formatsProp))
            {
                foreach (var format in formatsProp.EnumerateArray())
                {
                    if (format.TryGetProperty("height", out var heightProp) && !heightProp.ValueKind.Equals(JsonValueKind.Null))
                    {
                        int height = heightProp.GetInt32();
                        if (height >= 1080) has1080p = true;
                        if (height >= 720) has720p = true;
                    }
                }
            }

            return new VideoMetadata
            {
                Title = title,
                Duration = FormatDuration(durationSec),
                ThumbnailUrl = thumbnailUrl,
                Has1080p = has1080p,
                Has720p = has720p
            };
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to parse metadata JSON: {ex.Message}", ex);
        }
    }

    public static async Task<Bitmap?> DownloadThumbnailAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            using var client = new System.Net.Http.HttpClient();
            var bytes = await client.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            return new Bitmap(ms);
        }
        catch
        {
            return null;
        }
    }

    public static async Task DownloadAsync(string url, string targetFormat, Action<double> progressCallback)
    {
        if (!AreBinariesPresent())
        {
            throw new FileNotFoundException("yt-dlp.exe or ffmpeg.exe is missing from Assets/Binaries folder.");
        }

        string downloadsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(downloadsFolder))
        {
            Directory.CreateDirectory(downloadsFolder);
        }

        string outputPath = Path.Combine(downloadsFolder, "%(title)s.%(ext)s");
        string arguments;

        if (targetFormat.Contains("MP3"))
        {
            arguments = $"-x --audio-format mp3 --audio-quality 0 --ffmpeg-location \"{BaseBinariesPath}\" -o \"{outputPath}\" \"{url}\"";
        }
        else
        {
            string maxSelection = "2160"; // default for Best
            if (targetFormat.Contains("1080p")) maxSelection = "1080";
            else if (targetFormat.Contains("720p")) maxSelection = "720";

            arguments = $"-f \"bv*[height<={maxSelection}]+ba/b[height<={maxSelection}]\" --ffmpeg-location \"{BaseBinariesPath}\" -o \"{outputPath}\" \"{url}\"";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = YtDlpPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = BaseBinariesPath
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        using var reader = process.StandardOutput;
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            var match = ProgressRegex.Match(line);
            if (match.Success && double.TryParse(match.Groups[1].Value, out double progress))
            {
                progressCallback(progress);
            }
        }

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            string errOutput = await process.StandardError.ReadToEndAsync();
            throw new Exception($"Download failed (Exit code {process.ExitCode}). {errOutput}");
        }
    }

    private static string FormatDuration(double totalSeconds)
    {
        TimeSpan t = TimeSpan.FromSeconds(totalSeconds);
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}";
        return $"{t.Minutes:D2}:{t.Seconds:D2}";
    }
}

public class VideoMetadata
{
    public string Title { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public bool Has1080p { get; set; }
    public bool Has720p { get; set; }
}
