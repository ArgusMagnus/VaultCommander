﻿// <auto-generated />
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VaultCommander.Vaults;

#nullable disable

namespace VaultCommander.Migrations
{
    [DbContext(typeof(KeeperVault.KeeperStorage))]
    [Migration("20250310143909_KeeperSdk110")]
    partial class KeeperSdk110
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "8.0.13");

            modelBuilder.Entity("VaultCommander.Vaults.KeeperVault+KeeperStorage+EnterpriseTeamEntity", b =>
                {
                    b.Property<string>("PersonalScopeUid")
                        .HasColumnType("TEXT");

                    b.Property<string>("Uid")
                        .HasColumnType("TEXT");

                    b.Property<int>("KeyType")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .HasColumnType("TEXT");

                    b.Property<bool>("RestrictEdit")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("RestrictShare")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("RestrictView")
                        .HasColumnType("INTEGER");

                    b.Property<string>("TeamEcPrivateKey")
                        .HasColumnType("TEXT");

                    b.Property<string>("TeamKey")
                        .HasColumnType("TEXT");

                    b.Property<string>("TeamRsaPrivateKey")
                        .HasColumnType("TEXT");

                    b.HasKey("PersonalScopeUid", "Uid");

                    b.ToTable("teams");
                });

            modelBuilder.Entity("VaultCommander.Vaults.KeeperVault+KeeperStorage+FolderEntity", b =>
                {
                    b.Property<string>("PersonalScopeUid")
                        .HasColumnType("TEXT");

                    b.Property<string>("Uid")
                        .HasColumnType("TEXT");

                    b.Property<string>("Data")
                        .HasColumnType("TEXT");

                    b.Property<string>("FolderKey")
                        .HasColumnType("TEXT");

                    b.Property<string>("FolderType")
                        .HasColumnType("TEXT");

                    b.Property<string>("ParentUid")
                        .HasColumnType("TEXT");

                    b.Property<long>("Revision")
                        .HasColumnType("INTEGER");

                    b.Property<string>("SharedFolderUid")
                        .HasColumnType("TEXT");

                    b.HasKey("PersonalScopeUid", "Uid");

                    b.ToTable("folders");
                });

            modelBuilder.Entity("VaultCommander.Vaults.KeeperVault+KeeperStorage+FolderRecordLinkEntity", b =>
                {
                    b.Property<string>("PersonalScopeUid")
                        .HasColumnType("TEXT");

                    b.Property<string>("SubjectUid")
                        .HasColumnType("TEXT");

                    b.Property<string>("ObjectUid")
                        .HasColumnType("TEXT");

                    b.HasKey("PersonalScopeUid", "SubjectUid", "ObjectUid");

                    b.ToTable("folderRecords");
                });

            modelBuilder.Entity("VaultCommander.Vaults.KeeperVault+KeeperStorage+NonSharedDataEntity", b =>
                {
                    b.Property<string>("PersonalScopeUid")
                        .HasColumnType("TEXT");

                    b.Property<string>("Uid")
                        .HasColumnType("TEXT");

                    b.Property<string>("Data")
                        .HasColumnType("TEXT");

                    b.HasKey("PersonalScopeUid", "Uid");

                    b.ToTable("nonSharedData");
                });

            modelBuilder.Entity("VaultCommander.Vaults.KeeperVault+KeeperStorage+RecordEntity", b =>
                {
                    b.Property<string>("PersonalScopeUid")
                        .HasColumnType("TEXT");

                    b.Property<string>("Uid")
                        .HasColumnType("TEXT");

                    b.Property<long>("ClientModifiedTime")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Data")
                        .HasColumnType("TEXT");

                    b.Property<string>("Extra")
                        .HasColumnType("TEXT");

                    b.Property<long>("Revision")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("Shared")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Udata")
                        .HasColumnType("TEXT");

                    b.Property<int>("Version")
                        .HasColumnType("INTEGER");

                    b.HasKey("PersonalScopeUid", "Uid");

                    b.ToTable("records");
                });

            modelBuilder.Entity("VaultCommander.Vaults.KeeperVault+KeeperStorage+RecordMetadataEntity", b =>
                {
                    b.Property<string>("PersonalScopeUid")
                        .HasColumnType("TEXT");

                    b.Property<string>("SubjectUid")
                        .HasColumnType("TEXT");

                    b.Property<string>("ObjectUid")
                        .HasColumnType("TEXT");

                    b.Property<bool>("CanEdit")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("CanShare")
                        .HasColumnType("INTEGER");

                    b.Property<long>("Expiration")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("Owner")
                        .HasColumnType("INTEGER");

                    b.Property<string>("OwnerAccountUid")
                        .HasColumnType("TEXT");

                    b.Property<string>("RecordKey")
                        .HasColumnType("TEXT");

                    b.Property<int>("RecordKeyType")
                        .HasColumnType("INTEGER");

                    b.HasKey("PersonalScopeUid", "SubjectUid", "ObjectUid");

                    b.ToTable("recordKeys");
                });

            modelBuilder.Entity("VaultCommander.Vaults.KeeperVault+KeeperStorage+RecordTypeEntity", b =>
                {
                    b.Property<string>("PersonalScopeUid")
                        .HasColumnType("TEXT");

                    b.Property<string>("Uid")
                        .HasColumnType("TEXT");

                    b.Property<string>("Content")
                        .HasColumnType("TEXT");

                    b.Property<string>("Name")
                        .HasColumnType("TEXT");

                    b.Property<int>("RecordTypeId")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Scope")
                        .HasColumnType("INTEGER");

                    b.HasKey("PersonalScopeUid", "Uid");

                    b.ToTable("recordTypes");
                });

            modelBuilder.Entity("VaultCommander.Vaults.KeeperVault+KeeperStorage+SharedFolderEntity", b =>
                {
                    b.Property<string>("PersonalScopeUid")
                        .HasColumnType("TEXT");

                    b.Property<string>("Uid")
                        .HasColumnType("TEXT");

                    b.Property<string>("Data")
                        .HasColumnType("TEXT");

                    b.Property<bool>("DefaultCanEdit")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("DefaultCanShare")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("DefaultManageRecords")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("DefaultManageUsers")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .HasColumnType("TEXT");

                    b.Property<string>("OwnerAccountUid")
                        .HasColumnType("TEXT");

                    b.Property<long>("Revision")
                        .HasColumnType("INTEGER");

                    b.HasKey("PersonalScopeUid", "Uid");

                    b.ToTable("sharedFolders");
                });

            modelBuilder.Entity("VaultCommander.Vaults.KeeperVault+KeeperStorage+SharedFolderKeyEntity", b =>
                {
                    b.Property<string>("PersonalScopeUid")
                        .HasColumnType("TEXT");

                    b.Property<string>("SubjectUid")
                        .HasColumnType("TEXT");

                    b.Property<string>("ObjectUid")
                        .HasColumnType("TEXT");

                    b.Property<int>("KeyType")
                        .HasColumnType("INTEGER");

                    b.Property<string>("SharedFolderKey")
                        .HasColumnType("TEXT");

                    b.HasKey("PersonalScopeUid", "SubjectUid", "ObjectUid");

                    b.ToTable("sharedFolderKeys");
                });

            modelBuilder.Entity("VaultCommander.Vaults.KeeperVault+KeeperStorage+SharedFolderPermissionEntity", b =>
                {
                    b.Property<string>("PersonalScopeUid")
                        .HasColumnType("TEXT");

                    b.Property<string>("SubjectUid")
                        .HasColumnType("TEXT");

                    b.Property<string>("ObjectUid")
                        .HasColumnType("TEXT");

                    b.Property<long>("Expiration")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("ManageRecords")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("ManageUsers")
                        .HasColumnType("INTEGER");

                    b.Property<int>("UserType")
                        .HasColumnType("INTEGER");

                    b.HasKey("PersonalScopeUid", "SubjectUid", "ObjectUid");

                    b.ToTable("sharedFolderPermissions");
                });

            modelBuilder.Entity("VaultCommander.Vaults.KeeperVault+KeeperStorage+StorageUserEmailEntity", b =>
                {
                    b.Property<string>("PersonalScopeUid")
                        .HasColumnType("TEXT");

                    b.Property<string>("SubjectUid")
                        .HasColumnType("TEXT");

                    b.Property<string>("ObjectUid")
                        .HasColumnType("TEXT");

                    b.HasKey("PersonalScopeUid", "SubjectUid", "ObjectUid");

                    b.ToTable("userEmails");
                });

            modelBuilder.Entity("VaultCommander.Vaults.KeeperVault+KeeperStorage+UserStorage", b =>
                {
                    b.Property<string>("PersonalScopeUid")
                        .HasColumnType("TEXT");

                    b.Property<long>("Revision")
                        .HasColumnType("INTEGER");

                    b.HasKey("PersonalScopeUid");

                    b.ToTable("userStorage");
                });

            modelBuilder.Entity("VaultCommander.Vaults.KeeperVault+KeeperStorage+VaultSettingsEntity", b =>
                {
                    b.Property<string>("PersonalScopeUid")
                        .HasColumnType("TEXT");

                    b.Property<byte[]>("SyncDownToken")
                        .IsRequired()
                        .HasColumnType("BLOB");

                    b.HasKey("PersonalScopeUid");

                    b.ToTable("vaultSettings");
                });
#pragma warning restore 612, 618
        }
    }
}
