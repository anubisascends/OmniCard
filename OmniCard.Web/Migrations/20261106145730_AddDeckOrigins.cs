using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniCard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddDeckOrigins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OriginLocationId",
                table: "Lots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginPage",
                table: "Lots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginSection",
                table: "Lots",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginSlot",
                table: "Lots",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeckReturnReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeckBoxId = table.Column<int>(type: "int", nullable: false),
                    DeckBoxName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WholeDeck = table.Column<bool>(type: "bit", nullable: false),
                    CardCount = table.Column<int>(type: "int", nullable: false),
                    LinesJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeckReturnReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeckReturnReports_StorageContainers_DeckBoxId",
                        column: x => x.DeckBoxId,
                        principalTable: "StorageContainers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeckReturnReports_DeckBoxId_CreatedUtc",
                table: "DeckReturnReports",
                columns: new[] { "DeckBoxId", "CreatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeckReturnReports");

            migrationBuilder.DropColumn(
                name: "OriginLocationId",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "OriginPage",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "OriginSection",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "OriginSlot",
                table: "Lots");
        }
    }
}
