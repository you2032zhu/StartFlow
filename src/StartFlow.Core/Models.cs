using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StartFlow.Core;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Raise(name); return true; }
    public void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public enum TargetKind { Application, StoreApp, Website, Folder, File, Shortcut, Script, Command }
public enum ScheduleMode { Sequential, Timeline }
public enum WindowMode { Normal, Maximized, Minimized }
public enum CommandShell { PowerShell, Cmd }
public enum CloseMode { Ask, Tray, Exit }
public enum RunStatus { Waiting, Launching, Submitted, Skipped, Running, Succeeded, Failed, Cancelled, RetryWaiting }

public sealed class StartupItem : Observable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    private string name = "";
    public string Name { get => name; set => Set(ref name, value); }
    private bool enabled = true;
    public bool Enabled { get => enabled; set => Set(ref enabled, value); }
    public TargetKind Kind { get; set; }
    public string Target { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string IconPath { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Group { get; set; } = "";
    public double Seconds { get; set; }
    public int RetryCount { get; set; }
    public double RetryIntervalSeconds { get; set; } = 5;
    public WindowMode Window { get; set; }
    public bool RunAsAdmin { get; set; }
    public bool DetectRunning { get; set; } = true;
    public string ProcessPath { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public CommandShell Shell { get; set; }
}
public sealed class StartupPlan : Observable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    private string name = "";
    public string Name { get => name; set => Set(ref name, value); }
    public ScheduleMode Mode { get; set; }
    public double InitialDelaySeconds { get; set; }
    public ObservableCollection<StartupItem> Items { get; set; } = [];
    [JsonIgnore] public int EnabledCount => Items.Count(i => i.Enabled);
    [JsonIgnore] public string Summary => $"{EnabledCount} / {Items.Count}";
}
public sealed class AppSettings
{
    public string Language { get; set; } = "zh-CN";
    public string Theme { get; set; } = "System";
    public CloseMode CloseBehavior { get; set; }
    public Guid? LastPlanId { get; set; }
    public string? SkipArmedSession { get; set; }
    public string? LastHandledSession { get; set; }
    public string ProjectUrl { get; set; } = "https://github.com/you2032zhu/StartFlow";
}
public sealed class Configuration
{
    public int FormatVersion { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public ObservableCollection<StartupPlan> Plans { get; set; } = [];
}
public static class ConfigJson
{
    public static readonly JsonSerializerOptions Options = new()
    { WriteIndented = true, PropertyNameCaseInsensitive = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All), Converters = { new JsonStringEnumConverter() } };
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
}
public sealed class RunResult : Observable
{
    public Guid RunId { get; init; }
    public Guid ItemId { get; init; }
    public string Plan { get; init; } = "";
    public string Item { get; init; } = "";
    public DateTimeOffset? StartedAt { get; set; }
    public RunStatus Status { get; set; } = RunStatus.Waiting;
    public int Attempts { get; set; }
    public string Message { get; set; } = "";
    public void Notify() => Raise("");
}
