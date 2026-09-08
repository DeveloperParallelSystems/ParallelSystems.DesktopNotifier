using ParallelSystems.DesktopNotifier.Models;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace ParallelSystems.DesktopNotifier.Services;

public sealed class DesktopApiClient
{
    private readonly HttpClient _client;
    public DesktopApiClient(AppSettings settings)
    {
        _client = new HttpClient { BaseAddress = new Uri(settings.ApiBaseUrl + "/"), Timeout = TimeSpan.FromSeconds(30) };
        _client.DefaultRequestHeaders.Add("X-Tracker-Key", settings.ApiKey);
    }
    public Task<DeviceModel> EnsureDeviceAsync(string machine) => SendAsync<DeviceModel>(HttpMethod.Post, $"api/desktop/daily-work/devices/ensure?machineName={Uri.EscapeDataString(machine)}");
    public Task<DailyStatusModel> GetStatusAsync(Guid device, DateOnly date) => SendAsync<DailyStatusModel>(HttpMethod.Get, $"api/desktop/daily-work/status?deviceId={device}&workDate={date:yyyy-MM-dd}");
    public Task<List<DateOnly>> GetSessionDatesAsync(Guid device, DateOnly startDate, DateOnly endDate) =>
        SendAsync<List<DateOnly>>(HttpMethod.Get, $"api/desktop/daily-work/session-dates?deviceId={device}&startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");
    public Task<List<ExistingManualSessionModel>> GetManualSessionsAsync(Guid device, DateOnly date) =>
        SendAsync<List<ExistingManualSessionModel>>(HttpMethod.Get, $"api/desktop/daily-work/manual-sessions?deviceId={device}&workDate={date:yyyy-MM-dd}");
    public Task<List<ProjectModel>> GetProjectsAsync() => SendAsync<List<ProjectModel>>(HttpMethod.Get, "api/desktop/daily-work/projects");
    public Task<List<ClientModel>> GetClientsAsync() => SendAsync<List<ClientModel>>(HttpMethod.Get, "api/desktop/daily-work/clients");
    public Task<List<string>> GetTaskCategoriesAsync() => SendAsync<List<string>>(HttpMethod.Get, "api/desktop/daily-work/task-categories");
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
