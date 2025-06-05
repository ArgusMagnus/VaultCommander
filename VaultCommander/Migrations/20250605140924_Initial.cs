using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VaultCommander.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "folderRecords",
                columns: table => new
                {
                    PersonalScopeUid = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectUid = table.Column<string>(type: "TEXT", nullable: false),
                    ObjectUid = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_folderRecords", x => new { x.PersonalScopeUid, x.SubjectUid, x.ObjectUid });
                });

            migrationBuilder.CreateTable(
                name: "folders",
                columns: table => new
                {
                    PersonalScopeUid = table.Column<string>(type: "TEXT", nullable: false),
                    Uid = table.Column<string>(type: "TEXT", nullable: false),
                    ParentUid = table.Column<string>(type: "TEXT", nullable: true),
                    FolderType = table.Column<string>(type: "TEXT", nullable: true),
                    FolderKey = table.Column<string>(type: "TEXT", nullable: true),
                    SharedFolderUid = table.Column<string>(type: "TEXT", nullable: true),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_folders", x => new { x.PersonalScopeUid, x.Uid });
                });

            migrationBuilder.CreateTable(
                name: "nonSharedData",
                columns: table => new
                {
                    PersonalScopeUid = table.Column<string>(type: "TEXT", nullable: false),
                    Uid = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nonSharedData", x => new { x.PersonalScopeUid, x.Uid });
                });

            migrationBuilder.CreateTable(
                name: "recordKeys",
                columns: table => new
                {
                    PersonalScopeUid = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectUid = table.Column<string>(type: "TEXT", nullable: false),
                    ObjectUid = table.Column<string>(type: "TEXT", nullable: false),
                    RecordKey = table.Column<string>(type: "TEXT", nullable: true),
                    RecordKeyType = table.Column<int>(type: "INTEGER", nullable: false),
                    CanShare = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanEdit = table.Column<bool>(type: "INTEGER", nullable: false),
                    Owner = table.Column<bool>(type: "INTEGER", nullable: false),
                    OwnerAccountUid = table.Column<string>(type: "TEXT", nullable: true),
                    Expiration = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recordKeys", x => new { x.PersonalScopeUid, x.SubjectUid, x.ObjectUid });
                });

            migrationBuilder.CreateTable(
                name: "records",
                columns: table => new
                {
                    PersonalScopeUid = table.Column<string>(type: "TEXT", nullable: false),
                    Uid = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    ClientModifiedTime = table.Column<long>(type: "INTEGER", nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: true),
                    Extra = table.Column<string>(type: "TEXT", nullable: true),
                    Udata = table.Column<string>(type: "TEXT", nullable: true),
                    Shared = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_records", x => new { x.PersonalScopeUid, x.Uid });
                });

            migrationBuilder.CreateTable(
                name: "recordTypes",
                columns: table => new
                {
                    PersonalScopeUid = table.Column<string>(type: "TEXT", nullable: false),
                    Uid = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    RecordTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recordTypes", x => new { x.PersonalScopeUid, x.Uid });
                });

            migrationBuilder.CreateTable(
                name: "sharedFolderKeys",
                columns: table => new
                {
                    PersonalScopeUid = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectUid = table.Column<string>(type: "TEXT", nullable: false),
                    ObjectUid = table.Column<string>(type: "TEXT", nullable: false),
                    KeyType = table.Column<int>(type: "INTEGER", nullable: false),
                    SharedFolderKey = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sharedFolderKeys", x => new { x.PersonalScopeUid, x.SubjectUid, x.ObjectUid });
                });

            migrationBuilder.CreateTable(
                name: "sharedFolderPermissions",
                columns: table => new
                {
                    PersonalScopeUid = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectUid = table.Column<string>(type: "TEXT", nullable: false),
                    ObjectUid = table.Column<string>(type: "TEXT", nullable: false),
                    UserType = table.Column<int>(type: "INTEGER", nullable: false),
                    ManageRecords = table.Column<bool>(type: "INTEGER", nullable: false),
                    ManageUsers = table.Column<bool>(type: "INTEGER", nullable: false),
                    Expiration = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sharedFolderPermissions", x => new { x.PersonalScopeUid, x.SubjectUid, x.ObjectUid });
                });

            migrationBuilder.CreateTable(
                name: "sharedFolders",
                columns: table => new
                {
                    PersonalScopeUid = table.Column<string>(type: "TEXT", nullable: false),
                    Uid = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    Data = table.Column<string>(type: "TEXT", nullable: true),
                    DefaultManageRecords = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultManageUsers = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultCanEdit = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultCanShare = table.Column<bool>(type: "INTEGER", nullable: false),
                    OwnerAccountUid = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sharedFolders", x => new { x.PersonalScopeUid, x.Uid });
                });

            migrationBuilder.CreateTable(
                name: "teams",
                columns: table => new
                {
                    PersonalScopeUid = table.Column<string>(type: "TEXT", nullable: false),
                    Uid = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    TeamKey = table.Column<string>(type: "TEXT", nullable: true),
                    KeyType = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamRsaPrivateKey = table.Column<string>(type: "TEXT", nullable: true),
                    TeamEcPrivateKey = table.Column<string>(type: "TEXT", nullable: true),
                    RestrictEdit = table.Column<bool>(type: "INTEGER", nullable: false),
                    RestrictShare = table.Column<bool>(type: "INTEGER", nullable: false),
                    RestrictView = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teams", x => new { x.PersonalScopeUid, x.Uid });
                });

            migrationBuilder.CreateTable(
                name: "userEmails",
                columns: table => new
                {
                    PersonalScopeUid = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectUid = table.Column<string>(type: "TEXT", nullable: false),
                    ObjectUid = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_userEmails", x => new { x.PersonalScopeUid, x.SubjectUid, x.ObjectUid });
                });

            migrationBuilder.CreateTable(
                name: "userStorage",
                columns: table => new
                {
                    PersonalScopeUid = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_userStorage", x => x.PersonalScopeUid);
                });

            migrationBuilder.CreateTable(
                name: "vaultSettings",
                columns: table => new
                {
                    PersonalScopeUid = table.Column<string>(type: "TEXT", nullable: false),
                    SyncDownToken = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vaultSettings", x => x.PersonalScopeUid);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "folderRecords");

            migrationBuilder.DropTable(
                name: "folders");

            migrationBuilder.DropTable(
                name: "nonSharedData");

            migrationBuilder.DropTable(
                name: "recordKeys");

            migrationBuilder.DropTable(
                name: "records");

            migrationBuilder.DropTable(
                name: "recordTypes");

            migrationBuilder.DropTable(
                name: "sharedFolderKeys");

            migrationBuilder.DropTable(
                name: "sharedFolderPermissions");

            migrationBuilder.DropTable(
                name: "sharedFolders");

            migrationBuilder.DropTable(
                name: "teams");

            migrationBuilder.DropTable(
                name: "userEmails");

            migrationBuilder.DropTable(
                name: "userStorage");

            migrationBuilder.DropTable(
                name: "vaultSettings");
        }
    }
}
