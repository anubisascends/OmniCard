using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniCard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryLotNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "Lots",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Note",
                table: "Lots");
        }
    }
}
