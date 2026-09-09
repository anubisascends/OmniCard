using Microsoft.EntityFrameworkCore;

namespace OmniCard.Data.Catalogs;

public class PokemonDbContext : TcgCsvDbContext
{
    public PokemonDbContext(DbContextOptions<PokemonDbContext> options) : base(options) { }
}
