namespace OmniCard.Models;

public class StorageContainer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ContainerType ContainerType { get; set; }
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }
    public int? CoverCardId { get; set; }

    public ICollection<CollectionCard> Cards { get; set; } = [];
}
