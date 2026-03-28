using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalBrandAssistant.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoRespondFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EngagementTasks_Platform_IsEnabled_NextExecutionAt",
                table: "EngagementTasks");

            migrationBuilder.AddColumn<bool>(
                name: "AutoRespond",
                table: "EngagementTasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_EngagementTasks_Platform_IsEnabled_AutoRespond_NextExecutio~",
                table: "EngagementTasks",
                columns: new[] { "Platform", "IsEnabled", "AutoRespond", "NextExecutionAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EngagementTasks_Platform_IsEnabled_AutoRespond_NextExecutio~",
                table: "EngagementTasks");

            migrationBuilder.DropColumn(
                name: "AutoRespond",
                table: "EngagementTasks");

            migrationBuilder.CreateIndex(
                name: "IX_EngagementTasks_Platform_IsEnabled_NextExecutionAt",
                table: "EngagementTasks",
                columns: new[] { "Platform", "IsEnabled", "NextExecutionAt" });
        }
    }
}
