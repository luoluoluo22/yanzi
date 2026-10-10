using OpenQuickHost;

internal static class AiModelDiscoveryVerification
{
    public static void Run()
    {
        // New upstream model IDs are parsed dynamically; no GPT family is hardcoded.
        const string response = "{\"object\":\"list\",\"data\":[{\"id\":\"gpt-6.1-sol\"},{\"id\":\"gpt-6-astra\"},{\"id\":\"gpt-7-future\"}]}";
        var remote = SettingsAiController.ParseAvailableModelNames("OpenAI", response);
        Check(remote.Count == 3 && remote.Contains("gpt-7-future"), "new models must be discovered");

        var pinned = SettingsAiController.IncludeSelectedModel(remote, "gpt-5.6-luna", "gpt-6.1-sol");
        Check(pinned[0] == "gpt-5.6-luna" && pinned.Count == 4, "previous choice must survive model removal");

        var inList = SettingsAiController.IncludeSelectedModel(remote, "gpt-6-astra", "gpt-5.6-luna");
        Check(inList.Count == 3, "already listed selected model must not duplicate");

        var fallback = SettingsAiController.IncludeSelectedModel(
            new[] { "gpt-6-astra", "gpt-5.6-luna" }, "gpt-5.6-luna", "");
        Check(fallback.Count == 2, "cached models must be usable when offline");

        Check(SettingsAiController.SameModelIds(remote, remote.ToArray()), "unchanged list must not cause saves");
        Check(!SettingsAiController.SameModelIds(remote, new[] { "gpt-6-astra" }), "changed list must be detected");

        // Ensure the server's model discovery URL uses the existing credentials
        // without leaking secrets into the URL for OpenAI-compatible providers.
        using var request = SettingsAiController.BuildModelListRequest(
            new AiProviderSnapshot("OpenAI", "https://gateway.example/v1/", "test-only-key"));
        Check(request.RequestUri!.AbsoluteUri == "https://gateway.example/v1/models",
            "models endpoint must be derived correctly");
        Check(request.Headers.Authorization?.Scheme == "Bearer" &&
              request.Headers.Authorization?.Parameter == "test-only-key",
              "the provider key must be sent using the Authorization header");

        Console.WriteLine("PASS AI model discovery: future models, fallback, pinned selection, dedup, API request");
    }

    private static void Check(bool condition, string error)
    {
        if (!condition) throw new InvalidOperationException(error);
    }
}