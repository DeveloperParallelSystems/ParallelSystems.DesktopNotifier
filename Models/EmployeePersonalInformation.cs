using System.Text.Json.Serialization;

namespace ParallelSystems.DesktopNotifier.Models;

public sealed class EmployeePersonalInformation
{
    public string? DisplayName { get; set; }
    public bool CanManageCatalogs { get; set; }
    public bool IsAdministrator { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TimesheetNotificationsEnabled { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? Suffix { get; set; }
    public string? Gender { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? CivilStatus { get; set; }
    public string? Nationality { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
}
