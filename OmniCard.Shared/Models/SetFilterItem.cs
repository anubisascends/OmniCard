namespace OmniCard.Models;

public class SetFilterItem
{
    public string SetCode { get; init; } = "";
    public string SetName { get; init; } = "";
    public string DisplayName => $"{SetName} ({SetCode})";
}
