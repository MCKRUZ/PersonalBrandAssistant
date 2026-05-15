using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PBA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HangfireJobId",
                table: "Contents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Contents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.InsertData(
                table: "BrandProfiles",
                columns: new[] { "Id", "AvoidWords", "ExamplePosts", "LearningLog", "Personality", "Tone", "Topics", "UpdatedAt", "Vocabulary" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000001"), "[]", null, null, "", "", "[]", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "[]" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedItems_Type_CreatedAt",
                table: "FeedItems",
                columns: new[] { "Type", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedItems_Type_IsActedOn",
                table: "FeedItems",
                columns: new[] { "Type", "IsActedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPlatformPublishes_Platform_Status",
                table: "ContentPlatformPublishes",
                columns: new[] { "Platform", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FeedItems_Type_CreatedAt",
                table: "FeedItems");

            migrationBuilder.DropIndex(
                name: "IX_FeedItems_Type_IsActedOn",
                table: "FeedItems");

            migrationBuilder.DropIndex(
                name: "IX_ContentPlatformPublishes_Platform_Status",
                table: "ContentPlatformPublishes");

            migrationBuilder.DeleteData(
                table: "BrandProfiles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.DropColumn(
                name: "HangfireJobId",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Contents");
        }
    }
}
