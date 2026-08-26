using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PBA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStagedMediaToContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StagedMediaKey",
                table: "Contents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StagedMediaUrl",
                table: "Contents",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StagedMediaKey",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "StagedMediaUrl",
                table: "Contents");
        }
    }
}
