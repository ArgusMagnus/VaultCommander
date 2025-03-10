using KeeperSecurity.Storage;
using KeeperSecurity.Vault;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;

namespace VaultCommander.Vaults;

sealed partial class KeeperVault
{
    sealed class KeeperStorage(Func<IDbConnection> getConnection, string? personalScopeUid) : IKeeperStorage, IDisposable
    {
        // dirty hack to allow to directly initialize properties with getConnection from path
        sealed class ConnectionHolder
        {
            public IDbConnection Connection { get; set; } = default!;

            [ThreadStatic]
            public static ConnectionHolder? Init;
        }

        readonly IDbConnection? _connection;

        public KeeperStorage(string path, string? personalScopeUid)
            : this((ConnectionHolder.Init = new()) is var x ? () => x.Connection : null!, personalScopeUid)
        {
            ConnectionHolder.Init!.Connection = _connection = new SqliteConnection();
            ConnectionHolder.Init = null;
        }

        public void Dispose() => _connection?.Dispose();

        public string PersonalScopeUid { get; } = personalScopeUid ?? "";

        public IRecordStorage<IVaultSettings> VaultSettings { get; } = new SqliteRecordStorage<IVaultSettings, VaultSettings>(getConnection);
        public IEntityStorage<IStorageRecord> Records { get; } = new SqliteEntityStorage<IStorageRecord, StorageRecord>(getConnection);
        public IEntityStorage<IStorageSharedFolder> SharedFolders { get; } = new SqliteEntityStorage<IStorageSharedFolder, StorageSharedFolder>(getConnection);
        public IEntityStorage<IStorageTeam> Teams { get; } = new SqliteEntityStorage<IStorageTeam, StorageTeam>(getConnection);
        public IEntityStorage<IStorageNonSharedData> NonSharedData { get; } = new SqliteEntityStorage<IStorageNonSharedData, StorageNonSharedData>(getConnection);
        public ILinkStorage<IStorageRecordKey> RecordKeys { get; } = new SqliteLinkStorage<IStorageRecordKey, StorageRecordKey>(getConnection);
        public ILinkStorage<IStorageSharedFolderKey> SharedFolderKeys { get; } = new SqliteLinkStorage<IStorageSharedFolderKey, StorageSharedFolderKey>(getConnection);
        public ILinkStorage<ISharedFolderPermission> SharedFolderPermissions { get; } = new SqliteLinkStorage<ISharedFolderPermission, StorageSharedFolderPermission>(getConnection);
        public IEntityStorage<IStorageFolder> Folders { get; } = new SqliteEntityStorage<IStorageFolder, StorageFolder>(getConnection);
        public ILinkStorage<IStorageFolderRecord> FolderRecords { get; } = new SqliteLinkStorage<IStorageFolderRecord, StorageFolderRecord>(getConnection);
        public IEntityStorage<IStorageRecordType> RecordTypes { get; } = new SqliteEntityStorage<IStorageRecordType, StorageRecordType>(getConnection);
        public ILinkStorage<IStorageUserEmail> UserEmails { get; } = new SqliteLinkStorage<IStorageUserEmail, StorageUserEmail>(getConnection);

        public void Clear()
        {
            foreach (SqliteStorage storage in (IEnumerable<object>)[
                VaultSettings, Records, SharedFolders, Teams,NonSharedData, RecordKeys, SharedFolderKeys, SharedFolderPermissions, Folders, FolderRecords, RecordTypes, UserEmails])
                storage.DeleteAll();
        }
    }
}
