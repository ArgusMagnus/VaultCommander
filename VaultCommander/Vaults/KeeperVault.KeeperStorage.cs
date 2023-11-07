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
        public IEntityStorage<ISharedFolder> SharedFolders { get; }

        private DbSet<EnterpriseTeamEntity> teams { get; init; } = null!;
        public IEntityStorage<IEnterpriseTeam> Teams { get; }

        private DbSet<NonSharedDataEntity> nonSharedData { get; init; } = null!;
        public IEntityStorage<INonSharedData> NonSharedData { get; }

        private DbSet<RecordMetadataEntity> recordKeys { get; init; } = null!;
        public IPredicateStorage<IRecordMetadata> RecordKeys { get; }

        private DbSet<SharedFolderKeyEntity> sharedFolderKeys { get;init; } = null!;
        public IPredicateStorage<ISharedFolderKey> SharedFolderKeys { get; }

        private DbSet<SharedFolderPermissionEntity> sharedFolderPermissions { get; init; } = null!;
        public IPredicateStorage<ISharedFolderPermission> SharedFolderPermissions { get; }

        private DbSet<FolderEntity> folders { get; init; } = null!;
        public IEntityStorage<IFolder> Folders { get; }

        private DbSet<FolderRecordLinkEntity> folderRecords { get; init; } = null!;
        public IPredicateStorage<IFolderRecordLink> FolderRecords { get; }

        private DbSet<RecordTypeEntity> recordTypes { get; init; } = null!;
        public IEntityStorage<IRecordType> RecordTypes { get; }

        readonly string _dbFilename;

        public KeeperStorage(string? personalScopeUid, string dbFilename)
        {
            _dbFilename = dbFilename;
            PersonalScopeUid = personalScopeUid ?? string.Empty;
            Records = new EntityStorage<IStorageRecord, RecordEntity>(this, records);
            SharedFolders = new EntityStorage<ISharedFolder, SharedFolderEntity>(this, sharedFolders);
            Teams = new EntityStorage<IEnterpriseTeam, EnterpriseTeamEntity>(this, teams);
            NonSharedData = new EntityStorage<INonSharedData, NonSharedDataEntity>(this, nonSharedData);
            RecordKeys = new PredicateStorage<IRecordMetadata, RecordMetadataEntity>(this, recordKeys);
            SharedFolderKeys = new PredicateStorage<ISharedFolderKey, SharedFolderKeyEntity>(this, sharedFolderKeys);
            SharedFolderPermissions = new PredicateStorage<ISharedFolderPermission, SharedFolderPermissionEntity>(this, sharedFolderPermissions);
            Folders = new EntityStorage<IFolder, FolderEntity>(this, folders);
            FolderRecords = new PredicateStorage<IFolderRecordLink, FolderRecordLinkEntity>(this, folderRecords);
            RecordTypes = new EntityStorage<IRecordType, RecordTypeEntity>(this, recordTypes);
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
            ConfigureEntityUid<ISharedFolder, SharedFolderEntity>(modelBuilder);
            ConfigureEntityUid<IEnterpriseTeam, EnterpriseTeamEntity>(modelBuilder);
            ConfigureEntityUid<INonSharedData, NonSharedDataEntity>(modelBuilder);
            ConfigureEntityUidLink<IRecordMetadata, RecordMetadataEntity>(modelBuilder);
            ConfigureEntityUidLink<ISharedFolderKey, SharedFolderKeyEntity>(modelBuilder);
            ConfigureEntityUidLink<ISharedFolderPermission, SharedFolderPermissionEntity>(modelBuilder);
            ConfigureEntityUid<IFolder, FolderEntity>(modelBuilder);
            ConfigureEntityUidLink<IFolderRecordLink, FolderRecordLinkEntity>(modelBuilder);
            ConfigureEntityUid<IRecordType, RecordTypeEntity>(modelBuilder);
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

        sealed class PredicateStorage<I, T> : IPredicateStorage<I>
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
            public bool Shared { get; private set; }
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
                Owner = other.Owner;
                Uid = other.Uid;
            }
        }

        sealed class SharedFolderEntity : ISharedFolder, IEntity<ISharedFolder>
        {
            public string PersonalScopeUid { get; set; } = null!;
            public long Revision { get; private set; }
            public string? Name { get; private set; }
            public bool DefaultManageRecords { get; private set; }
            public bool DefaultManageUsers { get; private set; }
            public bool DefaultCanEdit { get; private set; }
            public bool DefaultCanShare { get; private set; }
            public string Uid { get; private set; } = null!;
            string ISharedFolder.SharedFolderUid => Uid;

            public void CopyFrom(ISharedFolder other)
            {
                Revision = other.Revision;
                Name = other.Name;
                DefaultManageRecords = other.DefaultManageRecords;
                DefaultManageUsers = other.DefaultManageUsers;
                DefaultCanEdit = other.DefaultCanEdit;
                DefaultCanShare = other.DefaultCanShare;
                Uid = other.Uid;
            }
        }

        sealed class EnterpriseTeamEntity : IEnterpriseTeam, IEntity<IEnterpriseTeam>
        {
            public string PersonalScopeUid { get; set; } = null!;
            public string? Name { get; private set; }
            public string? TeamKey { get; private set; }
            public int KeyType { get; private set; }
            public string? TeamPrivateKey { get; private set; }
            public bool RestrictEdit { get; private set; }
            public bool RestrictShare { get; private set; }
            public bool RestrictView { get; private set; }
            public string Uid { get; private set; } = null!;
            string IEnterpriseTeam.TeamUid => Uid;

            public void CopyFrom(IEnterpriseTeam other)
            {
                Name = other.Name;
                TeamKey = other.TeamKey;
                KeyType = other.KeyType;
                TeamPrivateKey = other.TeamPrivateKey;
                RestrictEdit = other.RestrictEdit;
                RestrictShare = other.RestrictShare;
                RestrictView = other.RestrictView;
                Uid = other.Uid;
            }
        }

        sealed class NonSharedDataEntity : INonSharedData, IEntity<INonSharedData>
        {
            public string PersonalScopeUid { get; set; } = null!;
            public string? Data { get; set; }
            public string Uid { get; private set; } = null!;
            string INonSharedData.RecordUid => Uid;

            public void CopyFrom(INonSharedData other)
            {
                Data = other.Data;
                Uid = other.Uid;
            }
        }

        sealed class RecordMetadataEntity : IRecordMetadata, IEntity<IRecordMetadata>
        {
            public string PersonalScopeUid { get; set; } = null!;
            public string? RecordKey { get; private set; }
            public int RecordKeyType { get; private set; }
            public bool CanShare { get; set; }
            public bool CanEdit { get; set; }
            public string SubjectUid { get; private set; } = null!;
            public string ObjectUid { get; private set; } = null!;
            string IRecordMetadata.RecordUid => SubjectUid;
            string IRecordMetadata.SharedFolderUid => ObjectUid;

            public void CopyFrom(IRecordMetadata other)
            {
                RecordKey = other.RecordKey;
                RecordKeyType = other.RecordKeyType;
                CanShare = other.CanShare;
                CanEdit = other.CanEdit;
                SubjectUid = other.SubjectUid;
                ObjectUid = other.ObjectUid;
            }
        }

        sealed class SharedFolderKeyEntity : ISharedFolderKey, IEntity<ISharedFolderKey>
        {
            public string PersonalScopeUid { get; set; } = null!;
            public int KeyType { get; private set; }
            public string? SharedFolderKey { get; private set; }
            public string SubjectUid { get; private set; } = null!;
            public string ObjectUid { get; private set; } = null!;
            string ISharedFolderKey.SharedFolderUid => SubjectUid!;
            string ISharedFolderKey.TeamUid => ObjectUid!;

            public void CopyFrom(ISharedFolderKey other)
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
            string ISharedFolderPermission.SharedFolderUid => SubjectUid;
            string ISharedFolderPermission.UserId => ObjectUid;

            public void CopyFrom(ISharedFolderPermission other)
            {
                UserType = other.UserType;
                ManageRecords = other.ManageRecords;
                ManageUsers = other.ManageUsers;
                SubjectUid = other.SubjectUid;
                ObjectUid = other.ObjectUid;
            }
        }

        sealed class FolderEntity : IFolder, IEntity<IFolder>
        {
            public string PersonalScopeUid { get; set; } = null!;
            public string? ParentUid { get; private set; }
            public string? FolderType { get; private set; }
            public string? FolderKey { get; private set; }
            public string? SharedFolderUid { get; private set; }
            public long Revision { get; private set; }
            public string? Data { get; private set; }
            public string Uid { get; private set; } = null!;
            string IFolder.FolderUid => Uid!;

            public void CopyFrom(IFolder other)
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

        sealed class FolderRecordLinkEntity : IFolderRecordLink, IEntity<IFolderRecordLink>
        {
            public string PersonalScopeUid { get; set; } = null!;
            public string SubjectUid { get; private set; } = null!;
            public string ObjectUid { get; private set; } = null!;
            string IFolderRecordLink.FolderUid => SubjectUid;
            string IFolderRecordLink.RecordUid => ObjectUid;

            public void CopyFrom(IFolderRecordLink other)
            {
                SubjectUid = other.SubjectUid;
                ObjectUid = other.ObjectUid;
            }
        }

        sealed class RecordTypeEntity : IRecordType, IEntity<IRecordType>
        {
            public string PersonalScopeUid { get; set; } = null!;

            public int Id { get; private set; }

            public RecordTypeScope Scope { get; private set; }

            public string? Content { get; private set; }

            public string Uid { get; private set; } = null!;

            public void CopyFrom(IRecordType other)
            {
                Id = other.Id;
                Scope = other.Scope;
                Content = other.Content;
                Uid = other.Uid;
            }
        }
    }
}
