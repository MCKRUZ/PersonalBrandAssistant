using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalBrandAssistant.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBlogPublishingEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(
                name: "BlogDelayOverride",
                table: "Contents",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlogDeployCommitSha",
                table: "Contents",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlogPostUrl",
                table: "Contents",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BlogSkipped",
                table: "Contents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SubstackPostUrl",
                table: "Contents",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ScheduledAt",
                table: "ContentPlatformStatuses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BlogPublishRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Html = table.Column<string>(type: "text", nullable: false),
                    TargetPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CommitSha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CommitUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    BlogUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    VerificationAttempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlogPublishRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlogPublishRequests_Contents_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Contents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Messages = table.Column<string>(type: "jsonb", nullable: false),
                    ConversationSummary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    LastMessageAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatConversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatConversations_Contents_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Contents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubstackDetections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RssGuid = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SubstackUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DetectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Confidence = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubstackDetections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubstackDetections_Contents_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Contents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UserNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNotifications_Contents_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Contents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Contents_ContentType_Status",
                table: "Contents",
                columns: new[] { "ContentType", "Status" },
                filter: "\"ContentType\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_BlogPublishRequests_ContentId",
                table: "BlogPublishRequests",
                column: "ContentId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_ContentId",
                table: "ChatConversations",
                column: "ContentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubstackDetections_ContentId",
                table: "SubstackDetections",
                column: "ContentId");

            migrationBuilder.CreateIndex(
                name: "IX_SubstackDetections_RssGuid",
                table: "SubstackDetections",
                column: "RssGuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubstackDetections_SubstackUrl",
                table: "SubstackDetections",
                column: "SubstackUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_ContentId_Type",
                table: "UserNotifications",
                columns: new[] { "ContentId", "Type" },
                unique: true,
                filter: "\"Status\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlogPublishRequests");

            migrationBuilder.DropTable(
                name: "ChatConversations");

            migrationBuilder.DropTable(
                name: "SubstackDetections");

            migrationBuilder.DropTable(
                name: "UserNotifications");

            migrationBuilder.DropIndex(
                name: "IX_Contents_ContentType_Status",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "BlogDelayOverride",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "BlogDeployCommitSha",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "BlogPostUrl",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "BlogSkipped",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "SubstackPostUrl",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "ScheduledAt",
                table: "ContentPlatformStatuses");
        }
    }
}
