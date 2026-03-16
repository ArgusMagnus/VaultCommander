using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VaultCommander.Migrations
{
    /// <inheritdoc />
    public partial class BreachWatchRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "breachWatchRecords",
                columns: table => new
                {
                    PersonalScopeUid = table.Column<string>(type: "TEXT", nullable: false),
                    Uid = table.Column<string>(type: "TEXT", nullable: false),
                    RecordUid = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_breachWatchRecords", x => new { x.PersonalScopeUid, x.Uid });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "breachWatchRecords");
        }
    }
}
