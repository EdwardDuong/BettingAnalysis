using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingAnalysis.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBrokenSeedUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "CreatedAt", "CurrentBankroll", "Email", "InitialBankroll", "IsActive", "LastLoginAt", "PasswordHash", "Role", "Username" },
                values: new object[] { 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 10000m, "default@betting.local", 10000m, true, null, "CHANGE_ME", "Admin", "default" });
        }
    }
}
