using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace BluJay_YT_downloader.Services;

public class DownloadService
{
    private static readonly string BaseBinariesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Binaries");
    private static readonly string YtDlpPath = Path.Combine(BaseBinariesPath, "yt-dlp.exe");
    private static readonly string FfmpegPath = Path.Combine(BaseBinariesPath, "ffmpeg.exe");

    private static readonly Regex ProgressRegex = new(@"\[download\]\s+(\d+(?:\.\d+)?)%", RegexOptions.Compiled);

    // Global Concurrency Limiter
    private static readonly SemaphoreSlim DownloadSemaphore = new(1, 1);

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
            Arguments = $"--flat-playlist -J \"{url}\"",
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

            // Check if it's a playlist or a single video
            bool isPlaylist = false;
            if (root.TryGetProperty("_type", out var typeProp) && typeProp.GetString() == "playlist")
            {
                isPlaylist = true;
            }
            else if (root.TryGetProperty("entries", out _))
            {
                isPlaylist = true;
            }

            if (isPlaylist)
            {
                string playlistTitle = "Unknown Playlist";
                if (root.TryGetProperty("title", out var titleProp))
                {
                    playlistTitle = titleProp.GetString() ?? "Unknown Playlist";
                }

                var playlistItems = new List<PlaylistItemMetadata>();
                if (root.TryGetProperty("entries", out var entriesProp) && entriesProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in entriesProp.EnumerateArray())
                    {
                        string videoTitle = "Unknown Video";
                        if (entry.TryGetProperty("title", out var entryTitleProp))
                        {
                            videoTitle = entryTitleProp.GetString() ?? "Unknown Video";
                        }

                        string videoId = string.Empty;
                        if (entry.TryGetProperty("id", out var idProp))
                        {
                            videoId = idProp.GetString() ?? string.Empty;
                        }

                        string videoUrl = string.Empty;
                        if (entry.TryGetProperty("url", out var urlProp))
                        {
                            videoUrl = urlProp.GetString() ?? string.Empty;
                        }
                        if (string.IsNullOrEmpty(videoUrl) && !string.IsNullOrEmpty(videoId))
                        {
                            videoUrl = $"https://www.youtube.com/watch?v={videoId}";
                        }

                        double entryDurationSec = 0;
                        if (entry.TryGetProperty("duration", out var entryDurProp) && entryDurProp.ValueKind != JsonValueKind.Null)
                        {
                            entryDurationSec = entryDurProp.GetDouble();
                        }

                        playlistItems.Add(new PlaylistItemMetadata
                        {
                            Title = videoTitle,
                            Url = videoUrl,
                            Duration = entryDurationSec > 0 ? FormatDuration(entryDurationSec) : "--:--",
                            ThumbnailUrl = !string.IsNullOrEmpty(videoId) ? $"https://img.youtube.com/vi/{videoId}/mqdefault.jpg" : string.Empty
                        });
                    }
                }

                return new VideoMetadata
                {
                    Title = playlistTitle,
                    IsPlaylist = true,
                    PlaylistItems = playlistItems
                };
            }
            else
            {
                // Single Video info
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

                return new VideoMetadata
                {
                    Title = title,
                    Duration = FormatDuration(durationSec),
                    ThumbnailUrl = thumbnailUrl,
                    IsPlaylist = false
                };
            }
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

    public static async Task DownloadAsync(string url, string targetFormat, string videoTitle, Action<double> progressCallback, CancellationToken cancellationToken)
    {
        // Concurrency limiter lock
        await DownloadSemaphore.WaitAsync(cancellationToken);
        try
        {
            string downloadsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(downloadsFolder))
            {
                Directory.CreateDirectory(downloadsFolder);
            }

            // 1. Preemptive Storage Check on download drive
            string pathRoot = Path.GetPathRoot(downloadsFolder) ?? "C:\\";
            try
            {
                DriveInfo drive = new(pathRoot);
                if (drive.AvailableFreeSpace < 500L * 1024 * 1024) // 500MB
                {
                    throw new InvalidOperationException("Storage space critically low");
                }
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                // drive check failed to query, proceed but log
            }

            // 2. Contextual Format Fallbacks setup
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

                // Format fallback tree: bv*[height<=X] + ba / b[height<=X] / best
                arguments = $"-f \"bv*[height<={maxSelection}]+ba/b[height<={maxSelection}]/best\" --ffmpeg-location \"{BaseBinariesPath}\" -o \"{outputPath}\" \"{url}\"";
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

            // Link cancellation token to kill process tree
            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch
                {
                    // ignore
                }
            });

            using var reader = process.StandardOutput;
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var match = ProgressRegex.Match(line);
                if (match.Success && double.TryParse(match.Groups[1].Value, out double progress))
                {
                    progressCallback(progress);
                }
            }

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string errOutput = await process.StandardError.ReadToEndAsync();
                throw new Exception($"Download failed (Exit code {process.ExitCode}). {errOutput}");
            }
        }
        catch (OperationCanceledException)
        {
            // 3. Clean up unfinished part files on cancellation
            try
            {
                string downloadsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                string sanitizedTitle = GetSanitizedFileName(videoTitle);
                foreach (var file in Directory.GetFiles(downloadsFolder, $"{sanitizedTitle}*"))
                {
                    if (file.EndsWith(".part") || file.EndsWith(".ytdl") || file.Contains(".temp"))
                    {
                        File.Delete(file);
                    }
                }
            }
            catch
            {
                // ignore
            }
            throw;
        }
        finally
        {
            DownloadSemaphore.Release();
        }
    }

    private static string GetSanitizedFileName(string fileName)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(c, '_');
        }
        return fileName;
    }

    private static string FormatDuration(double totalSeconds)
    {
        TimeSpan t = TimeSpan.FromSeconds(totalSeconds);
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}";
        return $"{t.Minutes:D2}:{t.Seconds:D2}";
    }
}

public class PlaylistItemMetadata
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
}

public class VideoMetadata
{
    public string Title { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public bool IsPlaylist { get; set; }
    public List<PlaylistItemMetadata> PlaylistItems { get; set; } = new();
}
