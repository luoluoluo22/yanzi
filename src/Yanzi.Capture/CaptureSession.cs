using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Yanzi.Capture;

public sealed class CaptureSession
{
    public BitmapSource ScreenImage { get; }
    public CaptureDocument Document { get; }
    public Int32Rect SelectionPixels { get; private set; }

    public event Action? SelectionChanged;

    public CaptureSession(BitmapSource screenImage, Int32Rect selectionPixels)
    {
        ScreenImage = screenImage;
        if (ScreenImage.CanFreeze) ScreenImage.Freeze();

        SelectionPixels = Clamp(selectionPixels);
        Document = new CaptureDocument(
            ScreenCaptureService.Crop(ScreenImage, SelectionPixels));
    }

    public void SetSelection(Int32Rect next)
    {
        var clamped = Clamp(next);
        if (SameRect(clamped, SelectionPixels))
            return;

        var previous = SelectionPixels;

        // Annotation coordinates are local to the current selection bitmap.
        // Moving the selection origin must shift them by the inverse delta so
        // their absolute screen position stays unchanged.
        var annotationDelta = new Vector(
            previous.X - clamped.X,
            previous.Y - clamped.Y);

        if (annotationDelta.LengthSquared > 0.01)
        {
            foreach (var annotation in Document.Annotations)
                annotation.Move(annotationDelta);
        }

        SelectionPixels = clamped;
        Document.ReplaceBaseImage(
            ScreenCaptureService.Crop(ScreenImage, SelectionPixels));
        SelectionChanged?.Invoke();
    }

    private Int32Rect Clamp(Int32Rect rect)
    {
        var x = Math.Clamp(rect.X, 0, Math.Max(0, ScreenImage.PixelWidth - 1));
        var y = Math.Clamp(rect.Y, 0, Math.Max(0, ScreenImage.PixelHeight - 1));
        var width = Math.Clamp(
            rect.Width,
            1,
            Math.Max(1, ScreenImage.PixelWidth - x));
        var height = Math.Clamp(
            rect.Height,
            1,
            Math.Max(1, ScreenImage.PixelHeight - y));
        return new Int32Rect(x, y, width, height);
    }

    private static bool SameRect(Int32Rect a, Int32Rect b) =>
        a.X == b.X &&
        a.Y == b.Y &&
        a.Width == b.Width &&
        a.Height == b.Height;
}

public sealed class ChangeSelectionCommand(
    CaptureSession session,
    Int32Rect before,
    Int32Rect after) : IEditCommand
{
    public void Execute() => session.SetSelection(after);
    public void Undo() => session.SetSelection(before);
}

public sealed record SaveLocationState(
    string CurrentDirectory,
    string[] RecentDirectories);

public sealed class SaveLocationHistory
{
    private const int MaxRecent = 5;
    private readonly string _settingsPath;

    public string CurrentDirectory { get; private set; }
    public IReadOnlyList<string> RecentDirectories => _recent;
    private readonly List<string> _recent = [];

    public SaveLocationHistory(string extensionDataDirectory)
    {
        Directory.CreateDirectory(extensionDataDirectory);
        _settingsPath = Path.Combine(
            extensionDataDirectory,
            "save-locations.json");

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Screenshots");

        CurrentDirectory = fallback;

        try
        {
            if (File.Exists(_settingsPath))
            {
                var state = JsonSerializer.Deserialize<SaveLocationState>(
                    File.ReadAllText(_settingsPath));

                if (state is not null)
                {
                    if (!string.IsNullOrWhiteSpace(state.CurrentDirectory))
                        CurrentDirectory = state.CurrentDirectory;

                    foreach (var item in state.RecentDirectories ?? [])
                    {
                        if (!string.IsNullOrWhiteSpace(item) &&
                            !_recent.Contains(item, StringComparer.OrdinalIgnoreCase))
                        {
                            _recent.Add(item);
                        }
                    }
                }
            }
        }
        catch
        {
        }

        Remember(CurrentDirectory, persist: false);
    }

    public void Remember(string directory, bool persist = true)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;

        directory = Path.GetFullPath(directory);
        CurrentDirectory = directory;

        _recent.RemoveAll(
            item => string.Equals(
                item,
                directory,
                StringComparison.OrdinalIgnoreCase));
        _recent.Insert(0, directory);

        if (_recent.Count > MaxRecent)
            _recent.RemoveRange(MaxRecent, _recent.Count - MaxRecent);

        if (persist)
            Save();
    }

    public string BuildAutomaticFilePath()
    {
        Directory.CreateDirectory(CurrentDirectory);
        var name =
            "Yanzi-" +
            DateTime.Now.ToString("yyyyMMdd-HHmmss") +
            ".png";
        return Path.Combine(CurrentDirectory, name);
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(_settingsPath)!);

            var state = new SaveLocationState(
                CurrentDirectory,
                _recent.ToArray());

            File.WriteAllText(
                _settingsPath,
                JsonSerializer.Serialize(
                    state,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
        }
        catch
        {
        }
    }
}
