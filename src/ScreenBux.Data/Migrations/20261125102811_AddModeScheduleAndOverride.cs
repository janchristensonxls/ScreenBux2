using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenBux.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModeScheduleAndOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OverrideEndsAtScheduleChange",
                table: "PolicyDocuments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "OverrideExpiresAtUtc",
                table: "PolicyDocuments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverrideProfileId",
                table: "PolicyDocuments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OverrideStartedAtUtc",
                table: "PolicyDocuments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScheduleJson",
                table: "PolicyDocuments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Devices",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyDocuments_OverrideProfileId",
                table: "PolicyDocuments",
                column: "OverrideProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_PolicyDocuments_PolicyProfiles_OverrideProfileId",
                table: "PolicyDocuments",
                column: "OverrideProfileId",
                principalTable: "PolicyProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PolicyDocuments_PolicyProfiles_OverrideProfileId",
                table: "PolicyDocuments");

            migrationBuilder.DropIndex(
                name: "IX_PolicyDocuments_OverrideProfileId",
                table: "PolicyDocuments");

            migrationBuilder.DropColumn(
                name: "OverrideEndsAtScheduleChange",
                table: "PolicyDocuments");

            migrationBuilder.DropColumn(
                name: "OverrideExpiresAtUtc",
                table: "PolicyDocuments");

            migrationBuilder.DropColumn(
                name: "OverrideProfileId",
                table: "PolicyDocuments");

            migrationBuilder.DropColumn(
                name: "OverrideStartedAtUtc",
                table: "PolicyDocuments");

            migrationBuilder.DropColumn(
                name: "ScheduleJson",
                table: "PolicyDocuments");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Devices");
        }
    }
}
