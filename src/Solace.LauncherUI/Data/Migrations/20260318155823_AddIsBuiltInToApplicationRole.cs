using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ViennaDotNet.LauncherUI.Migrations
{
    /// <inheritdoc />
    public partial class AddIsBuiltInToApplicationRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBuiltIn",
                table: "AspNetRoles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBuiltIn",
                table: "AspNetRoles");
        }
    }
}
