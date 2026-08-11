using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IScannerSettingsService
{
    ScanWorkflowMode WorkflowMode { get; }
    void SetWorkflowMode(ScanWorkflowMode mode);
}
