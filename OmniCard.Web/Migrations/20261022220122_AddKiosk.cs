using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniCard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddKiosk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KioskDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KioskDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KioskSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerName = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ActiveNameKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClaimedByUserId = table.Column<int>(type: "int", nullable: true),
                    ClaimedByName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    OrderId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KioskSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KioskHolds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    LotId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KioskHolds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KioskHolds_KioskSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "KioskSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KioskHolds_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KioskHolds_LotId",
                table: "KioskHolds",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_KioskHolds_SessionId",
                table: "KioskHolds",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_KioskSessions_ActiveNameKey",
                table: "KioskSessions",
                column: "ActiveNameKey",
                unique: true,
                filter: "[ActiveNameKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_KioskSessions_DeviceId",
                table: "KioskSessions",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_KioskSessions_Status",
                table: "KioskSessions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KioskDevices");

            migrationBuilder.DropTable(
                name: "KioskHolds");

            migrationBuilder.DropTable(
                name: "KioskSessions");
        }
    }
}
