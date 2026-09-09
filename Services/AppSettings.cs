using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Globalization;

namespace ParallelSystems.DesktopNotifier.Services;

public sealed class AppSettings
{
    public string ApiBaseUrl { get; set; } = "http://3.24.250.195";
    public string ApiKey { get; set; } = "TzuOp6FOUBaRuRtHX8/krK3ztrxY/OmSIowsJMdnso/rcXvWtdaQEP5Ee86FQcjx";
    public int CheckIntervalMinutes { get; set; } = 1;
    public double RequiredHoursPerDay { get; set; } = 8;
    public string MorningNotificationTime { get; set; } = "08:00";
    public string AfternoonNotificationTime { get; set; } = "16:00";

    [JsonIgnore]
    public TimeOnly MorningNotificationAt { get; private set; } = new(8, 0);

    [JsonIgnore]
    public TimeOnly AfternoonNotificationAt { get; private set; } = new(16, 0);

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
        value.MorningNotificationAt = ParseNotificationTime(
            value.MorningNotificationTime, nameof(MorningNotificationTime));
        value.AfternoonNotificationAt = ParseNotificationTime(
            value.AfternoonNotificationTime, nameof(AfternoonNotificationTime));
        return value;
    }

    private static TimeOnly ParseNotificationTime(string? value, string settingName)
    {
        if (TimeOnly.TryParseExact(value?.Trim(), ["HH:mm", "H:mm"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return time;

        throw new InvalidDataException(
            $"{settingName} must be a valid local time in HH:mm format, for example 08:00.");
    }

    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
