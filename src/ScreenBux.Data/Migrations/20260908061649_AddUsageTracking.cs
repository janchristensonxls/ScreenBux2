using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenBux.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUsageTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.CreateTable(
                name: "AppCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsSystemDefault = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppCategories_AspNetUsers_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppCategoryRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcessNameRegex = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WindowTitleRegex = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppCategoryRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppCategoryRules_AppCategories_AppCategoryId",
                        column: x => x.AppCategoryId,
                        principalTable: "AppCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UsageDailyTotals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ChildProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Seconds = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageDailyTotals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsageDailyTotals_AppCategories_AppCategoryId",
                        column: x => x.AppCategoryId,
                        principalTable: "AppCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UsageDailyTotals_ChildProfiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "ChildProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UsageDailyTotals_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppCategories_AccountId",
                table: "AppCategories",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AppCategoryRules_AppCategoryId",
                table: "AppCategoryRules",
                column: "AppCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageDailyTotals_AppCategoryId",
                table: "UsageDailyTotals",
                column: "AppCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageDailyTotals_ChildProfileId",
                table: "UsageDailyTotals",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageDailyTotals_DeviceId",
                table: "UsageDailyTotals",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageDailyTotals_EffectiveDate_ChildProfileId_DeviceId_AppCategoryId",
                table: "UsageDailyTotals",
                columns: new[] { "EffectiveDate", "ChildProfileId", "DeviceId", "AppCategoryId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppCategoryRules");

            migrationBuilder.DropTable(
                name: "UsageDailyTotals");

            migrationBuilder.DropTable(
                name: "AppCategories");

            migrationBuilder.DropColumn(
                name: "DailyBudgetMinutes",
                table: "ChildProfiles");

            migrationBuilder.DropColumn(
                name: "DayStartHour",
                table: "ChildProfiles");
        }
    }
}
