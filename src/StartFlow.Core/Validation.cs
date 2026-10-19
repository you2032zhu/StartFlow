namespace StartFlow.Core;

public static class Validation
{
    public static List<string> Check(Configuration config)
    {
        var errors = new List<string>();
        if (config.FormatVersion != 1) errors.Add("Unsupported formatVersion. Supported: 1.");
        if (config.Settings == null || config.Plans == null) return ["Missing settings or plans."];
        if (config.Settings.ProjectUrl == null) errors.Add("Null project URL.");
        if (config.Settings.Language is not ("zh-CN" or "en")) errors.Add("Invalid language.");
        if (config.Settings.Theme is not ("System" or "Light" or "Dark") || !Enum.IsDefined(config.Settings.CloseBehavior)) errors.Add("Invalid appearance settings.");
        var ids = new HashSet<Guid>();
        foreach (var plan in config.Plans)
        {
            if (plan == null) { errors.Add("Null plan."); continue; }
            if (plan.Id == Guid.Empty || !ids.Add(plan.Id)) errors.Add("Duplicate or empty plan ID.");
            if (string.IsNullOrWhiteSpace(plan.Name) || plan.Name.Length > 200) errors.Add("Invalid plan name.");
            if (!Enum.IsDefined(plan.Mode) || !ValidSeconds(plan.InitialDelaySeconds)) errors.Add("Invalid plan timing.");
            if (plan.Items == null) { errors.Add("Missing items."); continue; }
            foreach (var item in plan.Items)
            {
                if (item == null) { errors.Add("Null item."); continue; }
                if (item.Id == Guid.Empty || !ids.Add(item.Id)) errors.Add("Duplicate or empty item ID.");
                errors.AddRange(CheckItem(item).Select(e => $"{plan.Name} / {item.Name}: {e}"));
            }
        }
        return errors;
    }
    private static bool ValidSeconds(double n) => double.IsFinite(n) && n is >= 0 and <= 604800;
    public static List<string> CheckItem(StartupItem item)
    {
        var errors = new List<string>();
        if (item.Name == null || item.Target == null || item.Arguments == null || item.WorkingDirectory == null || item.IconPath == null || item.Notes == null || item.Group == null || item.ProcessPath == null || item.ProcessName == null) return ["Null item field."];
        if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 200) errors.Add("NameRequired");
        if (string.IsNullOrWhiteSpace(item.Target) || item.Target.Length > 32768 || item.Target.Contains('\0')) errors.Add("TargetRequired");
        if (!Enum.IsDefined(item.Kind) || !Enum.IsDefined(item.Window) || !Enum.IsDefined(item.Shell)) errors.Add("InvalidType");
        if (!ValidSeconds(item.Seconds) || !ValidSeconds(item.RetryIntervalSeconds) || item.RetryCount is < 0 or > 20) errors.Add("InvalidTiming");
        if (item.Kind == TargetKind.Website && !TryWebsite(item.Target, out _)) errors.Add("InvalidUrl");
        if (item.Kind == TargetKind.StoreApp && (!item.Target.Contains('!') || item.Target.Any(char.IsWhiteSpace))) errors.Add("InvalidAppId");
        if (item.Kind is not (TargetKind.Command or TargetKind.Website or TargetKind.StoreApp) && !Path.IsPathFullyQualified(Environment.ExpandEnvironmentVariables(item.Target))) errors.Add("AbsolutePathRequired");
        if (item.RunAsAdmin && item.Kind is TargetKind.Website or TargetKind.StoreApp or TargetKind.Folder or TargetKind.File) errors.Add("AdminUnsupported");
        return errors;
    }
    public static bool TryWebsite(string input, out string normalized)
    {
        normalized = input.Trim();
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);
    }
    public static string SuggestWebsite(string input) => "https://" + input.Trim();
    public static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'))).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant(); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return path.Trim().ToUpperInvariant(); }
    }
    public static string DuplicateKey(StartupItem item)
    {
        var target = item.Kind switch
        {
            TargetKind.Website => Uri.TryCreate(item.Target.Trim(), UriKind.Absolute, out var u) ? u.GetComponents(UriComponents.SchemeAndServer, UriFormat.SafeUnescaped).ToLowerInvariant() + u.PathAndQuery + u.Fragment : item.Target.Trim(),
            TargetKind.Command => item.Target.Trim(),
            TargetKind.StoreApp => item.Target.Trim().ToUpperInvariant(),
            _ => NormalizePath(item.Target)
        };
        return string.Join('\u001f', item.Kind, target, item.Arguments, item.WorkingDirectory.Length == 0 ? "" : NormalizePath(item.WorkingDirectory), item.Kind == TargetKind.Command ? item.Shell.ToString() : "", item.RunAsAdmin);
    }
    public static bool MissingTarget(StartupItem item) => item.Kind is not (TargetKind.Website or TargetKind.StoreApp or TargetKind.Command) && !File.Exists(Environment.ExpandEnvironmentVariables(item.Target)) && !Directory.Exists(Environment.ExpandEnvironmentVariables(item.Target));
}
