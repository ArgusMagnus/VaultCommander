using KeeperSecurity.Authentication;
using KeeperSecurity.Authentication.Sync;
using KeeperSecurity.Configuration;
using KeeperSecurity.Utils;
using KeeperSecurity.Vault;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VaultCommander.Commands;

namespace VaultCommander.Vaults;

sealed partial class KeeperVault : IVault, IAsyncDisposable
{
    // https://keeper-security.github.io/gitbook-keeper-sdk/CSharp/html/R_Project_Documentation.htm

    public string VaultName => "Keeper";
    public string UriScheme => "KeeperCmd";
    public string UriFieldName => nameof(VaultCommander);

    readonly string _dataDirectory;
    readonly AuthSync _auth;
    readonly KeeperStorage _storage;
    readonly Lazy<Task<VaultOnline>> _vault;

    KeeperVault(string dataDirectoryRoot)
    {
        _dataDirectory = Path.Combine(dataDirectoryRoot, VaultName);
        Directory.CreateDirectory(_dataDirectory);
        _auth = new(new JsonConfigurationStorage(Path.Combine(_dataDirectory, "storage.json"))
        {
            ConfigurationProtection = new ConfigurationProtectionFactory()
        })
        {
            ResumeSession = true
        };
        _auth.Endpoint.DeviceName = $"{Environment.MachineName}_{nameof(VaultCommander)}";
        _storage = new(_auth.Storage.Get().LastLogin, Path.Combine(_dataDirectory, "storage.sqlite"));
        _vault = new(VaultFactory);
    }

    public async ValueTask DisposeAsync()
    {
        if (_vault.IsValueCreated)
        {
            var vault = await _vault.Value.ConfigureAwait(false);
            vault.Dispose();
        }
        await _storage.DisposeAsync();
        _auth.Dispose();
    }

    private async Task<VaultOnline> VaultFactory()
    {
        if (_auth.AuthContext is null)
            await Login().ConfigureAwait(false);
        var vault = new VaultOnline(_auth, _storage) { AutoSync = true, VaultUi = new VaultUi() };
        await vault.SyncDown();
        return vault;
    }

    public async Task<Record?> GetItem(string uid, bool includeTotp)
    {
        var vault = await _vault.Value.ConfigureAwait(false);
        return vault.TryGetKeeperRecord(uid, out var record) ? ToRecord(record, includeTotp) : null;
    }

    public Task<StatusDto?> GetStatus()
    {
        var status = Status.Unauthenticated;
        var storage = _auth.Storage.Get();
        if (_auth.IsAuthenticated())
            status = Status.Unlocked;
        else if (!string.IsNullOrEmpty(storage.LastLogin))
            status = Status.Locked;
        return Task.FromResult<StatusDto?>(new(storage.LastServer, null, storage.LastLogin, null, status));
    }

    public async Task<StatusDto?> Initialize()
    {
        try { await _storage.Database.MigrateAsync().ConfigureAwait(false); }
        catch (SqliteException)
        {
            await _storage.Database.EnsureDeletedAsync();
            await _storage.Database.MigrateAsync();
        }
        return await GetStatus();
    }

    public async Task<StatusDto?> Login()
    {
        var storage = _auth.Storage.Get();
        var server = string.IsNullOrEmpty(storage.LastServer) ? storage.Servers.List.FirstOrDefault()?.Server ?? "" : storage.LastServer;
        (server, var user, _) = await Application.Current.Dispatcher.InvokeAsync(() => PasswordDialog.Show(Application.Current.MainWindow, server, storage.LastLogin, emailOnly: true)).Task.ConfigureAwait(false);
        if (!string.IsNullOrEmpty(user))
        {
            using (var progressBox = await ProgressBox.Show())
            using (var uiCallback = new AuthSyncUiCallback(_auth, progressBox, _dataDirectory))
            {
                _auth.UiCallback = uiCallback;
                progressBox.DetailText = "Anmelden...";
                progressBox.DetailProgress = double.NaN;
                _auth.Endpoint.Server = server;
                await _auth.Login(user, []);
                await uiCallback.Wait();
                _storage.PersonalScopeUid = _auth.Username;
            }
        }
        return await GetStatus();
    }

    public Task Logout()
    {
        _auth.Logout();
        if (Directory.Exists(_dataDirectory))
        {
            try { Directory.Delete(_dataDirectory, true); }
            catch
            {
                foreach (var file in Directory.EnumerateFiles(_dataDirectory))
                {
                    try { File.Delete(file); }
                    catch { }
                }
            }
        }
        return Task.CompletedTask;
    }

    public async Task Sync() => await (await _vault.Value.ConfigureAwait(false)).SyncDown();

    public async Task<Record?> UpdateUris(IEnumerable<string> validCommandSchemes, string? uid)
    {
        Record? item = null;
        using var progressBox = await ProgressBox.Show().ConfigureAwait(false);
        progressBox.DetailText = "Einträge aktualisieren...";
        var vault = await _vault.Value.ConfigureAwait(false);
        await vault.SyncDown();
        var records = vault.KeeperRecords as IReadOnlyCollection<KeeperRecord> ?? vault.KeeperRecords.ToList();
        var prefix = $"{UriScheme}:";
        validCommandSchemes = validCommandSchemes.Select(x => $"{x}:").ToList();
        foreach (var (record, idx) in records.Select((x, i) => (x, i)))
        {
            progressBox.DetailProgress = (idx + 1.0) / records.Count;
            if (record is not TypedRecord data)
                continue;
            if (item is null && data.Uid == uid)
                item = ToRecord(data);

            var hasCommandField = data.Custom
                .OfType<TypedField<string>>()
                .Any(x => validCommandSchemes.Any(y => x.TypedValue?.StartsWith(y, StringComparison.OrdinalIgnoreCase) is true));
            if (!hasCommandField)
                continue;

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
            return new(record.Uid, record.Title, []);
        return new(record.Uid, record.Title, [.. TransformFields(data.Fields.Concat(data.Custom), includeTotp)]);

        static IEnumerable<RecordField> TransformFields(IEnumerable<ITypedField> fields, bool includeTotp)
        {
            foreach (var field in fields)
            {
                yield return new(
                    field.FieldName switch
                    {
                        "login" => nameof(IArgumentsUsername.Username),
                        _ => string.IsNullOrEmpty(field.FieldLabel) ? field.FieldName : field.FieldLabel
                    },
                    field switch
                    {
                        TypedField<FieldTypeHost> host => string.IsNullOrEmpty(host.TypedValue.Port) ? host.TypedValue.HostName : $"{host.TypedValue.HostName}:{host.TypedValue.Port}",
                        ISerializeTypedField sertf => sertf.ExportTypedField(),
                        _ => field.ObjectValue?.ToString()
                    });

                if (includeTotp && field.FieldName == "oneTimeCode" && field is TypedField<string> stf && !string.IsNullOrEmpty(stf.TypedValue))
                    yield return new(nameof(IArgumentsTotp.Totp), CryptoUtils.GetTotpCode(stf.TypedValue).Item1);
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

    sealed class AuthSyncUiCallback(AuthSync auth, ProgressBox.IViewModel progressBox, string dataDir) : IAuthSyncCallback, IDisposable
    {
        readonly AuthSync _auth = auth;
        readonly ProgressBox.IViewModel _progressBox = progressBox;
        readonly string _dataDir = dataDir;

        readonly TaskCompletionSource _tcs = new();
        readonly CancellationTokenSource _cts = new();

        public Task Wait() => _tcs.Task.WaitAsync(_cts.Token);

        async void IAuthSyncCallback.OnNextStep()
        {
            switch (_auth.Step)
            {
                default:
                    _tcs.TrySetResult();
                    _cts.Cancel();
                    break;

                case ReadyToLoginStep:
                    break;

                case DeviceApprovalStep { Channels: { Length: > 0 } } deviceApprovalStep:
                    {
                        _progressBox.DetailText = $"Anfrage ({string.Join(", ", deviceApprovalStep.Channels)}) wurde versandt. Auf Genehmigung warten...";
                        _progressBox.DetailProgress = double.NaN;
                        foreach (var channel in deviceApprovalStep.Channels)
                            await deviceApprovalStep.SendPush(channel).ConfigureAwait(false);
                    }
                    break;

                case TwoFactorStep { Channels: { Length: > 0 } } twoFactorStep:
                    {
                        twoFactorStep.Duration = TwoFactorDuration.Forever;

                        foreach (var action in twoFactorStep.Channels.SelectMany(twoFactorStep.GetChannelPushActions))
                            await twoFactorStep.SendPush(action).ConfigureAwait(false);

                        foreach (var channel in twoFactorStep.Channels.Where(twoFactorStep.IsCodeChannel))
                        {
                            var phoneNumber = twoFactorStep.GetPhoneNumber(channel);
                            var (_, _, pw) = await Application.Current.Dispatcher.InvokeAsync(() => PasswordDialog.Show(Application.Current.MainWindow, "", string.Join(' ', channel, phoneNumber))).Task;
                            if (pw is null)
                                continue;

                            try { await twoFactorStep.SendCode(channel, pw.GetAsClearText()); }
                            catch (KeeperApiException) { }
                            break;
                        }
                    }
                    break;

                case PasswordStep passwordStep:
                    {
                        var (_, _, pw) = await Application.Current.Dispatcher.InvokeAsync(() => PasswordDialog.Show(Application.Current.MainWindow, _auth.Storage.Get().LastServer, _auth.Username)).Task.ConfigureAwait(false);
                        if (pw is not null)
                            await passwordStep.VerifyPassword(pw.GetAsClearText());
                    }
                    break;

                case SsoTokenStep ssoTokenStep:
                    {
                        var tcs = new TaskCompletionSource<string?>();
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            var webView = new WebView2();
                            var window = new Window { Content = webView, Width = 500, Height = 800 };
                            window.Loaded += async (_, _) =>
                            {
                                var webViewOptions = new CoreWebView2EnvironmentOptions { AllowSingleSignOnUsingOSPrimaryAccount = true };
                                var env = await CoreWebView2Environment.CreateAsync(null, _dataDir, webViewOptions);
                                await webView.EnsureCoreWebView2Async(env);
                                webView.CoreWebView2.DocumentTitleChanged += (_, _) => window.Title = webView.CoreWebView2.DocumentTitle;
                                webView.Source = new(ssoTokenStep.SsoLoginUrl);
                            };
                            window.Closed += (_, _) => tcs.TrySetResult(null);

                            async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
                            {
                                var token = JsonSerializer.Deserialize<string?>(await webView.ExecuteScriptAsync("token"));
                                if (!string.IsNullOrEmpty(token) && tcs.TrySetResult(token))
                                {
                                    webView.NavigationCompleted -= OnNavigationCompleted;
                                    window.Close();
                                }
                            }
                            webView.NavigationCompleted += OnNavigationCompleted;

                            using (_cts.Token.Register(window.Close))
                            {
                                if (!_cts.IsCancellationRequested)
                                    window.ShowDialog();
                            }
                        }).Task.ConfigureAwait(false);
                        var token = await tcs.Task;
                        if (!string.IsNullOrEmpty(token))
                            await ssoTokenStep.SetSsoToken(token);
                        else
                        {
                            _auth.Cancel();
                            _tcs.TrySetResult();
                            _cts.Cancel();
                        }
                    }
                    break;

                case SsoDataKeyStep { Channels: { Length: > 0 } } ssoDataKeyStep:
                    {
                        _progressBox.DetailText = $"SSO Anfrage wurde versandt. Auf Genehmigung warten...";
                        _progressBox.DetailProgress = double.NaN;
                        foreach (var channel in ssoDataKeyStep.Channels)
                            await ssoDataKeyStep.RequestDataKey(channel).ConfigureAwait(false);
                    }
                    break;
            }
        }

        public void Dispose() => _cts.Cancel();
    }

    sealed class VaultUi : IVaultUi
    {
        public Task<bool> Confirmation(string information)
        {
            return Application.Current.Dispatcher.InvokeAsync(() => MessageBox.Show(information, nameof(VaultCommander), MessageBoxButton.YesNo, MessageBoxImage.Question) is MessageBoxResult.Yes).Task;
        }
    }
}
