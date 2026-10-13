using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniCard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddDeckBoxSourceLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SourceSyncedAt",
                table: "StorageContainers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "StorageContainers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeckBoxMissingCards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContainerId = table.Column<int>(type: "int", nullable: false),
                    CardName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SetCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CollectorNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    MarkedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeckBoxMissingCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeckBoxMissingCards_StorageContainers_ContainerId",
                        column: x => x.ContainerId,
                        principalTable: "StorageContainers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeckBoxMissingCards_ContainerId_CardName",
                table: "DeckBoxMissingCards",
                columns: new[] { "ContainerId", "CardName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeckBoxMissingCards");

            migrationBuilder.DropColumn(
                name: "SourceSyncedAt",
                table: "StorageContainers");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "StorageContainers");
        }
    }
}
