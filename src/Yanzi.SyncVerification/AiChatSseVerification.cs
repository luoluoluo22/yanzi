using System.IO;
using System.Text;
using OpenQuickHost;

internal static class AiChatSseVerification
{
    public static async Task RunAsync()
    {
        static MemoryStream Data(string source) => new(Encoding.UTF8.GetBytes(source));
        static void Check(bool ok, string message)
        {
            if (!ok) throw new Exception("AiChatSseVerification: " + message);
        }

        var chunks = new List<string>();
        await using (var stream = Data(
            ": keepalive\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"你好\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"世界\"},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: [DONE]\n\n"))
        {
            var result = await AiChatSseParser.ReadAsync(stream, chunks.Add);
            Check(result == "你好世界" && chunks.SequenceEqual(new[] { "你好", "世界" }),
                "must reassemble SSE deltas in order");
        }
        await using (var stream = Data("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n"))
        {
            try
            {
                await AiChatSseParser.ReadAsync(stream, _ => { });
                throw new Exception("Truncated SSE was accepted");
            }
            catch (IOException) { }
        }
        await using (var stream = Data("data: {\"error\":{\"message\":\"upstream failed\"}}\n\n"))
        {
            try
            {
                await AiChatSseParser.ReadAsync(stream, _ => { });
                throw new Exception("SSE error was accepted");
            }
            catch (IOException e) { Check(e.Message.Contains("upstream failed"), "must propagate provider error"); }
        }
        await using (var stream = Data(
            "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}\n\n"))
        {
            Check(await AiChatSseParser.ReadAsync(stream, _ => { }) == "ok",
                "accept finish_reason even if provider omits [DONE]");
        }
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await using (var stream = Data("data: [DONE]\n\n"))
        {
            try
            {
                await AiChatSseParser.ReadAsync(stream, _ => { }, canceled.Token);
                throw new Exception("Canceled SSE was accepted");
            }
            catch (OperationCanceledException) { }
        }
        Console.WriteLine("AI_CHAT_SSE_VERIFICATION_PASSED");
    }
}
