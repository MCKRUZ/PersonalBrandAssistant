using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalBrandAssistant.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSocialEngagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EngagementTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<int>(type: "integer", nullable: false),
                    TaskType = table.Column<int>(type: "integer", nullable: false),
                    TargetCriteria = table.Column<string>(type: "jsonb", nullable: false),
                    CronExpression = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextExecutionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MaxActionsPerExecution = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngagementTasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SocialInboxItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<int>(type: "integer", nullable: false),
                    ItemType = table.Column<int>(type: "integer", nullable: false),
                    AuthorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AuthorProfileUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    PlatformItemId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    DraftReply = table.Column<string>(type: "text", nullable: true),
                    ReplySent = table.Column<bool>(type: "boolean", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialInboxItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EngagementExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EngagementTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActionsAttempted = table.Column<int>(type: "integer", nullable: false),
                    ActionsSucceeded = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngagementExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EngagementExecutions_EngagementTasks_EngagementTaskId",
                        column: x => x.EngagementTaskId,
                        principalTable: "EngagementTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EngagementActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EngagementExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionType = table.Column<int>(type: "integer", nullable: false),
                    TargetUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    GeneratedContent = table.Column<string>(type: "text", nullable: true),
                    PlatformPostId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PerformedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngagementActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EngagementActions_EngagementExecutions_EngagementExecutionId",
                        column: x => x.EngagementExecutionId,
                        principalTable: "EngagementExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EngagementActions_EngagementExecutionId",
                table: "EngagementActions",
                column: "EngagementExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_EngagementExecutions_EngagementTaskId",
                table: "EngagementExecutions",
                column: "EngagementTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_EngagementExecutions_ExecutedAt",
                table: "EngagementExecutions",
                column: "ExecutedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EngagementTasks_Platform_IsEnabled_NextExecutionAt",
                table: "EngagementTasks",
                columns: new[] { "Platform", "IsEnabled", "NextExecutionAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SocialInboxItems_IsRead_ReceivedAt",
                table: "SocialInboxItems",
                columns: new[] { "IsRead", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SocialInboxItems_Platform_PlatformItemId",
                table: "SocialInboxItems",
                columns: new[] { "Platform", "PlatformItemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EngagementActions");

            migrationBuilder.DropTable(
                name: "SocialInboxItems");

            migrationBuilder.DropTable(
                name: "EngagementExecutions");

            migrationBuilder.DropTable(
                name: "EngagementTasks");
        }
    }
}
