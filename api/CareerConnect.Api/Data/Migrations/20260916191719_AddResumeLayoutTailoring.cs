using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CareerConnect.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddResumeLayoutTailoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtraFacts",
                table: "Resumes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Layout",
                table: "Resumes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Changes",
                table: "PrepRuns",
                type: "text",
                nullable: false,
                // Existing runs predate change tracking; "" isn't valid JSON and
                // would fail to load, an empty list reads as "nothing changed".
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Review",
                table: "PrepRuns",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TailoredResumeLayout",
                table: "Applications",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtraFacts",
                table: "Resumes");

            migrationBuilder.DropColumn(
                name: "Layout",
                table: "Resumes");

            migrationBuilder.DropColumn(
                name: "Changes",
                table: "PrepRuns");

            migrationBuilder.DropColumn(
                name: "Review",
                table: "PrepRuns");

            migrationBuilder.DropColumn(
                name: "TailoredResumeLayout",
                table: "Applications");
        }
    }
}
