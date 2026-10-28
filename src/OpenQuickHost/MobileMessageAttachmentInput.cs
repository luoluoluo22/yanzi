using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace OpenQuickHost;

/// <summary>Shared attachment classification and clipboard image persistence for desktop-to-mobile chat.</summary>
internal static class MobileMessageAttachmentInput
{
    internal const long MaxAttachmentBytes = 30L * 1024 * 1024;
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".heic", ".heif"
    };

    internal static bool IsImageFile(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path));

    internal static bool TryReadPastedAttachments(
        System.Windows.IDataObject? clipboardData,
        out BitmapSource? bitmap,
        out string[] filePaths)
    {
        bitmap = null;
        filePaths = [];
        if (clipboardData == null) return false;

        try
        {
            // Clipboard content from screenshot tools normally advertises CF_BITMAP.
            if (clipboardData.GetDataPresent(System.Windows.DataFormats.Bitmap) ||
                clipboardData.GetDataPresent(System.Windows.DataFormats.Dib))
            {
                bitmap = clipboardData.GetData(System.Windows.DataFormats.Bitmap) as BitmapSource;
                if (bitmap == null && System.Windows.Clipboard.ContainsImage())
                    bitmap = System.Windows.Clipboard.GetImage();
                if (bitmap != null) return true;
            }

            // Some browsers and image editors place PNG bytes on the clipboard without CF_BITMAP.
            if (clipboardData.GetDataPresent("PNG"))
            {
                var pngData = clipboardData.GetData("PNG");
                using var png = pngData switch
                {
                    MemoryStream memory => new MemoryStream(memory.ToArray(), writable: false),
                    byte[] bytes => new MemoryStream(bytes, writable: false),
                    _ => null
                };
                if (png != null)
                {
                    var decoder = new PngBitmapDecoder(png, BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    bitmap = decoder.Frames[0];
                    bitmap.Freeze();
                    return true;
                }
            }

            // Ctrl+C on images or documents in Explorer uses CF_HDROP, not a bitmap.
            if (clipboardData.GetDataPresent(System.Windows.DataFormats.FileDrop) &&
                clipboardData.GetData(System.Windows.DataFormats.FileDrop) is string[] files)
            {
                filePaths = files.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                return filePaths.Length > 0;
            }
        }
        catch (Exception error)
        {
            // Clipboard formats are external data. A busy clipboard, malformed PNG or unsupported
            // format must never propagate into the WPF dispatcher or interrupt ordinary text paste.
            HostAssets.AppendDebug("Clipboard attachment decoding skipped: " + error.GetType().Name);
        }

        return false;
    }

    internal static string SavePastedBitmap(BitmapSource bitmap)
    {
        if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            throw new InvalidDataException("剪贴板图片尺寸无效。");

        var directory = HostAssets.ResolveDataDirectoryPath("mobile-chat-pasted-images");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory,
            "clipboard-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss") +
            "-" + Guid.NewGuid().ToString("N") + ".png");
        var saved = false;
        try
        {
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                png.Save(stream);
                if (stream.Length > MaxAttachmentBytes)
                    throw new IOException("图片超过 30 MB，无法发送。");
            }
            saved = true;
            return file;
        }
        finally
        {
            if (!saved)
            {
                try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
