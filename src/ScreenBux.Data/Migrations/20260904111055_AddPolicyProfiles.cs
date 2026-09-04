using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenBux.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPolicyProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActivePolicyProfileId",
                table: "PolicyDocuments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PolicyProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ChildProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "bit", nullable: false),
                    PolicyJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PolicyProfiles_AspNetUsers_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PolicyProfiles_ChildProfiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "ChildProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyDocuments_ActivePolicyProfileId",
                table: "PolicyDocuments",
                column: "ActivePolicyProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyProfiles_AccountId",
                table: "PolicyProfiles",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyProfiles_ChildProfileId",
                table: "PolicyProfiles",
                column: "ChildProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_PolicyDocuments_PolicyProfiles_ActivePolicyProfileId",
                table: "PolicyDocuments",
                column: "ActivePolicyProfileId",
                principalTable: "PolicyProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PolicyDocuments_PolicyProfiles_ActivePolicyProfileId",
                table: "PolicyDocuments");

            migrationBuilder.DropTable(
                name: "PolicyProfiles");

            migrationBuilder.DropIndex(
                name: "IX_PolicyDocuments_ActivePolicyProfileId",
                table: "PolicyDocuments");

            migrationBuilder.DropColumn(
                name: "ActivePolicyProfileId",
                table: "PolicyDocuments");
        }
    }
}
