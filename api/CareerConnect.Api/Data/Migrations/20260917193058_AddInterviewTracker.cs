using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CareerConnect.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInterviewTracker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Debrief",
                table: "InterviewEvents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reflection",
                table: "InterviewEvents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResearchNotes",
                table: "InterviewEvents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SelfRating",
                table: "InterviewEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InterviewQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InterviewEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Side = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Answer = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    Quality = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Asked = table.Column<bool>(type: "boolean", nullable: false),
                    Suggested = table.Column<bool>(type: "boolean", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterviewQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InterviewQuestions_InterviewEvents_InterviewEventId",
                        column: x => x.InterviewEventId,
                        principalTable: "InterviewEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InterviewQuestions_InterviewEventId_Side_Position",
                table: "InterviewQuestions",
                columns: new[] { "InterviewEventId", "Side", "Position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InterviewQuestions");

            migrationBuilder.DropColumn(
                name: "Debrief",
                table: "InterviewEvents");

            migrationBuilder.DropColumn(
                name: "Reflection",
                table: "InterviewEvents");

            migrationBuilder.DropColumn(
                name: "ResearchNotes",
                table: "InterviewEvents");

            migrationBuilder.DropColumn(
                name: "SelfRating",
                table: "InterviewEvents");
        }
    }
}
