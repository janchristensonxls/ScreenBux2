using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenBux.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveChildProfileUnusedColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DailyBudgetMinutes",
                table: "ChildProfiles");

            migrationBuilder.DropColumn(
                name: "DayStartHour",
                table: "ChildProfiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DailyBudgetMinutes",
                table: "ChildProfiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DayStartHour",
                table: "ChildProfiles",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
