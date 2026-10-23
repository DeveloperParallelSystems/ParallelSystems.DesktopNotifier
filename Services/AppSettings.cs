using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Globalization;

namespace ParallelSystems.DesktopNotifier.Services;

public sealed class AppSettings
{
    public string ApiBaseUrl { get; set; } = "";
    public string WebAppUrl { get; set; } = "";

    public Uri GetWebAppUri()
    {
        var value = string.IsNullOrWhiteSpace(WebAppUrl)
            ? new Uri(new Uri(ApiBaseUrl.TrimEnd('/') + "/"), "desktop-notifier").AbsoluteUri
            : WebAppUrl.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath.TrimEnd('/') != "/desktop-notifier")
            throw new InvalidDataException("WebAppUrl must be an HTTPS URL ending in /desktop-notifier (HTTP is allowed for localhost development).");
        return uri;
    }
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
        var userPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Parallel Systems", "Desktop Notifier", "notifier.settings.json");
        var path = File.Exists(userPath) ? userPath : File.Exists(legacyPath) ? legacyPath : applicationPath;
        AppSettings? value = null;
        try { value = File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) : null; }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
        value ??= new AppSettings();
        var userSchedule = ReadUserNotificationSchedule();
        if (userSchedule is not null)
        {
            if (!string.IsNullOrWhiteSpace(userSchedule.MorningNotificationTime))
                value.MorningNotificationTime = userSchedule.MorningNotificationTime;

            if (!string.IsNullOrWhiteSpace(userSchedule.AfternoonNotificationTime))
                value.AfternoonNotificationTime = userSchedule.AfternoonNotificationTime;
        }

        value.ApiBaseUrl = CommonServerSettings.Resolve(CommonServerSettings.Load(value.ApiBaseUrl));
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

// Keep this resolver equivalent across the independently deployed desktop products.
internal static class CommonServerSettings
{
    internal const string DefaultHost = "app.parallelsystems.com.au";
    internal static string SettingsPath => System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "Parallel Systems", "Shared", "common.settings.json");

    internal static bool IsValid(string? value)
    {
        System.Uri? uri;
        return !string.IsNullOrWhiteSpace(value) &&
            System.Uri.TryCreate(value.Contains("://") ? value.Trim() : "https://" + value.Trim(), System.UriKind.Absolute, out uri) &&
            (uri.Scheme == "https" || uri.Scheme == "http") &&
            !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo) &&
            string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
    }

    internal static string Load(string? local, string? path = null)
    {
        if (IsValid(local)) return local!.Trim().TrimEnd('/');
        path = path ?? SettingsPath;
        try
        {
            var shared = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(System.IO.File.ReadAllText(path))?.FirstOrDefault(pair => pair.Key.Equals("ApiBaseUrl", System.StringComparison.OrdinalIgnoreCase)).Value;
            if (IsValid(shared)) return shared!.Trim().TrimEnd('/');
        }
        catch (System.Exception ex) when (ex is System.IO.IOException || ex is System.UnauthorizedAccessException || ex is System.Text.Json.JsonException || ex is System.InvalidOperationException) { }
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
            // Replace unusable shared settings with the default.
            using (var stream = new System.IO.StreamWriter(new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.Read)))
                    stream.Write("{\n  \"ApiBaseUrl\": \"" + DefaultHost + "\"\n}");
        }
        catch (System.IO.IOException) { }
        catch (System.UnauthorizedAccessException) { }
        return DefaultHost;
    }

    internal static string Resolve(string value)
    {
        if (!IsValid(value)) throw new System.ArgumentException("Enter a server host or HTTP(S) URL without credentials, query, or fragment.");
        value = value.Trim().TrimEnd('/');
        if (value.Contains("://")) return value;
        return ResolveAsync(value).GetAwaiter().GetResult();
    }

    private static async System.Threading.Tasks.Task<string> ResolveAsync(string host)
    {
        using (var client = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false }))
        {
            client.Timeout = System.TimeSpan.FromSeconds(2);
            foreach (var scheme in new[] { "https://", "http://" })
            {
                try
                {
                    using (var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, scheme + host + "/"))
                    using (var response = await client.SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                        return scheme + host; // Any HTTP status proves that the endpoint is reachable.
                }
                catch (System.Net.Http.HttpRequestException) { }
                catch (System.Threading.Tasks.TaskCanceledException) { }
            }
        }
        // An offline startup must not erase the configured host.
        return "https://" + host;
    }
}
