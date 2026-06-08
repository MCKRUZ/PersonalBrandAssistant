using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PBA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIdeaAlertedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AlertedAt",
                table: "Ideas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ideas_AlertedAt",
                table: "Ideas",
                column: "AlertedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Ideas_AlertedAt",
                table: "Ideas");

            migrationBuilder.DropColumn(
                name: "AlertedAt",
                table: "Ideas");
        }
    }
}
