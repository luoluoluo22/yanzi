using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Yanzi.Avalonia;

public class ClipboardHistoryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Text { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public class ClipboardMonitorService
{
    private readonly Window _window;
    private readonly string _filePath;
    private readonly List<ClipboardHistoryItem> _history = [];
    private readonly DispatcherTimer _timer;
    private string _lastCopiedText = string.Empty;

    public event EventHandler? HistoryChanged;

    public IReadOnlyList<ClipboardHistoryItem> History
    {
        get
        {
            lock (_history)
            {
                return [.. _history];
            }
        }
    }

    public ClipboardMonitorService(Window window)
    {
        _window = window;
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".yanzi_clipboard_history.json"
        );

        LoadHistory();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _timer.Tick += OnTimerTick;
    }

    public void Start()
    {
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            var clipboard = _window.Clipboard;
            if (clipboard == null) return;

            var text = await clipboard.GetTextAsync();
            if (string.IsNullOrEmpty(text)) return;

            if (text != _lastCopiedText)
            {
                _lastCopiedText = text;
                AddHistoryItem(text);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Clipboard monitoring error: {ex.Message}");
        }
    }

    private void AddHistoryItem(string text)
    {
        lock (_history)
        {
            _history.RemoveAll(item => item.Text == text);
            _history.Insert(0, new ClipboardHistoryItem
            {
                Text = text,
                Timestamp = DateTime.Now
            });

            if (_history.Count > 100)
            {
                _history.RemoveAt(_history.Count - 1);
            }
        }

        SaveHistory();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteItem(string id)
    {
        lock (_history)
        {
            _history.RemoveAll(item => item.Id == id);
        }
        SaveHistory();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearAll()
    {
        lock (_history)
        {
            _history.Clear();
            _lastCopiedText = string.Empty;
        }
        SaveHistory();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LoadHistory()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var items = JsonSerializer.Deserialize<List<ClipboardHistoryItem>>(json);
                if (items != null)
                {
                    lock (_history)
                    {
                        _history.AddRange(items);
                        if (_history.Count > 0)
                        {
                            _lastCopiedText = _history[0].Text;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load clipboard history: {ex.Message}");
        }
    }

    private void SaveHistory()
    {
        try
        {
            List<ClipboardHistoryItem> itemsToSave;
            lock (_history)
            {
                itemsToSave = [.. _history];
            }

            var json = JsonSerializer.Serialize(itemsToSave, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save clipboard history: {ex.Message}");
        }
    }
}
