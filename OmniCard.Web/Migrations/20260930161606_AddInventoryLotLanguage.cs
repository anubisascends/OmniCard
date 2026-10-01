using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniCard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryLotLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Lots",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                table: "Lots");
        }
    }
}
