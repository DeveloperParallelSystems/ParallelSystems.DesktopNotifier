using ParallelSystems.DesktopNotifier.Models;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace ParallelSystems.DesktopNotifier.Services;

public sealed class DesktopApiClient : IDisposable
{
    private readonly HttpClient _client;
    public DesktopApiClient(AppSettings settings, HttpMessageHandler? handler = null)
    {
        _client = (handler is null ? new HttpClient() : new HttpClient(handler));
        _client.BaseAddress = new Uri(settings.ApiBaseUrl + "/");
        _client.Timeout = TimeSpan.FromSeconds(30);
        _client.DefaultRequestHeaders.Add("X-Parallel-Product", "desktop-notifier");
        _client.DefaultRequestHeaders.Add("X-Parallel-Version", typeof(DesktopApiClient).Assembly.GetName().Version!.ToString());
        _client.DefaultRequestHeaders.Add("X-Parallel-Machine", Environment.MachineName);
    }
    public void Dispose() => _client.Dispose();

    public async Task<bool> ShouldSuppressDailyNotificationsAsync(AppSettings settings)
    {
        if (!settings.AdminDisableInAndOutNotifications) return false;
        try
        {
            // Never trust a local preference or a previously cached administrator role.
            return (await GetPersonalInformationAsync(Environment.MachineName)).IsAdministrator;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            // Keep reminders enabled when current administrator access cannot be verified.
            return false;
        }
    }

    public async Task<object> DashboardRequestAsync(string method, string path, JsonElement body)
    {
        if (!DesktopDashboardPolicy.Allows(method, path))
            throw new BridgeException("accessDenied", "This dashboard operation is not supported.");
        using var request = new HttpRequestMessage(new HttpMethod(method), "api" + path);
        request.Headers.Add("X-Parallel-Dashboard", "1");
        if (body.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            request.Content = JsonContent.Create(body);
        using var response = await _client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        var result = string.IsNullOrWhiteSpace(json) ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(json);
        return new { status = (int)response.StatusCode, body = result };
    }

    public Task<List<CatalogItem>> GetCatalogAsync(string catalog) =>
        SendAsync<List<CatalogItem>>(HttpMethod.Get, $"api/{catalog}");

    public async Task<CatalogItem> SaveCatalogItemAsync(string catalog, Guid? id, string name,
        Guid? projectId = null, Guid? levelId = null)
    {
        using var response = id.HasValue
            ? await _client.PutAsJsonAsync($"api/{catalog}/{id}", new { name, projectId, levelId })
            : await _client.PostAsJsonAsync($"api/{catalog}", new { name, projectId, levelId });
        return await ReadAsync<CatalogItem>(response);
    }

    public Task<List<LevelOptionModel>> GetLevelsAsync() => SendAsync<List<LevelOptionModel>>(HttpMethod.Get, "api/v2/desktop/daily-work/levels");
    public Task<EmployeePersonalInformation> GetPersonalInformationAsync(string machineName) =>
        SendAsync<EmployeePersonalInformation>(HttpMethod.Get, $"api/v2/desktop/daily-work/personal-information?machineName={Uri.EscapeDataString(machineName)}");

    public async Task<EmployeePersonalInformation> SavePersonalInformationAsync(string machineName, EmployeePersonalInformation information)
    {
        using var response = await _client.PutAsJsonAsync(
            $"api/v2/desktop/daily-work/personal-information?machineName={Uri.EscapeDataString(machineName)}", information, AppSettings.JsonOptions);
        return await ReadAsync<EmployeePersonalInformation>(response);
    }
    public Task<DeviceModel> EnsureDeviceAsync(string machine)
    {
        var osVersion = Environment.OSVersion.VersionString;
        var architecture = Environment.Is64BitProcess ? "x64" : "x86";
        return SendAsync<DeviceModel>(HttpMethod.Post,
            $"api/v2/desktop/daily-work/devices/ensure?machineName={Uri.EscapeDataString(machine)}" +
            $"&osVersion={Uri.EscapeDataString(osVersion)}&processArchitecture={architecture}");
    }
    public Task<DailyStatusModel> GetStatusAsync(Guid device, DateOnly date) => SendAsync<DailyStatusModel>(HttpMethod.Get, $"api/v2/desktop/daily-work/status?deviceId={device}&workDate={date:yyyy-MM-dd}");
    public Task<List<DateOnly>> GetSessionDatesAsync(Guid device, DateOnly startDate, DateOnly endDate) =>
        SendAsync<List<DateOnly>>(HttpMethod.Get, $"api/v2/desktop/daily-work/session-dates?deviceId={device}&startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");
    public Task<List<ExistingManualSessionModel>> GetManualSessionsAsync(Guid device, DateOnly date) =>
        SendAsync<List<ExistingManualSessionModel>>(HttpMethod.Get, $"api/v2/desktop/daily-work/manual-sessions?deviceId={device}&workDate={date:yyyy-MM-dd}");
    public Task<List<ProjectModel>> GetProjectsAsync() => SendAsync<List<ProjectModel>>(HttpMethod.Get, "api/v2/desktop/daily-work/projects");
    public Task<List<ClientModel>> GetClientsAsync() => SendAsync<List<ClientModel>>(HttpMethod.Get, "api/v2/desktop/daily-work/clients");
    public Task<List<string>> GetTaskCategoriesAsync() => SendAsync<List<string>>(HttpMethod.Get, "api/v2/desktop/daily-work/task-categories");
    public Task<List<PackageNameOptionModel>> GetPackageNamesAsync() => SendAsync<List<PackageNameOptionModel>>(HttpMethod.Get, "api/v2/desktop/daily-work/package-names");
    public async Task<SubmitResponse> SubmitAsync(DailySubmitRequest request)
    {
        using var response = await _client.PostAsJsonAsync("api/v2/desktop/daily-work/submit", request, AppSettings.JsonOptions);
        return await ReadAsync<SubmitResponse>(response);
    }
    private async Task<T> SendAsync<T>(HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, path);
        using var response = await _client.SendAsync(request);
        return await ReadAsync<T>(response);
    }
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var message = response.ReasonPhrase ?? "The API request failed.";
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("message", out var property) &&
                    !string.IsNullOrWhiteSpace(property.GetString()))
                    message = property.GetString()!;
                else if (document.RootElement.TryGetProperty("errors", out var errors) &&
                         errors.ValueKind == JsonValueKind.Object)
                {
                    var details = new List<string>();
                    foreach (var error in errors.EnumerateObject())
                    {
                        if (error.Value.ValueKind != JsonValueKind.Array) continue;
                        foreach (var detail in error.Value.EnumerateArray())
                        {
                            if (detail.ValueKind == JsonValueKind.String &&
                                !string.IsNullOrWhiteSpace(detail.GetString()))
                                details.Add($"{error.Name}: {detail.GetString()}");
                        }
                    }

                    if (details.Count > 0)
                        message = string.Join(" ", details);
                }
            }
            catch (JsonException)
            {
                if (!string.IsNullOrWhiteSpace(json))
                    message = json.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
            }
            throw new InvalidOperationException(message);
        }
        return JsonSerializer.Deserialize<T>(json, AppSettings.JsonOptions) ?? throw new InvalidOperationException("The server returned an empty response.");
    }
}
