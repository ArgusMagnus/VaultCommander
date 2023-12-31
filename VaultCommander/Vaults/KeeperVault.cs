﻿using KeeperSecurity.Authentication;
using KeeperSecurity.Authentication.Async;
using KeeperSecurity.Configuration;
using KeeperSecurity.Utils;
using KeeperSecurity.Vault;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
    readonly Auth _auth;
    readonly KeeperStorage _storage;
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
        _storage = new(_auth.Storage.LastLogin, Path.Combine(_dataDirectory, "storage.sqlite"));
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
        if (_auth.IsAuthenticated())
            status = Status.Unlocked;
        else if (!string.IsNullOrEmpty(_auth.Storage.LastLogin))
            status = Status.Locked;
        return Task.FromResult<StatusDto?>(new(_auth.Storage.LastServer, null, _auth.Storage.LastLogin, null, status));
    }

    public async Task<StatusDto?> Initialize()
    {
        await _storage.Database.EnsureCreatedAsync().ConfigureAwait(false);
        await _storage.Database.MigrateAsync();
        return await GetStatus();
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
                try
                {
                    await _auth.Login(user, pw.GetAsClearText());
                    _storage.PersonalScopeUid = _auth.Username;
                    //_storage.SetPasswordHashAndClientKey(CryptoUtils.DeriveV1KeyHash(pw.GetAsClearText(), null, 1), _auth.AuthContext.ClientKey);
                }
                catch (Exception)
                {
                    throw;
                }
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

            var hasCommandField = data.Custom.Any(x => validCommandSchemes.Any(y => x.Value?.StartsWith(y, StringComparison.OrdinalIgnoreCase) is true));
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

                if (includeTotp && field.Type == "oneTimeCode" && !string.IsNullOrEmpty(field.Value))
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
}
