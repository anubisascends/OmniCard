namespace OmniCard.Models;

/// <summary>Audit record for a correction made to a Completed order — a required reason plus the
/// field-level diff of everything that changed in that edit.</summary>
public class OrderEdit
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Reason { get; set; } = "";

    /// <summary>System.Text.Json-serialized List&lt;OrderEditChange&gt;.</summary>
    public string ChangesJson { get; set; } = "[]";
}
