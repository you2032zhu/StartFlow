using System.Globalization;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;

namespace StartFlow.App;

public static class L
{
    private static readonly Dictionary<string, Dictionary<string, string>> Strings = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "strings.json")))!;
    public static string Language { get; private set; } = "zh-CN";
    public static string T(string key) => Strings.TryGetValue(key, out var values) ? values.GetValueOrDefault(Language, values.GetValueOrDefault("en", key)) : key;
    public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);
    public static void Apply(string language)
    {
        Language = language;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
        foreach (var key in Strings.Keys) Application.Current.Resources[key] = T(key);
    }
    public static void Theme(string theme)
    {
        using var personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var dark = theme == "Dark" || theme == "System" && personalize?.GetValue("AppsUseLightTheme") is int value && value == 0;
        var colors = dark ? new[] { "#181C24", "#222833", "#F0F3F8", "#AFBDCE", "#414B5C", "#8EB7FF", "#334767" } : new[] { "#F3F5F9", "#FFFFFF", "#172437", "#566579", "#DAE1EA", "#185BC3", "#DFEBFF" };
        var keys = new[] { "BackgroundBrush", "SurfaceBrush", "TextBrush", "MutedBrush", "BorderBrush", "AccentBrush", "SelectionBrush" };
        for (var i = 0; i < keys.Length; i++) Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
    }
}
public sealed class EnumTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => L.T(value?.ToString() ?? "");
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
public sealed class IconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not StartFlow.Core.StartupItem item) return null;
        var path = item.IconPath.Length > 0 ? item.IconPath : item.Target;
        try
        {
            if (File.Exists(path))
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon != null) { var image = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions()); image.Freeze(); return image; }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or System.ComponentModel.Win32Exception) { }
        return null;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
