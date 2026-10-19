using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using StartFlow.Core;
using StartFlow.Windows;
using Forms = System.Windows.Forms;

namespace StartFlow.App;

public partial class App : Application
{
    private SingleInstance? instance;
    private Forms.NotifyIcon? tray;
    private Mutex? installerGuard;
    private MainViewModel? vm;
    private MainWindow? window;
    private bool exiting;
    private readonly DateTimeOffset entryReference = DateTimeOffset.UtcNow;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) => { Dialogs.Alert(args.Exception.Message); args.Handled = true; };
        try
        {
            var testIndex = Array.IndexOf(e.Args, "--test-data");
            var testPath = testIndex >= 0 && testIndex + 1 < e.Args.Length ? Path.GetFullPath(e.Args[testIndex + 1]) : null;
            var testScope = testPath == null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(testPath)))[..12];
            instance = new SingleInstance(testScope);
            var login = e.Args.Contains("--login");
            if (!instance.IsFirst) { await instance.NotifyAsync(testPath != null && e.Args.Contains("--test-close") ? "test-close" : testPath != null && e.Args.Contains("--test-exit") ? "test-exit" : login ? "login:" + entryReference.ToString("O") : "show"); Shutdown(); return; }
            if (testPath != null && (e.Args.Contains("--test-exit") || e.Args.Contains("--test-close"))) { Shutdown(); return; }
            if (testPath == null) installerGuard = new Mutex(false, @"Local\StartFlowInstallerGuard");
            var portable = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"));
            var data = testPath ?? (portable ? Path.Combine(AppContext.BaseDirectory, "Data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StartFlow"));
            var store = new ConfigurationStore(data); var loaded = store.Load();
            L.Apply(loaded.Config.Settings.Language); L.Theme(loaded.Config.Settings.Theme);
            // Verify the selected data location before accepting edits. Do not silently switch portable storage.
            var probe = Path.Combine(data, ".write-probe-" + Guid.NewGuid().ToString("N")); using (File.Create(probe)) { } File.Delete(probe);
            if (loaded.RequiresRecovery)
            {
                if (!Dialogs.Confirm(L.T("RecoveryPrompt") + "\n" + loaded.Warning)) { Shutdown(); return; }
                store.PreserveDamaged(); store.Save(loaded.Config);
            }
            vm = new(loaded.Config, store, new Scheduler(new TargetLauncher()), new StartupIntegration(Environment.ProcessPath!, testScope == null ? "StartFlow" : "StartFlow.Test." + testScope), LogonIdentity.Current());
            window = new MainWindow(vm); MainWindow = window; RebuildTray();
            SystemEvents.UserPreferenceChanged += ThemeChanged;
            _ = instance.ListenAsync(message => Dispatcher.BeginInvoke(async () => { if (message == "show") window.Present(); else if (message == "test-close" && testPath != null) window.Close(); else if (message == "test-exit" && testPath != null) await ExitAsync(); else if (message.StartsWith("login:", StringComparison.Ordinal) && DateTimeOffset.TryParse(message[6..], out var receivedAt)) HandleLogin(receivedAt); }));
            if (testPath != null && e.Args.Contains("--test-render"))
            {
                window.ContentRendered += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
                {
                    window.UpdateLayout();
                    var visual = (FrameworkElement)window.Content;
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(visual);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(testPath, "actual-window.png")); encoder.Save(stream);
                });
            }
            if (login) HandleLogin(entryReference); else window.Present();
        }
        catch (Exception ex) { MessageBox.Show("StartFlow: " + ex.Message + "\n\n" + L.T("StartupFailure")); Shutdown(1); }
    }
    private void ThemeChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(() => { if (vm != null) L.Theme(vm.Config.Settings.Theme); });
    private void HandleLogin(DateTimeOffset reference)
    {
        if (vm == null || window == null) return;
        if (!LoginPolicy.Handle(vm.Config.Settings, vm.Session, true)) { vm.Save(); return; }
        if (!vm.Save()) return;
        vm.Choose(reference, true);
    }
    public void RebuildTray()
    {
        if (vm == null || window == null) return;
        tray?.Dispose();
        using var iconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/StartFlow.ico")).Stream;
        tray = new Forms.NotifyIcon { Icon = new System.Drawing.Icon(iconStream), Text = L.T("AppTitle"), Visible = true };
        var menu = new Forms.ContextMenuStrip();
        void Add(string key, Action action) => menu.Items.Add(L.T(key), null, (_, _) => Dispatcher.BeginInvoke(action));
        Add("OpenManager", window.Present); Add("ChooseRun", () => vm.Choose(DateTimeOffset.UtcNow, false)); Add("ArmSkip", vm.ArmSkip); Add("Stop", () => vm.StopCommand.Execute(null));
        menu.Items.Add(new Forms.ToolStripSeparator()); Add("Exit", async () => await ExitAsync()); tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(window.Present);
    }
    public async Task ExitAsync()
    {
        if (exiting) return; exiting = true;
        if (vm != null) { await vm.Scheduler.DisposeAsync(); vm.Save(); }
        if (window != null) { window.Exiting = true; window.Close(); }
        Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    { SystemEvents.UserPreferenceChanged -= ThemeChanged; tray?.Dispose(); installerGuard?.Dispose(); instance?.Dispose(); base.OnExit(e); }
}
