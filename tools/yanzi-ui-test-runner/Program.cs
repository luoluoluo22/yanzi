using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Yanzi.UiTesting;

internal static class Program
{
    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true };
    private const int MaxRunSeconds = 90;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--worker")
            return RunWorker(args);
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine("Usage: dotnet run --project tools/yanzi-ui-test-runner -- --assembly PATH --type Full.Namespace.Scenario [--mode hidden|offscreen] [--out DIRECTORY] [--timeout-ms 15000] [--max-memory-mb 350]");
            Console.WriteLine("Default: hidden desktop, isolated child process, no global pointer/keyboard input.");
            return args.Length == 0 ? 2 : 0;
        }

        try
        {
            var options = Parse(args);
            return RunControllerAsync(options).GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("RUNNER_ERROR=" + error.Message);
            return 2;
        }
    }

    private static Options Parse(string[] args)
    {
        var values = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i+1>=args.Length)
                throw new ArgumentException("Provide --flag VALUE pairs; use --help.");
            if(!values.TryAdd(args[i],args[i+1]))
                throw new ArgumentException("Duplicate option: " + args[i]);
        }
        foreach (var key in values.Keys)
            if (key is not ("--assembly" or "--type" or "--mode" or "--out" or "--timeout-ms" or "--max-memory-mb"))
                throw new ArgumentException("Unknown option: " + key);

        if (!values.TryGetValue("--assembly",out var asm) || string.IsNullOrWhiteSpace(asm))
            throw new ArgumentException("--assembly is required.");
        if (!values.TryGetValue("--type",out var type) || string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("--type is required.");
        asm = Path.GetFullPath(asm);
        if (!File.Exists(asm) || !asm.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Scenario DLL does not exist.",asm);
        var mode = values.GetValueOrDefault("--mode") ?? "hidden";
        if (mode is not ("hidden" or "offscreen")) throw new ArgumentException("--mode must be hidden or offscreen.");
        var timeout = int.TryParse(values.GetValueOrDefault("--timeout-ms") ?? "15000",out var t) ? t : 0;
        var memory = int.TryParse(values.GetValueOrDefault("--max-memory-mb") ?? "350",out var m) ? m : 0;
        if (timeout < 1000 || timeout > MaxRunSeconds*1000)
            throw new ArgumentOutOfRangeException("--timeout-ms","Use 1000-90000 ms.");
        if (memory < 50 || memory > 4096)
            throw new ArgumentOutOfRangeException("--max-memory-mb","Use 50-4096 MB.");
        var output = Path.GetFullPath(values.GetValueOrDefault("--out") ?? Path.Combine("artifacts","ui-tests"));
        Directory.CreateDirectory(output);
        return new Options(asm,type,mode,output,timeout,memory);
    }

    private static async Task<int> RunControllerAsync(Options settings)
    {
        var runDirectory = Path.Combine(settings.Output,
            DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")[..7]);
        Directory.CreateDirectory(runDirectory);
        var beforeWindow = GetForegroundWindow();
        GetCursorPos(out var beforePointer);
        var beforeClipboard = GetClipboardSequenceNumber();
        var startedUtc = DateTimeOffset.UtcNow;
        var started = Stopwatch.StartNew();

        // Always invoke our DLL via dotnet, independent of apphost / dotnet run launch mode.
        var entry = Assembly.GetExecutingAssembly().Location;
        var dotnetExe = Environment.ProcessPath?.EndsWith("dotnet.exe",StringComparison.OrdinalIgnoreCase) == true
            ? Environment.ProcessPath! : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"dotnet","dotnet.exe");
        var arguments = new[]{entry,"--worker",settings.Assembly,settings.Type,settings.Mode,runDirectory};
        IntPtr desktop = IntPtr.Zero;
        IntPtr createdProcessHandle = IntPtr.Zero;
        Process? child = null;
        Task<string> stdout = Task.FromResult(string.Empty);
        Task<string> stderr = Task.FromResult(string.Empty);
        try
        {
            if(settings.Mode=="hidden")
            {
                var desktopName="YanziUITest_"+Guid.NewGuid().ToString("N")[..12];
                desktop=CreateDesktopW(desktopName,null,IntPtr.Zero,0,0x000F01FF,IntPtr.Zero);
                if(desktop==IntPtr.Zero)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"CreateDesktop failed");
                // Bind the PROCESS to the hidden desktop before WPF/COM can create HWNDs.
                // This avoids SetThreadDesktop ERROR_BUSY 170 during managed STA startup.
                var startup = new STARTUPINFO
                {
                    cb=Marshal.SizeOf<STARTUPINFO>(),
                    lpDesktop="WinSta0\\"+desktopName
                };
                var commandLine=new System.Text.StringBuilder(
                    Quote(dotnetExe)+" "+string.Join(" ",arguments.Select(Quote)));
                if(!CreateProcessW(dotnetExe,commandLine,IntPtr.Zero,IntPtr.Zero,false,
                    0x08000000,IntPtr.Zero,Path.GetDirectoryName(entry),
                    ref startup,out var processInformation))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"CreateProcess on hidden desktop failed");
                createdProcessHandle = processInformation.hProcess;
                CloseHandle(processInformation.hThread);
                child=Process.GetProcessById((int)processInformation.dwProcessId);
            }
            else
            {
                var info=new ProcessStartInfo(dotnetExe)
                {
                    UseShellExecute=false,RedirectStandardOutput=true,
                    RedirectStandardError=true,CreateNoWindow=true,
                    WorkingDirectory=Path.GetDirectoryName(entry)!
                };
                foreach(var argument in arguments)info.ArgumentList.Add(argument);
                child=new Process { StartInfo=info };
                child.Start();
                stdout=child.StandardOutput.ReadToEndAsync();
                stderr=child.StandardError.ReadToEndAsync();
            }
            var process=child;
        var peakWorking=0L; var peakPrivate=0L;var cpu=TimeSpan.Zero;
        string? stopReason = null;
        while (!process.HasExited)
        {
            process.Refresh();
            try
            {
                peakWorking = Math.Max(peakWorking, process.WorkingSet64);
                peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
                cpu = process.TotalProcessorTime;
            }
            catch(InvalidOperationException) { }
            if (started.ElapsedMilliseconds > settings.TimeoutMs)
            {
                stopReason="timeout";
                break;
            }
            if(peakWorking > settings.MaxMemoryMb*1024L*1024L)
            {
                stopReason="memory_limit";
                break;
            }
            await Task.Delay(50);
        }
        if (stopReason != null)
        {
            try { process.Kill(entireProcessTree:true); } catch (InvalidOperationException) { }
        }
        await process.WaitForExitAsync();
        started.Stop();
        var exitCode=createdProcessHandle==IntPtr.Zero ? process.ExitCode : 0;
        if(createdProcessHandle!=IntPtr.Zero)
        {
            if(!GetExitCodeProcess(createdProcessHandle,out var nativeExitCode))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Cannot read child exit code.");
            exitCode=unchecked((int)nativeExitCode);
        }

        UiWorkerReport? worker = null;
        var workerPath = Path.Combine(runDirectory, "worker.json");
        var workerReadError = "";
        if (File.Exists(workerPath))
        {
            try { worker=JsonSerializer.Deserialize<UiWorkerReport>(await File.ReadAllTextAsync(workerPath)); }
            catch (Exception error) { workerReadError=error.GetType().Name+":"+error.Message; }
        }
        GetCursorPos(out var afterPointer);
        var windowUnchanged = beforeWindow == GetForegroundWindow();
        var pointerUnchanged = beforePointer.X == afterPointer.X && beforePointer.Y == afterPointer.Y;
        var clipboardUnchanged = beforeClipboard == GetClipboardSequenceNumber();
        var succeeded = stopReason==null && exitCode==0 && worker is { Passed: true };
        var summary = new
        {
            succeeded, scenario=settings.Type, mode=settings.Mode, startedUtc,
            elapsedMs=started.ElapsedMilliseconds, timeoutMs=settings.TimeoutMs,
            maxMemoryMb=settings.MaxMemoryMb, peakWorkingSetMb=Math.Round(peakWorking/1048576d,1),
            peakPrivateMb=Math.Round(peakPrivate/1048576d,1),
            childCpuMs=Math.Round(cpu.TotalMilliseconds,1),
            childExitCode=exitCode,stopReason,workerReadError,
            foregroundWindowUnchanged=windowUnchanged, cursorPositionUnchanged=pointerUnchanged,
            clipboardSequenceUnchanged=clipboardUnchanged,
            worker,
            standardOutput=await stdout, standardError=await stderr
        };
        var summaryPath=Path.Combine(runDirectory,"summary.json");
        await File.WriteAllTextAsync(summaryPath,JsonSerializer.Serialize(summary,ReportJson));
        Console.WriteLine("UI_TEST_RESULT="+(succeeded?"PASS":"FAIL"));
        Console.WriteLine("UI_TEST_MODE="+settings.Mode);
        Console.WriteLine("UI_TEST_ELAPSED_MS="+started.ElapsedMilliseconds);
        Console.WriteLine("UI_TEST_PEAK_WORKINGSET_MB="+Math.Round(peakWorking/1048576d,1));
        Console.WriteLine("UI_TEST_PEAK_PRIVATE_MB="+Math.Round(peakPrivate/1048576d,1));
        Console.WriteLine("UI_TEST_CHECKS="+(worker is null?"unknown":worker.Checks.Count+ "/" +worker.Checks.Count(x=>x.Passed)));
        Console.WriteLine("UI_TEST_STOP_REASON="+(stopReason??"none"));
        Console.WriteLine("UI_TEST_FOREGROUND_UNCHANGED="+windowUnchanged);
        Console.WriteLine("UI_TEST_CURSOR_UNCHANGED="+pointerUnchanged);
        Console.WriteLine("UI_TEST_CLIPBOARD_UNCHANGED="+clipboardUnchanged);
        Console.WriteLine("UI_TEST_REPORT="+summaryPath);
        if (!succeeded)
        {
            var checkFailure=worker?.Checks.Where(x=>!x.Passed).Select(x=>x.Name).FirstOrDefault();
            var reason=new[]{stopReason,worker?.Error,checkFailure,workerReadError}
                .FirstOrDefault(s=>!string.IsNullOrWhiteSpace(s)) ?? "worker_exited_without_passing";
            Console.Error.WriteLine("UI_TEST_FAILURE="+reason);
        }
        return succeeded?0:1;
        }
        finally
        {
            child?.Dispose();
            if(createdProcessHandle!=IntPtr.Zero)CloseHandle(createdProcessHandle);
            if(desktop!=IntPtr.Zero)CloseDesktop(desktop);
        }
    }

    private static string Quote(string value)
    {
        var result=new System.Text.StringBuilder("\"");
        var backslashes=0;
        foreach(var ch in value)
        {
            if(ch=='\\') {backslashes++;continue;}
            if(ch=='"')
            {
                result.Append('\\',2*backslashes+1).Append('"');
                backslashes=0;
                continue;
            }
            result.Append('\\',backslashes).Append(ch);
            backslashes=0;
        }
        result.Append('\\',backslashes*2).Append('"');
        return result.ToString();
    }

    private static int RunWorker(string[] args)
    {
        // Parent uses CreateProcess STARTUPINFO.lpDesktop for isolation;
        // child creates WPF only after the operating system has bound its desktop.
        if(args.Length!=5) return 2;
        var assemblyPath=args[1];var type=args[2];var mode=args[3];var dir=args[4];
        try
        {
            if(Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA)
                throw new InvalidOperationException("WPF test process entry thread must be STA.");
            if(mode=="hidden")
            {
                var desktop=GetThreadDesktop(GetCurrentThreadId());
                var desktopName=new System.Text.StringBuilder(260);
                if(!GetUserObjectInformationW(desktop,2,desktopName,(uint)(desktopName.Capacity*2),out _)
                   || !desktopName.ToString().StartsWith("YanziUITest_",StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Desktop isolation verification failed; refusing to open a test window.");
            }
            return RunStaScenario(assemblyPath,type,mode,dir);
        }
        catch(Exception error)
        {
            WriteFailure(dir,type,mode,error.ToString());
            return 7;
        }
    }

    private static int RunStaScenario(string assemblyFile,string typeName,string mode,string dir)
    {
        var ctx=new UiTestContext(dir,mode);
        string? error=null;
        var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        var dispatcher=app.Dispatcher;
        dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                var assembly=Assembly.LoadFrom(assemblyFile);
                var t=assembly.GetType(typeName,true) ?? throw new TypeLoadException(typeName);
                if(!typeof(IUiTestScenario).IsAssignableFrom(t))
                    throw new InvalidOperationException("Scenario type must implement IUiTestScenario.");
                var scenario=(IUiTestScenario)(Activator.CreateInstance(t)
                    ?? throw new InvalidOperationException("Scenario needs public parameterless constructor."));
                await scenario.RunAsync(ctx,CancellationToken.None);
            }
            catch(Exception e) { error=e.ToString(); }
            finally
            {
                foreach(Window window in app.Windows.Cast<Window>().ToArray())
                    try { window.Close(); }catch { }
                app.Shutdown();
            }
        }),DispatcherPriority.Normal);
        app.Run();
        var report = new UiWorkerReport(typeName,mode,error==null && ctx.Passed,
            ctx.Checks,ctx.Captures,error,DateTimeOffset.UtcNow);
        File.WriteAllText(Path.Combine(dir,"worker.json"),JsonSerializer.Serialize(report,ReportJson));
        return report.Passed?0:1;
    }

    private static void WriteFailure(string dir,string scenario,string mode,string error)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir,"worker.json"), JsonSerializer.Serialize(
            new UiWorkerReport(scenario,mode,false,[],[],error,DateTimeOffset.UtcNow),ReportJson));
    }

    private sealed record Options(string Assembly,string Type,string Mode,string Output,int TimeoutMs,int MaxMemoryMb);

    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpReserved;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDesktop;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, EntryPoint="CreateProcessW", SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application,
        System.Text.StringBuilder commandLine,IntPtr processAttributes,IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,uint flags,
        IntPtr environment,string? currentDirectory,
        ref STARTUPINFO startupInfo,out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr process,out uint exitCode);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint threadId);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="GetUserObjectInformationW",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformationW(
        IntPtr handle,int index,System.Text.StringBuilder output,uint size,out uint needed);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern IntPtr CreateDesktopW(string desktop,string? device,IntPtr devMode,uint flags,uint access,IntPtr attributes);
    [DllImport("user32.dll",SetLastError=true)]
    private static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll",SetLastError=true)]
    private static extern bool CloseDesktop(IntPtr desktop);
}