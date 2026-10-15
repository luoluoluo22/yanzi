using System.Text.Json;
using OpenQuickHost;
using OpenQuickHost.Sync;

internal static class ChatCapabilityVerification
{
    public static async Task RunAsync()
    {
        int checks = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
        async Task Reject<T>(Func<Task> operation, string name) where T : Exception
        {
            try { await operation(); } catch (T) { checks++; return; }
            throw new Exception(name);
        }
        JsonElement Input(object value) => JsonSerializer.SerializeToElement(value);
        Check(RuntimeRpc.DefaultRequestTimeout("capability.invoke", Input(new { name = "chat.send" })).TotalSeconds == 180, "chat_rpc_budget_includes_upload_and_ack");
        Check(RuntimeRpc.DefaultRequestTimeout("capability.invoke", Input(new { name = "chat.status" })).TotalSeconds == 180, "status_wait_rpc_budget");
        Check(RuntimeRpc.DefaultRequestTimeout("capability.invoke", Input(new { name = "system.info" })).TotalSeconds == 45, "other_rpc_budget_preserved");
        YanziBuiltinCapabilityRegistration.Register();
        Check(YanziCapabilityRegistry.TryGet("chat.send", out var send) && send!.ProviderExtensionId == "yanzi-host", "discover_builtin_send");
        Check(YanziCapabilityRegistry.TryGet("chat.status", out var status), "discover_status");
        await Reject<UnauthorizedAccessException>(() => YanziCapabilityRegistry.InvokeAsync("chat.send", new { text = "hello" }), "anonymous_send_blocked");
        await Reject<UnauthorizedAccessException>(() => YanziCapabilityRegistry.InvokeAsync("chat.status", new { messageId = "fixture" }), "anonymous_status_blocked");
        var caller = new YanziCapabilityCaller("fixture", ["device.message.send"]);
        foreach (var invalid in new object[] { new { text = "hello", timeoutSeconds = 0 }, new { timeoutSeconds = 61 },
                     new { timeoutSeconds = 1.5 }, new { waitForAck = "true" }, new { unknown = true }, new { kind = "invalid" } })
            await Reject<ArgumentException>(() => YanziCapabilityRegistry.InvokeAsync("chat.send", invalid, caller), "schema_rejects_invalid_before_side_effect");
        await Reject<ArgumentException>(() => YanziCapabilityRegistry.InvokeAsync("chat.send", new { filePath = "relative.png" }, caller), "relative_file_rejected");
        await Reject<ArgumentException>(() => YanziCapabilityRegistry.InvokeAsync("chat.send", new { kind = "photo", filePath = " " }, caller), "missing_attachment_rejected");
        await Reject<ArgumentException>(() => YanziCapabilityRegistry.InvokeAsync("chat.send", new { text = " " }, caller), "empty_text_rejected");

        var root = Path.Combine(Path.GetTempPath(), "YanziDev", "chat-capability", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "original.png");
            File.WriteAllBytes(file, [1, 2, 3]);
            Check(YanziChatCapabilityProvider.ValidateContent(Input(new { filePath = file })).Kind == "photo", "photo_inferred");
            await Reject<ArgumentException>(() => YanziCapabilityRegistry.InvokeAsync("chat.send", new { kind = "text", text = "hello", filePath = file }, caller), "text_file_rejected");
            var peers = new[] {
                new PeerDeviceInfo("phone-a", "Phone A", "android", false, "2026-10-04T23:00:00Z"),
                new PeerDeviceInfo("phone-b", "Phone B", "android", true, "2026-10-04T22:00:00Z"),
                new PeerDeviceInfo("desktop", "K70", "desktop", true, "2026-10-04T23:00:00Z") };
            Check(YanziChatCapabilityProvider.SelectTarget(peers, null).DeviceId == "phone-b", "online_phone_selected");
            Check(YanziChatCapabilityProvider.SelectTarget(peers, "phone-a").DeviceId == "phone-a", "explicit_offline_phone_respected");
            Check(YanziChatCapabilityProvider.SelectTarget(peers, "K70", new Dictionary<string, string>{{"K70","phone-b"}}).DeviceId == "phone-b", "alias_resolved_with_account_membership");
            await Reject<ArgumentException>(() => Task.FromResult(YanziChatCapabilityProvider.SelectTarget(peers, "Phone")), "ambiguous_phone_rejected");
            await Reject<KeyNotFoundException>(() => Task.FromResult(YanziChatCapabilityProvider.SelectTarget(peers, "K70", new Dictionary<string, string>{{"K70","other-account-phone"}})), "foreign_alias_rejected");

            var message = new DeviceMessageRecord { MessageId = "msg_fixture", TargetDeviceId = "phone-b", Kind = "photo", Status = "acked", AckedAt = "global",
                DeliveredAt = "observed", Receipts = [new("phone-a", "Other phone", "completed", "receipt-a")] };
            message.Payload["accountChat"] = Input(true);
            Check(!YanziChatCapabilityProvider.MakeResult(message, "phone-b", "Phone B").acked, "other_phone_global_ack_not_success");
            message.Receipts.Add(new("phone-b", "Phone B", "failed", "receipt-b"));
            var failure = YanziChatCapabilityProvider.MakeResult(message, "phone-b", "Phone B");
            Check(failure.status == "failed" && !failure.acked, "failed_ack_not_success");
            YanziCapabilitySchema.Validate(send!.OutputSchema, Input(failure));
            message.Receipts[1] = new("phone-b", "Phone B", "completed", "receipt-b");
            var complete = YanziChatCapabilityProvider.MakeResult(message, "phone-b", "Phone B");
            Check(complete.status == "completed" && complete.acked, "target_completed_success");
            YanziCapabilitySchema.Validate(send.OutputSchema, Input(complete));

            message.Receipts.RemoveAt(1);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var timeout = await YanziChatCapabilityProvider.ObserveAsync(message, "phone-b", "Phone B", true, 1,
                async token => { await Task.Delay(Timeout.Infinite, token); return message; });
            Check(timeout.messageId == "msg_fixture" && timeout.status == "pending" && timeout.waitTimedOut && !timeout.acked && watch.Elapsed.TotalSeconds < 3, "timeout_bounded_and_id_preserved");
            YanziCapabilitySchema.Validate(send.OutputSchema, Input(timeout));
            var recovered = await YanziChatCapabilityProvider.ObserveAsync(message, "phone-b", "Phone B", true, 3,
                token => { message.Receipts.Add(new("phone-b", "Phone B", "acked", "receipt-b")); return Task.FromResult<DeviceMessageRecord?>(message); });
            Check(recovered.acked && recovered.status == "completed", "matching_ack_finishes_wait");
            message.Receipts.RemoveAt(1);
            var networkFailure = await YanziChatCapabilityProvider.ObserveAsync(message, "phone-b", "Phone B", true, 1,
                token => Task.FromException<DeviceMessageRecord?>(new HttpRequestException("fixture")));
            Check(networkFailure.messageId == "msg_fixture" && networkFailure.waitTimedOut && networkFailure.error.Contains("HttpRequestException"), "poll_failure_preserves_id");
            var cloudFailure = await YanziChatCapabilityProvider.ObserveAsync(message, "phone-b", "Phone B", true, 1,
                token => Task.FromException<DeviceMessageRecord?>(new InvalidOperationException("HTTP 503 fixture")));
            Check(cloudFailure.messageId == "msg_fixture" && cloudFailure.status == "pending" && cloudFailure.error.Contains("HTTP 503"), "cloud_status_error_preserves_id");
            var noWait = await YanziChatCapabilityProvider.ObserveAsync(message, "phone-b", "Phone B", false, 1,
                token => throw new Exception("unexpected_poll"));
            Check(!noWait.waitTimedOut && noWait.status == "pending", "no_wait_no_poll");
            message.Payload.Clear();
            message.Receipts.Clear();
            var direct = YanziChatCapabilityProvider.MakeResult(message, "phone-b", "Phone B");
            Check(direct.status == "completed" && direct.acked && direct.ackedAt == "global", "explicit_device_ack_without_receipt_table");
            Check(!YanziChatCapabilityProvider.MakeResult(message, "phone-a", "Other phone").acked, "different_direct_target_not_success");
            message.Status = "failed";
            Check(YanziChatCapabilityProvider.MakeResult(message, "phone-b", "Phone B").status == "failed", "explicit_device_failure_not_success");
            var account = new DeviceMessageRecord { MessageId="account-message", Kind="text", Status="acked", AckedAt="global" };
            account.Payload["accountChat"] = Input(true);
            Check(!YanziChatCapabilityProvider.MakeAccountResult(account, peers).allOnlineAcked, "broadcast_global_ack_cannot_confirm_phone");
            account.Receipts.Add(new("phone-b", "Phone B", "completed", "phone-b-ack"));
            var shared = YanziChatCapabilityProvider.MakeAccountResult(account, peers);
            Check(shared.status=="completed" && shared.allOnlineAcked && shared.devices.Length==2, "online_ack_completes_broadcast_with_offline_phone_visible");
            Check(shared.devices.Single(d=>d.deviceId=="phone-a").receipt.status=="pending", "offline_phone_pending_after_other_phone_ack");
            YanziCapabilitySchema.Validate(send.OutputSchema, Input(shared));
            var bothOnline = peers.Select(p=>p with { Online=true }).ToArray();
            Check(!YanziChatCapabilityProvider.MakeAccountResult(account, bothOnline).allOnlineAcked, "every_online_phone_requires_own_ack");
            account.Receipts.Add(new("phone-a", "Phone A", "completed", "phone-a-ack"));
            Check(YanziChatCapabilityProvider.MakeAccountResult(account, bothOnline).allOnlineAcked, "two_online_phone_acks_complete_broadcast");
            Check(!YanziChatCapabilityProvider.MakeAccountResult(account, []).allOnlineAcked, "zero_phones_not_claimed_delivered");
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine($"CHAT_CAPABILITY_SCHEMA_PERMISSIONS_SELECTION_TARGET_ACK_TIMEOUT=PASSED; checks={checks}");
    }
}
