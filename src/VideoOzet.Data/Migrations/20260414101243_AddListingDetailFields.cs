using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VideoOzet.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddListingDetailFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EducationBackground",
                table: "Listings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EducationLevel",
                table: "Listings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExperienceYears",
                table: "Listings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasTrialLesson",
                table: "Listings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsGroupLesson",
                table: "Listings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LessonDurationMinutes",
                table: "Listings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxGroupSize",
                table: "Listings",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EducationBackground",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "EducationLevel",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "ExperienceYears",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "HasTrialLesson",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "IsGroupLesson",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "LessonDurationMinutes",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "MaxGroupSize",
                table: "Listings");
        }
    }
}
