using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;
using OpenQuickHost;
using OpenQuickHost.Sync;

internal static class MobileMessageBridgeVerification
{
    public static Task RunAsync(string[] args)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fixturePath = args[Array.IndexOf(args, "--mobile-message-bridge") + 1];
        var thread = new Thread(() =>
        {
            try
            {
                using var fixture = JsonDocument.Parse(File.ReadAllText(fixturePath));
                var data = fixture.RootElement;
                var root = data.GetProperty("root").GetString()!;
                var useExistingAccount = data.TryGetProperty("useExistingAccount", out var existingAccount) && existingAccount.GetBoolean();
                using var scope = useExistingAccount ? HostAssets.UseExistingDataRootForVerification(root) : HostAssets.UseIsolatedDataRootForVerification(root);
                Directory.CreateDirectory(root);
                var baseUrl = data.GetProperty("baseUrl").GetString()!;
                var client = new CloudSyncClient(new SyncOptions { BaseUrl = baseUrl });
                if (!useExistingAccount) {
                client.SetCredential("mobile-bridge@local.test", "temporary-test-password", true);
                SyncSessionStore.Save(new SyncSession {
                    AccessToken = data.GetProperty("token").GetString()!,
                    UserId = data.GetProperty("userId").GetString()!, Username = "bridge-test",
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20).ToUnixTimeSeconds()
                });
                }
                File.WriteAllText(SyncConfigLoader.ConfigPath, JsonSerializer.Serialize(new { baseUrl }));
                var app = new App { IsVerificationHarness = true };
                app.InitializeComponent();
                var window = new MainWindow();
                app.MainWindow = window;
                File.WriteAllText(Path.Combine(root, "device-identity.json"), JsonSerializer.Serialize(new {
                    deviceId = data.GetProperty("desktopDeviceId").GetString(), createdAtUtc = DateTimeOffset.UtcNow.ToString("O")
                }));
                typeof(MainWindow).GetField("_desktopDeviceId", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(window, data.GetProperty("desktopDeviceId").GetString()!);
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                bool sendingChat = false;
                timer.Tick += async (_, _) => {
                    if (File.Exists(Path.Combine(root, "stop"))) Dispatcher.CurrentDispatcher.InvokeShutdown();
                    var requestPath = Path.Combine(root, "chat-request.json");
                    if (!sendingChat && File.Exists(requestPath)) {
                        sendingChat = true;
                        try {
                            using var request = JsonDocument.Parse(File.ReadAllText(requestPath));
                            File.Delete(requestPath);
                            typeof(LanDiscoveryService).GetProperty("LastKnownMobileIp")!.SetValue(null,
                                request.RootElement.GetProperty("staleLan").GetBoolean() ? System.Net.IPAddress.Loopback : null);
                            typeof(LanDiscoveryService).GetProperty("LastKnownMobileNotificationPort")!.SetValue(null, 42993);
                            var chat = new MobileMessageToastWindow();
                            chat.Show();
                            var input = (System.Windows.Controls.TextBox)chat.FindName("InputTextBox");
                            input.Text = request.RootElement.GetProperty("text").GetString()!;
                            if (request.RootElement.TryGetProperty("filePath", out var filePath))
                                await (Task)typeof(MobileMessageToastWindow).GetMethod("SendFileOrPhotoToMobileAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                                    .Invoke(chat, [filePath.GetString()!, request.RootElement.GetProperty("isPhoto").GetBoolean()])!;
                            else
                                await (Task)typeof(MobileMessageToastWindow).GetMethod("TriggerSendMessageAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(chat, null)!;
                            var status = ((System.Windows.Controls.TextBlock)chat.FindName("SendStatusText")).Text;
                            File.WriteAllText(Path.Combine(root, "chat-result.json"), JsonSerializer.Serialize(new { status, input = input.Text }));
                            chat.Close();
                        } catch (Exception ex) {
                            File.WriteAllText(Path.Combine(root, "chat-result.json"), JsonSerializer.Serialize(new { error = ex.ToString() }));
                        } finally { sendingChat = false; }
                    }
                };
                timer.Start();
                Dispatcher.CurrentDispatcher.BeginInvoke(async () => {
                    try {
                        var marker = Path.Combine(root, "execute-once.txt");
                        var message = new DeviceMessageRecord {
                            MessageId = "dedup-test", SourceDeviceId = "android-bridge-test", Kind = "run-shell",
                            Text = $"Add-Content -LiteralPath '{marker.Replace("'", "''")}' -Value 'once'"
                        };
                        var results = await Task.WhenAll(window.HandleMobileDeviceMessageAsync(message), window.HandleMobileDeviceMessageAsync(message));
                        if (results.Any(x => !x.success) || File.ReadAllLines(marker).Length != 1)
                            throw new Exception("Concurrent message was executed more than once");
                        typeof(MainWindow).GetField("_mobileMessageResults", BindingFlags.Instance | BindingFlags.NonPublic)!
                            .GetValue(window)!.GetType().GetMethod("Clear")!.Invoke(
                                typeof(MainWindow).GetField("_mobileMessageResults", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window), null);
                        var restoredReceipt = await window.HandleMobileDeviceMessageAsync(message);
                        if (!restoredReceipt.success || File.ReadAllLines(marker).Length != 1)
                            throw new Exception("Persisted execution receipt did not suppress reexecution");
                        if (!useExistingAccount)
                            typeof(MainWindow).GetMethod("StartMobileMessageBridge", BindingFlags.Instance | BindingFlags.NonPublic)!
                                .Invoke(window, ["physical-phone-verification"]);
                        File.WriteAllText(Path.Combine(root, "ready"), "DEDUP_PASSED");
                    } catch (Exception ex) { completion.TrySetException(ex); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
                });
                Dispatcher.Run();
                completion.TrySetResult();
            } catch (Exception ex) { completion.TrySetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
