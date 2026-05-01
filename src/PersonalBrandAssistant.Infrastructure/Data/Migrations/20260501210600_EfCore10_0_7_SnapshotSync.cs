using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalBrandAssistant.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class EfCore10_0_7_SnapshotSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutoPublishThreshold",
                table: "AutonomyConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoPublishThreshold",
                table: "AutonomyConfigurations");
        }
    }
}
