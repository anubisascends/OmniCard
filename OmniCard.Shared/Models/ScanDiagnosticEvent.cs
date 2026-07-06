namespace OmniCard.Models;

public class ScanDiagnosticEvent
{
    public int Id { get; set; }
    public string SessionId { get; set; } = "";
    public ulong ScanHash { get; set; }
    public string EventType { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Payload { get; set; } = "";
}
