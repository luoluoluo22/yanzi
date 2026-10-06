using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace OpenQuickHost;

public static class YanziWpsCapabilityProvider
{
    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name = "wps.status",
            Description = "查询 WPS Writer、表格、演示 COM 自动化接口及安装状态",
            Permissions = ["application.read"],
            Category = "wps",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = StatusAsync
        };

        yield return new()
        {
            Name = "wps.open",
            Description = "使用 WPS 对应组件打开本地 Writer、表格、演示或 PDF 文件",
            Permissions = ["application.run", "file.read"],
            Category = "wps",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{"path":{"type":"string","minLength":1}},
              "required":["path"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = OpenAsync
        };

        yield return new()
        {
            Name = "wps.writer.create",
            Description = "通过 WPS Writer COM 无界面创建 DOCX 文档，可直接写入正文",
            Permissions = ["application.run", "file.write"],
            Category = "wps",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "path":{"type":"string","minLength":1},
                "text":{"type":"string"},
                "overwrite":{"type":"boolean"}
              },
              "required":["path"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = CreateWriterAsync
        };

        yield return new()
        {
            Name = "wps.spreadsheet.create",
            Description = "通过 WPS 表格 COM 无界面创建 XLSX，并可按 A1/B2 等单元格地址写入值",
            Permissions = ["application.run", "file.write"],
            Category = "wps",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "path":{"type":"string","minLength":1},
                "sheetName":{"type":"string"},
                "cells":{"type":"object"},
                "overwrite":{"type":"boolean"}
              },
              "required":["path"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = CreateSpreadsheetAsync
        };

        yield return new()
        {
            Name = "wps.presentation.create",
            Description = "通过 WPS 演示 COM 无界面创建 PPTX，可生成标题页",
            Permissions = ["application.run", "file.write"],
            Category = "wps",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "path":{"type":"string","minLength":1},
                "title":{"type":"string"},
                "subtitle":{"type":"string"},
                "overwrite":{"type":"boolean"}
              },
              "required":["path"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = CreatePresentationAsync
        };

        yield return new()
        {
            Name = "wps.exportPdf",
            Description = "通过 WPS COM 将 DOC/DOCX、XLS/XLSX 或 PPT/PPTX 导出为 PDF",
            Permissions = ["application.run", "file.read", "file.write"],
            Category = "wps",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse("""
            {
              "type":"object",
              "properties":{
                "path":{"type":"string","minLength":1},
                "outputPath":{"type":"string"},
                "overwrite":{"type":"boolean"}
              },
              "required":["path"],
              "additionalProperties":false
            }
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = ExportPdfAsync
        };
    }

    private static Task<object?> StatusAsync(object? _)
    {
        var office = ResolveOfficeDirectory();
        return Task.FromResult<object?>(new
        {
            installed = office != null,
            officeDirectory = office,
            writer = ProgIdAvailable("KWPS.Application"),
            spreadsheet = ProgIdAvailable("KET.Application"),
            presentation = ProgIdAvailable("KWPP.Application"),
            running = Process.GetProcesses()
                .Any(p => p.ProcessName.Equals("wps", StringComparison.OrdinalIgnoreCase)
                       || p.ProcessName.Equals("et", StringComparison.OrdinalIgnoreCase)
                       || p.ProcessName.Equals("wpp", StringComparison.OrdinalIgnoreCase))
        });
    }

    private static Task<object?> OpenAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var path = RequireExistingFile(input.GetProperty("path").GetString()!);
        var office = ResolveOfficeDirectory() ?? throw new FileNotFoundException("未找到 WPS Office。");
        var extension = Path.GetExtension(path).ToLowerInvariant();

        var executable = extension switch
        {
            ".xls" or ".xlsx" or ".xlsm" or ".csv" or ".et" => Path.Combine(office, "et.exe"),
            ".ppt" or ".pptx" or ".pps" or ".dps" => Path.Combine(office, "wpp.exe"),
            ".pdf" => Path.Combine(office, "wpspdf.exe"),
            _ => Path.Combine(office, "wps.exe")
        };

        if (!File.Exists(executable))
            throw new FileNotFoundException("WPS 对应组件不存在。", executable);

        var info = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            WorkingDirectory = office
        };
        info.ArgumentList.Add(path);
        var process = Process.Start(info) ?? throw new InvalidOperationException("WPS 启动失败。");

        return Task.FromResult<object?>(new
        {
            opened = true,
            path,
            executable,
            processId = process.Id
        });
    }

    private static async Task<object?> CreateWriterAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var path = PrepareOutputPath(input.GetProperty("path").GetString()!, ".docx",
            input.TryGetProperty("overwrite", out var overwrite) && overwrite.GetBoolean());
        var text = input.TryGetProperty("text", out var textElement) ? textElement.GetString() ?? "" : "";

        await RunStaAsync(() =>
        {
            dynamic? app = null;
            dynamic? doc = null;
            try
            {
                app = CreateCom("KWPS.Application");
                app.Visible = false;
                app.DisplayAlerts = 0;
                doc = app.Documents.Add();
                if (!string.IsNullOrEmpty(text))
                    doc.Content.Text = text;
                doc.SaveAs2(path);
            }
            finally
            {
                try { if (doc != null) doc.Close(false); } catch { }
                try { if (app != null) app.Quit(); } catch { }
                ReleaseCom(doc);
                ReleaseCom(app);
            }
        });

        return new
        {
            created = File.Exists(path),
            path,
            bytes = new FileInfo(path).Length,
            kind = "writer"
        };
    }

    private static async Task<object?> CreateSpreadsheetAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var path = PrepareOutputPath(input.GetProperty("path").GetString()!, ".xlsx",
            input.TryGetProperty("overwrite", out var overwrite) && overwrite.GetBoolean());
        var sheetName = input.TryGetProperty("sheetName", out var sheetElement)
            ? sheetElement.GetString()?.Trim()
            : null;
        var cells = input.TryGetProperty("cells", out var cellsElement) && cellsElement.ValueKind == JsonValueKind.Object
            ? cellsElement.Clone()
            : default;

        await RunStaAsync(() =>
        {
            dynamic? app = null;
            dynamic? book = null;
            dynamic? sheet = null;
            try
            {
                app = CreateCom("KET.Application");
                app.Visible = false;
                app.DisplayAlerts = false;
                book = app.Workbooks.Add();
                sheet = book.Worksheets.Item(1);

                if (!string.IsNullOrWhiteSpace(sheetName))
                    sheet.Name = sheetName;

                if (cells.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in cells.EnumerateObject())
                    {
                        if (!IsCellAddress(property.Name))
                            throw new ArgumentException("无效单元格地址：" + property.Name);

                        dynamic range = sheet.Range(property.Name);
                        try
                        {
                            range.Value2 = JsonScalar(property.Value);
                        }
                        finally
                        {
                            ReleaseCom(range);
                        }
                    }
                }

                // 51 = xlOpenXMLWorkbook (.xlsx)
                book.SaveAs(path, 51);
            }
            finally
            {
                try { if (book != null) book.Close(false); } catch { }
                try { if (app != null) app.Quit(); } catch { }
                ReleaseCom(sheet);
                ReleaseCom(book);
                ReleaseCom(app);
            }
        });

        return new
        {
            created = File.Exists(path),
            path,
            bytes = new FileInfo(path).Length,
            kind = "spreadsheet"
        };
    }

    private static async Task<object?> CreatePresentationAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var path = PrepareOutputPath(input.GetProperty("path").GetString()!, ".pptx",
            input.TryGetProperty("overwrite", out var overwrite) && overwrite.GetBoolean());
        var title = input.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? "" : "";
        var subtitle = input.TryGetProperty("subtitle", out var subtitleElement) ? subtitleElement.GetString() ?? "" : "";

        await RunStaAsync(() =>
        {
            dynamic? app = null;
            dynamic? presentation = null;
            dynamic? slide = null;
            try
            {
                app = CreateCom("KWPP.Application");
                presentation = app.Presentations.Add();
                // 1 = ppLayoutTitle
                slide = presentation.Slides.Add(1, 1);

                if (!string.IsNullOrEmpty(title))
                    slide.Shapes.Title.TextFrame.TextRange.Text = title;

                if (!string.IsNullOrEmpty(subtitle))
                {
                    dynamic? placeholder = null;
                    try
                    {
                        placeholder = slide.Shapes.Placeholders.Item(2);
                        placeholder.TextFrame.TextRange.Text = subtitle;
                    }
                    finally
                    {
                        ReleaseCom(placeholder);
                    }
                }

                // WPS/PowerPoint: 24 = pptx
                presentation.SaveAs(path, 24);
            }
            finally
            {
                try { if (presentation != null) presentation.Close(); } catch { }
                try { if (app != null) app.Quit(); } catch { }
                ReleaseCom(slide);
                ReleaseCom(presentation);
                ReleaseCom(app);
            }
        });

        return new
        {
            created = File.Exists(path),
            path,
            bytes = new FileInfo(path).Length,
            kind = "presentation"
        };
    }

    private static async Task<object?> ExportPdfAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var source = RequireExistingFile(input.GetProperty("path").GetString()!);
        var output = input.TryGetProperty("outputPath", out var outputElement) &&
                     !string.IsNullOrWhiteSpace(outputElement.GetString())
            ? Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputElement.GetString()!.Trim().Trim('"')))
            : Path.ChangeExtension(source, ".pdf");

        if (!output.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            output += ".pdf";

        var overwrite = input.TryGetProperty("overwrite", out var overwriteElement) && overwriteElement.GetBoolean();
        EnsureWritableOutput(output, overwrite);

        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is ".doc" or ".docx" or ".wps")
            await ExportWriterPdfAsync(source, output);
        else if (extension is ".xls" or ".xlsx" or ".et" or ".csv")
            await ExportSpreadsheetPdfAsync(source, output);
        else if (extension is ".ppt" or ".pptx" or ".dps")
            await ExportPresentationPdfAsync(source, output);
        else
            throw new ArgumentException("暂不支持该 WPS 文件类型导出 PDF：" + extension);

        return new
        {
            exported = File.Exists(output),
            source,
            outputPath = output,
            bytes = new FileInfo(output).Length
        };
    }

    private static Task ExportWriterPdfAsync(string source, string output) => RunStaAsync(() =>
    {
        dynamic? app = null;
        dynamic? doc = null;
        try
        {
            app = CreateCom("KWPS.Application");
            app.Visible = false;
            app.DisplayAlerts = 0;
            doc = app.Documents.Open(source, ReadOnly: true);
            // 17 = wdExportFormatPDF
            doc.ExportAsFixedFormat(output, 17);
        }
        finally
        {
            try { if (doc != null) doc.Close(false); } catch { }
            try { if (app != null) app.Quit(); } catch { }
            ReleaseCom(doc);
            ReleaseCom(app);
        }
    });

    private static Task ExportSpreadsheetPdfAsync(string source, string output) => RunStaAsync(() =>
    {
        dynamic? app = null;
        dynamic? book = null;
        try
        {
            app = CreateCom("KET.Application");
            app.Visible = false;
            app.DisplayAlerts = false;
            book = app.Workbooks.Open(source, ReadOnly: true);
            // 0 = xlTypePDF
            book.ExportAsFixedFormat(0, output);
        }
        finally
        {
            try { if (book != null) book.Close(false); } catch { }
            try { if (app != null) app.Quit(); } catch { }
            ReleaseCom(book);
            ReleaseCom(app);
        }
    });

    private static Task ExportPresentationPdfAsync(string source, string output) => RunStaAsync(() =>
    {
        dynamic? app = null;
        dynamic? presentation = null;
        try
        {
            app = CreateCom("KWPP.Application");
            presentation = app.Presentations.Open(source, WithWindow: false);
            // 32 = ppSaveAsPDF
            presentation.SaveAs(output, 32);
        }
        finally
        {
            try { if (presentation != null) presentation.Close(); } catch { }
            try { if (app != null) app.Quit(); } catch { }
            ReleaseCom(presentation);
            ReleaseCom(app);
        }
    });

    private static string? ResolveOfficeDirectory()
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kingsoft", "WPS Office");

        if (Directory.Exists(local))
        {
            var candidate = Directory.EnumerateDirectories(local)
                .Select(path => Path.Combine(path, "office6"))
                .Where(path => File.Exists(Path.Combine(path, "wps.exe")))
                .OrderByDescending(path => File.GetLastWriteTimeUtc(Path.Combine(path, "wps.exe")))
                .FirstOrDefault();
            if (candidate != null) return candidate;
        }

        return null;
    }

    private static bool ProgIdAvailable(string progId)
    {
        try { return Type.GetTypeFromProgID(progId, throwOnError: false) != null; }
        catch { return false; }
    }

    private static dynamic CreateCom(string progId)
    {
        var type = Type.GetTypeFromProgID(progId, throwOnError: false)
            ?? throw new InvalidOperationException("WPS COM 接口不可用：" + progId);
        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("无法创建 WPS COM 对象：" + progId);
    }

    private static void ReleaseCom(object? value)
    {
        if (value == null || !Marshal.IsComObject(value)) return;
        try { Marshal.FinalReleaseComObject(value); } catch { }
    }

    private static Task RunStaAsync(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static string PrepareOutputPath(string raw, string requiredExtension, bool overwrite)
    {
        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"')));
        if (!path.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase))
            path += requiredExtension;
        EnsureWritableOutput(path, overwrite);
        return path;
    }

    private static void EnsureWritableOutput(string path, bool overwrite)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("输出路径无父目录。");
        Directory.CreateDirectory(directory);
        if (File.Exists(path))
        {
            if (!overwrite)
                throw new IOException("输出文件已存在；若确认覆盖，请设置 overwrite=true。");
            File.Delete(path);
        }
    }

    private static string RequireExistingFile(string raw)
    {
        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"')));
        if (!File.Exists(path)) throw new FileNotFoundException("文件不存在。", path);
        return path;
    }

    private static bool IsCellAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 12) return false;
        var seenDigit = false;
        foreach (var ch in value)
        {
            if (!seenDigit && char.IsLetter(ch)) continue;
            if (char.IsDigit(ch)) { seenDigit = true; continue; }
            return false;
        }
        return seenDigit;
    }

    private static object? JsonScalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => value.GetRawText()
    };
}
