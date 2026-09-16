using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CareerConnect.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPrepInstructions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Instructions",
                table: "PrepRuns",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Instructions",
                table: "PrepRuns");
        }
    }
}
