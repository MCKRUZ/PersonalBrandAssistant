using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalBrandAssistant.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_EngagementSnapshots_FetchedAt_ContentPlatformStatusId",
                table: "EngagementSnapshots",
                columns: new[] { "FetchedAt", "ContentPlatformStatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPlatformStatuses_PublishedAt_Platform",
                table: "ContentPlatformStatuses",
                columns: new[] { "PublishedAt", "Platform" },
                filter: "\"PublishedAt\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EngagementSnapshots_FetchedAt_ContentPlatformStatusId",
                table: "EngagementSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_ContentPlatformStatuses_PublishedAt_Platform",
                table: "ContentPlatformStatuses");
        }
    }
}
