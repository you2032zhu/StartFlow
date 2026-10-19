using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using StartFlow.Core;
using StartFlow.Windows;

namespace StartFlow.App;

public sealed class RelayCommand(Func<Task> action, Func<bool>? can = null) : ICommand
{
    private bool busy;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !busy && (can?.Invoke() ?? true);
    public async void Execute(object? parameter)
    { if (!CanExecute(parameter)) return; busy = true; Refresh(); try { await action(); } catch (Exception ex) { Dialogs.Alert(ex.Message); } finally { busy = false; Refresh(); } }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
public sealed record ResultRow(string Plan, string Item, RunStatus Status, int Attempts, DateTimeOffset? StartedAt, string DisplayMessage);
public sealed class MainViewModel : Observable
{
    public Configuration Config { get; private set; }
    public ConfigurationStore Store { get; }
    public Scheduler Scheduler { get; }
    public StartupIntegration Startup { get; }
    public string Session { get; }
    public string VersionLabel => "v" + typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) + " · GPL-3.0-only";
    public ObservableCollection<StartupPlan> Plans => Config.Plans;
    private StartupPlan? selected;
    public StartupPlan? SelectedPlan { get => selected; set { selected = value; Search = ""; RefreshView(); Raise(); RefreshCommands(); } }
    public List<StartupItem> Selection { get; set; } = [];
    private string search = "";
    public string Search { get => search; set { Set(ref search, value); ItemsView?.Refresh(); } }
    public ICollectionView? ItemsView { get; private set; }
    public string Heading => SelectedPlan?.Name ?? L.T("Welcome");
    public string PlanSummary => SelectedPlan == null ? L.T("CreateFirst") : L.F("PlanSummary", SelectedPlan.EnabledCount, SelectedPlan.Items.Count, L.T(SelectedPlan.Mode.ToString()), SelectedPlan.InitialDelaySeconds);
    public int ItemCount => SelectedPlan?.Items.Count ?? 0;
    public string StatusText { get; private set; } = "";
    public ObservableCollection<ResultRow> Results { get; } = [];
    public ObservableCollection<string> QueueLabels { get; } = [];
    private readonly List<RelayCommand> commands = [];
    private bool saving;
    private bool grouped;
    public RelayCommand NewPlanCommand { get; }
    public RelayCommand PlanOptionsCommand { get; }
    public RelayCommand RunCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand AddCommand { get; }
    public RelayCommand AddFilesCommand { get; }
    public RelayCommand InstalledCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand BatchCommand { get; }
    public RelayCommand TestCommand { get; }
    public RelayCommand UpCommand { get; }
    public RelayCommand DownCommand { get; }
    public RelayCommand GroupCommand { get; }
    public RelayCommand RetryCommand { get; }
    public RelayCommand ChooseCommand { get; }
    public RelayCommand SettingsCommand { get; }
    public RelayCommand HelpCommand { get; }
    public MainViewModel(Configuration config, ConfigurationStore store, Scheduler scheduler, StartupIntegration startup, string session)
    {
        Config = config; Store = store; Scheduler = scheduler; Startup = startup; Session = session;
        NewPlanCommand = Command(() => { var name = Dialogs.Text("NewPlan"); if (name != null) { var plan = new StartupPlan { Name = UniquePlanName(name) }; Plans.Add(plan); SelectedPlan = plan; Save(); } });
        PlanOptionsCommand = Command(PlanOptions, () => selected != null);
        RunCommand = Command(() => { if (selected != null) Run(selected, DateTimeOffset.UtcNow); }, () => selected != null);
        StopCommand = Command(async () => await scheduler.StopAsync());
        AddCommand = Command(() => { var item = Dialogs.EditItem(new()); if (item != null) Add(item); }, () => selected != null);
        AddFilesCommand = Command(() => { var file = new OpenFileDialog { Multiselect = true }; if (file.ShowDialog() == true) AddPaths(file.FileNames); }, () => selected != null);
        InstalledCommand = Command(async () => { var item = await Dialogs.PickInstalled(); if (item != null) { var edited = Dialogs.EditItem(item); if (edited != null) Add(edited); } }, () => selected != null);
        EditCommand = Command(Edit, () => Selection.Count == 1);
        BatchCommand = Command(Batch, () => Selection.Count > 0);
        TestCommand = Command(() => { if (Selection.Count == 1 && Dialogs.Confirm(L.T("TestConfirm"))) { var plan = new StartupPlan { Name = L.T("TestItem"), Items = [ConfigJson.Clone(Selection[0])] }; plan.Items[0].Enabled = true; plan.Items[0].Seconds = 0; Run(plan, DateTimeOffset.UtcNow); } }, () => Selection.Count == 1);
        UpCommand = Command(() => MoveSelected(-1), () => Selection.Count == 1);
        DownCommand = Command(() => MoveSelected(1), () => Selection.Count == 1);
        GroupCommand = Command(() => { grouped = !grouped; RefreshView(); }, () => selected != null);
        RetryCommand = Command(RetryFailed, () => Results.Any(r => r.Status == RunStatus.Failed));
        ChooseCommand = Command(() => Choose(DateTimeOffset.UtcNow, false));
        SettingsCommand = Command(Settings);
        HelpCommand = Command(() =>
        {
            var tutorial = Path.Combine(AppContext.BaseDirectory, "docs", "用户与制作教程.md");
            if (!File.Exists(tutorial)) tutorial = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "用户与制作教程.md"));
            if (File.Exists(tutorial)) Process.Start(new ProcessStartInfo(tutorial) { UseShellExecute = true });
            else Dialogs.Alert(L.T("HelpLocation"));
        });
        scheduler.Changed += () => Application.Current.Dispatcher.BeginInvoke(RefreshRun);
        Wire(); selected = Plans.FirstOrDefault(); RefreshView(); RefreshRun();
    }
    private RelayCommand Command(Action action, Func<bool>? can = null) => Command(() => { action(); return Task.CompletedTask; }, can);
    private RelayCommand Command(Func<Task> action, Func<bool>? can = null) { var command = new RelayCommand(action, can); commands.Add(command); return command; }
    public void RefreshCommands() { foreach (var command in commands) command.Refresh(); }
    private void Wire()
    {
        foreach (var plan in Plans)
        {
            plan.Items.CollectionChanged -= ItemsChanged; plan.Items.CollectionChanged += ItemsChanged;
            foreach (var item in plan.Items) { item.PropertyChanged -= ItemChanged; item.PropertyChanged += ItemChanged; }
        }
    }
    private void ItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) { Wire(); RefreshView(); }
    private void ItemChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(StartupItem.Enabled)) { Save(); RefreshPlanLabels(); } }
    private void RefreshPlanLabels() { foreach (var p in Plans) { p.Raise(nameof(p.Summary)); p.Raise(nameof(p.EnabledCount)); } Raise(nameof(Heading)); Raise(nameof(PlanSummary)); Raise(nameof(ItemCount)); }
    private void RefreshView()
    {
        Selection.Clear();
        ItemsView = selected == null ? null : new ListCollectionView((IList)selected.Items);
        if (ItemsView != null)
        {
            ItemsView.Filter = value => value is StartupItem i && (search.Length == 0 || $"{i.Name}\n{i.Target}\n{i.Group}\n{i.Notes}".Contains(search, StringComparison.CurrentCultureIgnoreCase));
            if (grouped) ItemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(StartupItem.Group)));
        }
        Raise(nameof(ItemsView)); RefreshPlanLabels(); RefreshCommands();
    }
    public bool Save()
    {
        if (saving) return true;
        try { saving = true; Store.Save(Config); return true; }
        catch (Exception ex) { Dialogs.Alert(L.T("SaveFailed") + "\n" + ex.Message); return false; }
        finally { saving = false; }
    }
    private string UniquePlanName(string value) { var name = value; var n = 2; while (Plans.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = $"{value} ({n++})"; return name; }
    public void AddPaths(IEnumerable<string> paths)
    {
        if (SelectedPlan == null) { Dialogs.Alert(L.T("SelectPlan")); return; }
        foreach (var path in paths) { try { Add(InstalledApps.FromPath(path)); } catch (Exception ex) { Dialogs.Alert(path + "\n" + ex.Message); } }
    }
    private bool Duplicate(StartupItem item, Guid? except = null) => selected!.Items.Any(i => i.Id != except && Core.Validation.DuplicateKey(i) == Core.Validation.DuplicateKey(item));
    private void Add(StartupItem item)
    {
        if (selected == null) return;
        if (Duplicate(item) && !Dialogs.Confirm(L.F("DuplicateConfirm", item.Name))) return;
        selected.Items.Add(item); Wire(); Save(); RefreshView();
    }
    private void Edit()
    {
        if (Selection.Count != 1 || selected == null) return;
        var original = Selection[0]; var item = Dialogs.EditItem(original); if (item == null) return;
        if (Duplicate(item, original.Id) && !Dialogs.Confirm(L.F("DuplicateConfirm", item.Name))) return;
        selected.Items[selected.Items.IndexOf(original)] = item; Wire(); Save(); RefreshView();
    }
    private void PlanOptions()
    {
        if (selected == null) return;
        switch (Dialogs.Menu("PlanOptions", "EditPlan", "CopyPlan", "DeletePlan"))
        {
            case 0: if (Dialogs.EditPlan(selected)) { Save(); RefreshPlanLabels(); } break;
            case 1:
                var copy = ConfigJson.Clone(selected); copy.Id = Guid.NewGuid(); copy.Name = UniquePlanName(copy.Name); foreach (var i in copy.Items) i.Id = Guid.NewGuid(); Plans.Add(copy); Wire(); SelectedPlan = copy; Save(); break;
            case 2: if (Dialogs.Confirm(L.F("DeletePlanConfirm", selected.Name))) { Plans.Remove(selected); SelectedPlan = Plans.FirstOrDefault(); Save(); } break;
        }
    }
    private void Batch()
    {
        if (selected == null) return;
        var items = Selection.ToArray(); var choice = Dialogs.Menu("BatchActions", "EnableSelected", "DisableSelected", "CopySelected", "MoveToPlan", "SetGroup", "DeleteSelected");
        switch (choice)
        {
            case 0: foreach (var i in items) i.Enabled = true; break;
            case 1: foreach (var i in items) i.Enabled = false; break;
            case 2: foreach (var i in items) { var copy = ConfigJson.Clone(i); copy.Id = Guid.NewGuid(); if (Dialogs.Confirm(L.F("DuplicateConfirm", copy.Name))) selected.Items.Add(copy); } break;
            case 3:
                var target = Dialogs.PickPlan(Plans.Where(p => p != selected), "MoveToPlan"); if (target == null) return;
                foreach (var i in items) { if (target.Items.Any(t => Core.Validation.DuplicateKey(t) == Core.Validation.DuplicateKey(i)) && !Dialogs.Confirm(L.F("DuplicateConfirm", i.Name))) continue; selected.Items.Remove(i); target.Items.Add(i); } break;
            case 4: var group = Dialogs.Text("Group"); if (group == null) return; foreach (var i in items) i.Group = group; break;
            case 5: if (!Dialogs.Confirm(L.F("DeleteItemsConfirm", items.Length))) return; foreach (var i in items) selected.Items.Remove(i); break;
            default: return;
        }
        Wire(); Save(); RefreshView();
    }
    public void DeleteSelection() { if (selected == null || Selection.Count == 0 || !Dialogs.Confirm(L.F("DeleteItemsConfirm", Selection.Count))) return; foreach (var item in Selection.ToArray()) selected.Items.Remove(item); Save(); RefreshView(); }
    public void Reorder(StartupItem item, StartupItem target)
    {
        if (selected == null || grouped || search.Length > 0) { Dialogs.Alert(L.T("ReorderHint")); return; }
        var from = selected.Items.IndexOf(item); var to = selected.Items.IndexOf(target); if (from < 0 || to < 0 || from == to) return;
        selected.Items.Move(from, to); Save(); RefreshView();
    }
    private void MoveSelected(int direction)
    {
        if (selected == null || Selection.Count != 1) return;
        var index = selected.Items.IndexOf(Selection[0]); var next = index + direction;
        if (next >= 0 && next < selected.Items.Count) Reorder(Selection[0], selected.Items[next]);
    }
    public void Run(StartupPlan plan, DateTimeOffset reference)
    {
        var snapshot = ConfigJson.Clone(plan);
        if (!Scheduler.Enqueue(snapshot, reference)) { Dialogs.Alert(L.T("DuplicateQueue")); return; }
        Config.Settings.LastPlanId = plan.Id; Save(); RefreshRun();
    }
    public void Choose(DateTimeOffset reference, bool login)
    {
        var plan = Dialogs.ChoosePlan(Config, reference, login);
        if (plan != null) Run(plan, login ? reference : DateTimeOffset.UtcNow);
    }
    private void RetryFailed()
    {
        var currentResults = Scheduler.Results; if (currentResults.Length == 0) return;
        var source = Scheduler.Latest?.Plan;
        if (source == null) return;
        var failed = currentResults.Where(r => r.Status == RunStatus.Failed).Select(r => r.ItemId).ToHashSet();
        var plan = ConfigJson.Clone(source); plan.Id = Guid.NewGuid(); plan.Name += " · " + L.T("RetryFailed"); plan.Mode = ScheduleMode.Sequential; plan.InitialDelaySeconds = 0;
        plan.Items = new(plan.Items.Where(i => failed.Contains(i.Id))); foreach (var i in plan.Items) { i.Seconds = 0; i.Enabled = true; }
        if (plan.Items.Count > 0) Run(plan, DateTimeOffset.UtcNow);
    }
    public void RefreshRun()
    {
        Results.Clear(); foreach (var r in Scheduler.Results) Results.Add(new(r.Plan, r.Item, r.Status, r.Attempts, r.StartedAt?.ToLocalTime(), L.T(r.Message)));
        QueueLabels.Clear(); if (Scheduler.Current is { } c) QueueLabels.Add(L.F("CurrentPlan", c.Plan.Name)); foreach (var q in Scheduler.Waiting) QueueLabels.Add(L.F("QueuedPlan", q.Plan.Name));
        StatusText = Scheduler.Current is { } current ? L.F("RunningSummary", current.Plan.Name, Scheduler.Waiting.Length) : L.T("ReadyHint"); Raise(nameof(StatusText)); RefreshCommands();
    }
    public void ArmSkip() { LoginPolicy.ArmSkip(Config.Settings, Session); Save(); Dialogs.Alert(L.T("SkipArmed")); }
    private void Settings()
    {
        var choice = Dialogs.Menu("Settings", "GeneralSettings", "Import", "Export", "ArmSkip", "ClearSkip", "OpenData", "ProjectHome");
        switch (choice)
        {
            case 0: GeneralSettings(); break;
            case 1: Import(); break;
            case 2: Export(); break;
            case 3: ArmSkip(); break;
            case 4: Config.Settings.SkipArmedSession = null; Save(); break;
            case 5: Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { Store.DirectoryPath }, UseShellExecute = true }); break;
            case 6:
                if (Uri.TryCreate(Config.Settings.ProjectUrl, UriKind.Absolute, out var url) && url.Scheme == "https" && url.Host == "github.com") Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
                else Dialogs.Alert(L.T("ProjectNotSet")); break;
        }
    }
    private void GeneralSettings()
    {
        var settings = ConfigJson.Clone(Config.Settings); var initial = Startup.Read(); var win = Dialogs.Window("GeneralSettings", 640, 710);
        CheckBox? startup = null;
        var form = Dialogs.Form(win, () =>
        {
            var desired = startup?.IsChecked == true;
            if (desired != initial.Exists) { var actual = Startup.Set(desired); if (actual.DisabledByWindows && desired) Dialogs.Alert(L.T("DisabledByWindows")); }
            Config.Settings = settings;
            if (!Save()) return;
            L.Apply(settings.Language); L.Theme(settings.Theme); Raise(nameof(Plans)); RefreshView(); RefreshRun(); ((App)Application.Current).RebuildTray(); win.DialogResult = true;
        });
        Dialogs.Label(form, "Language"); var language = new ComboBox { ItemsSource = new[] { new Choice<string>("zh-CN", "简体中文"), new Choice<string>("en", "English") }, SelectedIndex = settings.Language == "en" ? 1 : 0 }; language.SelectionChanged += (_, _) => settings.Language = ((Choice<string>)language.SelectedItem).Value; form.Children.Add(language);
        Dialogs.Label(form, "Theme"); var theme = new ComboBox { ItemsSource = new[] { new Choice<string>("System", L.T("System")), new Choice<string>("Light", L.T("Light")), new Choice<string>("Dark", L.T("Dark")) }, SelectedIndex = Array.IndexOf(new[] { "System", "Light", "Dark" }, settings.Theme) }; theme.SelectionChanged += (_, _) => settings.Theme = ((Choice<string>)theme.SelectedItem).Value; form.Children.Add(theme);
        Dialogs.Choices<CloseMode>(form, "CloseBehavior", settings.CloseBehavior, v => settings.CloseBehavior = v);
        startup = new CheckBox { Content = L.T("LoginStartup"), IsChecked = initial.Exists }; form.Children.Add(startup);
        form.Children.Add(new TextBlock { Text = L.T(initial.Matches ? "StartupCorrect" : initial.Exists ? "StartupStale" : "StartupOff") + "\n" + initial.Command, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        var repair = new Button { Content = L.T("RepairStartup"), IsEnabled = initial.Exists };
        repair.Click += (_, _) => { var actual = Startup.Set(true); Dialogs.Alert(L.T(actual.DisabledByWindows ? "DisabledByWindows" : "StartupCorrect")); }; form.Children.Add(repair);
        Dialogs.Label(form, "StartupHint"); Dialogs.Field(form, "ProjectUrl", settings, nameof(settings.ProjectUrl));
        form.Children.Add(new TextBlock { Text = L.T("DataLocation") + "\n" + Store.DirectoryPath, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) }); Dialogs.Label(form, "PrivacyHint");
        win.ShowDialog();
    }
    private void Import()
    {
        var dialog = new OpenFileDialog { Filter = "StartFlow JSON|*.json|All|*.*" }; if (dialog.ShowDialog() != true) return;
        var incoming = ConfigurationStore.Read(dialog.FileName); if (!Dialogs.ImportPreview(incoming, out var merge)) return;
        Store.BackupBeforeReplace(); var result = ConfigurationStore.Import(Config, incoming, merge); Store.Save(result); Config = result; Wire(); SelectedPlan = Plans.FirstOrDefault(); Raise(nameof(Plans));
        L.Apply(Config.Settings.Language); L.Theme(Config.Settings.Theme); ((App)Application.Current).RebuildTray(); RefreshRun(); Dialogs.Alert(L.T("ImportDone"));
    }
    private void Export()
    {
        var copy = ConfigJson.Clone(Config); copy.Settings.SkipArmedSession = null; copy.Settings.LastHandledSession = null;
        var win = Dialogs.Window("ExportPreview", 780, 720); var json = new Dialogs.TextValue { Value = JsonSerializer.Serialize(copy, ConfigJson.Options) };
        var form = Dialogs.Form(win, () => { var parsed = JsonSerializer.Deserialize<Configuration>(json.Value, ConfigJson.Options) ?? throw new InvalidDataException(); var errors = Core.Validation.Check(parsed); if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors)); win.DialogResult = true; });
        Dialogs.Label(form, "ExportWarning"); var text = Dialogs.Field(form, "ExportPreview", json, nameof(json.Value), true); text.MinHeight = 480; text.FontFamily = new System.Windows.Media.FontFamily("Consolas");
        if (win.ShowDialog() != true) return;
        var file = new SaveFileDialog { Filter = "StartFlow JSON|*.json", FileName = "StartFlow-config.json" };
        if (file.ShowDialog() == true)
        {
            if (Core.Validation.NormalizePath(Path.GetDirectoryName(file.FileName)!) == Core.Validation.NormalizePath(Store.DirectoryPath) && Path.GetFileName(file.FileName).StartsWith("config.json", StringComparison.OrdinalIgnoreCase)) { Dialogs.Alert(L.T("ExportReservedPath")); return; }
            File.WriteAllText(file.FileName, json.Value);
        }
    }
}
