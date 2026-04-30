using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Solace.LauncherUI.Migrations;

/// <inheritdoc />
public partial class AddApplicationRoleProperties : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Color",
            table: "AspNetRoles",
            type: "TEXT",
            nullable: false,
            defaultValue: "#99AAB5");

        migrationBuilder.AddColumn<int>(
            name: "Position",
            table: "AspNetRoles",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Color",
            table: "AspNetRoles");

        migrationBuilder.DropColumn(
            name: "Position",
            table: "AspNetRoles");
    }
}
