using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalBrandAssistant.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAutonomyControlFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoPublishEnabled",
                table: "AutonomyConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoScheduleEnabled",
                table: "AutonomyConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DefaultTone",
                table: "AutonomyConfigurations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "MaxAutoPostsPerDay",
                table: "AutonomyConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RequireApprovalForSocial",
                table: "AutonomyConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoPublishEnabled",
                table: "AutonomyConfigurations");

            migrationBuilder.DropColumn(
                name: "AutoScheduleEnabled",
                table: "AutonomyConfigurations");

            migrationBuilder.DropColumn(
                name: "DefaultTone",
                table: "AutonomyConfigurations");

            migrationBuilder.DropColumn(
                name: "MaxAutoPostsPerDay",
                table: "AutonomyConfigurations");

            migrationBuilder.DropColumn(
                name: "RequireApprovalForSocial",
                table: "AutonomyConfigurations");
        }
    }
}
