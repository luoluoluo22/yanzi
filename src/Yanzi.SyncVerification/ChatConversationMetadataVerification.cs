using System.Reflection;
using System.Text.Json;
using OpenQuickHost;
using OpenQuickHost.Sync;

internal static class ChatConversationMetadataVerification
{
    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"YanziChatObjects-"+Guid.NewGuid().ToString("N"));
        using var scope=(IDisposable)typeof(HostAssets).GetMethod("UseIsolatedDataRootForVerification",
            BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[root])!;
        var provider=YanziChatCapabilityProvider.GetProviders().Single(p=>p.Name=="chat.send");
        if(!provider.InputSchema.GetProperty("properties").TryGetProperty("conversationId",out _))
            throw new Exception("chat.send does not declare conversation metadata.");
        var id=Guid.NewGuid().ToString("N");
        var job=DesktopChatOutbox.Enqueue("fixture","desktop-fixture",id,"text","reply",null,"android-fixture","agent:antigravity:desktop-fixture");
        var restored=DesktopChatOutbox.Pending("fixture").Single();
        if(restored.ConversationId!=job.ConversationId || restored.TargetDeviceId!=job.TargetDeviceId)
            throw new Exception("Durable retry lost conversation or recipient identity.");
        var old=JsonSerializer.Deserialize<DesktopChatJob>("""{"Id":"legacy","Account":"fixture","Source":"desktop-fixture","Kind":"text","Text":"old","FilePath":null,"CreatedAt":"2026-10-14T00:00:00Z"}""")!;
        if(old.ConversationId!=null || old.TargetDeviceId!=null) throw new Exception("Old outbox records are incompatible.");
        DesktopChatOutbox.Complete(job,"fixture-message");
        if(DesktopChatOutbox.Pending("fixture").Count!=0) throw new Exception("Completed conversation job remained pending.");
        Console.WriteLine("CHAT_CONVERSATION_METADATA=PASSED (schema, durable recipient and thread identity, legacy records, completion)");
    }
}
