using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ViennaDotNet.LauncherUI.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildplatePreview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BuildplatePreviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<string>(type: "TEXT", nullable: true),
                    BuildplateId = table.Column<string>(type: "TEXT", nullable: false),
                    PreviewData = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildplatePreviews", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Player_Buildplate",
                table: "BuildplatePreviews",
                columns: new[] { "PlayerId", "BuildplateId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildplatePreviews");
        }
    }
}
