namespace ParallelSystems.DesktopNotifier.Services;

public static class DesktopDashboardPolicy
{
    // Keep in sync with the API's DesktopDashboardAccess allowlist. No arbitrary URLs.
    public static bool Allows(string method, string path)
    {
        if (path.Contains('#') || path.Contains('\\') || path.Contains('\r') || path.Contains('\n')) return false;
        var route = path.Split('?')[0];
        return method switch
        {
            "GET" => route is "/auth/me" or "/dashboard" or "/dashboard/missing-timesheets"
                or "/notifications/dashboard" or "/preferences/dashboard/billing"
                or "/billing/catalog" or "/timesheets/summary" or "/timesheets/billing-sessions",
            "PUT" => route == "/preferences/dashboard/billing",
            "POST" => route == "/notifications/dashboard/seen" || IsReminder(route),
            _ => false
        };
    }
    private static bool IsReminder(string route)
    {
        var parts = route.Split('/');
        return parts.Length == 5 && parts[1] == "dashboard" && parts[2] == "missing-timesheets"
            && Guid.TryParseExact(parts[3], "D", out _) && parts[4] == "email";
    }
}
