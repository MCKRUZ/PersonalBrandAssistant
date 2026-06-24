using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PBA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDigestKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Digests_Date",
                table: "Digests");

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "Digests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Digests_Date_Kind",
                table: "Digests",
                columns: new[] { "Date", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Digests_Date_Kind",
                table: "Digests");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Digests");

            migrationBuilder.CreateIndex(
                name: "IX_Digests_Date",
                table: "Digests",
                column: "Date",
                unique: true);
        }
    }
}
