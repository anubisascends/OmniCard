namespace OmniCard.Models;

public class StorageContainer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ContainerType ContainerType { get; set; }
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }
    public int? CoverCardId { get; set; }
    public bool ExcludeFromDeckCheck { get; set; }
    public int SlotsPerPage { get; set; } = 9;
    public int TotalPages { get; set; } = 1;
    public int Columns { get; set; } = 3;

    public ICollection<CollectionCard> Cards { get; set; } = [];

    public void AddPage() => TotalPages++;
}
