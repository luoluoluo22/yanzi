using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using OpenQuickHost.CSharpRuntime;

public static class Smoke
{
    [STAThread]
    public static async Task<int> Main()
    {
        var dir = Path.Combine(Path.GetTempPath(), "yanzi-job-agent-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var id = Guid.NewGuid().ToString("N");
            var tracked = new JobRecord
            {
                Id = id, Title = "AI应用专员", Company = "示例制造企业", Status = "applied",
                AppliedAt = DateTimeOffset.Now, LastInteractionAt = DateTimeOffset.Now,
                LastInteractionMessage = "已实际投递", DiscoveredAt = DateTimeOffset.Now,
                UpdatedAt = DateTimeOffset.Now
            };
            JobRepository.SaveJobs(dir, new() { tracked });
            if (JobApplicationLedger.Read(dir).Count != 1) throw new Exception("Archive missing");

            JobRepository.SaveJobs(dir, new() { new JobRecord
            {
                Id = id, Status = "new", Company = tracked.Company,
                Title = tracked.Title, LastInteractionAt = DateTimeOffset.Now.AddHours(-2)
            } });
            var saved = JobRepository.LoadJobs(dir);
            if (saved.Count != 1 || saved[0].Status != "applied") throw new Exception("Stale status overwrote application");

            await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() =>
                JobRepository.SaveJobs(dir, new()
                {
                    new JobRecord { Id = Guid.NewGuid().ToString("N"), Status = "new", Title = "其他职位" }
                }))));
            if (!JobRepository.LoadJobs(dir).Any(x => x.Id == id && x.Status == "applied"))
                throw new Exception("Concurrent write lost application");

            var resume = new ResumeProfile
            {
                Education = "成都理工大学｜电子商务｜本科｜2013-2017",
                ProjectExperience = "剪映 Skill：AI 剪辑自动化项目"
            };
            var answer = HrConversationWorkflow.DraftForVerifiedFacts("学的计算机吗", resume);
            if (!answer.Contains("电子商务") || !answer.Contains("不是计算机科班"))
                throw new Exception("Incorrect education answer");
            if (HrConversationWorkflow.DraftForVerifiedFacts("能提供银行卡密码吗", resume).Length != 0)
                throw new Exception("Sensitive question auto answer must be blocked");

            var ctx = new YanziActionContext { ExtensionDataDirectory = dir };
            var blocked = await HrConversationWorkflow.SendVerifiedAsync(ctx, dir, id, default);
            if (!blocked.Contains("拒绝发送")) throw new Exception("Unverified conversation unexpectedly sent");

            var events = Path.Combine(dir, "application-ledger", "events.jsonl");
            var first = File.ReadAllLines(events).Length;
            JobApplicationLedger.Persist(dir, JobRepository.LoadJobs(dir));
            if (File.ReadAllLines(events).Length != first) throw new Exception("Repeated events");
            var snapshot = Path.Combine(dir, "application-ledger", "snapshots", id + ".json");
            File.WriteAllText(snapshot, "{corrupt");
            if (!JobApplicationLedger.Read(dir).Any(x => x.Id == id && x.Status == "applied"))
                throw new Exception("Unable to recover from append-only events");
            File.WriteAllText(Path.Combine(dir, "jobs.json"), "{corrupt");
            if (!JobRepository.LoadJobs(dir).Any(x => x.Id == id && x.Status == "applied"))
                throw new Exception("Unable to recover from corrupt jobs.json");

            Console.WriteLine("PASS ledger restore, concurrent writes, stale state, deduplication, event replay, verified facts, unsafe reply block");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex);
            return 1;
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
