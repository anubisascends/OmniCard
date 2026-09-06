using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.DbMigrator;

/// <summary>
/// A copy-target <see cref="OmniCardDbContext"/> whose integer keys are marked
/// <c>ValueGeneratedNever</c> so EF emits the source row's explicit id in the INSERT (paired with
/// <c>SET IDENTITY_INSERT</c> in the copier). Everything else — including the SQL Server rowversion
/// shadow columns — comes from the base model unchanged.
/// </summary>
public sealed class MigrationTargetContext(DbContextOptions<OmniCardDbContext> options)
    : OmniCardDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // CollectionCard is a query/projection model, never a table. Make sure re-scanning below
        // can't pull it into the model via StorageContainer's (ignored) Cards navigation.
        modelBuilder.Ignore<CollectionCard>();

        // Flip integer identity keys to "never generated" by mutating metadata directly — do NOT
        // call modelBuilder.Entity(...) here, which would re-run relationship discovery and re-add
        // ignored navigations.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var key = entity.FindPrimaryKey();
            if (key is null) continue;
            foreach (var p in key.Properties)
            {
                if (p.ClrType == typeof(int) || p.ClrType == typeof(long))
                    ((IMutableProperty)p).ValueGenerated = ValueGenerated.Never;
            }
        }
    }
}
