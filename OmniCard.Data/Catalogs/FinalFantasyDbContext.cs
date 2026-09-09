using Microsoft.EntityFrameworkCore;

namespace OmniCard.Data.Catalogs;

public class FinalFantasyDbContext : TcgCsvDbContext
{
    public FinalFantasyDbContext(DbContextOptions<FinalFantasyDbContext> options) : base(options) { }
}
