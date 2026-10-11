using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

public static class Program
{
    static int _assertions;
    static void Assert(bool success, string scenario)
    {
        _assertions++;
        if (!success) throw new Exception("FAILED: " + scenario);
    }

    public static async Task<int> Main(string[] args)
    {
        var now = DateTimeOffset.Now;
        var cookFinished = now.AddMinutes(-31).ToString("O");
        var cancellation = now.AddSeconds(1).ToString("O");
        var warm = new RiceCookerFinishMonitor(".");
        Assert(!warm.ObserveStatus("烹饪中",cookFinished,cancellation,"cancel_payload",now),"cooking is quiet");
        Assert(!warm.ObserveStatus("烹饪完成",cookFinished,cancellation,"cancel_payload",now),"cooking finished is quiet");
        Assert(!warm.ObserveStatus("保温中",cookFinished,now.AddMinutes(-30).ToString("O"),"finisha_event",now),"keep warm is quiet");
        Assert(!warm.ObserveStatus("保温中",cookFinished,now.AddMinutes(-30).ToString("O"),"finisha_event",now),"warm repeat is quiet");
        Assert(warm.ObserveStatus("待机中",cookFinished,cancellation,"cancel_01060200000002e1001e",now.AddSeconds(2)),"native cancel then standby fires");
        Assert(!warm.ObserveStatus("待机中",cookFinished,cancellation,"cancel_01060200000002e1001e",now.AddSeconds(3)),"no replay");

        var automatic = new RiceCookerFinishMonitor(".");
        Assert(!automatic.ObserveStatus("保温中",cookFinished,"","",now),"arm auto test");
        Assert(!automatic.ObserveStatus("待机中",cookFinished,"","",now.AddSeconds(2)),"ordinary keepwarm ending does not fire");

        var unrelated = new RiceCookerFinishMonitor(".");
        Assert(!unrelated.ObserveStatus("保温中",cookFinished,"","",now),"arm unrelated test");
        Assert(!unrelated.ObserveStatus("待机中",cookFinished,cancellation,"finisha_01060200000002e1001e",now.AddSeconds(2)),"finished event not cancel");

        var oldCancel = new RiceCookerFinishMonitor(".");
        Assert(!oldCancel.ObserveStatus("保温中",cookFinished,"","",now),"arm stale cancel test");
        Assert(!oldCancel.ObserveStatus("待机中",cookFinished,now.AddMinutes(-10).ToString("O"),"cancel_old",now.AddSeconds(2)),"old cancel cannot fire");

        var offline = new RiceCookerFinishMonitor(".");
        Assert(!offline.ObserveStatus("保温中",cookFinished,"","",now),"arm offline");
        offline.ResetConnection();
        Assert(!offline.ObserveStatus("待机中",cookFinished,cancellation,"cancel_now",now.AddSeconds(2)),"offline gap never fires");

        var staleCook = new RiceCookerFinishMonitor(".");
        var oldFinish = now.AddDays(-2).ToString("O");
        Assert(!staleCook.ObserveStatus("保温中",oldFinish,"","",now),"old cook cannot arm");
        Assert(!staleCook.ObserveStatus("待机中",oldFinish,cancellation,"cancel_new",now.AddSeconds(2)),"old cook never fires");

        var ongoing = new RiceCookerFinishMonitor(".");
        Assert(!ongoing.ObserveStatus("保温中",cookFinished,"","",now),"arm ongoing");
        Assert(!ongoing.ObserveStatus("烹饪中",cookFinished,"","",now.AddSeconds(1)),"new cook resets");
        Assert(!ongoing.ObserveStatus("待机中",cookFinished,cancellation,"cancel_new",now.AddSeconds(2)),"cooking to standby is quiet");

        var beforeWarm = new RiceCookerFinishMonitor(".");
        Assert(!beforeWarm.ObserveStatus("保温中",cookFinished,"","",now),"arm pre-warm");
        Assert(!beforeWarm.ObserveStatus("待机中",cookFinished,now.AddSeconds(-15).ToString("O"),"cancel_previous",now.AddSeconds(2)),"cancel must follow observed warm");

        var settings = JsonDocument.Parse("{\"EventRunStatus信息\":\"cancel_01060200000002e1001e\"}");
        Assert(RiceCookerFinishMonitor.ExtractRunStatus(settings.RootElement).StartsWith("cancel_",StringComparison.Ordinal),"read native MiOT cancellation payload");

        Console.WriteLine($"STATE_MACHINE_PASS ({_assertions} assertions)");
        if (Array.Exists(args, x => x == "--live"))
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenQuickHost","Extensions","video-resume");
            var monitor = new RiceCookerFinishMonitor(dir);
            Assert(!await monitor.CheckAsync(),"live baseline must not replay");
            Assert(!await monitor.CheckAsync(),"live state remains quiet");
            var status = (string?)typeof(RiceCookerFinishMonitor)
                .GetField("_lastStatus",BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(monitor);
            Console.WriteLine("HOME_ASSISTANT_STATUS=" + status);
            var found = await new HistoryFinder(dir, Console.WriteLine).FindAsync(DateTime.Today);
            Assert(found != null,"local history has playable video");
            Console.WriteLine("RESUME_MATCH=" + found!.Title);
            Console.WriteLine("LIVE_READ_ONLY_PASS");
        }
        Console.WriteLine("VERIFICATION_PASS");
        return 0;
    }
}
