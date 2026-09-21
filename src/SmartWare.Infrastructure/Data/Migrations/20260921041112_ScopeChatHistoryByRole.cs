using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartWare.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ScopeChatHistoryByRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatSessions_UserId_UpdatedAt",
                table: "ChatSessions");

            migrationBuilder.AddColumn<string>(
                name: "AccessRole",
                table: "ChatSessions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_UserId_AccessRole_UpdatedAt",
                table: "ChatSessions",
                columns: new[] { "UserId", "AccessRole", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatSessions_UserId_AccessRole_UpdatedAt",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "AccessRole",
                table: "ChatSessions");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_UserId_UpdatedAt",
                table: "ChatSessions",
                columns: new[] { "UserId", "UpdatedAt" });
        }
    }
}
