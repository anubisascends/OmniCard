namespace OmniCard.Shared.Audit;

/// <summary>How the user chose to audit a (non-binder) location: by re-scanning every card, by
/// importing a known-good collection file, or not at all.</summary>
public enum AuditSourceChoice
{
    Cancel,
    Scan,
    File,
}
