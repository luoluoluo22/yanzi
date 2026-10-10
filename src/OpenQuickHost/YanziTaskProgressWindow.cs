using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Application = System.Windows.Application;
using System.Windows.Markup;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using IconPath = System.Windows.Shapes.Path;
using FontFamily = System.Windows.Media.FontFamily;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace OpenQuickHost;

internal sealed class YanziTaskProgressWindow : Window
{
    private static YanziTaskProgressWindow? Current;
    internal static bool InterruptAvailable
    {
        get
        {
            if(Application.Current==null)return true;
            if(Current==null||Current._hook==IntPtr.Zero)return false;
            // A hook on Default cannot receive Esc while Windows uses a secure input desktop.
            var desktop=OpenInputDesktop(0,false,1);
            if(desktop==IntPtr.Zero)return false;
            CloseDesktop(desktop);return true;
        }
    }
    private readonly FrameworkElement _surface;
    private readonly DispatcherTimer _dismiss = new() { Interval=TimeSpan.FromSeconds(2) };
    private readonly HookProc _callback;
    private IntPtr _hook;
    private bool _escDown;
    private long _lastEsc;
    private string? _taskId;
    private delegate IntPtr HookProc(int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr SetWindowsHookEx(int id,HookProc proc,IntPtr module,uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd,int index,int value);
    private YanziTaskProgressWindow()
    {
        Title="燕子 · 任务进度";Width=460;Height=164;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
        ShowInTaskbar=false;ShowActivated=false;Topmost=true;AllowsTransparency=true;Background=Brushes.Transparent;
        FontFamily=new FontFamily("Microsoft YaHei UI");UseLayoutRounding=true;
        _surface=(FrameworkElement)XamlReader.Parse("""
        <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Margin="4" CornerRadius="36" Background="#191E23" BorderBrush="#39424C" BorderThickness="1" Padding="20,16,20,12">
          <Grid>
            <Grid.RowDefinitions><RowDefinition Height="72"/><RowDefinition Height="6"/><RowDefinition Height="*"/></Grid.RowDefinitions>
            <Grid>
              <Grid.ColumnDefinitions><ColumnDefinition Width="64"/><ColumnDefinition Width="*"/><ColumnDefinition Width="92"/></Grid.ColumnDefinitions>
              <Border x:Name="AppTile" Width="52" Height="52" CornerRadius="15" Background="#154F3E" HorizontalAlignment="Left" VerticalAlignment="Top" Margin="0,4,0,0">
                <Viewbox Width="30" Height="30"><Path x:Name="AppIcon" Fill="#D8FFF0"/></Viewbox>
              </Border>
              <StackPanel Grid.Column="1">
                <TextBlock x:Name="Identity" FontSize="12" Foreground="#A7B2BE" Text="燕子 · 微信"/>
                <TextBlock x:Name="Action" Margin="0,3,4,0" FontSize="20" FontWeight="SemiBold" Foreground="#F5F7FA" TextTrimming="CharacterEllipsis"/>
                <TextBlock x:Name="Target" Margin="0,3,4,0" FontSize="13" Foreground="#BAC4CE" TextTrimming="CharacterEllipsis"/>
              </StackPanel>
              <Border x:Name="StatusPill" Grid.Column="2" CornerRadius="16" Height="32" VerticalAlignment="Center" Background="#162F4C" BorderBrush="#315D8B" BorderThickness="1">
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" VerticalAlignment="Center">
                  <Viewbox Width="15" Height="15" Margin="0,0,6,0"><Path x:Name="StatusIcon" Fill="#66B7FF" RenderTransformOrigin="0.5,0.5"><Path.RenderTransform><RotateTransform/></Path.RenderTransform></Path></Viewbox>
                  <TextBlock x:Name="StatusText" FontSize="12" FontWeight="SemiBold" Foreground="#66B7FF"/>
                </StackPanel>
              </Border>
            </Grid>
            <Border x:Name="Track" Grid.Row="1" Background="#303B49" CornerRadius="3" ClipToBounds="True">
              <Border x:Name="Progress" Width="116" HorizontalAlignment="Left" Background="#66B7FF" CornerRadius="3"><Border.RenderTransform><TranslateTransform/></Border.RenderTransform></Border>
            </Border>
            <Border Grid.Row="2" Height="1" Background="#39424C" VerticalAlignment="Top" Margin="0,8,0,0"/>
            <Grid Grid.Row="2" Margin="0,10,0,0">
              <Grid.ColumnDefinitions><ColumnDefinition Width="20"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
              <Viewbox Width="15" Height="15" VerticalAlignment="Center" HorizontalAlignment="Left"><Path x:Name="StageIcon" Fill="#A7B2BE"/></Viewbox>
              <TextBlock x:Name="Stage" Grid.Column="1" VerticalAlignment="Center" Foreground="#A7B2BE" FontSize="12" Margin="0,0,8,0" TextTrimming="CharacterEllipsis"/>
              <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
                <Border x:Name="KeyBadge" CornerRadius="5" BorderBrush="#65717F" BorderThickness="1" Padding="5,2" Margin="0,0,6,0"><TextBlock Text="Esc ×2" Foreground="#C1CBD5" FontSize="11"/></Border>
                <TextBlock x:Name="Hint" Foreground="#C1CBD5" FontSize="11" VerticalAlignment="Center"/>
              </StackPanel>
            </Grid>
          </Grid>
        </Border>
        """);
        Content=_surface;
        Left=SystemParameters.WorkArea.Left+20;Top=SystemParameters.WorkArea.Bottom-Height-20;
        _dismiss.Tick+=(_,_)=>{_dismiss.Stop();if(ReferenceEquals(Current,this)){Close();Current=null;}};
        SourceInitialized+=(_,_)=>{var h=new WindowInteropHelper(this).Handle;SetWindowLong(h,-20,GetWindowLong(h,-20)|0x08000000|0x20);};
        _callback=Keyboard;_hook=SetWindowsHookEx(13,_callback,GetModuleHandle(null),0);
        if(_hook==IntPtr.Zero)HostAssets.AppendLog("Task interruption hook unavailable; sending will be blocked before submission.");
        Closed+=(_,_)=>{_dismiss.Stop();if(_hook!=IntPtr.Zero)UnhookWindowsHookEx(_hook);_hook=IntPtr.Zero;};
    }
    private T Element<T>(string name) where T:FrameworkElement => (T)_surface.FindName(name);
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    private void SetIcon(string name,string key) => Element<IconPath>(name).Data=ExtensionIconLibrary.ResolveVectorIcon("mdi:"+key);
    internal void RenderRecord(YanziTaskRecord record,int count=1)
    {
        _dismiss.Stop();
        bool terminal=record.Status!="running";
        var cancellableId=terminal||record.Submitted||record.CancelRequested||!record.CanCancel?null:record.TaskId;
        if(_taskId!=cancellableId){_lastEsc=0;_escDown=false;}
        _taskId=cancellableId;
        string color,state,icon;
        if(record.Status=="failed") (color,state,icon)=("#FF6B75","失败","alert-circle-outline");
        else if(record.Status=="cancelled"||record.CancelRequested) (color,state,icon)=("#A7B2BE",terminal?"已中断":"中断中","pause");
        else if(record.Status is "pending" or "unknown" || record.Submitted&&!terminal) (color,state,icon)=("#FFBF5C","待确认","clock");
        else if(terminal) (color,state,icon)=("#51D998","已完成","check-circle-outline");
        else (color,state,icon)=("#66B7FF","执行中","loading");
        bool phone=record.Operation=="chat.send";
        bool sending=phone||record.Operation.Contains(".send",StringComparison.Ordinal);
        Element<TextBlock>("Identity").Text="燕子 · "+(phone?"手机消息":"微信")+(count>1?$" · {count} 项任务":"");
        Element<TextBlock>("Action").Text=terminal?record.Status switch {"completed"=>sending?"发送完成":"任务完成","cancelled"=>"任务已中断","failed"=>sending?"发送失败":"任务失败",_=>"等待回执确认"}
            :record.CancelRequested?"正在中断任务":record.Submitted?"正在核对回执":record.Operation switch {
                "wechat.messages.sendFile"=>"正在发送文件","wechat.messages.sendImage"=>"正在发送图片","chat.send"=>"正在发送手机消息",
                "wechat.messages.sendText"=>"正在发送消息","wechat.contacts.search"=>"正在搜索联系人","wechat.chats.open"=>"正在打开会话",
                "wechat.messages.list"=>"正在读取聊天记录","wechat.messages.latest"=>"正在读取最新消息","wechat.chats.inspectCurrent"=>"正在识别聊天界面",_=>"正在执行任务"};
        Element<TextBlock>("Target").Text=record.Target.Length==0?(phone?"所有在线手机":"当前会话"):record.Target;
        SetIcon("AppIcon",phone?"cellphone":"wechat");
        Element<Border>("AppTile").Background=Brush(phone?"#193A56":"#154F3E");
        Element<TextBlock>("StatusText").Text=state;Element<TextBlock>("StatusText").Foreground=Brush(color);
        Element<IconPath>("StatusIcon").Fill=Brush(color);SetIcon("StatusIcon",icon);
        Element<Border>("StatusPill").BorderBrush=Brush(color);
        var tinted=(Color)ColorConverter.ConvertFromString(color);tinted.A=30;Element<Border>("StatusPill").Background=new SolidColorBrush(tinted);
        Element<TextBlock>("Stage").Text=record.Stage;
        SetIcon("StageIcon","file-document-outline");
        Element<Border>("KeyBadge").Visibility=_taskId!=null?Visibility.Visible:Visibility.Collapsed;
        Element<TextBlock>("Hint").Text=_taskId!=null?"中断":record.Submitted?"已提交，无法撤回":terminal?"即将关闭":"保留草稿";
        var rotation=(RotateTransform)Element<IconPath>("StatusIcon").RenderTransform;
        rotation.BeginAnimation(RotateTransform.AngleProperty,null);
        if(!terminal&&!record.CancelRequested&&!record.Submitted)
            rotation.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(0,360,TimeSpan.FromSeconds(1.5)){RepeatBehavior=RepeatBehavior.Forever});
        var progress=Element<Border>("Progress");progress.Background=Brush(color);
        var translation=(TranslateTransform)progress.RenderTransform;translation.BeginAnimation(TranslateTransform.XProperty,null);
        progress.Width=terminal?410:116;
        if(!terminal)translation.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(-116,410,TimeSpan.FromSeconds(1.8)){RepeatBehavior=RepeatBehavior.Forever});
    }
    public static void Refresh(YanziTaskRecord? finished=null)
    {
        var dispatcher=Application.Current?.Dispatcher;if(dispatcher==null||dispatcher.HasShutdownStarted)return;
        dispatcher.BeginInvoke(new Action(()=>
        {
            var running=YanziTaskService.Running();
            if(running.Length==0&&finished==null){return;}
            Current??=new YanziTaskProgressWindow();
            Current.RenderRecord(running.Length>0?running.Last():finished!,running.Length);
            if(!Current.IsVisible)Current.Show();
            if(running.Length==0)Current._dismiss.Start();
        }));
    }
    private IntPtr Keyboard(int code,IntPtr message,IntPtr data)
    {
        if(code>=0&&_taskId!=null&&Marshal.ReadInt32(data)==27)
        {
            int msg=message.ToInt32();
            if(msg==0x101||msg==0x105)_escDown=false;
            else if((msg==0x100||msg==0x104)&&!_escDown)
            {
                _escDown=true;long now=Environment.TickCount64;
                if(_lastEsc!=0&&now-_lastEsc<=700){if(_taskId!=null)YanziTaskService.RequestCancelFromKeyboard(_taskId);_lastEsc=0;}
                else _lastEsc=now;
            }
            return new IntPtr(1);
        }
        return CallNextHookEx(_hook,code,message,data);
    }
}
