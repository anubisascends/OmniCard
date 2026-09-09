namespace OmniCard.Shared.Scanning;

public interface IScannerSettingsService
{
    ScanWorkflowMode WorkflowMode { get; }
    void SetWorkflowMode(ScanWorkflowMode mode);
}
