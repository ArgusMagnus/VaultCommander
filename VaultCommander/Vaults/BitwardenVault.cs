using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using VaultCommander.Commands;

namespace VaultCommander.Vaults;

sealed partial class BitwardenVault : IVault, IAsyncDisposable
{
    readonly CliClient _cli;
    EncryptedString? _savedPw;

    public string VaultName => "Bitwarden";
    public string UriScheme => "BwCmd";
    public string UriFieldName => nameof(VaultCommander);

    BitwardenVault(string dataDirectoryRoot) => _cli = new(Path.Combine(dataDirectoryRoot, VaultName, "bw.exe"));

    public async Task<StatusDto?> GetStatus()
    {
        if (!File.Exists(_cli.ExePath))
            return new(null, null, null, null, Status.Unauthenticated);
        if (_cli.IsAttachedToApiServer)
            return await (await _cli.GetApiClient()).GetStatus();
        return await _cli.GetStatus();
    }

    async Task UpdateCli()
    {
        if (await _cli.GetUpdateUri() is not Uri uri)
            return;

        using var progressBox = await ProgressBox.Show();
        progressBox.StepText = "0 / 1";
        progressBox.StepProgress = 0.5;
        progressBox.DetailText = "Bitwarden CLI herunterladen...";

        void OnProgress(double progress)
        {
            progressBox.DetailProgress = progress * 2;
            if (progress is 0.5)
            {
                progressBox.StepText = "1 / 1";
                progressBox.StepProgress = 1;
                progressBox.DetailText = "Bitwarden CLI installieren...";
            }
        }

        if (_cli.TryAttachToApiServer())
            _cli.KillServer();
        await Utils.DownloadAndExpandZipArchive(uri,
            name => string.Equals(".exe", Path.GetExtension(name), StringComparison.OrdinalIgnoreCase) ? _cli.ExePath : null,
            OnProgress);

        progressBox.StepProgress = 1;
    }

    public async Task<StatusDto?> Initialize()
    {
        if (File.Exists(_cli.ExePath))
        {
            await UpdateCli();
            if (await _cli.StartApiServer(null))
                return await GetStatus();
        }
        return new(null, null, null, null, Status.Unauthenticated);
    }

    public async Task<StatusDto?> Login()
    {
        if (!File.Exists(_cli.ExePath))
            await UpdateCli();

        _savedPw = null;

        while (true)
        {
            var cred = PasswordDialog.Show(Application.Current.MainWindow, null);
            if (cred == default)
                break;
            if (string.IsNullOrEmpty(cred.UserEmail) || cred.Password is null)
                continue;
            var sessionToken = await _cli.Login(cred.UserEmail, cred.Password);
            if (string.IsNullOrEmpty(sessionToken))
                continue;
            _savedPw = cred.Password;
            await _cli.StartApiServer(sessionToken);
            break;
        }

        return await GetStatus();
    }

    public async Task Sync() => await UseApi(api => api.Sync());

    public async Task<Record?> UpdateUris(IEnumerable<string> validCommandSchemes, string? uid)
    {
        using var progressBox = await ProgressBox.Show();
        progressBox.DetailText = "Einträge aktualisieren...";
        return await UseApi(async api =>
        {
            await api.Sync();
            Record? record = null;
            var itemsDto = await api.GetItems();
            if (itemsDto?.Success is not true || itemsDto.Data?.Data is null)
                return record;

            var prefix = $"{UriScheme}:";
            validCommandSchemes = validCommandSchemes.Select(x => $"{x}:").ToList();
            foreach (var (data, idx) in itemsDto.Data.Data.Select((x,i) => (x,i)))
            {
                progressBox.DetailProgress = (idx + 1.0) / itemsDto.Data.Data.Count;
                if (record is null && data.Id == uid)
                    record = ToRecord(data);

                var hasCommandField = data.Fields.Any(x => validCommandSchemes.Any(y => x.Value?.StartsWith(y, StringComparison.OrdinalIgnoreCase) is true));
                if (!hasCommandField)
                    continue;

                var element = data.Fields.Select((x, i) => (x, i)).FirstOrDefault(x => x.x.Name == UriFieldName);
                if (data.Login is not null)
                {
                    if (element != default)
                        data.Fields.RemoveAt(element.i);
                    var uri = data.Login.Uris.Select((x, i) => (x, i)).FirstOrDefault(x => x.x.Uri?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) is true);
                    if (uri.x?.Uri?.Substring(prefix.Length) == data.Id)
                        continue;
                    var newUri = new ItemUri { Uri = $"{prefix}{data.Id}", Match = UriMatchType.Never };
                    if (uri == default)
                        data.Login.Uris.Add(newUri);
                    else
                        data.Login.Uris[uri.i] = newUri;
                }
                else
                {
                    if (element.x?.Value?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) is true && element.x.Value.Substring(prefix.Length) == data.Id)
                        continue;
                    var newField = new Field { Name = UriFieldName, Value = $"{prefix}{data.Id}", Type = FieldType.Text };
                    if (element == default)
                        data.Fields.Insert(0, newField);
                    else
                        data.Fields[element.i] = newField;
                }
                await api.PutItem(data);
            }

            return record;
        });
    }

    public async Task Logout()
    {
        _cli.KillServer();
        await _cli.Logout();
        if (Directory.Exists(_cli.AppDataDir))
        {
            try { Directory.Delete(_cli.AppDataDir, true); }
            catch
            {
                foreach (var file in Directory.EnumerateFiles(_cli.AppDataDir))
                {
                    try { File.Delete(file); }
                    catch { }
                }
            }
        }
    }

    async Task<T> UseApi<T>(Func<ApiClient, Task<T>> func)
    {
        var api = await _cli.GetApiClient();
        T result;
        try
        {
            var status = await api.GetStatus();
            if (status?.Status is Status.Locked)
            {
                _savedPw = null;
                while (_savedPw is null || !await api.Unlock(_savedPw))
                {
                    _savedPw = PasswordDialog.Show(Application.Current.MainWindow, status.UserEmail).Password;
                }
            }
            result = await func(api);
        }
        finally
        {
            if (!Debugger.IsAttached)
                await api.Lock();
        }
        return result;
    }

    Task UseApi(Func<ApiClient, Task> func) => UseApi(async api => { await func(api); return true; });

    public Task<Record?> GetItem(string uid, bool includeTotp)
    {
        return UseApi(async api =>
        {
            var response = await api.GetItem(uid);
            if (response?.Success is not true)
                return null;
            var item = response.Data;
            if (!string.IsNullOrEmpty(item?.Login?.Totp))
                item = item with { Login = item.Login with { Totp = (await api.GetTotp(uid))?.Data?.Data } };
            return item is null ? null : ToRecord(item);
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_cli.IsAttachedToApiServer)
        {
            try { await (await _cli.GetApiClient()).Lock(); }
            catch { }
        }
        _cli.Dispose();
    }

    static Record ToRecord(ItemTemplate item) => new(item.Id, item.Name, new RecordField[] {
        new(nameof(IArgumentsTitle.Title), item.Name),
        new(nameof(IArgumentsUsername.Username), item.Login?.Username),
        new(nameof(IArgumentsPassword.Password), item.Login?.Password),
        new(nameof(IArgumentsTotp.Totp), item.Login?.Totp)}
        .Concat(item.Fields.Select(x => new RecordField(x.Name, x.Value)))
        .ToList());

    sealed class Factory : IVaultFactory
    {
        public IVault CreateVault(string dataDirectoryRoot) => new BitwardenVault(dataDirectoryRoot);
    }

    enum ItemType
    {
        Login = 1,
        SecureNote,
        Card,
        Identity
    }

    enum FieldType
    {
        Text = 0,
        Hidden,
        Boolean,
        Link
    }

    enum UriMatchType
    {
        Domain = 0,
        Host,
        StartsWith,
        Exact,
        Regex,
        Never
    }

    sealed record Field
    {
        public string Name { get; init; } = string.Empty;
        public string? Value { get; init; }
        public FieldType Type { get; init; }
        public string? LinkedId { get; init; }
    }

    sealed record ItemTemplate
    {
        public string Id { get; init; } = null!;
        public Guid? OrganizationId { get; init; }
        public IReadOnlyList<Guid> CollectionIds { get; init; } = Array.Empty<Guid>();
        public Guid? FolderId { get; init; }
        public ItemType Type { get; init; }
        public string? Name { get; init; }
        public string? Notes { get; init; }
        public bool Favorite { get; init; }
        public IList<Field> Fields { get; init; } = new List<Field>();
        public ItemLogin? Login { get; init; }
        public ItemSecureNote? SecureNote { get; init; }
        public ItemCard? Card { get; init; }
        public IReadOnlyDictionary<string, string>? Identity { get; init; }
        public int Reprompt { get; init; }
    }

    sealed record ItemLogin
    {
        public IList<ItemUri> Uris { get; init; } = new List<ItemUri>();
        public string? Username { get; init; }
        public string? Password { get; init; }
        public string? Totp { get; init; }
    }

    sealed record ItemSecureNote
    {
        public int Type { get; init; }
    }

    sealed record ItemCard
    {
        public string CardholderName { get; init; } = string.Empty;
        public string Brand { get; init; } = string.Empty;
        public string Number { get; init; } = string.Empty;
        public string ExpMonth { get; init; } = string.Empty;
        public string ExpYear { get; init; } = string.Empty;
        public string Code { get; init; } = string.Empty;
    }

    sealed record ItemUri
    {
        public UriMatchType? Match { get; init; }
        public string? Uri { get; init; }
    }

    abstract record ObjectResponse<T>
    {
        public bool Success { get; init; }
        public DataDto? Data { get; init; }

        public sealed record DataDto
        {
            public string Object { get; init; } = string.Empty;
            public T? Data { get; init; }
        }
    }

    sealed record GetListItemsDto : ObjectResponse<IReadOnlyList<ItemTemplate>>;
    sealed record GetTotpDto : ObjectResponse<string>;

    sealed record GetItemDto
    {
        public bool Success { get; init; }
        public ItemTemplate Data { get; init; } = new();
    }
}