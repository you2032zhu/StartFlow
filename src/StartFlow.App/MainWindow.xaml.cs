using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StartFlow.Core;

namespace StartFlow.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel vm;
    private Point dragStart;
    private StartupItem? dragged;
    public bool Exiting { get; set; }
    public MainWindow(MainViewModel model)
    {
        vm = model; DataContext = model; InitializeComponent();
        var work = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, Math.Max(420, work.Width - 20));
        MinHeight = Math.Min(MinHeight, Math.Max(300, work.Height - 20));
        Width = Math.Min(Width, work.Width - 20); Height = Math.Min(Height, work.Height - 20);
    }
    public void Present() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
    private void SelectionChanged(object sender, SelectionChangedEventArgs e) { vm.Selection = ItemsGrid.SelectedItems.OfType<StartupItem>().ToList(); vm.RefreshCommands(); }
    private void EditDoubleClick(object sender, MouseButtonEventArgs e) { if (Find<DataGridRow>(e.OriginalSource as DependencyObject) != null && vm.EditCommand.CanExecute(null)) vm.EditCommand.Execute(null); }
    private void GridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { vm.DeleteSelection(); e.Handled = true; }
        if (e.Key == Key.F2 && vm.EditCommand.CanExecute(null)) { vm.EditCommand.Execute(null); e.Handled = true; }
        if (e.Key == Key.Insert && vm.AddCommand.CanExecute(null)) { vm.AddCommand.Execute(null); e.Handled = true; }
    }
    private void DragStart(object sender, MouseButtonEventArgs e)
    {
        dragged = null;
        if (Find<CheckBox>(e.OriginalSource as DependencyObject) != null) return;
        dragStart = e.GetPosition(ItemsGrid); dragged = Find<DataGridRow>(e.OriginalSource as DependencyObject)?.Item as StartupItem;
    }
    private void DragMove(object sender, MouseEventArgs e)
    {
        if (dragged == null || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(ItemsGrid);
        if (Math.Abs(current.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(current.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var item = dragged; dragged = null; System.Windows.DragDrop.DoDragDrop(ItemsGrid, item, DragDropEffects.Move);
    }
    private void ReorderDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(StartupItem)) is StartupItem item && Find<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is StartupItem target) { vm.Reorder(item, target); e.Handled = true; }
    }
    private void FilesDropped(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) { vm.AddPaths(paths); e.Handled = true; } }
    private static T? Find<T>(DependencyObject? value) where T : DependencyObject { while (value != null) { if (value is T found) return found; value = VisualTreeHelper.GetParent(value); } return null; }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (Exiting) return;
        e.Cancel = true;
        var behavior = vm.Config.Settings.CloseBehavior;
        if (behavior == CloseMode.Ask)
        {
            var win = Dialogs.Window("CloseChoice", 480, 260); var panel = new StackPanel { Margin = new Thickness(24) }; win.Content = panel; Dialogs.Label(panel, "CloseHint");
            var remember = new CheckBox { Content = L.T("Remember") }; panel.Children.Add(remember); CloseMode? choice = null;
            foreach (var mode in new[] { CloseMode.Tray, CloseMode.Exit }) { var button = new Button { Content = L.T(mode.ToString()) }; button.Click += (_, _) => { choice = mode; win.DialogResult = true; }; panel.Children.Add(button); }
            win.ShowDialog(); if (!choice.HasValue) return; behavior = choice.Value; if (remember.IsChecked == true) { vm.Config.Settings.CloseBehavior = behavior; vm.Save(); }
        }
        if (behavior == CloseMode.Tray) Hide();
        else Dispatcher.BeginInvoke(async () => await ((App)Application.Current).ExitAsync());
    }
}
