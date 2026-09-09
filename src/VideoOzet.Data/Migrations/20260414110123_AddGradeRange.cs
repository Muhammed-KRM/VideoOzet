using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoOzet.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGradeRange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GradeMax",
                table: "Listings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GradeMin",
                table: "Listings",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GradeMax",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "GradeMin",
                table: "Listings");
        }
    }
}
