using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using StartFlow.Core;
using StartFlow.Windows;
using Microsoft.Win32;

namespace StartFlow.App;

public sealed record Choice<T>(T Value, string Label) { public override string ToString() => Label; }
public static class Dialogs
{
    public static Window Window(string key, double width = 540, double height = 650)
        => new() { Title = L.T(key), Owner = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => w.IsVisible), Width = Math.Min(width, SystemParameters.WorkArea.Width - 30), Height = Math.Min(height, SystemParameters.WorkArea.Height - 30), MinWidth = Math.Min(width, 420), MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = true };
    public static StackPanel Form(Window window, Action save)
    {
        var dock = new DockPanel { Margin = new Thickness(24) }; window.Content = dock;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = L.T("Cancel"), IsCancel = true };
        cancel.Click += (_, _) => window.DialogResult = false;
        var ok = new Button { Content = L.T("Save"), IsDefault = true, Style = (Style)Application.Current.FindResource("PrimaryButton") };
        ok.Click += (_, _) => { try { if (HasErrors(dock)) { Alert(L.T("InvalidInput")); return; } save(); } catch (Exception ex) { Alert(ex.Message); } };
        actions.Children.Add(cancel); actions.Children.Add(ok); DockPanel.SetDock(actions, Dock.Bottom); dock.Children.Add(actions);
        var panel = new StackPanel(); dock.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); return panel;
    }
    public static void Label(Panel panel, string key) => panel.Children.Add(new TextBlock { Text = L.T(key), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
    public static TextBox Field(Panel panel, string key, object source, string property, bool multiline = false)
    {
        Label(panel, key);
        var text = new TextBox { AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 80 : 34 };
        text.SetBinding(TextBox.TextProperty, new Binding(property) { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, ValidatesOnExceptions = true });
        System.Windows.Automation.AutomationProperties.SetName(text, L.T(key)); panel.Children.Add(text); return text;
    }
    public static ComboBox Choices<T>(Panel panel, string key, T current, Action<T> changed) where T : struct, Enum
    {
        Label(panel, key); var combo = new ComboBox { ItemsSource = Enum.GetValues<T>().Select(v => new Choice<T>(v, L.T(v.ToString()))).ToArray(), SelectedIndex = Array.IndexOf(Enum.GetValues<T>(), current) };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is Choice<T> c) changed(c.Value); }; panel.Children.Add(combo); return combo;
    }
    public static CheckBox Check(Panel panel, string key, object source, string property)
    { var check = new CheckBox { Content = L.T(key) }; check.SetBinding(CheckBox.IsCheckedProperty, new Binding(property) { Source = source, Mode = BindingMode.TwoWay }); panel.Children.Add(check); return check; }
    private static bool HasErrors(DependencyObject node)
    {
        if (System.Windows.Controls.Validation.GetHasError(node)) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) if (HasErrors(VisualTreeHelper.GetChild(node, i))) return true;
        return false;
    }
    public static void Alert(string text) => MessageBox.Show(text, L.T("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    public static bool Confirm(string text) => MessageBox.Show(text, L.T("AppTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    public static string? Text(string title, string initial = "")
    {
        var value = new TextValue { Value = initial }; var win = Window(title, 460, 240);
        var form = Form(win, () => { if (string.IsNullOrWhiteSpace(value.Value)) return; win.DialogResult = true; }); Field(form, title, value, nameof(TextValue.Value));
        return win.ShowDialog() == true ? value.Value.Trim() : null;
    }
    public sealed class TextValue { public string Value { get; set; } = ""; }
    public static int Menu(string title, params string[] keys)
    {
        var win = Window(title, 430, Math.Min(650, 135 + keys.Length * 49)); var panel = new StackPanel { Margin = new Thickness(22) }; win.Content = panel; var answer = -1;
        for (var i = 0; i < keys.Length; i++) { var index = i; var button = new Button { Content = L.T(keys[i]), HorizontalContentAlignment = HorizontalAlignment.Left }; button.Click += (_, _) => { answer = index; win.DialogResult = true; }; panel.Children.Add(button); }
        win.ShowDialog(); return answer;
    }
    public static StartupItem? EditItem(StartupItem original)
    {
        var item = ConfigJson.Clone(original); var win = Window("ItemDetails", 610, 770);
        var form = Form(win, () =>
        {
            if (item.Kind == TargetKind.Website && !Core.Validation.TryWebsite(item.Target, out _))
            {
                var suggestion = Core.Validation.SuggestWebsite(item.Target);
                if (Core.Validation.TryWebsite(suggestion, out _) && Confirm(L.F("CorrectUrl", suggestion))) item.Target = suggestion;
            }
            var errors = Core.Validation.CheckItem(item);
            if (errors.Count > 0) { Alert(string.Join("\n", errors.Select(L.T))); return; }
            if (Core.Validation.MissingTarget(item) && !Confirm(L.T("MissingSave"))) return;
            win.DialogResult = true;
        });
        Field(form, "Name", item, nameof(item.Name));
        StackPanel? commandPanel = null;
        Action refreshType = () => { };
        Choices<TargetKind>(form, "Type", item.Kind, v => { item.Kind = v; if (commandPanel != null) commandPanel.Visibility = v == TargetKind.Command ? Visibility.Visible : Visibility.Collapsed; refreshType(); });
        var target = Field(form, "Target", item, nameof(item.Target), true);
        var browse = new Button { Content = L.T("BrowseTarget"), HorizontalAlignment = HorizontalAlignment.Left };
        browse.Click += (_, _) =>
        {
            if (item.Kind == TargetKind.Folder) { var folder = new OpenFolderDialog(); if (folder.ShowDialog(win) == true) target.Text = folder.FolderName; }
            else { var file = new OpenFileDialog(); if (file.ShowDialog(win) == true) target.Text = file.FileName; }
        }; form.Children.Add(browse);
        commandPanel = new StackPanel { Visibility = item.Kind == TargetKind.Command ? Visibility.Visible : Visibility.Collapsed };
        Choices<CommandShell>(commandPanel, "Interpreter", item.Shell, v => item.Shell = v); Label(commandPanel, "CommandHint"); form.Children.Add(commandPanel);
        Field(form, "Group", item, nameof(item.Group)); Check(form, "Enabled", item, nameof(item.Enabled));
        Field(form, "ItemSeconds", item, nameof(item.Seconds));
        var retry = new StackPanel(); Field(retry, "RetryCount", item, nameof(item.RetryCount)); Field(retry, "RetryInterval", item, nameof(item.RetryIntervalSeconds));
        form.Children.Add(new Expander { Header = L.T("RetryOptions"), Content = retry });
        Choices<WindowMode>(form, "WindowState", item.Window, v => item.Window = v); Label(form, "WindowHint");
        Field(form, "Notes", item, nameof(item.Notes), true);
        var advancedPanel = new StackPanel();
        var argumentsPanel = new StackPanel(); Field(argumentsPanel, "Arguments", item, nameof(item.Arguments)); advancedPanel.Children.Add(argumentsPanel);
        var workPanel = new StackPanel(); Field(workPanel, "WorkingDirectory", item, nameof(item.WorkingDirectory)); advancedPanel.Children.Add(workPanel);
        var admin = Check(advancedPanel, "RunAsAdmin", item, nameof(item.RunAsAdmin));
        var detectionPanel = new StackPanel(); Check(detectionPanel, "DetectRunning", item, nameof(item.DetectRunning)); Label(detectionPanel, "DetectionHint"); advancedPanel.Children.Add(detectionPanel);
        var processPanel = new StackPanel(); Field(processPanel, "ProcessPath", item, nameof(item.ProcessPath)); Field(processPanel, "ProcessName", item, nameof(item.ProcessName)); advancedPanel.Children.Add(processPanel);
        refreshType = () =>
        {
            argumentsPanel.Visibility = item.Kind is TargetKind.Application or TargetKind.Shortcut or TargetKind.Script or TargetKind.StoreApp ? Visibility.Visible : Visibility.Collapsed;
            workPanel.Visibility = item.Kind is TargetKind.Application or TargetKind.Shortcut or TargetKind.Script or TargetKind.Command ? Visibility.Visible : Visibility.Collapsed;
            admin.Visibility = workPanel.Visibility;
            if (admin.Visibility == Visibility.Collapsed) { item.RunAsAdmin = false; admin.IsChecked = false; }
            detectionPanel.Visibility = item.Kind is TargetKind.Application or TargetKind.StoreApp ? Visibility.Visible : Visibility.Collapsed;
            processPanel.Visibility = item.Kind == TargetKind.Application ? Visibility.Visible : Visibility.Collapsed;
        };
        refreshType();
        Field(advancedPanel, "IconPath", item, nameof(item.IconPath));
        var icon = new Button { Content = L.T("BrowseIcon") }; icon.Click += (_, _) => { var dialog = new OpenFileDialog { Filter = "Icon / Program|*.ico;*.exe;*.lnk|All|*.*" }; if (dialog.ShowDialog(win) == true) { item.IconPath = dialog.FileName; ((TextBox)advancedPanel.Children[advancedPanel.Children.Count - 1]).Text = item.IconPath; } }; advancedPanel.Children.Add(icon);
        form.Children.Add(new Expander { Header = L.T("Advanced"), Content = advancedPanel });
        return win.ShowDialog() == true ? item : null;
    }
    public static bool EditPlan(StartupPlan plan)
    {
        var copy = ConfigJson.Clone(plan); var win = Window("PlanOptions", 540, 540);
        var form = Form(win, () => { if (string.IsNullOrWhiteSpace(copy.Name) || !double.IsFinite(copy.InitialDelaySeconds) || copy.InitialDelaySeconds is < 0 or > 604800) { Alert(L.T("InvalidInput")); return; } win.DialogResult = true; });
        Field(form, "Name", copy, nameof(copy.Name)); Choices<ScheduleMode>(form, "ScheduleMode", copy.Mode, v => copy.Mode = v);
        Field(form, "InitialDelay", copy, nameof(copy.InitialDelaySeconds)); Label(form, "SchedulingHelp");
        if (win.ShowDialog() != true) return false;
        plan.Name = copy.Name.Trim(); plan.Mode = copy.Mode; plan.InitialDelaySeconds = copy.InitialDelaySeconds; return true;
    }
    public static StartupPlan? PickPlan(IEnumerable<StartupPlan> plans, string title)
    {
        var win = Window(title, 540, 440); var list = new ListBox { ItemsSource = plans.ToArray(), DisplayMemberPath = "Name", MinHeight = 220 };
        var form = Form(win, () => { if (list.SelectedItem != null) win.DialogResult = true; }); form.Children.Add(list);
        return win.ShowDialog() == true ? list.SelectedItem as StartupPlan : null;
    }
    public static StartupPlan? ChoosePlan(Configuration config, DateTimeOffset reference, bool login)
    {
        var win = Window("ChooseRun", 620, 670); var dock = new DockPanel { Margin = new Thickness(26) }; win.Content = dock;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var skip = new Button { Content = L.T("SkipNow") }; skip.Click += (_, _) => win.DialogResult = false;
        var run = new Button { Content = L.T("RunPlan"), Style = (Style)Application.Current.FindResource("PrimaryButton") }; actions.Children.Add(skip); actions.Children.Add(run); DockPanel.SetDock(actions, Dock.Bottom); dock.Children.Add(actions);
        var top = new StackPanel(); top.Children.Add(new TextBlock { Text = L.T("ChooseTitle"), FontSize = 25, FontWeight = FontWeights.SemiBold });
        Label(top, "ChooseHint"); top.Children.Add(new TextBlock { Text = L.F("LastPlan", config.Plans.FirstOrDefault(p => p.Id == config.Settings.LastPlanId)?.Name ?? L.T("None")), Margin = new Thickness(0, 14, 0, 8), TextWrapping = TextWrapping.Wrap });
        if (login) top.Children.Add(new TextBlock { Text = L.F("LoginReference", reference.ToLocalTime().ToString("HH:mm:ss")), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        DockPanel.SetDock(top, Dock.Top); dock.Children.Add(top);
        var stack = new StackPanel(); var scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; dock.Children.Add(scroll);
        StartupPlan? selected = null;
        foreach (var plan in config.Plans)
        {
            var radio = new RadioButton { Content = $"{plan.Name}   ·   {plan.Summary}", GroupName = "Plan", Padding = new Thickness(8), Foreground = (Brush)Application.Current.FindResource("TextBrush") };
            radio.Checked += (_, _) => selected = plan;
            stack.Children.Add(radio);
            var details = new TextBlock { Text = string.Join("\n", plan.Items.Select(i => $"{(i.Enabled ? "●" : "○")}  {i.Name} · {L.T(i.Kind.ToString())} · {i.Seconds}s")), TextWrapping = TextWrapping.Wrap };
            stack.Children.Add(new Expander { Header = L.T("PreviewItems"), Content = details, Margin = new Thickness(28, 0, 0, 14) });
        }
        if (config.Plans.Count == 0) Label(stack, "CreateFirst");
        run.Click += (_, _) => { if (selected == null) { Alert(L.T("SelectPlan")); return; } win.DialogResult = true; };
        return win.ShowDialog() == true ? selected : null;
    }
    public static async Task<StartupItem?> PickInstalled()
    {
        var win = Window("Installed", 750, 650); var dock = new DockPanel { Margin = new Thickness(22) }; win.Content = dock;
        var hint = new TextBlock { Text = L.T("InstalledHint"), TextWrapping = TextWrapping.Wrap }; DockPanel.SetDock(hint, Dock.Top); dock.Children.Add(hint);
        var search = new TextBox(); DockPanel.SetDock(search, Dock.Top); dock.Children.Add(search);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; var add = new Button { Content = L.T("AddItem") }; var manual = new Button { Content = L.T("BrowseTarget") }; buttons.Children.Add(manual); buttons.Children.Add(add); DockPanel.SetDock(buttons, Dock.Bottom); dock.Children.Add(buttons);
        var list = new ListBox { DisplayMemberPath = "Name" }; dock.Children.Add(list); StartupItem? result = null;
        var apps = new List<InstalledApp>();
        win.Loaded += async (_, _) => { try { apps = await InstalledApps.ReadAsync(); list.ItemsSource = apps; } catch (Exception ex) { Alert(ex.Message); } };
        search.TextChanged += (_, _) => list.ItemsSource = apps.Where(a => a.Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is InstalledApp a) hint.Text = L.T(a.CanLaunch ? "InstalledHint" : "ManualEntryRequired") + "\n" + a.Detail; };
        manual.Click += (_, _) => { var file = new OpenFileDialog(); if (file.ShowDialog(win) == true) { result = InstalledApps.FromPath(file.FileName); win.DialogResult = true; } };
        add.Click += (_, _) => { if (list.SelectedItem is not InstalledApp a) return; if (!a.CanLaunch) { Alert(L.T("ManualEntryRequired")); return; } result = a.Kind == TargetKind.Shortcut ? InstalledApps.FromPath(a.Target) : new StartupItem { Name = a.Name, Target = a.Target, Kind = a.Kind }; win.DialogResult = true; };
        win.ShowDialog(); await Task.CompletedTask; return result;
    }
    public static bool ImportPreview(Configuration incoming, out bool merge)
    {
        var win = Window("ImportPreview", 760, 700); var dock = new DockPanel { Margin = new Thickness(22) }; win.Content = dock;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; var merged = true;
        foreach (var key in new[] { "Cancel", "Merge", "Replace" }) { var button = new Button { Content = L.T(key) }; button.Click += (_, _) => { if (key == "Cancel") { win.DialogResult = false; return; } merged = key == "Merge"; if (!merged && !Confirm(L.T("ReplaceConfirm"))) return; win.DialogResult = true; }; actions.Children.Add(button); }
        DockPanel.SetDock(actions, Dock.Bottom); dock.Children.Add(actions);
        var panel = new StackPanel(); Label(panel, "ImportWarning");
        foreach (var plan in incoming.Plans)
        {
            panel.Children.Add(new TextBlock { Text = plan.Name + " · " + plan.Summary, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 5) });
            foreach (var i in plan.Items) panel.Children.Add(new TextBlock { Text = $"{i.Name} · {L.T(i.Kind.ToString())}\n{i.Target}\n{L.T("Arguments")}: {i.Arguments}\n{L.T("WorkingDirectory")}: {i.WorkingDirectory}\n{L.T("ProcessPath")}: {i.ProcessPath}\n{L.T("IconPath")}: {i.IconPath}\n{L.T("RunAsAdmin")}: {i.RunAsAdmin}\n{L.T("Interpreter")}: {i.Shell}\n{L.T("Notes")}: {i.Notes}\n{(Core.Validation.MissingTarget(i) ? L.T("MissingTarget") : "")}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 12) });
        }
        dock.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); var accepted = win.ShowDialog() == true; merge = merged; return accepted;
    }
}
