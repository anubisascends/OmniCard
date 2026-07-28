namespace OmniCard.Models;

public record BatchFileResult(string FileName, string TargetName, int Added, int Unresolved);

public record BatchDecklistImportSummary(
    int FileCount,
    int TotalAdded,
    int TotalUnresolved,
    bool AnyListTarget,
    bool AnyLocationTarget,
    IReadOnlyList<BatchFileResult> Files);
