using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniCard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddDeckBoxGameAndDeckTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeckTypeId",
                table: "StorageContainers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Game",
                table: "StorageContainers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeckTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Game = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "bit", nullable: false),
                    BuiltInKey = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DeckSizeMin = table.Column<int>(type: "int", nullable: true),
                    DeckSizeMax = table.Column<int>(type: "int", nullable: true),
                    MaxCopiesPerCard = table.Column<int>(type: "int", nullable: true),
                    Singleton = table.Column<bool>(type: "bit", nullable: false),
                    BasicLandsExempt = table.Column<bool>(type: "bit", nullable: false),
                    CommanderSlots = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeckTypes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StorageContainers_DeckTypeId",
                table: "StorageContainers",
                column: "DeckTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_DeckTypes_BuiltInKey",
                table: "DeckTypes",
                column: "BuiltInKey",
                unique: true,
                filter: "[BuiltInKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeckTypes_Game_Name",
                table: "DeckTypes",
                columns: new[] { "Game", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_StorageContainers_DeckTypes_DeckTypeId",
                table: "StorageContainers",
                column: "DeckTypeId",
                principalTable: "DeckTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StorageContainers_DeckTypes_DeckTypeId",
                table: "StorageContainers");

            migrationBuilder.DropTable(
                name: "DeckTypes");

            migrationBuilder.DropIndex(
                name: "IX_StorageContainers_DeckTypeId",
                table: "StorageContainers");

            migrationBuilder.DropColumn(
                name: "DeckTypeId",
                table: "StorageContainers");

            migrationBuilder.DropColumn(
                name: "Game",
                table: "StorageContainers");
        }
    }
}
