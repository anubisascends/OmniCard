using Microsoft.EntityFrameworkCore;

namespace OmniCard.Data;

public class FinalFantasyDbContext : TcgCsvDbContext
{
    public FinalFantasyDbContext(DbContextOptions<FinalFantasyDbContext> options) : base(options) { }
}
