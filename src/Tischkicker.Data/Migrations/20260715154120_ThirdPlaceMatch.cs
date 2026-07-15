using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tischkicker.Data.Migrations
{
    /// <inheritdoc />
    public partial class ThirdPlaceMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ThirdPlaceMatch",
                table: "Tournaments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsThirdPlace",
                table: "Matches",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LoserNextMatchId",
                table: "Matches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoserNextSlot",
                table: "Matches",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThirdPlaceMatch",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "IsThirdPlace",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "LoserNextMatchId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "LoserNextSlot",
                table: "Matches");
        }
    }
}
