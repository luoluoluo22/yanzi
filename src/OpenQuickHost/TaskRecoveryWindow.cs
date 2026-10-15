using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenQuickHost.Sync;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using MessageBox = System.Windows.MessageBox;

using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Orientation = System.Windows.Controls.Orientation;
namespace OpenQuickHost;

internal sealed class TaskRecoveryWindow : Window
{
    private readonly MainWindow _main;
    private readonly CloudSyncClient _client;
    private readonly string _account;
    private readonly StackPanel _rows = new();
    private readonly TextBlock _status = new() { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,10,0,14) };
    private bool _recovery, _busy;
    public TaskRecoveryWindow(MainWindow main, CloudSyncClient client)
    {
        _main=main;_client=client;_account=client.TaskJournalAccount;
        Title="任务中心＋数据恢复"; Width=800;Height=680;MinWidth=560;MinHeight=420;
        Background=new SolidColorBrush(Color.FromRgb(15,20,29)); Foreground=Brushes.WhiteSmoke;
        var page=new DockPanel {Margin=new Thickness(20)};
        var top=new StackPanel(); DockPanel.SetDock(top,Dock.Top);
        var tabs=new StackPanel {Orientation=Orientation.Horizontal};
        tabs.Children.Add(Action("任务中心",()=>{_recovery=false;return RefreshAsync();}));
        tabs.Children.Add(Action("数据恢复",()=>{_recovery=true;return RefreshAsync();}));
        tabs.Children.Add(Action("刷新",RefreshAsync));top.Children.Add(tabs);top.Children.Add(_status);page.Children.Add(top);
        page.Children.Add(new ScrollViewer {Content=_rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}); Content=page;
        Loaded+=async(_,_)=>{_busy=true;try{await RefreshAsync();}catch(Exception error){_status.Text="未完成："+error.Message;}finally{_busy=false;}};
    }
    private void RequireAccount() { if(_client.CurrentUserId==null||_client.TaskJournalAccount!=_account) throw new InvalidOperationException("账号已改变，请关闭此窗口后重新打开。"); }
    private Button Action(string title,Func<Task> run)
    {
        var button=new Button {Content=title,Margin=new Thickness(0,0,8,8),Padding=new Thickness(12,7,12,7),Background=new SolidColorBrush(Color.FromRgb(35,48,66)),Foreground=Brushes.WhiteSmoke};
        button.Click+=async(_,_)=>{if(_busy)return;_busy=true;button.IsEnabled=false;try{RequireAccount();await run();}catch(Exception error){_status.Text="未完成："+error.Message;}finally{_busy=false;button.IsEnabled=true;}};return button;
    }
    private StackPanel Card(string title,string detail)
    {
        var content=new StackPanel {Margin=new Thickness(14)};
        content.Children.Add(new TextBlock {Text=title,FontSize=16,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
        content.Children.Add(new TextBlock {Text=detail,Foreground=Brushes.LightSlateGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,10)});
        _rows.Children.Add(new Border {Child=content,Background=new SolidColorBrush(Color.FromRgb(24,32,45)),CornerRadius=new CornerRadius(10),Margin=new Thickness(0,0,0,10)});return content;
    }
    private async Task RefreshAsync()
    {
        RequireAccount();
        if(_recovery) {
            _status.Text="正在读取云端历史数据…";
            var objects=new Dictionary<string,CloudSyncObjectRecord>();var response=await _client.GetSyncObjectsAsync();long cursor=0;
            while(true) {
                RequireAccount();if(!response.Ok||response.UserId!=_client.CurrentUserId)throw new InvalidDataException("账号或同步响应无效");
                foreach(var item in response.Objects)objects[item.ObjectId]=item;
                if(!response.HasMore)break;
                if(response.CursorRevision<=cursor)throw new InvalidDataException("分页没有前进");
                cursor=response.CursorRevision;response=await _client.GetSyncChangesAsync(cursor,500);
            }
            RequireAccount();_rows.Children.Clear();_status.Text="选择数据查看历史并预览。恢复生成新版本；文件引用不能恢复未备份的原文件。";
            foreach(var item in objects.Values.OrderBy(item=>item.ObjectId)) {
                var card=Card(item.ObjectId+(item.Deleted?" · 已删除":""),$"版本 {item.Revision} · {item.UpdatedAtUtc}");
                card.Children.Add(Action("查看历史",()=>{
                    RequireAccount();var history=new CloudSyncHistoryWindow(_main,_client,item.ObjectId,item.ObjectId,item.Revision){Owner=this};history.ShowDialog();return RefreshAsync();
                }));
            }
            if(objects.Count==0)Card("暂无云端数据","同步产生的历史会显示在这里。");
        } else {
            var tasks=PlatformTaskJournal.List(_account);_rows.Children.Clear();_status.Text=$"共 {tasks.Count} 条本机任务记录。查询只读取原任务，不会重新执行。";
            foreach(var task in tasks) {
                var card=Card(task.Title+" · "+PlatformTaskJournal.Label(task.Status),$"{task.CreatedAt.LocalDateTime:g} · {task.Kind} · {task.Target}");
                var detail=new TextBox {Text=$"操作标识：{task.Id}\n消息标识：{task.MessageId}\n{task.Result}",IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=160,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=Brushes.Transparent,Foreground=Brushes.LightGray,BorderThickness=new Thickness(0)};card.Children.Add(detail);
                if(!PlatformTaskJournal.Terminal(task.Status)&&!string.IsNullOrEmpty(task.MessageId)&&task.MessageId.StartsWith("msg_"))
                    card.Children.Add(Action("查询原任务",async()=>{_status.Text="正在查询原任务…";await _client.QueryTaskMessageAsync(task.MessageId);RequireAccount();await RefreshAsync();}));
            }
            if(tasks.Count==0)Card("暂无任务记录","新版发送的消息、文件和接收到的电脑执行请求会显示在这里。");
        }
    }
}
