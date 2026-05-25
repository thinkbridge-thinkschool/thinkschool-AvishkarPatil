
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotesApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorizationColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Users",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "viewer");

            migrationBuilder.AddColumn<int>(
                name: "OwnerId",
                table: "Quotes",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Role",    table: "Users");
            migrationBuilder.DropColumn(name: "OwnerId", table: "Quotes");
        }
    }
}
