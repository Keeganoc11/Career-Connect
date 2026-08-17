using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CareerConnect.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGmailConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GmailConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConnectedEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    EncryptedRefreshToken = table.Column<string>(type: "TEXT", nullable: false),
                    ConnectedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastCheckedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GmailConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GmailConnections_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GmailConnections_UserId",
                table: "GmailConnections",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GmailConnections");
        }
    }
}
