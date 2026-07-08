namespace OmniCard.Models;

public class FilterPreset
{
    public string Name { get; set; } = "";
    public CardGame Game { get; set; }
    public string Query { get; set; } = "";

    // Legacy — kept for JSON deserialization backward compat, ignored at runtime
    public List<FilterCriterion>? FilterCriteria { get; set; }
}

public class FilterCriterion
{
    public string Field { get; set; } = "";
    public FilterOperator Operator { get; set; } = FilterOperator.Equals;
    public List<string> Values { get; set; } = [];
}

public enum FilterOperator
{
    Equals,
    NotEquals,
    Contains,
    In
}
