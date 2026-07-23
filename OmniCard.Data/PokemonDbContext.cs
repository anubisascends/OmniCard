using Microsoft.EntityFrameworkCore;

namespace OmniCard.Data;

public class PokemonDbContext : TcgCsvDbContext
{
    public PokemonDbContext(DbContextOptions<PokemonDbContext> options) : base(options) { }
}
