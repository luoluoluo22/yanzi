using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenQuickHost;

internal static class MobileAttachmentInputVerification
{
    public static async Task RunAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "yanzi-clipboard-attachment-check-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var scope = HostAssets.UseIsolatedDataRootForVerification(root);
                Directory.CreateDirectory(root);

                static void Check(bool condition, string message)
                {
                    if (!condition) throw new InvalidOperationException("Clipboard attachments: " + message);
                }

                Check(MobileMessageAttachmentInput.IsImageFile(@"C:\照片.PNG"), "PNG must be photo");
                Check(MobileMessageAttachmentInput.IsImageFile(@"C:\screen.JPG"), "JPG must be photo");
                Check(MobileMessageAttachmentInput.IsImageFile(@"C:\photo.webp"), "WEBP must be photo");
                Check(!MobileMessageAttachmentInput.IsImageFile(@"C:\draft.docx"), "DOCX must remain file");
                Check(!MobileMessageAttachmentInput.IsImageFile(@"C:\archive.zip"), "ZIP must remain file");

                var bitmap = BitmapSource.Create(2, 2, 96, 96,
                    PixelFormats.Bgra32, null,
                    new byte[] { 255, 0, 0, 255, 0, 255, 0, 255,
                                 0, 0, 255, 255, 255, 255, 255, 255 }, 8);
                var bitmapData = new System.Windows.DataObject();
                bitmapData.SetData(System.Windows.DataFormats.Bitmap, bitmap);
                Check(MobileMessageAttachmentInput.TryReadPastedAttachments(bitmapData, out var copiedBitmap, out var files) &&
                    copiedBitmap != null && files.Length == 0, "bitmap clipboard must be recognized");
                var saved = MobileMessageAttachmentInput.SavePastedBitmap(copiedBitmap!);
                Check(File.Exists(saved) && new FileInfo(saved).Length is > 0 and <= MobileMessageAttachmentInput.MaxAttachmentBytes,
                    "copied bitmap must persist as a valid PNG within size limits");
                using (var stream = File.OpenRead(saved))
                {
                    var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    Check(decoder.Frames[0].PixelWidth == 2 && decoder.Frames[0].PixelHeight == 2,
                        "PNG image dimensions must survive serialization");
                }

                var pngClipboard = new System.Windows.DataObject();
                pngClipboard.SetData("PNG", File.ReadAllBytes(saved));
                Check(MobileMessageAttachmentInput.TryReadPastedAttachments(pngClipboard, out var pngImage, out _) &&
                    pngImage?.PixelWidth == 2, "PNG-only clipboard image must be recognized");

                var textData = new System.Windows.DataObject();
                textData.SetData(System.Windows.DataFormats.UnicodeText, "第一行\r\n第二行");
                Check(!MobileMessageAttachmentInput.TryReadPastedAttachments(textData, out _, out _),
                    "ordinary multiline text must remain untouched by attachment paste");

                var exampleFile = Path.Combine(root, "sample.pdf");
                File.WriteAllText(exampleFile, "test document");
                var fileDrop = new System.Windows.DataObject();
                fileDrop.SetData(System.Windows.DataFormats.FileDrop, new[] { exampleFile });
                Check(MobileMessageAttachmentInput.TryReadPastedAttachments(fileDrop, out var droppedImage, out var droppedFiles) &&
                    droppedImage == null && droppedFiles.Length == 1 && droppedFiles[0] == exampleFile,
                    "Explorer file copy must be recognized");

                var app = new App { IsVerificationHarness = true };
                app.InitializeComponent();
                var window = new MobileMessageToastWindow();
                var input = (System.Windows.Controls.TextBox)window.FindName("InputTextBox");
                Check(input.AcceptsReturn && input.MaxLines == 5, "multiline composer regression");
                Check(window.FindName("AttachButton") is System.Windows.Controls.Button,
                    "attachment button exists");
                window.Close();
                app.Shutdown();

                Console.WriteLine("MOBILE_CLIPBOARD_ATTACHMENTS=PASSED; checks=13; real account and clipboard untouched");
                completion.TrySetResult();
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task;
    }
}
