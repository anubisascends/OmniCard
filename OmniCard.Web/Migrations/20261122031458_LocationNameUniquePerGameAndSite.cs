using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniCard.Web.Migrations
{
    /// <inheritdoc />
    public partial class LocationNameUniquePerGameAndSite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StorageContainers_Name",
                table: "StorageContainers");

            migrationBuilder.AlterColumn<string>(
                name: "Game",
                table: "StorageContainers",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorageContainers_Name_Game_SiteId",
                table: "StorageContainers",
                columns: new[] { "Name", "Game", "SiteId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StorageContainers_Name_Game_SiteId",
                table: "StorageContainers");

            migrationBuilder.AlterColumn<string>(
                name: "Game",
                table: "StorageContainers",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorageContainers_Name",
                table: "StorageContainers",
                column: "Name",
                unique: true);
        }
    }
}
