using KeeperSecurity.Storage;
using KeeperSecurity.Vault;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace VaultCommander.Vaults;

sealed partial class KeeperVault
{
    sealed class KeeperStorage : DbContext, IKeeperStorage
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

        private DbSet<SharedFolderKeyEntity> sharedFolderKeys { get;init; } = null!;
        public ILinkStorage<IStorageSharedFolderKey> SharedFolderKeys { get; }

        private DbSet<SharedFolderPermissionEntity> sharedFolderPermissions { get; init; } = null!;
        public ILinkStorage<ISharedFolderPermission> SharedFolderPermissions { get; }

        private DbSet<FolderEntity> folders { get; init; } = null!;
        public IEntityStorage<IStorageFolder> Folders { get; }

        private DbSet<FolderRecordLinkEntity> folderRecords { get; init; } = null!;
        public ILinkStorage<IStorageFolderRecord> FolderRecords { get; }

        private DbSet<RecordTypeEntity> recordTypes { get; init; } = null!;
        public IEntityStorage<IStorageRecordType> RecordTypes { get; }

        public IRecordStorage<IVaultSettings> VaultSettings => throw new NotImplementedException();

        public ILinkStorage<IStorageUserEmail> UserEmails => throw new NotImplementedException();

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
            modelBuilder.Entity<UserStorage>().HasKey(x => x.PersonalScopeUid);

            static EntityTypeBuilder<T> ConfigureEntity<I, T>(ModelBuilder modelBuilder) where T : class, I, IEntity<I>
            {
                var typeBuilder = modelBuilder.Entity<T>();
                var properties = typeof(I).GetProperties().Select(x => x.Name).Append(nameof(IEntity<I>.PersonalScopeUid)).ToHashSet();
                foreach (var columnName in typeof(T).GetProperties().Where(x => !properties.Contains(x.Name)))
                    typeBuilder.Ignore(columnName.Name);
                return typeBuilder;
            }

            static EntityTypeBuilder<T> ConfigureEntityUid<I,T>(ModelBuilder modelBuilder) where T : class, I, IEntity<I>, IUid
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

            public void DeleteUids(IEnumerable<string> uids)
            {
                var remove = _set.Where(x => x.PersonalScopeUid == _dbContext.PersonalScopeUid && uids.Contains(x.Uid)).ToList();
                _set.RemoveRange(remove);
                _dbContext.SaveChanges();
            }

            public IEnumerable<I> GetAll()
            {
                return _set.Where(x => x.PersonalScopeUid == _dbContext.PersonalScopeUid);
            }

            public I GetEntity(string uid)
            {
                return _set.Find(_dbContext.PersonalScopeUid, uid)!;
            }

            public void PutEntities(IEnumerable<I> entities)
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

        sealed class RecordEntity : IStorageRecord, IEntity<IStorageRecord>
        {
            public string PersonalScopeUid { get; set; } = null!;
            public long Revision { get; private set; }
            public int Version { get; private set; }
            public long ClientModifiedTime { get; private set; }
            public string? Data { get; set; }
            public string? Extra { get; private set; }
            public string? Udata { get; private set; }
            public bool Shared { get; set; }
            public bool Owner { get; set; }
            public string Uid { get; private set; } = null!;
            string IStorageRecord.RecordUid => Uid;

            public void CopyFrom(IStorageRecord other)
            {
                Revision = other.Revision;
                Version = other.Version;
                ClientModifiedTime = other.ClientModifiedTime;
                Data = other.Data;
                Extra = other.Extra;
                Udata = other.Udata;
                Shared = other.Shared;
                Uid = other.Uid;
            }
        }

        sealed class SharedFolderEntity : IStorageSharedFolder, IEntity<IStorageSharedFolder>
        {
            public string PersonalScopeUid { get; set; } = null!;
            public long Revision { get; private set; }
            public string? Name { get; private set; }
            public bool DefaultManageRecords { get; private set; }
            public bool DefaultManageUsers { get; private set; }
            public bool DefaultCanEdit { get; private set; }
            public bool DefaultCanShare { get; private set; }
            public string Uid { get; private set; } = null!;

            public string Data { get; private set; } = null!;

            public string? OwnerAccountUid { get; private set; } 

            string IStorageSharedFolder.SharedFolderUid => Uid;

            public void CopyFrom(IStorageSharedFolder other)
            {
                Revision = other.Revision;
                Name = other.Name;
                DefaultManageRecords = other.DefaultManageRecords;
                DefaultManageUsers = other.DefaultManageUsers;
                DefaultCanEdit = other.DefaultCanEdit;
                DefaultCanShare = other.DefaultCanShare;
                Data = other.Data;
                OwnerAccountUid = other.OwnerAccountUid;
                Uid = other.Uid;
            }
        }

        sealed class EnterpriseTeamEntity : IStorageTeam, IEntity<IStorageTeam>
        {
            public string PersonalScopeUid { get; set; } = null!;
            public string? Name { get; private set; }
            public string? TeamKey { get; private set; }
            public int KeyType { get; private set; }
            public string? TeamRsaPrivateKey { get; private set; }
            public string? TeamEcPrivateKey { get; private set; }
            public bool RestrictEdit { get; private set; }
            public bool RestrictShare { get; private set; }
            public bool RestrictView { get; private set; }
            public string Uid { get; private set; } = null!;
            string IStorageTeam.TeamUid => Uid;

            public void CopyFrom(IStorageTeam other)
            {
                Name = other.Name;
                TeamKey = other.TeamKey;
                KeyType = other.KeyType;
                TeamRsaPrivateKey = other.TeamRsaPrivateKey;
                TeamEcPrivateKey = other.TeamEcPrivateKey;
                RestrictEdit = other.RestrictEdit;
                RestrictShare = other.RestrictShare;
                RestrictView = other.RestrictView;
                Uid = other.Uid;
            }
        }

        sealed class NonSharedDataEntity : IStorageNonSharedData, IEntity<IStorageNonSharedData>
        {
            public string PersonalScopeUid { get; set; } = null!;
            public string? Data { get; set; }
            public string Uid { get; private set; } = null!;
            string IStorageNonSharedData.RecordUid => Uid;

            public void CopyFrom(IStorageNonSharedData other)
            {
                Data = other.Data;
                Uid = other.Uid;
            }
        }

        sealed class RecordMetadataEntity : IStorageRecordKey, IEntity<IStorageRecordKey>
        {
            public string PersonalScopeUid { get; set; } = null!;
            public string? RecordKey { get; private set; }
            public int RecordKeyType { get; private set; }
            public bool CanShare { get; set; }
            public bool CanEdit { get; set; }
            public string SubjectUid { get; private set; } = null!;
            public string ObjectUid { get; private set; } = null!;
            public bool Owner { get; private set; }
            public string? OwnerAccountUid { get; private set; }
            public long Expiration { get; private set; }

            string IStorageRecordKey.RecordUid => SubjectUid;
            string IStorageRecordKey.SharedFolderUid => ObjectUid;

            public void CopyFrom(IStorageRecordKey other)
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
            public string PersonalScopeUid { get; set; } = null!;
            public int KeyType { get; private set; }
            public string? SharedFolderKey { get; private set; }
            public string SubjectUid { get; private set; } = null!;
            public string ObjectUid { get; private set; } = null!;
            string IStorageSharedFolderKey.SharedFolderUid => SubjectUid!;
            string IStorageSharedFolderKey.TeamUid => ObjectUid!;

            public void CopyFrom(IStorageSharedFolderKey other)
            {
                KeyType = other.KeyType;
                SharedFolderKey = other.SharedFolderKey;
                SubjectUid = other.SubjectUid;
                ObjectUid = other.ObjectUid;
            }
        }

        sealed class SharedFolderPermissionEntity : ISharedFolderPermission, IEntity<ISharedFolderPermission>
        {
            public string PersonalScopeUid { get; set; } = null!;
            public int UserType { get; private set; }
            public bool ManageRecords { get; private set; }
            public bool ManageUsers { get; private set; }
            public string SubjectUid { get; private set; } = null!;
            public string ObjectUid { get; private set; } = null!;

            public long Expiration { get; private set; }

            string ISharedFolderPermission.SharedFolderUid => SubjectUid;
            string ISharedFolderPermission.UserId => ObjectUid;

            public void CopyFrom(ISharedFolderPermission other)
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
            public string PersonalScopeUid { get; set; } = null!;
            public string? ParentUid { get; private set; }
            public string? FolderType { get; private set; }
            public string? FolderKey { get; private set; }
            public string? SharedFolderUid { get; private set; }
            public long Revision { get; private set; }
            public string? Data { get; private set; }
            public string Uid { get; private set; } = null!;
            string IStorageFolder.FolderUid => Uid!;

            public void CopyFrom(IStorageFolder other)
            {
                ParentUid = other.ParentUid;
                FolderType = other.FolderType;
                FolderKey = other.FolderKey;
                SharedFolderUid = other.SharedFolderUid;
                Revision = other.Revision;
                Data = other.Data;
                Uid = other.Uid;
            }
        }

        sealed class FolderRecordLinkEntity : IStorageFolderRecord, IEntity<IStorageFolderRecord>
        {
            public string PersonalScopeUid { get; set; } = null!;
            public string SubjectUid { get; private set; } = null!;
            public string ObjectUid { get; private set; } = null!;
            string IStorageFolderRecord.FolderUid => SubjectUid;
            string IStorageFolderRecord.RecordUid => ObjectUid;

            public void CopyFrom(IStorageFolderRecord other)
            {
                SubjectUid = other.SubjectUid;
                ObjectUid = other.ObjectUid;
            }
        }

        sealed class RecordTypeEntity : IStorageRecordType, IEntity<IStorageRecordType>
        {
            public string PersonalScopeUid { get; set; } = null!;

            public int RecordTypeId { get; private set; }

            public int Scope { get; private set; }

            public string? Content { get; private set; }

            public string Uid { get; private set; } = null!;

            public string Name { get; private set; } = null!;

            public void CopyFrom(IStorageRecordType other)
            {
                RecordTypeId = other.RecordTypeId;
                Scope = other.Scope;
                Content = other.Content;
                Name = other.Name;
                Uid = other.Uid;
            }
        }
    }
}
