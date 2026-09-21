using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniCard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddDeckTypeCopiesByCollectorNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CopiesCountByCollectorNumber",
                table: "DeckTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Existing installs already have the One Piece built-in seeded, so EnsureSeeded won't
            // re-add it. Turn on card-number copy-counting for it here (OPTCG: max 4 per card number).
            migrationBuilder.Sql(
                "UPDATE [DeckTypes] SET [CopiesCountByCollectorNumber] = 1 WHERE [BuiltInKey] = 'optcg.constructed';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CopiesCountByCollectorNumber",
                table: "DeckTypes");
        }
    }
}
