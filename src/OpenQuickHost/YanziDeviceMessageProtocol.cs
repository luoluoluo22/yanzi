namespace OpenQuickHost;

public static class YanziDeviceMessageProtocol
{
    public const int Version = 1;
    public static bool IsExecution(string kind) => kind.StartsWith("run-", System.StringComparison.Ordinal) ||
        kind.StartsWith("fs-", System.StringComparison.Ordinal) || kind == "capability.invoke";
    public static object Describe() => new
    {
        name = "yanzi.device-messaging", version = Version, supportedVersions = new[] { Version },
        transports = new[] { "lan-aead-v1" }, routing = new[] { "device" },
        features = new[] { "explicit-pairing", "pair-revocation", "execution-deadline", "trace-context", "block-resume", "transfer-cancel" },
        executionDeadlineSeconds = 120, maximumExecutionDeadlineSeconds = 300,
        delivery = "at-least-once", idempotency = "clientOperationId/clientTransferId",
        receiptScope = "device", commandRouting = "single-device",
        limits = new { attachmentBytes = 30 * 1024 * 1024, textCharacters = 4000 }
    };
}
