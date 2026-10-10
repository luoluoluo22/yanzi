using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SD = System.Drawing;

internal static class Program
{
    [STAThread]
    static int Main()
    {
        var root=Path.Combine(AppContext.BaseDirectory,"results");
        Directory.CreateDirectory(root);
        var exe=Environment.ProcessPath!;
        if(Environment.GetCommandLineArgs().Length >= 2 &&
           Environment.GetCommandLineArgs()[1] == "worker")
            return Worker(root, Environment.GetCommandLineArgs()[2]);
        var summary=new List<object>();
        bool pass=true;
        foreach(var mode in new[]{"hidden","offscreen"})
        {
            var process=Process.Start(new ProcessStartInfo(exe){
                UseShellExecute=false,
                Arguments="worker "+mode,
                RedirectStandardError=true,
                RedirectStandardOutput=true,
                CreateNoWindow=true
            })!;
            var sw=Stopwatch.StartNew();
            long maxWs=0,maxPriv=0;TimeSpan cpu=TimeSpan.Zero;
            while(!process.HasExited && sw.ElapsedMilliseconds<12000){
                process.Refresh();maxWs=Math.Max(maxWs,process.WorkingSet64);
                maxPriv=Math.Max(maxPriv,process.PrivateMemorySize64);
                cpu=process.TotalProcessorTime;Thread.Sleep(70);
            }
            if(!process.HasExited)process.Kill();
            process.WaitForExit();sw.Stop();
            var report=File.Exists(Path.Combine(root,mode+".json"))
                ? File.ReadAllText(Path.Combine(root,mode+".json")) : "NO_REPORT";
            Console.WriteLine(mode+" "+JsonSerializer.Serialize(new{
                exitCode=process.ExitCode,
                durationMs=sw.ElapsedMilliseconds,
                maxWsMb=Math.Round(maxWs/1048576d,1),
                maxPrivMb=Math.Round(maxPriv/1048576d,1),
                cpuMs=Math.Round(cpu.TotalMilliseconds,1),
                report,
                output=process.StandardOutput.ReadToEnd(),
                err=process.StandardError.ReadToEnd()}));
            pass &= process.ExitCode==0;
            summary.Add(new{mode,exitCode=process.ExitCode,wsMB=maxWs/1048576d,privateMb=maxPriv/1048576d,report});
        }
        File.WriteAllText(Path.Combine(root,"summary.json"),JsonSerializer.Serialize(new{pass,summary}));
        return pass?0:1;
    }
    static int Worker(string root,string mode)
    {
        if(mode=="offscreen")return RunUi(root,mode);
        var desk=CreateDesktopW("YanziWpfProbe_"+Guid.NewGuid().ToString("N")[..10],
            null,IntPtr.Zero,0,0x000F01FF,IntPtr.Zero);
        if(desk==IntPtr.Zero){Console.WriteLine("CreateDesktopError="+Marshal.GetLastWin32Error());return 11;}
        try{
            int exit=12;
            var thread=new Thread(()=>{
                var ok=SetThreadDesktop(desk);
                if(!ok){Console.WriteLine("SetThreadDesktopError="+Marshal.GetLastWin32Error());return;}
                try{exit=RunUi(root,mode);}catch(Exception e){Console.WriteLine(e);exit=13;}
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();thread.Join();
            return exit;
        }finally{CloseDesktop(desk);}
    }
    static int RunUi(string root,string mode)
    {
        var app=new System.Windows.Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        var left=mode=="offscreen" ? SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth+350 : 70;
        var top=mode=="offscreen" ? SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight+350 : 70;
        var window=new Window {
            Title="Yanzi.WPF.HiddenDesktop.Probe",
            Width=600,Height=260,
            Left=left,Top=top,
            WindowStartupLocation=WindowStartupLocation.Manual,
            ShowInTaskbar=false,ShowActivated=false,
            Background=System.Windows.Media.Brushes.White
        };
        var label=new TextBlock {
            Text="WPF UI: WAITING",
            FontFamily=new System.Windows.Media.FontFamily("Arial"),
            FontSize=26,FontWeight=FontWeights.Bold,
            Foreground=System.Windows.Media.Brushes.Black,
            Margin=new Thickness(20,20,20,25)
        };
        var button=new System.Windows.Controls.Button {
            Content="CLICK WPF TEST",
            Width=340,Height=55,
            HorizontalAlignment=System.Windows.HorizontalAlignment.Left,
            Background=System.Windows.Media.Brushes.LightGray,
            FontSize=20
        };
        var panel=new StackPanel { Margin=new Thickness(20) };
        panel.Children.Add(label);panel.Children.Add(button);
        window.Content=panel;
        var clicked=false;
        button.Click += (_,_)=>{
            clicked=true;label.Text="WPF UI: CLICKED";
            label.Foreground=System.Windows.Media.Brushes.DarkGreen;
            button.Content="WPF BUTTON VERIFIED";
            button.Background=System.Windows.Media.Brushes.LightGreen;
        };
        bool printed=false;int coloredPixels=0;bool rendered=false;int renderedBytes=0;
        var timer1=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(400)};
        var timer2=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(1100)};
        timer1.Tick += (_,_)=>{
            timer1.Stop();
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            timer2.Start();
        };
        timer2.Tick += (_,_)=>{
            timer2.Stop();
            window.UpdateLayout();
            try{
                using var b=new SD.Bitmap((int)window.ActualWidth,(int)window.ActualHeight);
                using(var g=SD.Graphics.FromImage(b)){
                    g.Clear(SD.Color.White);
                    var hdc=g.GetHdc();
                    try { printed=PrintWindow(new System.Windows.Interop.WindowInteropHelper(window).Handle,hdc,0);}
                    finally {g.ReleaseHdc(hdc);}
                }
                b.Save(Path.Combine(root,mode+"-printwindow.png"),ImageFormat.Png);
                for(var y=30;y<b.Height-30;y+=4)for(var x=30;x<b.Width-30;x+=4){
                    var c=b.GetPixel(x,y);
                    if(c.R<170&&c.G<170&&c.B<170) coloredPixels++;
                }
                // Visual-tree render fallback is not a desktop screenshot.
                var r=new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);
                r.Render(window);
                var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(r));
                using var file=File.Create(Path.Combine(root,mode+"-visualtree.png"));
                encoder.Save(file);
                renderedBytes=(int)file.Length;
                rendered=renderedBytes>2000;
            }catch(Exception e){Console.WriteLine("captureError="+e.GetType().Name+":"+e.Message);}
            window.Close();
            app.Shutdown();
        };
        window.Loaded += (_,_)=>timer1.Start();
        window.Show();
        app.Run();
        var outside=left>=SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth;
        var report=new{mode,clicked,printed,coloredPixels,rendered,renderedBytes,outside,windowLeft=left,windowTop=top};
        File.WriteAllText(Path.Combine(root,mode+".json"),JsonSerializer.Serialize(report));
        return clicked && (printed&&coloredPixels>60 || rendered) && (mode!="offscreen"||outside) ? 0:9;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern IntPtr CreateDesktopW(string name,string? device,IntPtr devMode,uint flags,uint access,IntPtr attr);
    [DllImport("user32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool SetThreadDesktop(IntPtr desk);
    [DllImport("user32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool CloseDesktop(IntPtr desk);
    [DllImport("user32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool PrintWindow(IntPtr hwnd,IntPtr hdc,uint flags);
}
