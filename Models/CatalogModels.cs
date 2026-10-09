namespace ParallelSystems.DesktopNotifier.Models;

public sealed class PortalAccount
{
    public string Name { get; set; } = "";
    public bool CanManageCatalogs { get; set; }
}

public sealed class CatalogItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}
