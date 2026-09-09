using Microsoft.EntityFrameworkCore;

namespace OmniCard.Data.Catalogs;

public class YugiohDbContext : TcgCsvDbContext
{
    public YugiohDbContext(DbContextOptions<YugiohDbContext> options) : base(options) { }
}
