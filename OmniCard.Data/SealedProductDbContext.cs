using Microsoft.EntityFrameworkCore;
using OmniCard.Models;

namespace OmniCard.Data;

public class SealedProductDbContext : DbContext
{
    public DbSet<SealedProductTemplate> Templates => Set<SealedProductTemplate>();
    public DbSet<SealedProductContents> TemplateContents => Set<SealedProductContents>();
    public DbSet<SealedProductInstance> Instances => Set<SealedProductInstance>();

    public SealedProductDbContext(DbContextOptions<SealedProductDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SealedProductTemplate>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).ValueGeneratedOnAdd();
            e.Property(t => t.ProductType).HasConversion<string>();
            e.HasIndex(t => t.Name);
            e.HasIndex(t => t.Upc).IsUnique();
        });

        modelBuilder.Entity<SealedProductContents>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).ValueGeneratedOnAdd();
            e.Property(c => c.ChildProductType).HasConversion<string>();

            e.HasOne(c => c.Template)
                .WithMany(t => t.Contents)
                .HasForeignKey(c => c.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(c => c.ChildTemplate)
                .WithMany()
                .HasForeignKey(c => c.ChildTemplateId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SealedProductInstance>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).ValueGeneratedOnAdd();

            e.HasOne(i => i.Template)
                .WithMany()
                .HasForeignKey(i => i.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
