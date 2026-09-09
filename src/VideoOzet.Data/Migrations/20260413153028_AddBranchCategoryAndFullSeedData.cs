using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoOzet.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchCategoryAndFullSeedData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Branches",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "Branches");
        }
    }
}
