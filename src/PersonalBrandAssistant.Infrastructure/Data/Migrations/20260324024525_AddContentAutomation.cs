using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalBrandAssistant.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContentAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageFileId",
                table: "Contents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ImageRequired",
                table: "Contents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AutomationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TriggeredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SelectedSuggestionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PrimaryContentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ImageFileId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ImagePrompt = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SelectionReasoning = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ErrorDetails = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    PlatformVersionCount = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRuns_Status",
                table: "AutomationRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRuns_TriggeredAt",
                table: "AutomationRuns",
                column: "TriggeredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationRuns");

            migrationBuilder.DropColumn(
                name: "ImageFileId",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "ImageRequired",
                table: "Contents");
        }
    }
}
