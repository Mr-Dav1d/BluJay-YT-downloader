using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace BluJay_YT_downloader.Services;

public class ClipboardService
{
    private static readonly Regex YoutubeRegex = new(
        @"^https?:\/\/(?:www\.)?(?:youtube\.com\/watch\?v=|youtu\.be\/|youtube\.com\/shorts\/)([\w-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly Thread _thread;
    private bool _isRunning;
    private string? _lastUrl;

    public event Action<string>? UrlDetected;

    public ClipboardService()
    {
        _thread = new Thread(PollClipboardLoop);
        if (OperatingSystem.IsWindows())
        {
            _thread.SetApartmentState(ApartmentState.STA);
        }
        _thread.IsBackground = true;
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _thread.Start();
    }

    public void Stop()
    {
        _isRunning = false;
    }

    private void PollClipboardLoop()
    {
        while (_isRunning)
        {
            try
            {
                string? text = GetClipboardTextWithRetry(3, 50);
                if (!string.IsNullOrEmpty(text))
                {
                    text = text.Trim();
                    if (text != _lastUrl && YoutubeRegex.IsMatch(text))
                    {
                        _lastUrl = text;
                        UrlDetected?.Invoke(text);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Clipboard polling error: {ex.Message}");
            }

            Thread.Sleep(1000);
        }
    }

    private string? GetClipboardTextWithRetry(int retries, int delayMs)
    {
        for (int i = 0; i < retries; i++)
        {
            try
            {
                return Win32Clipboard.GetText();
            }
            catch (ExternalException)
            {
                if (i == retries - 1) throw;
                Thread.Sleep(delayMs);
            }
        }
        return null;
    }

    private static class Win32Clipboard
    {
        private const uint CF_UNICODETEXT = 13;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        public static string? GetText()
        {
            if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
                return null;

            if (!OpenClipboard(IntPtr.Zero))
                return null;

            try
            {
                IntPtr hMem = GetClipboardData(CF_UNICODETEXT);
                if (hMem == IntPtr.Zero)
                    return null;

                IntPtr pReg = GlobalLock(hMem);
                if (pReg == IntPtr.Zero)
                    return null;

                try
                {
                    return Marshal.PtrToStringUni(pReg);
                }
                finally
                {
                    GlobalUnlock(hMem);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
    }
}
