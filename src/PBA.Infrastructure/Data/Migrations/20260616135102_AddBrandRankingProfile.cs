using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace PBA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandRankingProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrandRankingProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Positioning = table.Column<string>(type: "text", nullable: false),
                    AudiencePrimary = table.Column<string>(type: "text", nullable: false),
                    AudienceSecondary = table.Column<string>(type: "text", nullable: true),
                    AuthorityTopics = table.Column<string>(type: "jsonb", nullable: false),
                    AntiTopics = table.Column<string>(type: "jsonb", nullable: false),
                    VoiceMarkers = table.Column<string>(type: "jsonb", nullable: false),
                    HalfLifeDays = table.Column<double>(type: "double precision", nullable: false),
                    DecayFloor = table.Column<double>(type: "double precision", nullable: false),
                    AntiTopicMultiplier = table.Column<double>(type: "double precision", nullable: false),
                    AuthorityBoost = table.Column<double>(type: "double precision", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandRankingProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BrandPillars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandRankingProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Weight = table.Column<double>(type: "double precision", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    DescriptionEmbedding = table.Column<Vector>(type: "vector(1536)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandPillars", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrandPillars_BrandRankingProfiles_BrandRankingProfileId",
                        column: x => x.BrandRankingProfileId,
                        principalTable: "BrandRankingProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrandPillars_BrandRankingProfileId",
                table: "BrandPillars",
                column: "BrandRankingProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_BrandRankingProfiles_IsActive",
                table: "BrandRankingProfiles",
                column: "IsActive",
                unique: true,
                filter: "\"IsActive\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrandPillars");

            migrationBuilder.DropTable(
                name: "BrandRankingProfiles");
        }
    }
}
