using System.IO;
using System.Text.Json;

namespace OpenQuickHost;

public partial class MainWindow
{
    private async Task RunAiToolTurnAsync(AiChatTopic topic, AiRequestProfile profile, CancellationToken token)
    {
        var cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var shell = new AiShellSessionManager(cwd);
        var transcript = new AiToolTranscript();
        var cards = new Dictionary<AiToolExecution, AiChatMessage>();
        var transport = new AiToolCompletionClient();
        AiChatMessage? current = null;
        try
        {
            var finished = await AiAgentTurnRunner.RunAsync(
                BuildAiRequestMessages(AiConversationMode.Agent, topic),
                async (messages, cancellation) =>
                {
                    if (GeminiNativeChatClient.IsGeminiApi(profile.BaseUrl))
                    {
                        var text = await GeminiNativeChatClient.SendAsync(_aiHttpClient, profile.BaseUrl,
                            profile.ApiKey, profile.Model, AiToolCompletionClient.ToLegacyMessages(messages), cancellation,
                            stage => AiChatDiagnostics.Record(topic.Id, profile.Model, stage));
                        return AiToolRegistry.FromLegacy(text);
                    }
                    return await transport.SendAsync(_aiHttpClient, BuildAiChatEndpoint(profile.BaseUrl),
                        profile.ApiKey, profile.Model, messages, profile.IsChatGptPlan, cancellation);
                },
                async (call, cancellation) =>
                {
                    using var args = JsonDocument.Parse(call.Arguments);
                    if (args.RootElement.ValueKind != JsonValueKind.Object)
                        throw new ArgumentException("Tool arguments must be a JSON object.");
                    if (call.Name is "exec_command" or "execute_command" or "write_stdin" or "terminate_command")
                        return await shell.InvokeAsync(call.Name, args.RootElement, cancellation,
                            preview => transcript.Find(call.Id)?.SetPreview(preview));
                    return await ExecuteToolAsync(new ToolCallInfo
                    { ToolName = call.Name, RawPayload = args.RootElement.Clone() }, cancellation);
                },
                progress =>
                {
                    switch (progress.Phase)
                    {
                        case "request":
                            current = new AiChatMessage(false, string.Empty) { IsStreaming = true };
                            AppendAiTopicMessage(topic, current);
                            SetAiTopicStatus(topic, "AI 正在生成…");
                            break;
                        case "answer":
                            current!.Text = progress.Text;
                            current.IsStreaming = false;
                            break;
                        case "tool_start":
                            // Keep model commentary once; replace empty placeholders
                            // with the operation's stable card, even across polls.
                            if (current is { IsStreaming: true })
                            {
                                current.IsStreaming = false;
                                if (string.IsNullOrWhiteSpace(progress.Text))
                                {
                                    topic.Messages.Remove(current);
                                    if (ReferenceEquals(_selectedAiChatTopic, topic)) _aiChatMessages.Remove(current);
                                }
                                else current.Text = progress.Text;
                            }
                            var execution = transcript.Begin(progress.Call!);
                            execution.CancelTurn = () =>
                            {
                                if (_aiRequestTracker.TryGetActive(topic.Id, out var active)) active.Cancel();
                            };
                            if (!cards.TryGetValue(execution, out current))
                            {
                                current = new AiChatMessage(false, execution.Command)
                                { IsToolCall = true, ToolName = execution.Name, ToolExecution = execution };
                                cards.Add(execution, current);
                                AppendAiTopicMessage(topic, current);
                            }
                            current.IsToolRunning = true;
                            SetAiTopicStatus(topic, $"AI 正在调用工具：{progress.Call!.Name}…");
                            break;
                        case "tool_result":
                            var completed = transcript.Apply(progress.Call!, progress.Text);
                            if (!cards.TryGetValue(completed, out current))
                            {
                                current = new AiChatMessage(false, completed.Command)
                                { IsToolCall = true, ToolName = completed.Name, ToolExecution = completed };
                                cards.Add(completed, current);
                                AppendAiTopicMessage(topic, current);
                            }
                            current.ToolFeedback = completed.BuildContext();
                            current.IsToolRunning = completed.IsRunning;
                            topic.NotifyMessagesChanged();
                            // Persist at tool boundaries, not every output chunk.
                            SaveTopicsToStorage();
                            SetAiTopicStatus(topic, "AI 正在分析工具结果…");
                            break;
                    }
                }, token);
            if (!finished)
                AppendAiTopicMessage(topic, new AiChatMessage(false,
                    "本轮已达到工具调用上限，未结束的命令已安排停止。请确认结果后继续。"));
            SetAiTopicStatus(topic, finished ? "继续对话" : "已达到本轮工具调用上限");
        }
        catch
        {
            if (current is { IsStreaming: true })
                current.Text = token.IsCancellationRequested ? "生成已停止，原始问题已保留。" : "工具请求中断，请重试。";
            throw;
        }
        finally
        {
            try { await shell.DisposeAsync(); }
            finally
            {
                transcript.Finish(token.IsCancellationRequested);
                foreach (var (execution, message) in cards)
                {
                    message.ToolFeedback = execution.BuildContext();
                    message.IsToolRunning = false;
                }
                if (current != null) current.IsStreaming = false;
                SaveTopicsToStorage();
            }
        }
    }
}
