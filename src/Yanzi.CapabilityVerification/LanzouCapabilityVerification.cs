using System.Text.Json;
using OpenQuickHost;

public static class LanzouCapabilityVerification
{
    public static async Task RunAsync()
    {
        var definitions = YanziLanzouCapabilityProvider.Create().ToDictionary(x => x.Name);
        var required = new[] { "lanzou.status", "lanzou.open", "lanzou.inspect",
            "lanzou.browser.read", "lanzou.browser.act", "lanzou.network.observe", "lanzou.folders.list", "lanzou.files.list",
            "lanzou.files.upload", "lanzou.files.move", "lanzou.files.share", "lanzou.files.rename", "lanzou.files.delete", "lanzou.files.download" };
        foreach (var name in required)
            if (!definitions.ContainsKey(name))
                throw new Exception("Missing: " + name);

        if (definitions["lanzou.browser.act"].RequiresConfirmation != true)
            throw new Exception("Writes must require confirmation");
        if (definitions["lanzou.browser.act"].RiskLevel != "high")
            throw new Exception("Writes must be high risk");

        static async Task MustReject(Func<Task<object?>> run)
        {
            try { await run(); }
            catch (ArgumentException) { return; }
            throw new Exception("Unsafe workflow was accepted");
        }

        await MustReject(() => definitions["lanzou.browser.read"].Handler(
            JsonSerializer.SerializeToElement(new { steps = new object[]
                { new { type = "fetch", url = "/doupload.php", method = "POST" } } })));
        await MustReject(() => definitions["lanzou.browser.read"].Handler(
            JsonSerializer.SerializeToElement(new { steps = new object[]
                { new { type = "scrape", selectors = new Dictionary<string,string>
                    { ["password"] = "input[type=password] | value" } } } })));
        await MustReject(() => definitions["lanzou.browser.act"].Handler(
            JsonSerializer.SerializeToElement(new { confirmAction = false, steps = new object[]
                { new { type = "click", selector = "#delete" } } })));

        await MustReject(() => definitions["lanzou.folders.list"].Handler(
            JsonSerializer.SerializeToElement(new { folderId = "-1&task=9" })));
        await MustReject(() => definitions["lanzou.folders.list"].Handler(
            JsonSerializer.SerializeToElement(new { folderId = "-1", page = 0 })));
        var response = JsonSerializer.SerializeToElement(new
        {
            status = 200, data = new { zt = 1, text = new[] { new { name = "燕子", fol_id = 123 } } }
        }).GetRawText();
        var folderList = JsonSerializer.SerializeToElement(
            YanziLanzouCapabilityProvider.ParseFolderListResponse(response, "-1", 1));
        if (!folderList.GetProperty("authenticated").GetBoolean() ||
            folderList.GetProperty("folderCount").GetInt32() != 1 ||
            folderList.GetProperty("folders")[0].GetProperty("name").GetString() != "燕子")
            throw new Exception("Lanzou read-only folder parser failed");
        try
        {
            YanziLanzouCapabilityProvider.ParseFolderListResponse("{\"status\":200,\"data\":{\"zt\":0}}", "-1", 1);
            throw new Exception("Lanzou API authorization failure must reject");
        }
        catch (InvalidOperationException) { }

        await MustReject(() => definitions["lanzou.files.list"].Handler(
            JsonSerializer.SerializeToElement(new { folderId = "../../etc" })));
        var fileResponse = JsonSerializer.SerializeToElement(new
        {
            status = 200, data = new { zt = 1, text = new[] {
                new { name_all = "example.txt", id = 789, size = "1K", time = "now", downs = 0, pwd = "PRIVATE_SECRET" }
            } }
        }).GetRawText();
        var fileList = JsonSerializer.SerializeToElement(
            YanziLanzouCapabilityProvider.ParseFileListResponse(fileResponse, "-1", 1));
        if (fileList.GetProperty("fileCount").GetInt32() != 1 ||
            fileList.GetProperty("files")[0].GetProperty("name").GetString() != "example.txt" ||
            fileList.GetRawText().Contains("PRIVATE_SECRET", StringComparison.Ordinal))
            throw new Exception("Lanzou file listing parser leaked sensitive fields");

        foreach (var operation in new[] { "lanzou.files.upload", "lanzou.files.move", "lanzou.files.share",
                     "lanzou.files.rename", "lanzou.files.delete" })
        {
            if (!definitions[operation].RequiresConfirmation || definitions[operation].RiskLevel != "high")
                throw new Exception("File mutations must require explicit high-risk confirmation: " + operation);
        }
        await MustReject(() => definitions["lanzou.files.delete"].Handler(
            JsonSerializer.SerializeToElement(new { fileId = "123450001", folderId = "67890001", confirmAction = false })));
        await MustReject(() => definitions["lanzou.files.rename"].Handler(
            JsonSerializer.SerializeToElement(new { fileId = "bad", folderId = "67890001", newBaseName = "test", confirmAction = true })));
        await MustReject(() => definitions["lanzou.files.upload"].Handler(
            JsonSerializer.SerializeToElement(new { localPath = @"C:\\missing.zip", folderId = "67890001", confirmAction = false })));

        if (!definitions["lanzou.files.download"].RequiresConfirmation)
            throw new Exception("Download must require confirmation");
        await MustReject(() => definitions["lanzou.files.download"].Handler(
            JsonSerializer.SerializeToElement(new { fileId = "123450001", expectedName = "proof.zip", confirmAction = false })));

        await MustReject(() => definitions["lanzou.files.download"].Handler(
            JsonSerializer.SerializeToElement(new { fileId = "123450002", expectedName = "test.zip",
                expectedSha256 = "not-a-sha256", confirmAction = true })));
        await MustReject(() => definitions["lanzou.files.delete"].Handler(
            JsonSerializer.SerializeToElement(new { fileId = "123450002", folderId = "-1",
                confirmAction = true })));
        if (definitions["lanzou.files.share"].InputSchema.GetProperty("properties")
            .TryGetProperty("includeAccessCode", out var _unusedShareCode) == false)
            throw new Exception("Protected share must support explicit access-code option");

        var status = await definitions["lanzou.status"].Handler(
            JsonSerializer.SerializeToElement(new { }));
        var json = JsonSerializer.SerializeToElement(status);
        if (json.GetProperty("authenticated").ValueKind != JsonValueKind.Null ||
            json.GetProperty("apiReplay").GetString() != "selected-read-write-operations-verified;rename-membership-gated;download-not-end-to-end")
            throw new Exception("Authentication / replay must not claim verified");

        Console.WriteLine("LANZOU_CAPABILITY_TESTS=PASSED");
    }
}
