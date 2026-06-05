using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PBA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIdeaRadarFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClusteredAt",
                table: "Ideas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DuplicateOfId",
                table: "Ideas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Score",
                table: "Ideas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScoreReason",
                table: "Ideas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ScoredAt",
                table: "Ideas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ideas_DuplicateOfId",
                table: "Ideas",
                column: "DuplicateOfId");

            migrationBuilder.CreateIndex(
                name: "IX_Ideas_Score",
                table: "Ideas",
                column: "Score");

            migrationBuilder.CreateIndex(
                name: "IX_Ideas_ScoredAt",
                table: "Ideas",
                column: "ScoredAt");

            migrationBuilder.AddForeignKey(
                name: "FK_Ideas_Ideas_DuplicateOfId",
                table: "Ideas",
                column: "DuplicateOfId",
                principalTable: "Ideas",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Ideas_Ideas_DuplicateOfId",
                table: "Ideas");

            migrationBuilder.DropIndex(
                name: "IX_Ideas_DuplicateOfId",
                table: "Ideas");

            migrationBuilder.DropIndex(
                name: "IX_Ideas_Score",
                table: "Ideas");

            migrationBuilder.DropIndex(
                name: "IX_Ideas_ScoredAt",
                table: "Ideas");

            migrationBuilder.DropColumn(
                name: "ClusteredAt",
                table: "Ideas");

            migrationBuilder.DropColumn(
                name: "DuplicateOfId",
                table: "Ideas");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "Ideas");

            migrationBuilder.DropColumn(
                name: "ScoreReason",
                table: "Ideas");

            migrationBuilder.DropColumn(
                name: "ScoredAt",
                table: "Ideas");
        }
    }
}
