using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using StartFlow.Core;
using StartFlow.Windows;

namespace StartFlow.Tests;

internal static class Program
{
    private static int count;
    private static string root = "";
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        root = Path.GetFullPath(Path.Combine(".test-data", "StartFlow-Tests-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(root);
        try
        {
            await Test("Sequential delays and submission order", Sequential);
            await Test("Timeline: no execution before confirmation; overdue and equal-time order", Timeline);
            await Test("Timeline adds overall delay exactly once", TimelineDelay);
            await Test("Timeline future items follow due time, equal-time entries follow list order", TimelineFuture);
            await Test("Independent retry does not block later items and stops after success", Retries);
            await Test("Stop cancels delays, retries and queue; old timers cannot launch; restart works", Stop);
            await Test("Queue serializes plans and captures immutable execution snapshot", Queue);
            await Test("Waiting for script exit does not block later item; blocks next plan", Completion);
            await Test("UAC cancellation is not retried", UacCancellation);
            await Test("Configuration atomic backup, corrupt recovery and explicit preservation", ConfigRecovery);
            await Test("Import merge IDs, names, replacement and backup", Imports);
            await Test("Configuration rejects unknown versions, invalid IDs, URLs and timing", ConfigValidation);
            await Test("Duplicate identity preserves URL path and argument differences", Duplicates);
            await Test("Manual/login distinction and one-shot skip across sessions", Login);
            await Test("Chinese paths, spaces and argument quoting", Quoting);
            await Test("Missing targets return truthful failures", Missing);
            await Test("Website bypasses process detection even with running browser identity", WebsiteRule);
            if (args.Contains("--windows-integration"))
            {
                await Test("Windows: exact process-path running detection", Running);
                await Test("Windows: current-user startup write, verification, repair and removal", StartupRegistry);
                await Test("Windows: real PowerShell Unicode command and exit observation", RealPowerShell);
                await Test("Windows: real cmd script with Chinese path and quoted arguments", RealScript);
                await Test("Windows: per-user single instance and named-pipe activation", Instance);
                await Test("Windows: real shortcut metadata roundtrip", Shortcut);
                await Test("Windows: installed desktop and Store discovery", Discovery);
            }
            if (args.Contains("--interactive-targets")) await Test("Windows: real app, associated file, folder, default browser and Store activation requests", RealTargets);
            Console.WriteLine($"PASS: {count} tests. Temporary targets: {root}"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
        finally { Directory.Delete(root, true); }
    }
    private static async Task Test(string name, Func<Task> action) { await action(); count++; Console.WriteLine("PASS " + name); }
    private static void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
    private static async Task Eventually(Func<bool> condition)
    { for (var i = 0; i < 1000; i++) { if (condition()) return; await Task.Delay(2); } throw new TimeoutException("Condition not reached."); }
    private static StartupItem Item(string name, double seconds = 0) => new() { Name = name, Target = "https://example.com/" + name, Kind = TargetKind.Website, Seconds = seconds };
    private static StartupPlan Plan(params StartupItem[] items) => new() { Name = "Test", Items = new(items) };
    private sealed class FakeLauncher : ITargetLauncher
    {
        public readonly ConcurrentQueue<string> Calls = new();
        public Func<StartupItem, LaunchOutcome> Outcome = _ => new(RunStatus.Submitted);
        public Task<LaunchOutcome> LaunchAsync(StartupItem item, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Calls.Enqueue(item.Name); return Task.FromResult(Outcome(item)); }
    }
    private static async Task Sequential()
    {
        var clock = new ManualClock(); var launch = new FakeLauncher(); await using var scheduler = new Scheduler(launch, clock);
        var plan = Plan(Item("A", 2), Item("B", 3)); plan.InitialDelaySeconds = 5; scheduler.Enqueue(plan, clock.GetUtcNow());
        await Eventually(() => clock.TimerCount > 0); clock.Advance(6); Assert(launch.Calls.IsEmpty); clock.Advance(1);
        await Eventually(() => launch.Calls.Count == 1 && clock.TimerCount > 0); clock.Advance(2); Assert(launch.Calls.Count == 1); clock.Advance(1);
        await Eventually(() => scheduler.Current == null); Assert(launch.Calls.SequenceEqual(new[] { "A", "B" }));
    }
    private static async Task Timeline()
    {
        var clock = new ManualClock(); var launch = new FakeLauncher(); await using var scheduler = new Scheduler(launch, clock);
        var reference = clock.GetUtcNow(); var plan = Plan(Item("A", 5), Item("B", 5), Item("C", 2)); plan.Mode = ScheduleMode.Timeline;
        clock.Advance(100); Assert(launch.Calls.IsEmpty); scheduler.Enqueue(plan, reference);
        await Eventually(() => launch.Calls.Count == 3 && scheduler.Current == null); Assert(launch.Calls.SequenceEqual(new[] { "A", "B", "C" }));
    }
    private static async Task TimelineDelay()
    {
        var c = new ManualClock(); var l = new FakeLauncher(); await using var s = new Scheduler(l, c);
        var p = Plan(Item("A", 2)); p.Mode = ScheduleMode.Timeline; p.InitialDelaySeconds = 5; s.Enqueue(p, c.GetUtcNow());
        await Eventually(() => c.TimerCount > 0); c.Advance(6); Assert(l.Calls.IsEmpty); c.Advance(1); await Eventually(() => l.Calls.Count == 1);
    }
    private static async Task Retries()
    {
        var c = new ManualClock(); var l = new FakeLauncher(); var attempts = 0;
        l.Outcome = i => i.Name == "A" && ++attempts == 1 ? new(RunStatus.Failed, "test", true) : new(RunStatus.Submitted);
        await using var s = new Scheduler(l, c); var a = Item("A"); a.RetryCount = 3; a.RetryIntervalSeconds = 10; s.Enqueue(Plan(a, Item("B", 1)), c.GetUtcNow());
        await Eventually(() => c.TimerCount == 2); c.Advance(1); await Eventually(() => l.Calls.Count == 2); Assert(l.Calls.SequenceEqual(new[] { "A", "B" })); c.Advance(9);
        await Eventually(() => s.Current == null); c.Advance(100); Assert(l.Calls.SequenceEqual(new[] { "A", "B", "A" })); Assert(s.Results[0].Attempts == 2);
    }
    private static async Task TimelineFuture()
    {
        var c = new ManualClock(); var l = new FakeLauncher(); await using var s = new Scheduler(l, c);
        var p = Plan(Item("A", 10), Item("B", 2), Item("C", 2)); p.Mode = ScheduleMode.Timeline; s.Enqueue(p, c.GetUtcNow());
        await Eventually(() => c.TimerCount > 0); c.Advance(2); await Eventually(() => l.Calls.Count == 2 && c.TimerCount > 0); Assert(l.Calls.SequenceEqual(new[] { "B", "C" })); c.Advance(8); await Eventually(() => l.Calls.Count == 3);
    }
    private static async Task Stop()
    {
        var c = new ManualClock(); var l = new FakeLauncher { Outcome = _ => new(RunStatus.Failed, "test", true) }; await using var s = new Scheduler(l, c);
        var a = Item("A"); a.RetryCount = 2; a.RetryIntervalSeconds = 10; s.Enqueue(Plan(a, Item("B", 20)), c.GetUtcNow());
        await Eventually(() => c.TimerCount == 2); s.Enqueue(Plan(Item("Queued")), c.GetUtcNow()); await s.StopAsync(); var calls = l.Calls.Count; c.Advance(100); await Task.Delay(10);
        Assert(l.Calls.Count == calls && s.Waiting.Length == 0); Assert(s.Results.All(r => r.Status == RunStatus.Cancelled));
        l.Outcome = _ => new(RunStatus.Submitted); Assert(s.Enqueue(Plan(Item("Restart")), c.GetUtcNow())); await Eventually(() => l.Calls.Count == calls + 1);
    }
    private static async Task Queue()
    {
        var c = new ManualClock(); var l = new FakeLauncher(); await using var s = new Scheduler(l, c); var a = Plan(Item("First", 10)); var b = Plan(Item("Snapshot")); b.Name = "Second";
        Assert(s.Enqueue(a, c.GetUtcNow())); await Eventually(() => c.TimerCount > 0); Assert(s.Enqueue(b, c.GetUtcNow())); Assert(!s.Enqueue(b, c.GetUtcNow())); b.Items[0].Name = "Changed"; b.Items.Clear();
        c.Advance(10); await Eventually(() => l.Calls.Count == 2 && s.Current == null); Assert(l.Calls.SequenceEqual(new[] { "First", "Snapshot" }));
    }
    private static async Task Completion()
    {
        var c = new ManualClock(); var exit = new TaskCompletionSource<LaunchOutcome>(); var l = new FakeLauncher { Outcome = i => i.Name == "Script" ? new(RunStatus.Running, Completion: exit.Task) : new(RunStatus.Submitted) }; await using var s = new Scheduler(l, c);
        s.Enqueue(Plan(Item("Script"), Item("Later")), c.GetUtcNow()); await Eventually(() => l.Calls.Count == 2); s.Enqueue(Plan(Item("NextPlan")), c.GetUtcNow()); Assert(l.Calls.Count == 2);
        exit.SetResult(new(RunStatus.Succeeded)); await Eventually(() => l.Calls.Count == 3);
    }
    private static async Task UacCancellation()
    {
        var c = new ManualClock(); var l = new FakeLauncher { Outcome = _ => new(RunStatus.Cancelled, "UacCancelled") }; await using var s = new Scheduler(l, c);
        var a = Item("A"); a.RetryCount = 10; s.Enqueue(Plan(a), c.GetUtcNow()); await Eventually(() => s.Results.Length == 1 && s.Current == null); c.Advance(100); Assert(l.Calls.Count == 1);
    }
    private static Task ConfigRecovery()
    {
        var store = new ConfigurationStore(Path.Combine(root, "恢复 空格")); var config = new Configuration(); config.Plans.Add(Plan(Item("A"))); store.Save(config); config.Plans[0].Name = "Updated"; store.Save(config);
        Assert(ConfigurationStore.Read(store.BackupPath).Plans[0].Name == "Test"); File.WriteAllText(store.ConfigPath, "{broken"); var loaded = store.Load(); Assert(loaded.RequiresRecovery && loaded.Config.Plans[0].Name == "Test"); Assert(File.ReadAllText(store.ConfigPath) == "{broken");
        store.PreserveDamaged(); store.Save(loaded.Config); Assert(Directory.GetFiles(store.DirectoryPath, "*.recovery-*").Length == 1); return Task.CompletedTask;
    }
    private static Task Imports()
    {
        var config = new Configuration(); config.Plans.Add(Plan(Item("A"))); config.Settings.LastHandledSession = "session";
        var incoming = ConfigJson.Clone(config); var merged = ConfigurationStore.Import(config, incoming, true); Assert(merged.Plans.Count == 2 && merged.Plans[1].Name == "Test (2)"); Assert(merged.Plans[0].Id != merged.Plans[1].Id && merged.Plans[0].Items[0].Id != merged.Plans[1].Items[0].Id); Assert(Core.Validation.Check(merged).Count == 0);
        incoming.Settings.LastHandledSession = "foreign"; var replaced = ConfigurationStore.Import(config, incoming, false); Assert(replaced.Plans.Count == 1 && replaced.Settings.LastHandledSession == "session");
        var store = new ConfigurationStore(Path.Combine(root, "imports")); store.Save(config); Assert(File.Exists(store.BackupBeforeReplace())); return Task.CompletedTask;
    }
    private static Task ConfigValidation()
    {
        var config = new Configuration { FormatVersion = 2 }; Assert(Core.Validation.Check(config).Count > 0); config.FormatVersion = 1; var a = Item("A"); config.Plans.Add(Plan(a, ConfigJson.Clone(a))); Assert(Core.Validation.Check(config).Count > 0);
        Assert(!Core.Validation.TryWebsite("javascript:alert(1)", out _)); Assert(!Core.Validation.TryWebsite("https://user:password@example.com", out _)); Assert(!Core.Validation.TryWebsite("example.com", out _)); Assert(Core.Validation.TryWebsite("https://example.com/a", out _));
        a.Seconds = double.NaN; Assert(Core.Validation.CheckItem(a).Count > 0); return Task.CompletedTask;
    }
    private static Task Duplicates()
    {
        var a = Item("A"); a.Target = "https://EXAMPLE.com/A"; var b = ConfigJson.Clone(a); b.Name = "different"; b.Target = "https://example.com/A"; Assert(Core.Validation.DuplicateKey(a) == Core.Validation.DuplicateKey(b)); b.Target = "https://example.com/a"; Assert(Core.Validation.DuplicateKey(a) != Core.Validation.DuplicateKey(b));
        a.Kind = b.Kind = TargetKind.Application; a.Target = @"C:\Program Files\测试\..\测试\app.exe"; b.Target = @"c:\program files\测试\app.exe"; Assert(Core.Validation.DuplicateKey(a) == Core.Validation.DuplicateKey(b)); b.Arguments = "--new-window"; Assert(Core.Validation.DuplicateKey(a) != Core.Validation.DuplicateKey(b)); return Task.CompletedTask;
    }
    private static Task Login()
    {
        var settings = new AppSettings(); Assert(!LoginPolicy.Handle(settings, "A", false)); Assert(settings.LastHandledSession == null); LoginPolicy.ArmSkip(settings, "A"); Assert(LoginPolicy.Handle(settings, "A", true)); Assert(settings.SkipArmedSession == "A"); Assert(!LoginPolicy.Handle(settings, "A", true)); Assert(!LoginPolicy.Handle(settings, "B", true)); Assert(settings.SkipArmedSession == null); Assert(!LoginPolicy.Handle(settings, "B", true)); Assert(LoginPolicy.Handle(settings, "C", true)); return Task.CompletedTask;
    }
    private static Task Quoting()
    {
        var item = new StartupItem { Name = "中文", Kind = TargetKind.Application, Target = @"C:\Program Files\中文 软件\app.exe", Arguments = "--file \"C:\\测试 文件\\a.txt\"", DetectRunning = false };
        var info = TargetLauncher.CreateStartInfo(item); Assert(info.FileName == item.Target && info.Arguments == item.Arguments && !info.UseShellExecute); Assert(TargetLauncher.QuoteArgument("a b\\") == "\"a b\\\\\""); return Task.CompletedTask;
    }
    private static async Task Missing() { var outcome = await new TargetLauncher().LaunchAsync(new() { Name = "Missing", Target = Path.Combine(root, "missing.exe") }, default); Assert(outcome.Status == RunStatus.Failed && outcome.Message == "MissingTarget"); }
    private static Task WebsiteRule()
    { var item = Item("A"); item.ProcessPath = Environment.ProcessPath!; item.ProcessName = Process.GetCurrentProcess().ProcessName; Assert(RunningDetector.Check(item) == RunningState.NotRunning); Assert(TargetLauncher.CreateStartInfo(item).FileName == item.Target); return Task.CompletedTask; }
    private static async Task Running()
    { var item = new StartupItem { Name = "Current process", Target = Environment.ProcessPath!, Kind = TargetKind.Application }; var outcome = await new TargetLauncher().LaunchAsync(item, default); Assert(outcome.Status == RunStatus.Skipped); }
    private static Task StartupRegistry()
    {
        var name = "StartFlow.Test." + Guid.NewGuid().ToString("N");
        var integration = new StartupIntegration(@"C:\测试 空格\StartFlow.exe", name);
        try { Assert(!integration.Read().Exists); Assert(integration.Set(true).Matches); var moved = new StartupIntegration(@"D:\Moved\StartFlow.exe", name); Assert(!moved.Read().Matches); Assert(moved.Set(true).Matches); Assert(!integration.Read().Matches); Assert(integration.Expected.Contains(" --login")); }
        finally { Assert(!integration.Set(false).Exists); }
        return Task.CompletedTask;
    }
    private static async Task RealPowerShell()
    {
        var output = Path.Combine(root, "中文 输出.txt"); var item = new StartupItem { Name = "PowerShell", Kind = TargetKind.Command, Shell = CommandShell.PowerShell, Target = "[IO.File]::WriteAllText('" + output.Replace("'", "''") + "', '你好 Windows'); exit 0", Window = WindowMode.Minimized };
        var outcome = await new TargetLauncher().LaunchAsync(item, default); Assert(outcome.Status == RunStatus.Running); var exit = await outcome.Completion!.WaitAsync(TimeSpan.FromSeconds(20)); Assert(exit.Status == RunStatus.Succeeded && File.ReadAllText(output) == "你好 Windows");
    }
    private static async Task RealScript()
    {
        var folder = Path.Combine(root, "中文 空格"); Directory.CreateDirectory(folder); var script = Path.Combine(folder, "test script.cmd"); var output = Path.Combine(root, "script.txt"); File.WriteAllText(script, "@echo off\r\n>\"%~1\" echo ok\r\nexit /b 7\r\n");
        var item = new StartupItem { Name = "Script", Kind = TargetKind.Script, Target = script, Arguments = TargetLauncher.QuoteArgument(output), Window = WindowMode.Minimized }; var outcome = await new TargetLauncher().LaunchAsync(item, default); var exit = await outcome.Completion!.WaitAsync(TimeSpan.FromSeconds(20)); Assert(exit.Status == RunStatus.Failed && File.ReadAllText(output).Trim() == "ok");
    }
    private static async Task Instance()
    {
        var scope = Guid.NewGuid().ToString("N"); using var first = new SingleInstance(scope); using var second = new SingleInstance(scope); Assert(first.IsFirst && !second.IsFirst);
        var received = new TaskCompletionSource<string>(); var listen = first.ListenAsync(s => received.TrySetResult(s)); await second.NotifyAsync("show"); Assert(await received.Task.WaitAsync(TimeSpan.FromSeconds(5)) == "show");
    }
    private static Task Shortcut()
    {
        var path = Path.Combine(root, "test.lnk"); dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        try { dynamic link = shell.CreateShortcut(path); try { link.TargetPath = Environment.ProcessPath!; link.Arguments = "--test \"中文 空格\""; link.WorkingDirectory = root; link.Save(); } finally { Marshal.FinalReleaseComObject(link); } } finally { Marshal.FinalReleaseComObject(shell); }
        var read = InstalledApps.ReadShortcut(path); Assert(read.Arguments == "--test \"中文 空格\"" && read.WorkingDirectory == root); var item = InstalledApps.FromPath(path); Assert(item.Kind == TargetKind.Shortcut && item.Arguments == "" && item.Target == path); return Task.CompletedTask;
    }
    private static async Task Discovery() { var apps = await InstalledApps.ReadAsync(); Assert(apps.Count > 0 && apps.Any(a => a.CanLaunch)); Console.WriteLine($"  discovered {apps.Count} entries; Store: {apps.Count(a => a.Kind == TargetKind.StoreApp)}"); }
    private static async Task RealTargets()
    {
        var launcher = new TargetLauncher(); var file = Path.Combine(root, "启动验证 空格.txt"); File.WriteAllText(file, "StartFlow actual Windows validation. Temporary file; safe to close.");
        foreach (var item in new[] {
            new StartupItem { Name = "Notepad", Kind = TargetKind.Application, Target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe"), DetectRunning = false },
            new StartupItem { Name = "Text association", Kind = TargetKind.File, Target = file },
            new StartupItem { Name = "Explorer folder", Kind = TargetKind.Folder, Target = root },
            new StartupItem { Name = "Website", Kind = TargetKind.Website, Target = "https://example.com" }
        })
        { var result = await launcher.LaunchAsync(item, default); Console.WriteLine($"  {item.Name}: {result.Status} / {result.Message}"); Assert(result.Status == RunStatus.Submitted); }
        var apps = await InstalledApps.ReadAsync(); var calculator = apps.FirstOrDefault(a => a.Kind == TargetKind.StoreApp && a.Target.Contains("WindowsCalculator", StringComparison.OrdinalIgnoreCase)) ?? throw new Exception("Calculator not installed");
        var store = await launcher.LaunchAsync(new StartupItem { Name = calculator.Name, Kind = TargetKind.StoreApp, Target = calculator.Target, DetectRunning = false }, default);
        Console.WriteLine($"  {calculator.Name}: {store.Status} / {store.Message}"); Assert(store.Status == RunStatus.Submitted);
    }
}
