using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniCard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddScanBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScanBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Game = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FolderKey = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastFileUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadyUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsFoil = table.Column<bool>(type: "bit", nullable: false),
                    Condition = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Language = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    SetCodes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DefaultContainerId = table.Column<int>(type: "int", nullable: true),
                    ClaimedByUserId = table.Column<int>(type: "int", nullable: true),
                    ClaimedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClaimedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanBatchItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScanBatchId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    PreviewFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    MatchJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OverrideJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Include = table.Column<bool>(type: "bit", nullable: false),
                    Verified = table.Column<bool>(type: "bit", nullable: false),
                    Condition = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    IsFoil = table.Column<bool>(type: "bit", nullable: false),
                    FoilType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    PurchasePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TagsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanBatchItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScanBatchItems_ScanBatches_ScanBatchId",
                        column: x => x.ScanBatchId,
                        principalTable: "ScanBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScanBatches_Game_FolderKey_Status",
                table: "ScanBatches",
                columns: new[] { "Game", "FolderKey", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ScanBatches_Status_ClaimedByUserId",
                table: "ScanBatches",
                columns: new[] { "Status", "ClaimedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScanBatchItems_ScanBatchId_State_Status",
                table: "ScanBatchItems",
                columns: new[] { "ScanBatchId", "State", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScanBatchItems");

            migrationBuilder.DropTable(
                name: "ScanBatches");
        }
    }
}
