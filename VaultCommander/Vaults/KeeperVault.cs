﻿using Enterprise;
using KeeperSecurity.Authentication;
using KeeperSecurity.Authentication.Sync;
using KeeperSecurity.Configuration;
using KeeperSecurity.Utils;
using KeeperSecurity.Vault;
using Microsoft.EntityFrameworkCore;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    readonly AuthUi _authUi;
    readonly AuthSync _auth;
    readonly KeeperStorage _storage;
    readonly Lazy<Task<VaultOnline>> _vault;

    KeeperVault(string dataDirectoryRoot)
    {
        _dataDirectory = Path.Combine(dataDirectoryRoot, VaultName);
        Directory.CreateDirectory(_dataDirectory);
        var configStorage = new JsonConfigurationStorage(Path.Combine(_dataDirectory, "storage.json")) 
        {
            ConfigurationProtection = new ConfigurationProtectionFactory()
        };
        _auth = new AuthSync(configStorage) { 
            ResumeSession = true
        };

        _authUi = new AuthUi(_dataDirectory);

        _auth.Endpoint.DeviceName = $"{Environment.MachineName}_{nameof(VaultCommander)}";
        _vault = new(VaultFactory);
        var config = _auth.Storage.Get();

        _storage = new(config.LastLogin, Path.Combine(_dataDirectory, "storage.sqlite"));
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
        var config = _auth.Storage.Get();
        var status = Status.Unauthenticated;
        if (_auth.IsAuthenticated())
            status = Status.Unlocked;
        else if (!string.IsNullOrEmpty(config.LastLogin))
            status = Status.Locked;
        return Task.FromResult<StatusDto?>(new(config.LastServer, null, config.LastLogin, null, status));
    }

    public async Task<StatusDto?> Initialize()
    {
        await _storage.Database.EnsureCreatedAsync().ConfigureAwait(false);
        await _storage.Database.MigrateAsync();
        return await GetStatus();
    }

    public async Task<StatusDto?> Login()
    {
        var config = _auth.Storage.Get();

        var (user, _) = await Application.Current.Dispatcher.InvokeAsync(() => PasswordDialog.Show(Application.Current.MainWindow, config.LastLogin, emailOnly: true)).Task.ConfigureAwait(false);
        if (!string.IsNullOrEmpty(user))
        {
            var ui = _authUi;
            ui.Reset();

            using (ui.ProgressBox = await ProgressBox.Show())
            {
                ui.ProgressBox.DetailText = "Anmelden...";
                ui.ProgressBox.DetailProgress = double.NaN;

                using var token = new CancellationTokenSource();
                var signal = new ManualResetEventSlim(false);
                using var authUi = new AuthSyncUi(signal.Set);
                _auth.UiCallback = authUi;

                await _auth.Login(user, []);
                while (_auth.IsCompleted)
                {
                    switch (_auth.Step)
                    {
                        case DeviceApprovalStep das:
                            {
                                //await das.SendPush(DeviceApprovalChannel.Email);
                                //await das.SendPush(DeviceApprovalChannel.TwoFactorAuth);
                                await das.SendPush(DeviceApprovalChannel.KeeperPush);
                                ui.ProgressBox.DetailText = $"Anfrage (...) wurde versandt. Auf Genehmigung warten...";
                                ui.ProgressBox.DetailProgress = double.NaN;
                            }
                            break;

                        case TwoFactorStep tfs:
                            {
                                tfs.Duration = TwoFactorDuration.Forever;
                                var phoneNumber = string.Empty;
                                TwoFactorChannel? codeChannel = null;
                                foreach (var ch in tfs.Channels)
                                {
                                    if (codeChannel == null && tfs.IsCodeChannel(ch))
                                    {
                                        codeChannel = ch;
                                        phoneNumber = tfs.GetPhoneNumber(ch);
                                    }
                                    foreach (var pch in tfs.GetChannelPushActions(ch))
                                    {
                                        try
                                        {
                                            await tfs.SendPush(pch);
                                        }
                                        catch { }
                                    }

                                }
                                if (codeChannel != null)
                                {
                                    var (_, pw) = await Application.Current.Dispatcher.InvokeAsync(
                                        () => PasswordDialog.Show(Application.Current.MainWindow, string.Join(' ', phoneNumber))).Task.ConfigureAwait(false);
                                    if (pw != null)
                                    {
                                        try
                                        {
                                            await tfs.SendCode(codeChannel.Value, pw.GetAsClearText());
                                            continue;    // the login step is already changed at this point
                                        }
                                        catch {
                                            // invalid code
                                        }
                                    }
                                }
                            }
                            break;

                        case PasswordStep ps:
                            {
                                var (_, pw) = await Application.Current.Dispatcher.InvokeAsync(() => PasswordDialog.Show(Application.Current.MainWindow, _auth.Username)).Task.ConfigureAwait(false);
                                if (pw is null) {
                                    break;
                                }
                                try {
                                    await ps.VerifyPassword(pw.GetAsClearText());
                                    continue;
                                }
                                catch { 
                                    // invalid password
                                }
                            }
                            break;
                    }
                    signal.Reset();
                    signal.Wait(token.Token);
                }

                _auth.UiCallback = null ;
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
                .Where(x => x.Count > 0)
                .Where(x => !string.IsNullOrEmpty(x.TypedValue))
                .Any(x => validCommandSchemes.Any(y => x.TypedValue.StartsWith(y, StringComparison.OrdinalIgnoreCase) is true));
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
            return new(record.Uid, record.Title, Array.Empty<RecordField>());
        return new(record.Uid, record.Title, TransformFields(data.Fields.Concat(data.Custom), includeTotp).ToList());

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

    sealed class AuthSyncUi : IAuthSyncCallback, IDisposable
    {
        private Action? _callback;
        public AuthSyncUi(Action callback) {
            _callback = callback;
        }
        public void OnNextStep()
        {
            _callback?.Invoke();
        }

        void IDisposable.Dispose()
        {
            _callback = null;
        }
    }


    sealed class AuthUi(string dataDir) 
    {
        readonly string _dataDir = dataDir;

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

            if (codeChannel is ITwoFactorPushInfo pushInfo)
            {
                foreach (var action in pushInfo.SupportedActions)
                    await pushInfo.InvokeTwoFactorPushAction(action);
            }

            codeChannel.Duration = TwoFactorDuration.Forever;
            var (_, pw) = await Application.Current.Dispatcher.InvokeAsync(() => PasswordDialog.Show(Application.Current.MainWindow, string.Join(' ', codeChannel.ApplicationName, codeChannel.PhoneNumber))).Task.ConfigureAwait(false);
            if (pw is null)
                return false;
            try { await codeChannel.InvokeTwoFactorCodeAction(pw.GetAsClearText()); }
            catch (KeeperApiException) { }
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

        public void SsoLogoutUrl(string url)
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }

        public async Task<bool> WaitForSsoToken(ISsoTokenActionInfo actionInfo, CancellationToken cancellationToken)
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
                    webView.Source = new(actionInfo.SsoLoginUrl);
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

                using (cancellationToken.Register(window.Close))
                {
                    if (!cancellationToken.IsCancellationRequested)
                        window.ShowDialog();
                }
            });

            var token = await tcs.Task;
            if (string.IsNullOrEmpty(token))
                return false;
            await actionInfo.InvokeSsoTokenAction(token);
            return true;
        }

        bool _dataKeyRequested = false;

        internal void Reset() => _dataKeyRequested = false;

        public async Task<bool> WaitForDataKey(IDataKeyChannelInfo[] channels, CancellationToken token)
        {
            if (_dataKeyRequested)
                return true;
            var ssoChannel = channels.FirstOrDefault(x => x.Channel is DataKeyShareChannel.KeeperPush);
            if (ssoChannel is null)
                return false;

            if (ProgressBox is not null)
            {
                ProgressBox.DetailText = $"Anfrage ({ssoChannel.Channel.SsoDataKeyShareChannelText()}) wurde versandt. Auf Genehmigung warten...";
                ProgressBox.DetailProgress = double.NaN;
            }

            //var tcs = new TaskCompletionSource();
            //await using (token.Register(tcs.SetResult))
            {
                await ssoChannel.InvokeGetDataKeyAction().ConfigureAwait(false);
                _dataKeyRequested = true;
                //await tcs.Task;
            }
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
}
