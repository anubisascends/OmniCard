namespace OmniCard.Shared.Settings;

/// <summary>
/// A reusable permission bundle assigned to users. A role's <see cref="Permissions"/> is a list of
/// permission keys from <see cref="Security.Permissions"/>. Admin accounts bypass roles entirely
/// (they hold every permission).
///
/// <para>System roles (<see cref="IsSystem"/>) — "Administrator", "Viewer", "Staff" — are seeded on
/// first run and cannot be deleted. New non-admin users default to the "Viewer" role (view-only
/// baseline).</para>
/// </summary>
public class Role
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>True for seeded built-in roles; these can't be deleted.</summary>
    public bool IsSystem { get; set; }

    /// <summary>Permission keys granted by this role (persisted as a JSON string column).</summary>
    public List<string> Permissions { get; set; } = [];
}
