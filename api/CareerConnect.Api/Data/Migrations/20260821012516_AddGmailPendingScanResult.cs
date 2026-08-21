using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CareerConnect.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGmailPendingScanResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PendingScanCompletedAtUtc",
                table: "GmailConnections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingScanResultJson",
                table: "GmailConnections",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingScanCompletedAtUtc",
                table: "GmailConnections");

            migrationBuilder.DropColumn(
                name: "PendingScanResultJson",
                table: "GmailConnections");
        }
    }
}
