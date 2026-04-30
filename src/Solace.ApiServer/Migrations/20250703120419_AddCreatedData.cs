using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ViennaDotNet.ApiServer.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatedData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CreatedDate",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedDate",
                table: "Accounts");
        }
    }
}
