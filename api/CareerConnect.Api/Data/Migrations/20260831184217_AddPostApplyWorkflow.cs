using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CareerConnect.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPostApplyWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CalendarEnabled",
                table: "GmailConnections",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "InterviewPrepGeneratedAtUtc",
                table: "Applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InterviewPrepJson",
                table: "Applications",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InterviewEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CalendarEventId = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterviewEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InterviewEvents_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InterviewEvents_ApplicationId_ScheduledAtUtc",
                table: "InterviewEvents",
                columns: new[] { "ApplicationId", "ScheduledAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InterviewEvents");

            migrationBuilder.DropColumn(
                name: "CalendarEnabled",
                table: "GmailConnections");

            migrationBuilder.DropColumn(
                name: "InterviewPrepGeneratedAtUtc",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "InterviewPrepJson",
                table: "Applications");
        }
    }
}
