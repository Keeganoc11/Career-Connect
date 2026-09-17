using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CareerConnect.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // "" is what EF generates for a new non-null string column, and
                // it doesn't map back to any PlanTier — every existing row would
                // throw on read. Existing accounts start on Free.
                defaultValue: "Free");

            migrationBuilder.AddColumn<DateTime>(
                name: "PlanChangedAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Plan",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PlanChangedAtUtc",
                table: "Users");
        }
    }
}
