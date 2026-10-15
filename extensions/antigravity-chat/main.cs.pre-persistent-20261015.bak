using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OpenQuickHost.CSharpRuntime;

public static class YanziAction
{
    public static Task<string> RunAsync(YanziActionContext context)
    {
        try { return Task.FromResult(Start(context)); }
        catch (Exception e)
        {
            context.ShowDesktopNotification("反重力 - 执行失败", e.Message);
            throw;
        }
    }

    private static string Start(YanziActionContext context)
    {
        var root = context.ExtensionDataDirectory;
        var input = (context.InputText ?? "").Trim();
        string replyDeviceId = "";
        string conversationId = "";
        if (input.StartsWith("{"))
        {
            try
            {
                using var request = JsonDocument.Parse(input);
                if(request.RootElement.TryGetProperty("prompt",out var prompt)
                    && request.RootElement.TryGetProperty("replyDeviceId",out var reply)
                    && request.RootElement.TryGetProperty("conversationId",out var conversation))
                {
                    input = prompt.GetString() ?? "";
                    replyDeviceId = reply.GetString() ?? "";
                    conversationId = conversation.GetString() ?? "";
                    if(!System.Text.RegularExpressions.Regex.IsMatch(replyDeviceId,"^[a-zA-Z0-9_-]{1,150}$")
                        || !conversationId.StartsWith("agent:antigravity:") || conversationId.Length>200)
                        throw new ArgumentException("反重力会话参数无效。");
                    var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                        Encoding.UTF8.GetBytes(replyDeviceId+"\0"+conversationId)));
                    root = Path.Combine(root,"threads",key);
                }
            }
            catch(JsonException) { /* A plain prompt may itself contain JSON. */ }
        }
        if (input == "/会话列表" || input == "/sessions" || (input.StartsWith("{") && input.Contains("\"list_sessions\"")))
        {
            var nodeRunner = FindNode();
            var script = Path.Combine(context.ExtensionDirectory, "conversations.cjs");
            if (File.Exists(script))
            {
                var psiList = new ProcessStartInfo(nodeRunner)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    WorkingDirectory = context.ExtensionDirectory
                };
                psiList.ArgumentList.Add(script);
                using var pList = Process.Start(psiList);
                if (pList != null)
                {
                    var stdout = pList.StandardOutput.ReadToEnd();
                    pList.WaitForExit(5000);
                    if (!string.IsNullOrWhiteSpace(stdout)) return stdout.Trim();
                }
            }
            return "[]";
        }
        if (input.StartsWith("/历史") || input.StartsWith("/history") || (input.StartsWith("{") && input.Contains("\"get_history\"")))
        {
            string convId = "";
            var parts = input.Split(' ', 2);
            if (parts.Length > 1) convId = parts[1].Trim();
            else if (input.StartsWith("{"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(input);
                    if (doc.RootElement.TryGetProperty("conversationId", out var cProp))
                        convId = cProp.GetString() ?? "";
                }
                catch {}
            }
            var uuidMatch = System.Text.RegularExpressions.Regex.Match(convId, @"[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (uuidMatch.Success) convId = uuidMatch.Value;
            var nodeRunner = FindNode();
            var script = Path.Combine(context.ExtensionDirectory, "history.cjs");
            if (File.Exists(script))
            {
                var psiHist = new ProcessStartInfo(nodeRunner)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    WorkingDirectory = context.ExtensionDirectory
                };
                psiHist.ArgumentList.Add(script);
                psiHist.ArgumentList.Add(convId);
                using var pHist = Process.Start(psiHist);
                if (pHist != null)
                {
                    var stdout = pHist.StandardOutput.ReadToEnd();
                    pHist.WaitForExit(5000);
                    if (!string.IsNullOrWhiteSpace(stdout)) return stdout.Trim();
                }
            }
            return "[]";
        }
        Directory.CreateDirectory(root);
        var cliAvailable = FindCli();
        if (input == "/部署" || input == "/安装" || input == "/更新" || input == "/检查"
            || (input.Length == 0 && cliAvailable == null))
            return RunSetup(context, input == "/更新" ? "update" : input == "/检查" ? "check" : "install");
        if (input == "/登录") return OpenLoginTerminal(context, cliAvailable);
        var activePath = Path.Combine(root, "active.json");
        var statePath = Path.Combine(root, "session.json");
        if (!File.Exists(statePath) && !string.IsNullOrEmpty(conversationId))
        {
            var uuidMatch = System.Text.RegularExpressions.Regex.Match(conversationId, @"[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (uuidMatch.Success)
            {
                File.WriteAllText(statePath, JsonSerializer.Serialize(new { conversationId = uuidMatch.Value, updatedAt = DateTime.UtcNow }));
            }
        }
        if (input == "/恢复")
        {
            if (!File.Exists(activePath)) return "当前没有需要恢复的任务。";
            using var active = JsonDocument.Parse(File.ReadAllText(activePath));
            var a = active.RootElement;
            var jobFile = Path.Combine(root, a.GetProperty("id").GetString() + ".json");
            if (DateTimeOffset.UtcNow - a.GetProperty("createdAt").GetDateTimeOffset() < TimeSpan.FromMinutes(1)) return "任务刚启动，请稍后检查。";
            if (a.TryGetProperty("pid", out var workerPid) && IsRunning(workerPid.GetInt32())) return "后台进程仍在运行，请先 /取消。";
            if (File.Exists(jobFile))
            {
                using var job = JsonDocument.Parse(File.ReadAllText(jobFile));
                if (job.RootElement.TryGetProperty("pid", out var cliPid) && IsRunning(cliPid.GetInt32())) return "CLI 进程仍在运行，请在电脑确认任务状态。";
            }
            File.Delete(activePath);
            return "已清除中断任务的执行锁，未重复执行任务。可用 /结果 查看最后保存的回复。";
        }
        if (input == "/结果")
            return File.Exists(Path.Combine(root, "last-result.txt"))
                ? File.ReadAllText(Path.Combine(root, "last-result.txt")) : "还没有完成的回复。";
        if (input == "/取消")
        {
            if (!File.Exists(activePath)) return "当前没有运行中的任务。";
            using var active = JsonDocument.Parse(File.ReadAllText(activePath));
            File.WriteAllText(Path.Combine(root, active.RootElement.GetProperty("id").GetString() + ".cancel"), "cancel");
            return "已请求取消；请等待结束后再发送下一条消息。";
        }
        if (input == "/状态" || input.Length == 0)
            return cliAvailable == null
                ? "本机尚未安装反重力 CLI，点击小程序可自动部署，或输入 /部署。"
                : (File.Exists(activePath) ? "反重力任务运行中。" : "反重力 CLI 已安装。")
                + " 首次使用输入 /登录 完成此电脑的 Google 授权。支持 /检查、/部署、/更新、/新建、/取消、/结果、/恢复。";
        if (input == "/新建")
        {
            if (File.Exists(activePath)) return "任务仍在运行，请先取消或等待完成。";
            if (File.Exists(statePath)) File.Move(statePath, Path.Combine(root, "session-" + DateTime.UtcNow.Ticks + ".json"));
            return "已开始新对话，原会话保留在本机。";
        }
        if (input.Length > 32000) throw new ArgumentException("单条问题请控制在 32000 字以内。");
        var cli = cliAvailable;
        if (cli == null) return "本机尚未安装反重力 CLI。请点击小程序或输入 /部署 完成自动部署，再发送消息。";
        var id = Guid.NewGuid().ToString("N");
        try
        {
            using var gate = new FileStream(activePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var content = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id, createdAt = DateTimeOffset.UtcNow }));
            gate.Write(content);
        }
        catch (IOException) when (File.Exists(activePath))
        { return "已有反重力任务在运行，请等待回复或输入 /取消；不会重复提交。"; }
        try
        {
            var jobPath = Path.Combine(root, id + ".json");
            File.WriteAllText(jobPath, JsonSerializer.Serialize(new { id, prompt = input, replyDeviceId, conversationId, cliPath = cli, status = "pending", createdAt = DateTimeOffset.UtcNow }));
            var node = FindNode();
            var psi = new ProcessStartInfo(node) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = context.ExtensionDirectory };
            psi.ArgumentList.Add(Path.Combine(context.ExtensionDirectory, "worker.cjs"));
            psi.ArgumentList.Add(jobPath);
            psi.Environment["AGY_BRIDGE_API"] = context.AgentApiBaseUrl;
            psi.Environment["AGY_BRIDGE_TOKEN"] = context.AgentApiToken;
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("后台任务启动失败。");
            context.Log("Antigravity job started: " + id);
            return "反重力已开始处理，回复会回到发起手机的聊天会话。任务：" + id;
        }
        catch { File.Delete(activePath); throw; }
    }

    private static string? FindCli()
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe");
        if (File.Exists(local)) return local;
        return FindOnPath("agy.exe");
    }

    private static string FindNode()
    {
        var system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe");
        if (File.Exists(system)) return system;
        var portable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenQuickHost", "Runtimes", "node", "node.exe");
        if (File.Exists(portable)) return portable;
        return FindOnPath("node.exe") ?? "node";
    }

    private static string? FindOnPath(string filename)
    {
        foreach (var raw in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var file = Path.Combine(raw.Trim().Trim('"'), filename);
                if (File.Exists(file)) return file;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    private static string RunSetup(YanziActionContext context, string mode)
    {
        var script = Path.Combine(context.ExtensionDirectory, "setup.ps1");
        if (!File.Exists(script)) return "缺少本小程序的 setup.ps1，无法部署反重力。";
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = context.ExtensionDirectory
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add("-Mode");
        psi.ArgumentList.Add(mode);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("部署程序启动失败。");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(300000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            return "反重力环境部署超时，已终止部署进程。请检查网络后输入 /部署 重试。";
        }
        Task.WaitAll(stdout, stderr);
        var output = (stdout.Result + "\n" + stderr.Result).Trim();
        if (output.Length > 1000) output = output.Substring(output.Length - 1000);
        if (process.ExitCode == 0 && (mode == "check" || FindCli() != null))
        {
            var message = output + (mode == "check" ? "" : "\n首次使用请发送 /登录 完成此电脑的 Google 授权。");
            context.ShowDesktopNotification("反重力环境已就绪", "CLI 安装/检查通过；需要时可用 /登录 授权。");
            return message;
        }
        return "反重力环境未就绪（退出码 " + process.ExitCode + "）：\n" + output;
    }

    private static string OpenLoginTerminal(YanziActionContext context, string? cli)
    {
        if (cli == null) return "请先输入 /部署 安装反重力 CLI。";
        var workspace = Path.Combine(context.ExtensionDataDirectory, "workspace");
        Directory.CreateDirectory(workspace);
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = true,
            WorkingDirectory = workspace
        };
        psi.ArgumentList.Add("-NoExit");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add("& '" + cli.Replace("'", "''") + "'");
        Process.Start(psi);
        return "已打开反重力 CLI 授权终端。请在该电脑选择正确的 Google 账号完成登录；燕子不会复制或同步 Google 凭据。";
    }

    private static bool IsRunning(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
        catch (ArgumentException) { return false; }
        catch { return true; }
    }
}
