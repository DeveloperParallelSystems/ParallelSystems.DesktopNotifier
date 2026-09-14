using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Globalization;

namespace ParallelSystems.DesktopNotifier.Services;

public sealed class AppSettings
{
        public string ApiBaseUrl { get; set; } = "http://app.parallelsystems.com.au";
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
        var userSchedule = ReadUserNotificationSchedule();
        if (userSchedule is not null)
        {
            if (!string.IsNullOrWhiteSpace(userSchedule.MorningNotificationTime))
                value.MorningNotificationTime = userSchedule.MorningNotificationTime;

            if (!string.IsNullOrWhiteSpace(userSchedule.AfternoonNotificationTime))
                value.AfternoonNotificationTime = userSchedule.AfternoonNotificationTime;
        }

        value.ApiBaseUrl = value.ApiBaseUrl.TrimEnd('/');
        value.CheckIntervalMinutes = Math.Clamp(value.CheckIntervalMinutes, 1, 1440);
        value.MorningNotificationAt = ParseNotificationTime(
            value.MorningNotificationTime, nameof(MorningNotificationTime));
        value.AfternoonNotificationAt = ParseNotificationTime(
            value.AfternoonNotificationTime, nameof(AfternoonNotificationTime));
        return value;
    }

    public void SaveNotificationSchedule(TimeOnly morning, TimeOnly evening)
    {
        MorningNotificationAt = morning;
        AfternoonNotificationAt = evening;
        MorningNotificationTime = morning.ToString("HH:mm", CultureInfo.InvariantCulture);
        AfternoonNotificationTime = evening.ToString("HH:mm", CultureInfo.InvariantCulture);

        var path = UserNotificationSchedulePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new NotificationScheduleSettings
        {
            MorningNotificationTime = MorningNotificationTime,
            AfternoonNotificationTime = AfternoonNotificationTime
        }, JsonOptions));
    }

    private static NotificationScheduleSettings? ReadUserNotificationSchedule()
    {
        try
        {
            return File.Exists(UserNotificationSchedulePath)
                ? JsonSerializer.Deserialize<NotificationScheduleSettings>(
                    File.ReadAllText(UserNotificationSchedulePath), JsonOptions)
                : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    private static TimeOnly ParseNotificationTime(string? value, string settingName)
    {
        if (TimeOnly.TryParseExact(value?.Trim(), ["HH:mm", "H:mm"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return time;

        throw new InvalidDataException(
            $"{settingName} must be a valid local time in HH:mm format, for example 08:00.");
    }

    private static string UserNotificationSchedulePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Parallel Systems", "Timesheet Notifier", "notification-settings.json");

    private sealed class NotificationScheduleSettings
    {
        public string? MorningNotificationTime { get; set; }
        public string? AfternoonNotificationTime { get; set; }
    }

    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
