using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SqlServerLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerName",
                table: "Labs",
                type: "TEXT",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OwnerName",
                table: "Labs");
        }
    }
}
