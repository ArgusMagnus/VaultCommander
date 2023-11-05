using KeeperSecurity.Authentication;
using KeeperSecurity.Authentication.Async;
using KeeperSecurity.Commands;
using KeeperSecurity.Configuration;
using KeeperSecurity.Utils;
using KeeperSecurity.Vault;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VaultCommander.Commands;

namespace VaultCommander.Vaults;

sealed class KeeperVault : IVault, IAsyncDisposable
{
    // https://keeper-security.github.io/gitbook-keeper-sdk/CSharp/html/R_Project_Documentation.htm

    public string VaultName => "Keeper";

    public string UriScheme => "KeeperCmd";

    public string UriFieldName => nameof(VaultCommander);

    readonly string _dataDirectory;
    readonly Auth _auth;
    readonly Lazy<Task<VaultOnline>> _vault;

    KeeperVault(string dataDirectoryRoot)
    {
        _dataDirectory = Path.Combine(dataDirectoryRoot, VaultName);
        Directory.CreateDirectory(_dataDirectory);
        _auth = new(new AuthUi(), new JsonConfigurationStorage(new JsonConfigurationCache(new JsonConfigurationFileLoader(Path.Combine(_dataDirectory, "storage.json")))
        {
            ConfigurationProtection = new ConfigurationProtectionFactory()
        }));
        _auth.Endpoint.DeviceName = $"{Environment.MachineName}_{nameof(VaultCommander)}";
        _vault = new(VaultFactory);
    }

    public async ValueTask DisposeAsync()
    {
        if (_vault.IsValueCreated)
        {
            var vault = await _vault.Value.ConfigureAwait(false);
            var storage = (KeeperStorage)vault.Storage;
            vault.Dispose();
            await storage.DisposeAsync();
        }
        _auth.Dispose();
    }

    private async Task<VaultOnline> VaultFactory()
    {
        if (_auth.AuthContext is null)
            await Login().ConfigureAwait(false);
        var storage = new KeeperStorage(_auth.Username, Path.Combine(_dataDirectory, "storage.sqlite"));
        await storage.Database.EnsureCreatedAsync().ConfigureAwait(false);
        await storage.Database.MigrateAsync();
        var vault = new VaultOnline(_auth, storage) { AutoSync = true, VaultUi = new VaultUi() };
        // await vault.SyncDown();


        //var data = CryptoUtils.Base64UrlDecode("1VKZVsa2-W94vNrfrj50SuZ53ZOkexjna3wQ6eOgQIndAJarYVpi3S8ZPx2gBY5997QKJdLl3fvlpfJYPPRJbZWENyet3ZFA-nHPwG3SkIsFOXdslvOQJsslAX1ve3hS4RrdyVeB700tWUtuWYEzUVGWFNMWz4B6HvjyyIP-q41g8VUQQXZ_8PEj-KRgTjPoOMcWlHzMs3bU8PpQrJC_rmoI3jzCISS6zhlmofLLvM2Evo0PuBRRX3BGIfHdiaZxkSMMhckQJYhfVxbJ22CSOZFZS8W7-HzMYWfGRnWj40hYf10Zz7rLKCchjN0JrK2OlyuggMsKw4ZPwxR8eJVtp109OhSem83cE5SjoYf_U3VEberMV_3Gd8pgGcnJQYDNIFCxk6ftwGdFiUz4JjJDDJeovp7EVqnvuWHFJvuBFD4phQLDmmdskD7OaICo9r1SaoeA7hCn2pdToqHa7GTdRBB1lgS0Da5Be8ODDPFWe8_UO8D4339n5e1ySExFWv8sFdZQBuzH0NCmjELmBNRLCpCQZwvblJaiVGuLcQ9NBUygufTha49SA6zsHDZEEV8z-ms7sg87Wha44rN0edBu-sMseQmr5d3WZWEJWuV2p9keMZgNO0j-BuEvbZNK-u2qfnKSOHo2nSyWodtzEceoO-qdpSo6cgAXmzsZ7b7UmcvJ_BMFsJ4GxMkvWwA4ImDhb8dG0ijtm-l5vUV4L-N1Ghn1LYat9oqOLc8xUyrpSomE_mG4GcQeptewNt-oFljfbJ9sk8TOWTs3CSuQupxitQvm3modlfDiXl_sYAKORX0srRero0dv_z3jlh8-zGOK2D9y88jIZm8Ftf8dtt1v4B5kNv0l4RnBDhGJ4dUBUtGMQLvLmPPazvdjd_jC5isih-nWMxwam9JHrzuzZunTb04PVm7cHSQJULGw25jHG-i-qiQUox-0QvDrgy5pQ9jeocHSFMCfuWBW677Tz3bSJf_pSpJe_troyuFdQ8eGtm0hQHlTJAdNRbT2Cq3_AUjyzJIg9fat4oQ3lO_r");
        //var key = CryptoUtils.DecryptAesV1(
        //    CryptoUtils.Base64UrlDecode("1bm3lJZtyQp6ulZpgTINrrwr6Tz2j-qtJbqZKt7gtDhT65HK1Wyz4l74-LmPdR_EsJcggZCZVDNa78x6uSTzWQ"),
        //    vault.ClientKey);
        //var test = CryptoUtils.DecryptAesV2(data, key);
        //var test2 = JsonUtils.ParseJson<RecordApplicationData>(test);

        return vault;
    }

    public async Task<Record?> GetItem(string uid, bool includeTotp)
    {
        var vault = await _vault.Value.ConfigureAwait(false);
        return vault.TryGetKeeperRecord(uid, out var record) ? ToRecord(record, includeTotp) : null;
    }

    public Task<StatusDto?> GetStatus()
    {
        var status = _auth.IsAuthenticated() ? Status.Unlocked :
            (string.IsNullOrEmpty(_auth.Storage.LastLogin) ? Status.Unauthenticated : Status.Locked);
        return Task.FromResult<StatusDto?>(new(_auth.Storage.LastServer, null, _auth.Storage.LastLogin, null, status));
    }

    public Task<StatusDto?> Initialize()
    {
        return GetStatus();
    }

    public async Task<StatusDto?> Login()
    {
        _auth.Endpoint.DeviceName = $"{Environment.MachineName}_{nameof(VaultCommander)}";
        var (user, pw) = await Application.Current.Dispatcher.InvokeAsync(() => PasswordDialog.Show(Application.Current.MainWindow, _auth.Storage.LastLogin)).Task.ConfigureAwait(false);
        if (pw is not null)
        {
            var ui = (AuthUi)_auth.Ui;
            using (ui.ProgressBox = await ProgressBox.Show())
            {
                ui.ProgressBox.DetailText = "Anmelden...";
                ui.ProgressBox.DetailProgress = double.NaN;
                await _auth.Login(user, pw.GetAsClearText());
            }
        }
        return await GetStatus();
    }

    public Task Logout() => _auth.Logout();

    public async Task Sync() => await (await _vault.Value.ConfigureAwait(false)).SyncDown();

    public async Task<Record?> UpdateUris(string? uid)
    {
        Record? item = null;
        using var progressBox = await ProgressBox.Show().ConfigureAwait(false);
        progressBox.DetailText = "Einträge aktualisieren...";
        var vault = await _vault.Value.ConfigureAwait(false);
        await vault.SyncDown();
        var records = vault.KeeperRecords as IReadOnlyCollection<KeeperRecord> ?? vault.KeeperRecords.ToList();
        var prefix = $"{UriScheme}:";
        foreach (var (record, idx) in records.Select((x, i) => (x, i)))
        {
            progressBox.DetailProgress = (idx + 1.0) / records.Count;
            if (record is not TypedRecord data)
                continue;
            if (item is null && data.Uid == uid)
                item = ToRecord(data);
            var field = data.Custom.Select((x, i) => (x, i)).FirstOrDefault(x => x.x.FieldLabel == UriFieldName && x.x.ObjectValue is string value && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (string.Equals((field.x?.ObjectValue as string)?.Substring(prefix.Length), data.Uid, StringComparison.OrdinalIgnoreCase))
                continue;
            var newField = new TypedField<string>("text", UriFieldName) { TypedValue = $"{prefix}{data.Uid}" };
            if (field == default)
                data.Custom.Insert(0, newField);
            else
                data.Custom[field.i] = newField;
            await vault.UpdateRecord(data);
        }
        return item;
    }

    static Record ToRecord(KeeperRecord record, bool includeTotp = false)
    {
        if (record is not TypedRecord data)
            return new(record.Uid, record.Title, Array.Empty<RecordField>());
        return new(record.Uid, record.Title, TransformFields(data.Fields.Concat(data.Custom), includeTotp).ToList());

        static IEnumerable<RecordField> TransformFields(IEnumerable<ITypedField> fields, bool includeTotp)
        {
            foreach (var field in fields)
            {
                yield return new(
                    field.Type switch
                    {
                        "login" => nameof(IArgumentsUsername.Username),
                        _ => string.IsNullOrEmpty(field.FieldLabel) ? field.FieldName : field.FieldLabel
                    },
                    field switch
                    {
                        TypedField<FieldTypeHost> host => string.IsNullOrEmpty(host.TypedValue.Port) ? host.TypedValue.HostName : $"{host.TypedValue.HostName}:{host.TypedValue.Port}",
                        _ => field.Value
                    });

                if (includeTotp && field.Type == "oneTimeCode")
                    yield return new(nameof(IArgumentsTotp.Totp), CryptoUtils.GetTotpCode(field.Value).Item1);
            }
        }
    }

    sealed class Factory : IVaultFactory
    {
        public IVault CreateVault(string dataDirectoryRoot) => new KeeperVault(dataDirectoryRoot);
    }

    sealed class ConfigurationProtectionFactory : IConfigurationProtectionFactory
    {
        public IConfigurationProtection Resolve(string protection)
        {
            return new ConfigurationProtection();
        }

        sealed class ConfigurationProtection : IConfigurationProtection
        {
            public string Clarify(string data)
                => Encoding.UTF8.GetString(ProtectedData.Unprotect(data.Base64UrlDecode(), null, DataProtectionScope.CurrentUser));

            public string Obscure(string data)
                => ProtectedData.Protect(Encoding.UTF8.GetBytes(data), null, DataProtectionScope.CurrentUser).Base64UrlEncode();
        }
    }

    sealed class AuthUi : IAuthUI
    {
        public ProgressBox.IViewModel? ProgressBox { get; set; }

        public async Task<bool> WaitForDeviceApproval(IDeviceApprovalChannelInfo[] channels, CancellationToken token)
        {
            var usedChannels = channels.OfType<IDeviceApprovalPushInfo>().ToList();
            if (!usedChannels.Any())
                return false;

            if (ProgressBox is not null)
            {
                ProgressBox.DetailText = $"Anfrage ({string.Join(", ", usedChannels.Select(x => x.Channel))}) wurde versandt. Auf Genehmigung warten...";
                ProgressBox.DetailProgress = double.NaN;
            }
            await Task.WhenAll(usedChannels.Select(x => Task.Run(() => x.InvokeDeviceApprovalPushAction()))).ConfigureAwait(false);
            return true;
        }

        public async Task<bool> WaitForTwoFactorCode(ITwoFactorChannelInfo[] channels, CancellationToken token)
        {
            var codeChannel = channels.OfType<ITwoFactorAppCodeInfo>().FirstOrDefault();
            if (codeChannel is null)
                return false;
            var (_, pw) = await Application.Current.Dispatcher.InvokeAsync(() => PasswordDialog.Show(Application.Current.MainWindow, "2FA")).Task.ConfigureAwait(false);
            if (pw is null)
                return false;
            await codeChannel.InvokeTwoFactorCodeAction(pw.GetAsClearText());
            return true;
        }

        public async Task<bool> WaitForUserPassword(IPasswordInfo info, CancellationToken token)
        {
            var (_, pw) = await Application.Current.Dispatcher.InvokeAsync(() => PasswordDialog.Show(Application.Current.MainWindow, info.Username)).Task.ConfigureAwait(false);
            if (pw is null)
                return false;
            await info.InvokePasswordActionDelegate(pw.GetAsClearText());
            return true;
        }
    }

    sealed class VaultUi : IVaultUi
    {
        public Task<bool> Confirmation(string information)
        {
            return Application.Current.Dispatcher.InvokeAsync(() => MessageBox.Show(information, nameof(VaultCommander), MessageBoxButton.YesNo, MessageBoxImage.Question) is MessageBoxResult.Yes).Task;
        }
    }

    sealed class KeeperStorage : DbContext, IKeeperStorage
    {
        public string PersonalScopeUid { get; }

        private DbSet<UserStorage> userStorage { get; init; } = null!;
        public long Revision
        {
            get => userStorage.Find(PersonalScopeUid)?.Revision ?? 0;
            set
            {
                if (userStorage.Find(PersonalScopeUid) is UserStorage entity)
                    entity.Revision = value;
                else
                {
                    entity = new() { PersonalScopeUid = PersonalScopeUid, Revision = value };
                    userStorage.Add(entity);
                }
                SaveChanges();
            }
        }

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

        public KeeperStorage(string personalScopeUid, string dbFilename)
        {
            PersonalScopeUid = personalScopeUid;
            _dbFilename = dbFilename;
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
            void Clear<T>(DbSet<T> set) where T : class, IPersonalScopeUid
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

            ConfigureEntity<IStorageRecord, RecordEntity>(modelBuilder);
            ConfigureEntity<ISharedFolder, SharedFolderEntity>(modelBuilder);
            ConfigureEntity<IEnterpriseTeam, EnterpriseTeamEntity>(modelBuilder);
            ConfigureEntity<INonSharedData, NonSharedDataEntity>(modelBuilder);
            ConfigureEntity<IRecordMetadata, RecordMetadataEntity>(modelBuilder);
            ConfigureEntity<ISharedFolderKey, SharedFolderKeyEntity>(modelBuilder);
            ConfigureEntity<ISharedFolderPermission, SharedFolderPermissionEntity>(modelBuilder);
            ConfigureEntity<IFolder, FolderEntity>(modelBuilder);
            ConfigureEntity<IFolderRecordLink, FolderRecordLinkEntity>(modelBuilder);
            ConfigureEntity<IRecordType, RecordTypeEntity>(modelBuilder);
            modelBuilder.Entity<UserStorage>().HasKey(x => x.PersonalScopeUid);

            static void ConfigureEntity<I, T>(ModelBuilder modelBuilder) where T : class, I
            {
                var typeBuilder = modelBuilder.Entity<T>();
                var properties = typeof(I).GetProperties().Select(x => x.Name).Append(nameof(IPersonalScopeUid.PersonalScopeUid)).ToHashSet();
                foreach (var columnName in typeof(T).GetProperties().Where(x => !properties.Contains(x.Name)))
                    typeBuilder.Ignore(columnName.Name);

                var table = typeof(T).BaseType?.GetCustomAttribute<SqlTableAttribute>() ?? throw new InvalidOperationException();
                typeBuilder
                    .ToTable(table.Name)
                    .HasKey(table.PrimaryKey.Prepend(nameof(IPersonalScopeUid.PersonalScopeUid)).ToArray());
            }
        }

        sealed class UserStorage : IPersonalScopeUid
        {
            public string PersonalScopeUid { get; set; } = null!;
            public long Revision { get; set; }
        }

        interface IPersonalScopeUid { public string PersonalScopeUid { get; set; } }
        sealed class RecordEntity : ExternalRecord, IPersonalScopeUid { public string PersonalScopeUid { get; set; } = null!; }
        sealed class SharedFolderEntity : ExternalSharedFolder, IPersonalScopeUid { public string PersonalScopeUid { get; set; } = null!; }
        sealed class EnterpriseTeamEntity : ExternalEnterpriseTeam, IPersonalScopeUid { public string PersonalScopeUid { get; set; } = null!; }
        sealed class NonSharedDataEntity : ExternalNonSharedData, IPersonalScopeUid { public string PersonalScopeUid { get; set; } = null!; }
        sealed class RecordMetadataEntity : ExternalRecordMetadata, IPersonalScopeUid { public string PersonalScopeUid { get; set; } = null!; }
        sealed class SharedFolderKeyEntity : ExternalSharedFolderKey, IPersonalScopeUid { public string PersonalScopeUid { get; set; } = null!; }
        sealed class SharedFolderPermissionEntity : ExternalSharedFolderPermission, IPersonalScopeUid { public string PersonalScopeUid { get; set; } = null!; }
        sealed class FolderEntity : ExternalFolder, IPersonalScopeUid { public string PersonalScopeUid { get; set; } = null!; }
        sealed class FolderRecordLinkEntity : ExternalFolderRecordLink, IPersonalScopeUid { public string PersonalScopeUid { get; set; } = null!; }
        sealed class RecordTypeEntity : ExternalRecordType, IPersonalScopeUid { public string PersonalScopeUid { get; set; } = null!; }

        sealed class EntityStorage<I, T> : IEntityStorage<I>
            where I : IUid
            where T : class, I, IPersonalScopeUid, IEntityCopy<I>, new()
        {
            readonly DbContext _dbContext;
            readonly DbSet<T> _set;
            readonly string _personalScopeUid;

            public EntityStorage(KeeperStorage dbContext, DbSet<T> set)
            {
                _dbContext = dbContext;
                _personalScopeUid = dbContext.PersonalScopeUid;
                _set = set;
            }

            public void DeleteUids(IEnumerable<string> uids)
            {
                var remove = _set.Where(x => x.PersonalScopeUid == _personalScopeUid && uids.Contains(x.Uid)).ToList();
                _set.RemoveRange(remove);
                _dbContext.SaveChanges();
            }

            public IEnumerable<I> GetAll()
            {
                return _set.Where(x => x.PersonalScopeUid == _personalScopeUid);
            }

            public I GetEntity(string uid)
            {
                return _set.Find(_personalScopeUid, uid)!;
            }

            public void PutEntities(IEnumerable<I> entities)
            {
                foreach (var value in entities)
                {
                    if (_set.Find(_personalScopeUid, value.Uid) is T entity)
                        Utils.CopyProperties(value, entity);
                    else
                    {
                        entity = new();
                        Utils.CopyProperties(value, entity);
                        entity.PersonalScopeUid = _personalScopeUid;
                        _set.Add(entity);
                    }
                }
                _dbContext.SaveChanges();
            }
        }

        sealed class PredicateStorage<I, T> : IPredicateStorage<I>
            where I : IUidLink
            where T : class, I, IPersonalScopeUid, IEntityCopy<I>, new()
        {
            readonly DbContext _dbContext;
            readonly DbSet<T> _set;
            readonly string _personalScopeUid;

            public PredicateStorage(KeeperStorage dbContext, DbSet<T> set)
            {
                _dbContext = dbContext;
                _personalScopeUid = dbContext.PersonalScopeUid;
                _set = set;
            }

            public void DeleteLinks(IEnumerable<IUidLink> links)
            {
                foreach (var link in links)
                {
                    if (_set.Find(_personalScopeUid, link.SubjectUid, link.ObjectUid) is T entity)
                        _set.Remove(entity);
                }
                _dbContext.SaveChanges();
            }

            public void DeleteLinksForObjects(IEnumerable<string> objectUids)
            {
                var remove = _set.Where(x => x.PersonalScopeUid == _personalScopeUid && objectUids.Contains(x.ObjectUid)).ToList();
                _set.RemoveRange(remove);
                _dbContext.SaveChanges();
            }

            public void DeleteLinksForSubjects(IEnumerable<string> subjectUids)
            {
                var remove = _set.Where(x => x.PersonalScopeUid == _personalScopeUid && subjectUids.Contains(x.SubjectUid)).ToList();
                _set.RemoveRange(remove);
                _dbContext.SaveChanges();
            }

            public IEnumerable<I> GetAllLinks()
            {
                return _set.Where(x => x.PersonalScopeUid == _personalScopeUid);
            }

            public IEnumerable<I> GetLinksForObject(string objectUid)
            {
                return _set.Where(x => x.PersonalScopeUid == _personalScopeUid && x.ObjectUid == objectUid);
            }

            public IEnumerable<I> GetLinksForSubject(string subjectUid)
            {
                return _set.Where(x => x.PersonalScopeUid == _personalScopeUid && x.SubjectUid == subjectUid);
            }

            public void PutLinks(IEnumerable<I> entities)
            {
                foreach (var value in entities)
                {
                    if (_set.Find(_personalScopeUid, value.SubjectUid, value.ObjectUid) is T entity)
                        Utils.CopyProperties(value, entity);
                    else
                    {
                        entity = new();
                        Utils.CopyProperties(value, entity);
                        entity.PersonalScopeUid = _personalScopeUid;
                        _set.Add(entity);
                    }
                }
                _dbContext.SaveChanges();
            }
        }
    }
}
