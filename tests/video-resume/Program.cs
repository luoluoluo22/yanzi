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

        // Fake HA protocol traffic; never modify a real device to test a state_changed event.
        var allowed = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
            { "sensor.cooker_status", "event.cooker_canceled", "event.cooker_finished" };
        string MakeChange(string entity, string state, object attributes) =>
            JsonSerializer.Serialize(new
            {
                id = 1, type = "event",
                @event = new
                {
                    event_type = "state_changed",
                    data = new
                    {
                        entity_id = entity,
                        old_state = (object?)null,
                        new_state = new { state, attributes }
                    }
                }
            });
        bool Parse(string json, out YanziHaEntityUpdate? message)
        {
            using var doc = JsonDocument.Parse(json);
            return YanziHaStateChangedStream.TryParseChange(doc.RootElement, allowed, out message);
        }
        Assert(Parse(MakeChange("sensor.cooker_status", "保温中", new { name = "cooker" }),
            out var statusMessage), "parse subscribed HA state_changed event");
        Assert(statusMessage?.State == "保温中" && !statusMessage.IsBaseline,
            "state_changed is marked realtime");
        Assert(!Parse(MakeChange("sensor.other", "保温中", new { }), out _),
            "other HA entities are filtered");
        Assert(!Parse(MakeChange("sensor.cooker_status", "待机中", new { }).Replace(
            "\"id\":1", "\"id\":9"), out _), "wrong subscription id is ignored");
        using (var delete = JsonDocument.Parse(
            "{\"id\":1,\"type\":\"event\",\"event\":{\"event_type\":\"state_changed\"," +
            "\"data\":{\"entity_id\":\"sensor.cooker_status\",\"new_state\":null}}}"))
            Assert(!YanziHaStateChangedStream.TryParseChange(delete.RootElement, allowed, out _),
                "deleted HA entity cannot trigger");

        var fromEvents = new RiceCookerFinishMonitor(".");
        var updates = new System.Collections.Generic.Dictionary<string,YanziHaEntityUpdate>(
            StringComparer.Ordinal);
        int opened = 0;
        void Feed(string entity, string state, object attributes, DateTimeOffset clock)
        {
            Assert(Parse(MakeChange(entity,state,attributes),out var update) && update != null,
                "parse requested event");
            updates[entity] = update!;
            if (updates.TryGetValue("sensor.cooker_status",out var s)
                && updates.TryGetValue("event.cooker_finished",out var f)
                && updates.TryGetValue("event.cooker_canceled",out var c))
            {
                if (fromEvents.ObserveStatus(s.State,f.State,c.State,
                    RiceCookerFinishMonitor.ExtractRunStatus(c.Attributes),clock)) opened++;
            }
        }
        Feed("event.cooker_finished",cookFinished,new { },now);
        Feed("event.cooker_canceled",now.AddMinutes(-40).ToString("O"),
            new { EventRunStatus = "finisha_start" },now);
        Feed("sensor.cooker_status","保温中",new { },now);
        Assert(opened == 0,"initial event sequence cannot launch video");
        Feed("event.cooker_canceled",cancellation,
            new { EventRunStatus = "cancel_physical_button" },now.AddSeconds(1));
        Assert(opened == 0,"cancel event without standby cannot launch");
        Feed("sensor.cooker_status","待机中",new { },now.AddSeconds(2));
        Assert(opened == 1,"cancel plus standby launched exactly once");
        Feed("sensor.cooker_status","待机中",new { },now.AddSeconds(3));
        Assert(opened == 1,"duplicate state event cannot launch twice");
        Console.WriteLine("HA_EVENT_ROUTING=PASS");

        Console.WriteLine($"STATE_MACHINE_PASS ({_assertions} assertions)");
        if (Array.Exists(args, x => x == "--live"))
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenQuickHost","Extensions","video-resume");
            var monitor = new RiceCookerFinishMonitor(dir);
            var config = monitor.LoadConnection() ?? throw new Exception("missing Home Assistant link");
            using var stop = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(12));
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var subscriber = YanziHaStateChangedStream.SubscribeAsync(
                config.Uri, config.Token, config.EntityIds,
                update =>
                {
                    if (update.IsBaseline && seen.Add(update.EntityId)
                        && seen.Count == config.EntityIds.Length) connected.TrySetResult(true);
                    return Task.CompletedTask;
                },
                (isOpen, message) => { if (!isOpen && message.StartsWith("订阅失败",StringComparison.Ordinal)) Console.WriteLine("STREAM_DIAGNOSTIC=" + message); },
                stop.Token);
            try { await connected.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { stop.Cancel(); await subscriber; }
            Assert(seen.Count == config.EntityIds.Length,"all HA entity baseline states arrived through websocket");
            Console.WriteLine("HOME_ASSISTANT_WS_SUBSCRIPTION=PASS; ENTITIES=" + seen.Count);
            var found = await new HistoryFinder(dir, Console.WriteLine).FindAsync(DateTime.Today);
            Assert(found != null,"local history has playable video");
            Console.WriteLine("RESUME_MATCH=" + found!.Title);
            Console.WriteLine("LIVE_READ_ONLY_PASS");
        }
        Console.WriteLine("VERIFICATION_PASS");
        return 0;
    }
}
