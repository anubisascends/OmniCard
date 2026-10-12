using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniCard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddScanBatchContentHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "ScanBatchItems",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DuplicateOfItemId",
                table: "ScanBatchItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScanBatchItems_ContentHash",
                table: "ScanBatchItems",
                column: "ContentHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScanBatchItems_ContentHash",
                table: "ScanBatchItems");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "ScanBatchItems");

            migrationBuilder.DropColumn(
                name: "DuplicateOfItemId",
                table: "ScanBatchItems");
        }
    }
}
