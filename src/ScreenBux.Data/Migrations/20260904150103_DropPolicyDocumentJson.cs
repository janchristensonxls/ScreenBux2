using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenBux.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropPolicyDocumentJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data migration: for any existing PolicyDocument that has real PolicyJson content
            // but no active profile pointer yet (predates the profile feature, or was written
            // via the old raw-save path bypassing profiles), materialize a "Normal" PolicyProfile
            // from that JSON and point the document at it, so no policy content is lost when the
            // PolicyJson column is dropped below.
            migrationBuilder.Sql(@"
                INSERT INTO [PolicyProfiles] ([Id], [AccountId], [ChildProfileId], [Name], [IsBuiltIn], [PolicyJson], [UpdatedAt])
                SELECT NEWID(), [AccountId], NULL, N'Normal', 1, [PolicyJson], [UpdatedAt]
                FROM [PolicyDocuments]
                WHERE [ActivePolicyProfileId] IS NULL AND [PolicyJson] IS NOT NULL AND [PolicyJson] <> N'';

                UPDATE d
                SET d.[ActivePolicyProfileId] = p.[Id]
                FROM [PolicyDocuments] d
                INNER JOIN [PolicyProfiles] p
                    ON p.[AccountId] = d.[AccountId] AND p.[Name] = N'Normal' AND p.[PolicyJson] = d.[PolicyJson]
                WHERE d.[ActivePolicyProfileId] IS NULL;
            ");

            migrationBuilder.DropColumn(
                name: "PolicyJson",
                table: "PolicyDocuments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PolicyJson",
                table: "PolicyDocuments",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
                UPDATE d
                SET d.[PolicyJson] = p.[PolicyJson]
                FROM [PolicyDocuments] d
                INNER JOIN [PolicyProfiles] p ON p.[Id] = d.[ActivePolicyProfileId]
                WHERE d.[ActivePolicyProfileId] IS NOT NULL;
            ");
        }
    }
}
