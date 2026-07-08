namespace OmniCard.Models;

public class SortPreset
{
    public string Name { get; set; } = "";
    public CardGame Game { get; set; }
    public List<SortLevel> SortLevels { get; set; } = [];
}

public class SortLevel
{
    public string Field { get; set; } = "";
    public SortDirection Direction { get; set; } = SortDirection.Ascending;
    public List<string>? CustomOrder { get; set; }
}

public enum SortDirection
{
    Ascending,
    Descending
}
