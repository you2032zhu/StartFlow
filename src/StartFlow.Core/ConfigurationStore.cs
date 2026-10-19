using System.Text.Json;

namespace StartFlow.Core;

public sealed record LoadResult(Configuration Config, string? Warning, bool RequiresRecovery);
public sealed class ConfigurationStore(string directory)
{
    public string DirectoryPath { get; } = directory;
    public string ConfigPath => Path.Combine(DirectoryPath, "config.json");
    public string BackupPath => ConfigPath + ".bak";
    public LoadResult Load()
    {
        Directory.CreateDirectory(DirectoryPath);
        if (!File.Exists(ConfigPath)) return new(new(), null, false);
        try { return new(Read(ConfigPath), null, false); }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            if (File.Exists(BackupPath))
            {
                try { return new(Read(BackupPath), $"Recovered backup. Original preserved: {ConfigPath}\n{ex.Message}", true); }
                catch (Exception backupEx) when (backupEx is IOException or JsonException or InvalidDataException or UnauthorizedAccessException) { }
            }
            return new(new(), $"Cannot read configuration. Original preserved: {ConfigPath}\n{ex.Message}", true);
        }
    }
    public static Configuration Read(string path)
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Configuration exceeds 16 MiB.");
        var config = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(path), ConfigJson.Options) ?? throw new InvalidDataException("Empty configuration.");
        var errors = Validation.Check(config);
        if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors.Take(30)));
        return config;
    }
    public void Save(Configuration config)
    {
        var errors = Validation.Check(config);
        if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
        Directory.CreateDirectory(DirectoryPath);
        var temp = ConfigPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { JsonSerializer.Serialize(stream, config, ConfigJson.Options); stream.Flush(true); }
            if (File.Exists(ConfigPath)) File.Replace(temp, ConfigPath, BackupPath, true);
            else File.Move(temp, ConfigPath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void PreserveDamaged()
    {
        if (File.Exists(ConfigPath)) File.Copy(ConfigPath, ConfigPath + ".recovery-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"), false);
    }
    public string BackupBeforeReplace()
    {
        var path = ConfigPath + ".before-import-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        if (File.Exists(ConfigPath)) File.Copy(ConfigPath, path, false);
        return path;
    }
    public static Configuration Import(Configuration current, Configuration incoming, bool merge)
    {
        var result = merge ? ConfigJson.Clone(current) : ConfigJson.Clone(incoming);
        if (!merge)
        { result.Settings.SkipArmedSession = current.Settings.SkipArmedSession; result.Settings.LastHandledSession = current.Settings.LastHandledSession; return result; }
        var names = result.Plans.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var original in incoming.Plans)
        {
            var plan = ConfigJson.Clone(original); plan.Id = Guid.NewGuid();
            var root = plan.Name; var n = 2;
            while (!names.Add(plan.Name)) plan.Name = $"{root} ({n++})";
            foreach (var item in plan.Items) item.Id = Guid.NewGuid();
            result.Plans.Add(plan);
        }
        return result;
    }
}
