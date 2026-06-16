using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace PBA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIdeaEmbeddingAndSubScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmbeddedAt",
                table: "Ideas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Vector>(
                name: "Embedding",
                table: "Ideas",
                type: "vector(1536)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAntiTopic",
                table: "Ideas",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAuthorityTopic",
                table: "Ideas",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PillarSubScores",
                table: "Ideas",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<int>(
                name: "ScoreAttempts",
                table: "Ideas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ScoredProfileVersion",
                table: "Ideas",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ideas_ScoredProfileVersion",
                table: "Ideas",
                column: "ScoredProfileVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Ideas_ScoredProfileVersion",
                table: "Ideas");

            migrationBuilder.DropColumn(
                name: "EmbeddedAt",
                table: "Ideas");

            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "Ideas");

            migrationBuilder.DropColumn(
                name: "IsAntiTopic",
                table: "Ideas");

            migrationBuilder.DropColumn(
                name: "IsAuthorityTopic",
                table: "Ideas");

            migrationBuilder.DropColumn(
                name: "PillarSubScores",
                table: "Ideas");

            migrationBuilder.DropColumn(
                name: "ScoreAttempts",
                table: "Ideas");

            migrationBuilder.DropColumn(
                name: "ScoredProfileVersion",
                table: "Ideas");
        }
    }
}
