namespace OmniCard.Interfaces;

public enum EbaySetupStepStatus { Ok, SkippedExisting, Failed }

public record EbaySetupStep(string Name, EbaySetupStepStatus Status, string? Message);

public class EbaySetupResult
{
    public List<EbaySetupStep> Steps { get; } = [];
    public bool Success { get; set; }
}

public interface IEbaySellerSetupService
{
    Task<EbaySetupResult> RunSetupAsync(IProgress<string>? progress = null);
}
