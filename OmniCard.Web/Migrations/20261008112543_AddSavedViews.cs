using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniCard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SavedViews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Page = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ContainerId = table.Column<int>(type: "int", nullable: true),
                    GameKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StateJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedViews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedViews_StorageContainers_ContainerId",
                        column: x => x.ContainerId,
                        principalTable: "StorageContainers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SavedViews_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavedViewDefaults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Page = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ContainerId = table.Column<int>(type: "int", nullable: true),
                    GameKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SavedViewId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedViewDefaults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedViewDefaults_SavedViews_SavedViewId",
                        column: x => x.SavedViewId,
                        principalTable: "SavedViews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SavedViewDefaults_SavedViewId",
                table: "SavedViewDefaults",
                column: "SavedViewId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedViewDefaults_UserId_Page_ContainerId_GameKey",
                table: "SavedViewDefaults",
                columns: new[] { "UserId", "Page", "ContainerId", "GameKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedViews_ContainerId",
                table: "SavedViews",
                column: "ContainerId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedViews_Page_ContainerId_GameKey",
                table: "SavedViews",
                columns: new[] { "Page", "ContainerId", "GameKey" });

            migrationBuilder.CreateIndex(
                name: "IX_SavedViews_UserId",
                table: "SavedViews",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SavedViewDefaults");

            migrationBuilder.DropTable(
                name: "SavedViews");
        }
    }
}
