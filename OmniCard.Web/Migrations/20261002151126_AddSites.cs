using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniCard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddSites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sites: a site is a MAJOR physical location (a home, a shop) holding many child storage
            // locations. Seeds the Default site (Id 1) and assigns every pre-existing location to it —
            // the new SiteId column defaults to 1, and the explicit UPDATE below makes that backfill
            // unambiguous before the FK is enforced.
            migrationBuilder.AddColumn<int>(
                name: "SiteId",
                table: "StorageContainers",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SiteAccessGrants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SiteId = table.Column<int>(type: "int", nullable: false),
                    PrincipalType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PrincipalId = table.Column<int>(type: "int", nullable: false),
                    Level = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteAccessGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteAccessGrants_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Sites",
                columns: new[] { "Id", "Description", "IsDefault", "Name", "SortOrder" },
                values: new object[] { 1, null, true, "Default", 0 });

            migrationBuilder.Sql("UPDATE [StorageContainers] SET [SiteId] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_StorageContainers_SiteId",
                table: "StorageContainers",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteAccessGrants_PrincipalType_PrincipalId",
                table: "SiteAccessGrants",
                columns: new[] { "PrincipalType", "PrincipalId" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteAccessGrants_SiteId_PrincipalType_PrincipalId",
                table: "SiteAccessGrants",
                columns: new[] { "SiteId", "PrincipalType", "PrincipalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sites_Name",
                table: "Sites",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_StorageContainers_Sites_SiteId",
                table: "StorageContainers",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StorageContainers_Sites_SiteId",
                table: "StorageContainers");

            migrationBuilder.DropTable(
                name: "SiteAccessGrants");

            migrationBuilder.DropTable(
                name: "Sites");

            migrationBuilder.DropIndex(
                name: "IX_StorageContainers_SiteId",
                table: "StorageContainers");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "StorageContainers");
        }
    }
}
