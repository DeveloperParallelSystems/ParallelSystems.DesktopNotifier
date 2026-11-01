namespace ParallelSystems.DesktopNotifier.Models;

public sealed class CatalogItem
{
    public List<Guid> ClientIds { get; set; } = [];
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Guid? ProjectId { get; set; }
    public Guid? LevelId { get; set; }
}
