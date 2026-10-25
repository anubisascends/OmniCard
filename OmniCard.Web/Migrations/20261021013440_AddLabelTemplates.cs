using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniCard.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddLabelTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LabelTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PageWidthMm = table.Column<double>(type: "float", nullable: false),
                    PageHeightMm = table.Column<double>(type: "float", nullable: false),
                    MarginTopMm = table.Column<double>(type: "float", nullable: false),
                    MarginRightMm = table.Column<double>(type: "float", nullable: false),
                    MarginBottomMm = table.Column<double>(type: "float", nullable: false),
                    MarginLeftMm = table.Column<double>(type: "float", nullable: false),
                    Columns = table.Column<int>(type: "int", nullable: false),
                    Rows = table.Column<int>(type: "int", nullable: false),
                    ColumnGapMm = table.Column<double>(type: "float", nullable: false),
                    RowGapMm = table.Column<double>(type: "float", nullable: false),
                    Design = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabelTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LabelTemplates_Name",
                table: "LabelTemplates",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LabelTemplates");
        }
    }
}
