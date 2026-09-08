using System.Text.Json;
using System.IO;

namespace ParallelSystems.DesktopNotifier.Services;

public sealed class AppSettings
{
    public string ApiBaseUrl { get; set; } = "http://3.24.250.195";
    public string ApiKey { get; set; } = "TzuOp6FOUBaRuRtHX8/krK3ztrxY/OmSIowsJMdnso/rcXvWtdaQEP5Ee86FQcjx";
    public int CheckIntervalMinutes { get; set; } = 1;
    public double RequiredHoursPerDay { get; set; } = 8;
    public static AppSettings Load()
    {
        var applicationPath = Path.Combine(AppContext.BaseDirectory, "notifier.settings.json");
        var legacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Parallel Systems",
            "Timesheet",
            "notifier.settings.json");
        var path = File.Exists(legacyPath) ? legacyPath : applicationPath;
        var value = File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) : new AppSettings();
        value ??= new AppSettings();
        value.ApiBaseUrl = value.ApiBaseUrl.TrimEnd('/');
        value.CheckIntervalMinutes = Math.Clamp(value.CheckIntervalMinutes, 1, 1440);
        return value;
    }
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
