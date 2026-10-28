using OpenQuickHost;

internal static class AiChatRequestTrackerVerification
{
    public static void Run()
    {
        static void Check(bool ok, string why)
        {
            if (!ok) throw new InvalidOperationException("Request tracker: " + why);
        }

        var tracker = new AiChatRequestTracker();
        using var a = new CancellationTokenSource();
        using var b = new CancellationTokenSource();
        Check(tracker.TryPrepare("A"), "A must be admitted");
        Check(!tracker.TryPrepare("A"), "duplicate Enter in A must be rejected");
        Check(tracker.TryPrepare("B"), "B must be admitted while A is preparing");
        tracker.Start("A", a);
        tracker.EndPreparing("A");
        tracker.Start("B", b);
        tracker.EndPreparing("B");
        Check(tracker.IsActive("A") && tracker.IsActive("B"), "parallel conversations must run");
        Check(!tracker.TryPrepare("A") && !tracker.TryPrepare("B"),
            "a second request to a busy topic must be rejected");
        Check(tracker.TryGetActive("A", out var selected) && ReferenceEquals(selected, a),
            "cancel target must be associated with A");
        selected.Cancel();
        Check(a.IsCancellationRequested && !b.IsCancellationRequested,
            "cancelling A must not affect B");
        tracker.Finish("A");
        Check(tracker.TryPrepare("A"), "A should be reusable after completion");
        tracker.EndPreparing("A");
        Check(tracker.IsActive("B"), "B must still run after A finishes");
        tracker.Finish("B");
        Check(tracker.TryPrepare("B"), "B should be reusable");
        Console.WriteLine("AI_CHAT_PARALLEL_TRACKER_VERIFICATION_PASSED");
    }
}
