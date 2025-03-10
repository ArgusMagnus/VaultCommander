using Google.Protobuf.WellKnownTypes;
using KeeperSecurity.Storage;
using KeeperSecurity.Vault;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Records;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace VaultCommander.Vaults;

sealed partial class KeeperVault
{
    internal sealed class KeeperStorage : DbContext, IKeeperStorage
    {
        private DbSet<UserStorage> userStorage { get; init; } = null!;
        private Lazy<UserStorage> _userStorage;

        public string PersonalScopeUid
        {
            get => _userStorage.Value.PersonalScopeUid;
            [MemberNotNull(nameof(_userStorage))]
            set => _userStorage = new(() => userStorage.Find(value) ?? userStorage.Add(new() { PersonalScopeUid = value }).Entity);
        }

        public long Revision
        {
            get => _userStorage.Value.Revision;
            set
            {
                _userStorage.Value.Revision = value;
                SaveChanges();
            }
        }

        //public byte[]? PasswordHash=> _userStorage.PasswordHash is null ? null : ProtectedData.Unprotect(_userStorage.PasswordHash, null, DataProtectionScope.CurrentUser);
        //public byte[]? ClientKey => _userStorage.ClientKey is null ? null : ProtectedData.Unprotect(_userStorage.ClientKey, null, DataProtectionScope.CurrentUser);

        //public void SetPasswordHashAndClientKey(byte[]? passwordHash, byte[]? clientKey)
        //{
        //    _userStorage.PasswordHash = passwordHash is null ? null : ProtectedData.Protect(passwordHash, null, DataProtectionScope.CurrentUser);
        //    _userStorage.ClientKey = clientKey is null ? null : ProtectedData.Protect(clientKey, null, DataProtectionScope.CurrentUser);
        //    SaveChanges();
        //}

        private DbSet<RecordEntity> records { get; init; } = null!;
        public IEntityStorage<IStorageRecord> Records { get; }

        private DbSet<SharedFolderEntity> sharedFolders { get; init; } = null!;
        public IEntityStorage<IStorageSharedFolder> SharedFolders { get; }

        private DbSet<EnterpriseTeamEntity> teams { get; init; } = null!;
        public IEntityStorage<IStorageTeam> Teams { get; }

        private DbSet<NonSharedDataEntity> nonSharedData { get; init; } = null!;
        public IEntityStorage<IStorageNonSharedData> NonSharedData { get; }

        private DbSet<RecordMetadataEntity> recordKeys { get; init; } = null!;
        public ILinkStorage<IStorageRecordKey> RecordKeys { get; }

        private DbSet<SharedFolderKeyEntity> sharedFolderKeys { get; init; } = null!;
        public ILinkStorage<IStorageSharedFolderKey> SharedFolderKeys { get; }

        private DbSet<SharedFolderPermissionEntity> sharedFolderPermissions { get; init; } = null!;
        public ILinkStorage<ISharedFolderPermission> SharedFolderPermissions { get; }

        private DbSet<FolderEntity> folders { get; init; } = null!;
        public IEntityStorage<IStorageFolder> Folders { get; }

        private DbSet<FolderRecordLinkEntity> folderRecords { get; init; } = null!;
        public ILinkStorage<IStorageFolderRecord> FolderRecords { get; }

        private DbSet<RecordTypeEntity> recordTypes { get; init; } = null!;
        public IEntityStorage<IStorageRecordType> RecordTypes { get; }

        private DbSet<VaultSettingsEntity> vaultSettings { get; init; } = null!;
        public IRecordStorage<IVaultSettings> VaultSettings { get; }

        private DbSet<StorageUserEmailEntity> userEmails { get; init; } = null!;
        public ILinkStorage<IStorageUserEmail> UserEmails { get; }

        readonly string _dbFilename;

        public KeeperStorage(string? personalScopeUid, string dbFilename)
        {
            _dbFilename = dbFilename;
            PersonalScopeUid = personalScopeUid ?? string.Empty;
            Records = new EntityStorage<IStorageRecord, RecordEntity>(this, records);
            SharedFolders = new EntityStorage<IStorageSharedFolder, SharedFolderEntity>(this, sharedFolders);
            Teams = new EntityStorage<IStorageTeam, EnterpriseTeamEntity>(this, teams);
            NonSharedData = new EntityStorage<IStorageNonSharedData, NonSharedDataEntity>(this, nonSharedData);
            RecordKeys = new PredicateStorage<IStorageRecordKey, RecordMetadataEntity>(this, recordKeys);
            SharedFolderKeys = new PredicateStorage<IStorageSharedFolderKey, SharedFolderKeyEntity>(this, sharedFolderKeys);
            SharedFolderPermissions = new PredicateStorage<ISharedFolderPermission, SharedFolderPermissionEntity>(this, sharedFolderPermissions);
            Folders = new EntityStorage<IStorageFolder, FolderEntity>(this, folders);
            FolderRecords = new PredicateStorage<IStorageFolderRecord, FolderRecordLinkEntity>(this, folderRecords);
            RecordTypes = new EntityStorage<IStorageRecordType, RecordTypeEntity>(this, recordTypes);
            VaultSettings = new RecordStorage<IVaultSettings, VaultSettingsEntity>(this, vaultSettings);
            UserEmails = new PredicateStorage<IStorageUserEmail, StorageUserEmailEntity>(this, userEmails);
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite($"Data Source={_dbFilename}");
            base.OnConfiguring(optionsBuilder);
        }

        void IKeeperStorage.Clear()
        {
            void Clear<T>(DbSet<T> set) where T : class, IEntity
                => set.RemoveRange(set.Where(x => x.PersonalScopeUid == PersonalScopeUid).ToList());

            Clear(records);
            Clear(sharedFolders);
            Clear(teams);
            Clear(nonSharedData);
            Clear(recordKeys);
            Clear(sharedFolderKeys);
            Clear(sharedFolderPermissions);
            Clear(folders);
            Clear(folderRecords);
            Clear(recordTypes);
            SaveChanges();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            ConfigureEntityUid<IStorageRecord, RecordEntity>(modelBuilder);
            ConfigureEntityUid<IStorageSharedFolder, SharedFolderEntity>(modelBuilder);
            ConfigureEntityUid<IStorageTeam, EnterpriseTeamEntity>(modelBuilder);
            ConfigureEntityUid<IStorageNonSharedData, NonSharedDataEntity>(modelBuilder);
            ConfigureEntityUidLink<IStorageRecordKey, RecordMetadataEntity>(modelBuilder);
            ConfigureEntityUidLink<IStorageSharedFolderKey, SharedFolderKeyEntity>(modelBuilder);
            ConfigureEntityUidLink<ISharedFolderPermission, SharedFolderPermissionEntity>(modelBuilder);
            ConfigureEntityUid<IStorageFolder, FolderEntity>(modelBuilder);
            ConfigureEntityUidLink<IStorageFolderRecord, FolderRecordLinkEntity>(modelBuilder);
            ConfigureEntityUid<IStorageRecordType, RecordTypeEntity>(modelBuilder);
            ConfigureEntity<IVaultSettings, VaultSettingsEntity>(modelBuilder).HasKey(x => x.PersonalScopeUid);
            ConfigureEntityUidLink<IStorageUserEmail, StorageUserEmailEntity>(modelBuilder);
            modelBuilder.Entity<UserStorage>().HasKey(x => x.PersonalScopeUid);

            static EntityTypeBuilder<T> ConfigureEntity<I, T>(ModelBuilder modelBuilder) where T : class, I, IEntity<I>
            {
                var typeBuilder = modelBuilder.Entity<T>();
                var properties = typeof(I).GetProperties().Select(x => x.Name).Append(nameof(IEntity<I>.PersonalScopeUid)).ToHashSet();
                foreach (var columnName in typeof(T).GetProperties().Where(x => !properties.Contains(x.Name)))
                    typeBuilder.Ignore(columnName.Name);
                return typeBuilder;
            }

            static EntityTypeBuilder<T> ConfigureEntityUid<I, T>(ModelBuilder modelBuilder) where T : class, I, IEntity<I>, IUid
            {
                var typeBulder = ConfigureEntity<I, T>(modelBuilder);
                typeBulder.HasKey(x => new { x.PersonalScopeUid, x.Uid });
                return typeBulder;
            }

            static EntityTypeBuilder<T> ConfigureEntityUidLink<I, T>(ModelBuilder modelBuilder) where T : class, I, IEntity<I>, IUidLink
            {
                var typeBulder = ConfigureEntity<I, T>(modelBuilder);
                typeBulder.HasKey(x => new { x.PersonalScopeUid, x.SubjectUid, x.ObjectUid });
                return typeBulder;
            }
        }

        sealed class UserStorage
        {
            public string PersonalScopeUid { get; set; } = null!;
            public long Revision { get; set; }
            //public byte[]? PasswordHash { get; set; }
            //public byte[]? ClientKey { get; set; }
        }

        interface IEntity
        {
            public string PersonalScopeUid { get; set; }
        }

        interface IEntity<T> : IEntity
        {
            public void CopyFrom(T other);
        }

        sealed class EntityStorage<I, T> : IEntityStorage<I>
            where I : IUid
            where T : class, I, IEntity<I>, new()
        {
            readonly KeeperStorage _dbContext;
            readonly DbSet<T> _set;

            public EntityStorage(KeeperStorage dbContext, DbSet<T> set)
            {
                _dbContext = dbContext;
                _set = set;
            }

            void IEntityStorage<I>.DeleteUids(IEnumerable<string> uids)
            {
                var remove = _set.Where(x => x.PersonalScopeUid == _dbContext.PersonalScopeUid && uids.Contains(x.Uid)).ToList();
                _set.RemoveRange(remove);
                _dbContext.SaveChanges();
            }

            IEnumerable<I> IEntityStorage<I>.GetAll()
            {
                return _set.Where(x => x.PersonalScopeUid == _dbContext.PersonalScopeUid);
            }

            I IEntityStorage<I>.GetEntity(string uid)
            {
                return _set.Find(_dbContext.PersonalScopeUid, uid)!;
            }

            void IEntityStorage<I>.PutEntities(IEnumerable<I> entities)
            {
                foreach (var value in entities)
                {
                    if (_set.Find(_dbContext.PersonalScopeUid, value.Uid) is T entity)
                        entity.CopyFrom(value);
                    else
                    {
                        entity = new();
                        entity.CopyFrom(value);
                        entity.PersonalScopeUid = _dbContext.PersonalScopeUid;
                        _set.Add(entity);
                    }
                }
                _dbContext.SaveChanges();
            }
        }

        sealed class PredicateStorage<I, T> : ILinkStorage<I>
            where I : IUidLink
            where T : class, I, IEntity<I>, new()
        {
            readonly KeeperStorage _dbContext;
            readonly DbSet<T> _set;

            public PredicateStorage(KeeperStorage dbContext, DbSet<T> set)
            {
                _dbContext = dbContext;
                _set = set;
            }

            public void DeleteLinks(IEnumerable<IUidLink> links)
            {
                foreach (var link in links)
                {
                    if (_set.Find(_dbContext.PersonalScopeUid, link.SubjectUid, link.ObjectUid) is T entity)
                        _set.Remove(entity);
                }
                _dbContext.SaveChanges();
            }

            public void DeleteLinksForObjects(IEnumerable<string> objectUids)
            {
                var remove = _set.Where(x => x.PersonalScopeUid == _dbContext.PersonalScopeUid && objectUids.Contains(x.ObjectUid)).ToList();
                _set.RemoveRange(remove);
                _dbContext.SaveChanges();
            }

            public void DeleteLinksForSubjects(IEnumerable<string> subjectUids)
            {
                var remove = _set.Where(x => x.PersonalScopeUid == _dbContext.PersonalScopeUid && subjectUids.Contains(x.SubjectUid)).ToList();
                _set.RemoveRange(remove);
                _dbContext.SaveChanges();
            }

            public IEnumerable<I> GetAllLinks()
            {
                return _set.Where(x => x.PersonalScopeUid == _dbContext.PersonalScopeUid);
            }

            public IEnumerable<I> GetLinksForObject(string objectUid)
            {
                return _set.Where(x => x.PersonalScopeUid == _dbContext.PersonalScopeUid && x.ObjectUid == objectUid);
            }

            public IEnumerable<I> GetLinksForSubject(string subjectUid)
            {
                return _set.Where(x => x.PersonalScopeUid == _dbContext.PersonalScopeUid && x.SubjectUid == subjectUid);
            }

#pragma warning disable CS8766 // Nullability of reference types in return type doesn't match implicitly implemented member (possibly because of nullability attributes).
            public I? GetLink(IUidLink link)
#pragma warning restore CS8766 // Nullability of reference types in return type doesn't match implicitly implemented member (possibly because of nullability attributes).
            {
                return _set.Where(x => x.PersonalScopeUid == _dbContext.PersonalScopeUid && x.SubjectUid == link.SubjectUid && x.ObjectUid == link.ObjectUid).FirstOrDefault();
            }

            public void PutLinks(IEnumerable<I> entities)
            {
                foreach (var value in entities)
                {
                    if (_set.Find(_dbContext.PersonalScopeUid, value.SubjectUid, value.ObjectUid) is T entity)
                        entity.CopyFrom(value);
                    else
                    {
                        entity = new();
                        entity.CopyFrom(value);
                        entity.PersonalScopeUid = _dbContext.PersonalScopeUid;
                        _set.Add(entity);
                    }
                }
                _dbContext.SaveChanges();
            }
        }

        sealed class RecordStorage<I, T>(KeeperStorage dbContext, DbSet<T> set) : IRecordStorage<I>
            where T : class, I, IEntity<I>, new()
        {
            readonly KeeperStorage _dbContext = dbContext;
            readonly DbSet<T> _set = set;

            void IRecordStorage<I>.Delete()
            {
                var remove = _set.ToList();
                _set.RemoveRange(remove);
                _dbContext.SaveChanges();
            }

            I IRecordStorage<I>.Load()
            {
                return _set.SingleOrDefault()!;
            }

            void IRecordStorage<I>.Store(I record)
            {
                if (_set.Find(_dbContext.PersonalScopeUid) is T entity)
                    entity.CopyFrom(record);
                else
                {
                    entity = new();
                    entity.CopyFrom(record);
                    entity.PersonalScopeUid = _dbContext.PersonalScopeUid;
                    _set.Add(entity);
                }
            }
        }

        sealed class RecordEntity : IStorageRecord, IEntity<IStorageRecord>
        {
            public string Uid { get; private set; } = default!;
            string IStorageRecord.RecordUid => Uid;

            public long Revision { get; private set; }
            long IStorageRecord.Revision => Revision;

            public int Version { get; private set; }
            int IStorageRecord.Version => Version;

            public long ClientModifiedTime { get; private set; }
            long IStorageRecord.ClientModifiedTime => ClientModifiedTime;

            public string? Data { get; private set; }
            string IStorageRecord.Data => Data!;

            public string? Extra { get; private set; }
            string IStorageRecord.Extra => Extra!;

            public string? Udata { get; private set; }
            string IStorageRecord.Udata => Udata!;

            public bool Shared { get; private set; }
            bool IStorageRecord.Shared { get => Shared; set => Shared = value; }

            public string PersonalScopeUid { get; set; } = default!;

            void IEntity<IStorageRecord>.CopyFrom(IStorageRecord other)
            {
                Uid = other.Uid;
                Revision = other.Revision;
                Version = other.Version;
                ClientModifiedTime = other.ClientModifiedTime;
                Data = other.Data;
                Extra = other.Extra;
                Udata = other.Udata;
                Shared = other.Shared;
            }
        }

        sealed class SharedFolderEntity : IStorageSharedFolder, IEntity<IStorageSharedFolder>
        {
            public string Uid { get; private set; } = default!;
            string IStorageSharedFolder.SharedFolderUid => Uid;

            public long Revision { get; private set; }
            long IStorageSharedFolder.Revision => Revision;

            public string? Name { get; private set; }
            string IStorageSharedFolder.Name => Name!;

            public string? Data { get; private set; }
            string IStorageSharedFolder.Data => Data!;

            public bool DefaultManageRecords { get; private set; }
            bool IStorageSharedFolder.DefaultManageRecords => DefaultManageRecords;

            public bool DefaultManageUsers { get; private set; }
            bool IStorageSharedFolder.DefaultManageUsers => DefaultManageUsers;

            public bool DefaultCanEdit { get; private set; }
            bool IStorageSharedFolder.DefaultCanEdit => DefaultCanEdit;

            public bool DefaultCanShare { get; private set; }
            bool IStorageSharedFolder.DefaultCanShare => DefaultCanShare;

            public string? OwnerAccountUid { get; private set; }
            string IStorageSharedFolder.OwnerAccountUid => OwnerAccountUid!;

            public string PersonalScopeUid { get; set; } = default!;

            void IEntity<IStorageSharedFolder>.CopyFrom(IStorageSharedFolder other)
            {
                Uid = other.Uid;
                Revision = other.Revision;
                Name = other.Name;
                DefaultManageRecords = other.DefaultManageRecords;
                DefaultManageUsers = other.DefaultManageUsers;
                DefaultCanEdit = other.DefaultCanEdit;
                DefaultCanShare = other.DefaultCanShare;
                Data = other.Data;
                OwnerAccountUid = other.OwnerAccountUid;
            }
        }

        sealed class EnterpriseTeamEntity : IStorageTeam, IEntity<IStorageTeam>
        {
            public string Uid { get; private set; } = default!;
            string IStorageTeam.TeamUid => Uid;

            public string? Name { get; private set; }
            string IStorageTeam.Name => Name!;

            public string? TeamKey { get; private set; }
            string IStorageTeam.TeamKey => TeamKey!;

            public int KeyType { get; private set; }
            int IStorageTeam.KeyType => KeyType;

            public string? TeamRsaPrivateKey { get; private set; }
            string IStorageTeam.TeamRsaPrivateKey => TeamRsaPrivateKey!;

            public string? TeamEcPrivateKey { get; private set; }
            string IStorageTeam.TeamEcPrivateKey => TeamEcPrivateKey!;

            public bool RestrictEdit { get; private set; }
            bool IStorageTeam.RestrictEdit => RestrictEdit;

            public bool RestrictShare { get; private set; }
            bool IStorageTeam.RestrictShare => RestrictShare;

            public bool RestrictView { get; private set; }
            bool IStorageTeam.RestrictView => RestrictView;

            public string PersonalScopeUid { get; set; } = default!;

            void IEntity<IStorageTeam>.CopyFrom(IStorageTeam other)
            {
                Uid = other.Uid;
                Name = other.Name;
                TeamKey = other.TeamKey;
                KeyType = other.KeyType;
                TeamRsaPrivateKey = other.TeamRsaPrivateKey;
                TeamEcPrivateKey = other.TeamEcPrivateKey;
                RestrictEdit = other.RestrictEdit;
                RestrictShare = other.RestrictShare;
                RestrictView = other.RestrictView;
            }
        }

        sealed class NonSharedDataEntity : IStorageNonSharedData, IEntity<IStorageNonSharedData>
        {
            public string Uid { get; private set; } = default!;
            string IStorageNonSharedData.RecordUid => Uid;

            public string? Data { get; private set; }
            string IStorageNonSharedData.Data { get => Data!; set => Data = value; }

            public string PersonalScopeUid { get; set; } = default!;

            void IEntity<IStorageNonSharedData>.CopyFrom(IStorageNonSharedData other)
            {
                Uid = other.Uid;
                Data = other.Data;
            }
        }

        sealed class RecordMetadataEntity : IStorageRecordKey, IEntity<IStorageRecordKey>
        {
            public string SubjectUid { get; private set; } = default!;
            string IStorageRecordKey.RecordUid => SubjectUid;

            public string ObjectUid { get; private set; } = default!;
            string IStorageRecordKey.SharedFolderUid => ObjectUid;

            public string? RecordKey { get; private set; }
            string IStorageRecordKey.RecordKey => RecordKey!;

            public int RecordKeyType { get; private set; }
            int IStorageRecordKey.RecordKeyType => RecordKeyType;

            public bool CanShare { get; private set; }
            bool IStorageRecordKey.CanShare => CanShare;

            public bool CanEdit { get; private set; }
            bool IStorageRecordKey.CanEdit => CanEdit;

            public bool Owner { get; private set; }
            bool IStorageRecordKey.Owner => Owner;

            public string? OwnerAccountUid { get; private set; }
            string IStorageRecordKey.OwnerAccountUid => OwnerAccountUid!;

            public long Expiration { get; private set; }
            long IStorageRecordKey.Expiration => Expiration;

            public string PersonalScopeUid { get; set; } = default!;

            void IEntity<IStorageRecordKey>.CopyFrom(IStorageRecordKey other)
            {
                RecordKey = other.RecordKey;
                RecordKeyType = other.RecordKeyType;
                Owner = other.Owner;
                OwnerAccountUid = other.OwnerAccountUid;
                Expiration = other.Expiration;
                CanShare = other.CanShare;
                CanEdit = other.CanEdit;
                SubjectUid = other.SubjectUid;
                ObjectUid = other.ObjectUid;
            }
        }

        sealed class SharedFolderKeyEntity : IStorageSharedFolderKey, IEntity<IStorageSharedFolderKey>
        {
            public string SubjectUid { get; private set; } = default!;
            string IStorageSharedFolderKey.SharedFolderUid => SubjectUid!;

            public string ObjectUid { get; private set; } = default!;
            string IStorageSharedFolderKey.TeamUid => ObjectUid;

            public int KeyType { get; private set; }
            int IStorageSharedFolderKey.KeyType => KeyType;

            public string? SharedFolderKey { get; private set; }
            string IStorageSharedFolderKey.SharedFolderKey => SharedFolderKey!;

            public string PersonalScopeUid { get; set; } = null!;

            void IEntity<IStorageSharedFolderKey>.CopyFrom(IStorageSharedFolderKey other)
            {
                KeyType = other.KeyType;
                SharedFolderKey = other.SharedFolderKey;
                SubjectUid = other.SubjectUid;
                ObjectUid = other.ObjectUid;
            }
        }

        sealed class SharedFolderPermissionEntity : ISharedFolderPermission, IEntity<ISharedFolderPermission>
        {
            public string SubjectUid { get; private set; } = default!;
            string ISharedFolderPermission.SharedFolderUid => SubjectUid;

            public string ObjectUid { get; private set; } = default!;
            string ISharedFolderPermission.UserId => ObjectUid;

            public int UserType { get; private set; }
            int ISharedFolderPermission.UserType => UserType;

            public bool ManageRecords { get; private set; }
            bool ISharedFolderPermission.ManageRecords => ManageRecords;

            public bool ManageUsers { get; private set; }
            bool ISharedFolderPermission.ManageUsers => ManageUsers;

            public long Expiration { get; private set; }
            long ISharedFolderPermission.Expiration => Expiration;

            public string PersonalScopeUid { get; set; } = default!;

            void IEntity<ISharedFolderPermission>.CopyFrom(ISharedFolderPermission other)
            {
                UserType = other.UserType;
                ManageRecords = other.ManageRecords;
                ManageUsers = other.ManageUsers;
                Expiration = other.Expiration;
                SubjectUid = other.SubjectUid;
                ObjectUid = other.ObjectUid;
            }
        }

        sealed class FolderEntity : IStorageFolder, IEntity<IStorageFolder>
        {
            public string Uid { get; private set; } = default!;
            string IStorageFolder.FolderUid => Uid;

            public string? ParentUid { get; private set; }
            string IStorageFolder.ParentUid => ParentUid!;

            public string? FolderType { get; private set; }
            string IStorageFolder.FolderType => FolderType!;

            public string? FolderKey { get; private set; }
            string IStorageFolder.FolderKey => FolderKey!;

            public string? SharedFolderUid { get; private set; }
            string IStorageFolder.SharedFolderUid => SharedFolderUid!;

            public long Revision { get; private set; }
            long IStorageFolder.Revision => Revision;

            public string? Data { get; private set; }
            string IStorageFolder.Data => Data!;

            public string PersonalScopeUid { get; set; } = default!;

            void IEntity<IStorageFolder>.CopyFrom(IStorageFolder other)
            {
                Uid = other.Uid;
                ParentUid = other.ParentUid;
                FolderType = other.FolderType;
                FolderKey = other.FolderKey;
                SharedFolderUid = other.SharedFolderUid;
                Revision = other.Revision;
                Data = other.Data;
            }
        }

        sealed class FolderRecordLinkEntity : IStorageFolderRecord, IEntity<IStorageFolderRecord>
        {
            public string SubjectUid { get; private set; } = default!;
            string IStorageFolderRecord.FolderUid => SubjectUid;

            public string ObjectUid { get; private set; } = default!;
            string IStorageFolderRecord.RecordUid => ObjectUid;

            public string PersonalScopeUid { get; set; } = default!;

            void IEntity<IStorageFolderRecord>.CopyFrom(IStorageFolderRecord other)
            {
                SubjectUid = other.SubjectUid;
                ObjectUid = other.ObjectUid;
            }
        }

        sealed class RecordTypeEntity : IStorageRecordType, IEntity<IStorageRecordType>
        {
            public string Uid { get; private set; } = default!;

            public string? Name { get; private set; }
            string IStorageRecordType.Name => Name!;

            public int RecordTypeId { get; private set; }
            int IStorageRecordType.RecordTypeId => RecordTypeId;

            public int Scope { get; private set; }
            int IStorageRecordType.Scope => Scope;

            public string? Content { get; private set; }
            string IStorageRecordType.Content => Content!;

            public string PersonalScopeUid { get; set; } = default!;

            void IEntity<IStorageRecordType>.CopyFrom(IStorageRecordType other)
            {
                RecordTypeId = other.RecordTypeId;
                Scope = other.Scope;
                Content = other.Content;
                Name = other.Name;
                Uid = other.Uid;
            }
        }

        sealed class VaultSettingsEntity : IVaultSettings, IEntity<IVaultSettings>
        {
            public byte[] SyncDownToken { get; private set; } = default!;
            byte[] IVaultSettings.SyncDownToken => SyncDownToken;

            public string PersonalScopeUid { get; private set; } = default!;
            string IEntity.PersonalScopeUid { get => PersonalScopeUid; set => PersonalScopeUid = value; }

            void IEntity<IVaultSettings>.CopyFrom(IVaultSettings other)
            {
                SyncDownToken = other.SyncDownToken;
            }
        }

        sealed class StorageUserEmailEntity : IStorageUserEmail, IEntity<IStorageUserEmail>
        {
            public string SubjectUid { get; private set; } = default!;
            public string ObjectUid { get; private set; } = default!;
            public string PersonalScopeUid { get; set; } = default!;
            string IStorageUserEmail.Email => SubjectUid;
            string IStorageUserEmail.AccountUid => ObjectUid;

            void IEntity<IStorageUserEmail>.CopyFrom(IStorageUserEmail other)
            {
                SubjectUid = other.SubjectUid;
                ObjectUid = other.ObjectUid;
            }
        }
    }

#if DEBUG
    sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<KeeperStorage>
    {
        public KeeperStorage CreateDbContext(string[] args)
            => new(null, "");
    }
#endif
}
