using Microsoft.EntityFrameworkCore;

namespace OmniCard.Data;

public class YugiohDbContext : TcgCsvDbContext
{
    public YugiohDbContext(DbContextOptions<YugiohDbContext> options) : base(options) { }
}
