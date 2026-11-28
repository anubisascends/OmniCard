using Microsoft.EntityFrameworkCore;

namespace OmniCard.Data.Catalogs;

public class CyberpunkDbContext : TcgCsvDbContext
{
    public CyberpunkDbContext(DbContextOptions<CyberpunkDbContext> options) : base(options) { }
}
