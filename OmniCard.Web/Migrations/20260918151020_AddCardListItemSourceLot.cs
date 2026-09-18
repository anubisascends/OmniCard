using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniCard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCardListItemSourceLot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceLotId",
                table: "CardListItems",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceLotId",
                table: "CardListItems");
        }
    }
}
