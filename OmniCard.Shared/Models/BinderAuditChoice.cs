namespace OmniCard.Models;

/// <summary>How the user chose to audit a binder location: the pocket-by-pocket marking audit, an
/// import-file + drag-drop reconcile, or not at all.</summary>
public enum BinderAuditChoice
{
    Cancel,
    Mark,
    Import,
}
