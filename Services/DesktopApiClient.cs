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
    }
    public void Dispose() => _client.Dispose();
    public Task<PortalAccount> GetAccountAsync() => SendAsync<PortalAccount>(HttpMethod.Get, "api/auth/me");

    public async Task<PortalAccount> LoginAsync(string username, string password)
    {
        using var response = await _client.PostAsJsonAsync("api/auth/login", new { username, password, rememberMe = false });
        return await ReadAsync<PortalAccount>(response);
    }

    public async Task LogoutAsync()
    {
        using var response = await _client.PostAsync("api/auth/logout", null);
        response.EnsureSuccessStatusCode();
    }

    public Task<List<CatalogItem>> GetCatalogAsync(string catalog) =>
        SendAsync<List<CatalogItem>>(HttpMethod.Get, $"api/{catalog}");

    public async Task<CatalogItem> SaveCatalogItemAsync(string catalog, Guid? id, string name)
    {
        using var response = id.HasValue
            ? await _client.PutAsJsonAsync($"api/{catalog}/{id}", new { name })
            : await _client.PostAsJsonAsync($"api/{catalog}", new { name });
        return await ReadAsync<CatalogItem>(response);
    }

    public Task<List<string>> GetLevelsAsync() => SendAsync<List<string>>(HttpMethod.Get, "api/desktop/daily-work/levels");
    public Task<EmployeePersonalInformation> GetPersonalInformationAsync(string machineName) =>
        SendAsync<EmployeePersonalInformation>(HttpMethod.Get, $"api/desktop/daily-work/personal-information?machineName={Uri.EscapeDataString(machineName)}");

    public async Task<EmployeePersonalInformation> SavePersonalInformationAsync(string machineName, EmployeePersonalInformation information)
    {
        using var response = await _client.PutAsJsonAsync(
            $"api/desktop/daily-work/personal-information?machineName={Uri.EscapeDataString(machineName)}", information, AppSettings.JsonOptions);
        return await ReadAsync<EmployeePersonalInformation>(response);
    }
    public Task<DeviceModel> EnsureDeviceAsync(string machine)
    {
        var osVersion = Environment.OSVersion.VersionString;
        var architecture = Environment.Is64BitProcess ? "x64" : "x86";
        return SendAsync<DeviceModel>(HttpMethod.Post,
            $"api/desktop/daily-work/devices/ensure?machineName={Uri.EscapeDataString(machine)}" +
            $"&osVersion={Uri.EscapeDataString(osVersion)}&processArchitecture={architecture}");
    }
    public Task<DailyStatusModel> GetStatusAsync(Guid device, DateOnly date) => SendAsync<DailyStatusModel>(HttpMethod.Get, $"api/desktop/daily-work/status?deviceId={device}&workDate={date:yyyy-MM-dd}");
    public Task<List<DateOnly>> GetSessionDatesAsync(Guid device, DateOnly startDate, DateOnly endDate) =>
        SendAsync<List<DateOnly>>(HttpMethod.Get, $"api/desktop/daily-work/session-dates?deviceId={device}&startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");
    public Task<List<ExistingManualSessionModel>> GetManualSessionsAsync(Guid device, DateOnly date) =>
        SendAsync<List<ExistingManualSessionModel>>(HttpMethod.Get, $"api/desktop/daily-work/manual-sessions?deviceId={device}&workDate={date:yyyy-MM-dd}");
    public Task<List<ProjectModel>> GetProjectsAsync() => SendAsync<List<ProjectModel>>(HttpMethod.Get, "api/desktop/daily-work/projects");
    public Task<List<ClientModel>> GetClientsAsync() => SendAsync<List<ClientModel>>(HttpMethod.Get, "api/desktop/daily-work/clients");
    public Task<List<string>> GetTaskCategoriesAsync() => SendAsync<List<string>>(HttpMethod.Get, "api/desktop/daily-work/task-categories");
    public Task<List<string>> GetPackageNamesAsync() => SendAsync<List<string>>(HttpMethod.Get, "api/desktop/daily-work/package-names");
    public async Task<SubmitResponse> SubmitAsync(DailySubmitRequest request)
    {
        using var response = await _client.PostAsJsonAsync("api/desktop/daily-work/submit", request, AppSettings.JsonOptions);
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
