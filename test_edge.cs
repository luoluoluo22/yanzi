using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;

public class Program
{
    public static void Main()
    {
        var edgeBookmarksPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ""Microsoft"", ""Edge"", ""User Data"", ""Default"", ""Bookmarks"");
        if (!File.Exists(edgeBookmarksPath)) { Console.WriteLine(""Not found""); return; }
        
        var json = File.ReadAllText(edgeBookmarksPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var count = 0;
        
        void ParseNode(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                if (node.TryGetProperty(""type"", out var typeProp) && typeProp.GetString() == ""url"")
                {
                    var title = node.TryGetProperty(""name"", out var n) ? n.GetString() : ""未命名"";
                    var url = node.TryGetProperty(""url"", out var u) ? u.GetString() : """";
                    if (!string.IsNullOrEmpty(url)) {
                        count++;
                        if (count <= 3) Console.WriteLine($""Parsed: {title} - {url}"");
                    }
                }
                if (node.TryGetProperty(""children"", out var children) && children.ValueKind == JsonValueKind.Array)
                {
                    foreach (var child in children.EnumerateArray()) ParseNode(child);
                }
            }
        }
        
        if (root.TryGetProperty(""roots"", out var roots))
        {
            if (roots.TryGetProperty(""bookmark_bar"", out var bar)) ParseNode(bar);
            if (roots.TryGetProperty(""other"", out var other)) ParseNode(other);
        }
        Console.WriteLine($""Total: {count}"");
    }
}
