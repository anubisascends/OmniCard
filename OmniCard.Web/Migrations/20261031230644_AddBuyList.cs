using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniCard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddBuyList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OnBuyList",
                table: "IntakeLines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SetAsideLocationName",
                table: "IntakeLines",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BuyListEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Game = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    MatchName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    GameCardId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SetCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SetName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CollectorNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ImageUri = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PinPrinting = table.Column<bool>(type: "bit", nullable: false),
                    Foil = table.Column<bool>(type: "bit", nullable: true),
                    WantedQuantity = table.Column<int>(type: "int", nullable: false),
                    AcquiredQuantity = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuyListEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuyListEntries_Game_WantedQuantity",
                table: "BuyListEntries",
                columns: new[] { "Game", "WantedQuantity" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuyListEntries");

            migrationBuilder.DropColumn(
                name: "OnBuyList",
                table: "IntakeLines");

            migrationBuilder.DropColumn(
                name: "SetAsideLocationName",
                table: "IntakeLines");
        }
    }
}
