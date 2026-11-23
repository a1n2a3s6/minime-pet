using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Windows.Automation;
using MiniMe.Native;
using Drawing = System.Drawing;
using Imaging = System.Drawing.Imaging;

namespace MiniMe.Engine;

public sealed record ScreenSnapshot(
    byte[] JpegBytes,
    ulong PerceptualHash,
    string ActiveWindowTitle,
    string? FocusedTextSnippet,
    long CaptureMilliseconds);

public sealed class ScreenVisionService
{
    private const int MaxCaptureWidth = 1024;
    private const long JpegQuality = 55L;

    private static readonly Imaging.ImageCodecInfo? JpegEncoder =
        Imaging.ImageCodecInfo.GetImageDecoders()
            .FirstOrDefault(c => c.FormatID == Imaging.ImageFormat.Jpeg.Guid);

    private ulong _lastAutoWatchHash;
    private bool _hasLastHash;

    /// <summary>
    /// Captures the active foreground window (or primary screen fallback), resizes to max 1024px wide,
    /// computes a fast 8x8 perceptual grayscale hash, encodes JPEG quality 55 (~40-75KB),
    /// and grabs any locally focused editor/textbox text via UI Automation in parallel.
    /// </summary>
    public async Task<ScreenSnapshot?> CaptureAsync(bool includeFocusedText = true)
    {
        var sw = Stopwatch.StartNew();

        Task<string?> focusedTextTask = includeFocusedText
            ? TryReadFocusedTextLocallyAsync()
            : Task.FromResult<string?>(null);

        (byte[] Bytes, ulong Hash, string Title)? capture = await Task.Run(CaptureScreenGdi);
        string? focusedText = await focusedTextTask;
        sw.Stop();

        if (capture is null)
        {
            return null;
        }

        return new ScreenSnapshot(
            capture.Value.Bytes,
            capture.Value.Hash,
            capture.Value.Title,
            focusedText,
            sw.ElapsedMilliseconds);
    }

    public bool HasScreenChangedSinceLastAutoWatch(ulong newHash, int minBitDifference = 4)
    {
        if (!_hasLastHash)
        {
            _lastAutoWatchHash = newHash;
            _hasLastHash = true;
            return true;
        }

        int diff = BitOperations.PopCount(_lastAutoWatchHash ^ newHash);
        if (diff >= minBitDifference)
        {
            _lastAutoWatchHash = newHash;
            return true;
        }

        return false;
    }

    private static (byte[] Bytes, ulong Hash, string Title)? CaptureScreenGdi()
    {
        try
        {
            IntPtr fg = Win32Interop.GetForegroundWindow();
            string title = Win32Interop.GetWindowTitle(fg);
            Drawing.Rectangle bounds = ResolveCaptureBounds(fg);

            if (bounds.Width < 32 || bounds.Height < 32)
            {
                return null;
            }

            using var raw = new Drawing.Bitmap(bounds.Width, bounds.Height, Imaging.PixelFormat.Format24bppRgb);
            using (Drawing.Graphics g = Drawing.Graphics.FromImage(raw))
            {
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, Drawing.CopyPixelOperation.SourceCopy);
            }

            using Drawing.Bitmap resized = ResizeProportionally(raw, MaxCaptureWidth);
            ulong hash = ComputeFastHash(resized);
            byte[] jpeg = EncodeJpeg(resized, JpegQuality);

            return (jpeg, hash, title);
        }
        catch
        {
            return null;
        }
    }

    private static Drawing.Rectangle ResolveCaptureBounds(IntPtr fgWindow)
    {
        Drawing.Rectangle primary = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
            ?? new Drawing.Rectangle(0, 0, 1920, 1080);

        if (fgWindow != IntPtr.Zero &&
            !Win32Interop.IsOwnProcessWindow(fgWindow) &&
            Win32Interop.IsWindowVisible(fgWindow) &&
            !Win32Interop.IsIconic(fgWindow) &&
            Win32Interop.GetWindowRect(fgWindow, out Win32Interop.RECT r))
        {
            var winRect = Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            winRect.Intersect(primary);
            if (winRect.Width >= 260 && winRect.Height >= 180)
            {
                return winRect;
            }
        }

        return primary;
    }

    private static Drawing.Bitmap ResizeProportionally(Drawing.Bitmap source, int maxWidth)
    {
        if (source.Width <= maxWidth)
        {
            return new Drawing.Bitmap(source);
        }

        double ratio = (double)maxWidth / source.Width;
        int targetW = maxWidth;
        int targetH = Math.Max(1, (int)Math.Round(source.Height * ratio));

        var dest = new Drawing.Bitmap(targetW, targetH, Imaging.PixelFormat.Format24bppRgb);
        using Drawing.Graphics g = Drawing.Graphics.FromImage(dest);
        g.InterpolationMode = Drawing.Drawing2D.InterpolationMode.Bilinear;
        g.CompositingQuality = Drawing.Drawing2D.CompositingQuality.HighSpeed;
        g.PixelOffsetMode = Drawing.Drawing2D.PixelOffsetMode.HighSpeed;
        g.DrawImage(source, 0, 0, targetW, targetH);
        return dest;
    }

    /// <summary>
    /// Computes an ultra-fast 8x8 (64-bit) perceptual average grayscale hash for instant change detection.
    /// </summary>
    public static ulong ComputeFastHash(Drawing.Bitmap bitmap)
    {
        using var tiny = new Drawing.Bitmap(8, 8, Imaging.PixelFormat.Format24bppRgb);
        using (Drawing.Graphics g = Drawing.Graphics.FromImage(tiny))
        {
            g.InterpolationMode = Drawing.Drawing2D.InterpolationMode.Bilinear;
            g.DrawImage(bitmap, 0, 0, 8, 8);
        }

        Span<int> luma = stackalloc int[64];
        int sum = 0;
        int idx = 0;
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                Drawing.Color c = tiny.GetPixel(x, y);
                int gray = (c.R * 30 + c.G * 59 + c.B * 11) / 100;
                luma[idx++] = gray;
                sum += gray;
            }
        }

        int avg = sum / 64;
        ulong hash = 0;
        for (int i = 0; i < 64; i++)
        {
            if (luma[i] >= avg)
            {
                hash |= 1UL << i;
            }
        }
        return hash;
    }

    private static byte[] EncodeJpeg(Drawing.Bitmap bitmap, long quality)
    {
        using var ms = new MemoryStream(65536);
        if (JpegEncoder is not null)
        {
            using var encParams = new Imaging.EncoderParameters(1);
            encParams.Param[0] = new Imaging.EncoderParameter(Imaging.Encoder.Quality, quality);
            bitmap.Save(ms, JpegEncoder, encParams);
        }
        else
        {
            bitmap.Save(ms, Imaging.ImageFormat.Jpeg);
        }
        return ms.ToArray();
    }

    /// <summary>
    /// 0ms-overhead local focused-text reader using Windows UI Automation on a background thread.
    /// Safely times out after 180ms so it never stalls screen vision.
    /// </summary>
    public static async Task<string?> TryReadFocusedTextLocallyAsync()
    {
        try
        {
            Task<string?> readTask = Task.Run(() =>
            {
                try
                {
                    AutomationElement? focused = AutomationElement.FocusedElement;
                    if (focused is null)
                    {
                        return null;
                    }

                    if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out object? valObj) &&
                        valObj is ValuePattern valuePattern)
                    {
                        string? text = valuePattern.Current.Value;
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return TrimSnippet(text);
                        }
                    }

                    if (focused.TryGetCurrentPattern(TextPattern.Pattern, out object? textObj) &&
                        textObj is TextPattern textPattern)
                    {
                        string? text = textPattern.DocumentRange?.GetText(500);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return TrimSnippet(text);
                        }
                    }
                }
                catch
                {
                    // UI Automation can throw ElementNotAvailableException on transient controls
                }

                return null;
            });

            Task completed = await Task.WhenAny(readTask, Task.Delay(180));
            return completed == readTask ? await readTask : null;
        }
        catch
        {
            return null;
        }
    }

    private static string TrimSnippet(string raw)
    {
        string cleaned = raw.Trim();
        return cleaned.Length > 480 ? cleaned[^480..] : cleaned;
    }
}
