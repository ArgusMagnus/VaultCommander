using KeeperSecurity.Authentication;
using KeeperSecurity.Authentication.Async;
using KeeperSecurity.Authentication.Sync;
using KeeperSecurity.Configuration;
using KeeperSecurity.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace VaultCommander.Vaults;

sealed class KeeperVault : IVault, IDisposable
{
    // https://keeper-security.github.io/gitbook-keeper-sdk/CSharp/html/R_Project_Documentation.htm

    public string VaultName => "Keeper";

    public string UriScheme => "KeeperCmd";

    public string UriFieldName => nameof(VaultCommander);

    readonly string _dataDirectory;
    readonly Auth _auth;

    KeeperVault(string dataDirectoryRoot)
    {
        _dataDirectory = Path.Combine(dataDirectoryRoot, VaultName);
        Directory.CreateDirectory(_dataDirectory);
        _auth = new(new AuthUi(), new JsonConfigurationStorage(new JsonConfigurationCache(new JsonConfigurationFileLoader(Path.Combine(_dataDirectory, "storage.json")))
        {
            ConfigurationProtection = new ConfigurationProtectionFactory()
        }));
        _auth.Endpoint.DeviceName = $"{Environment.MachineName}_{nameof(VaultCommander)}";
    }

    public void Dispose() => _auth.Dispose();

    public Task<ItemTemplate?> GetItem(Guid guid)
    {
        throw new NotImplementedException();
    }

    public Task<StatusDto?> GetStatus()
    {
        var status = _auth.IsAuthenticated() ? Status.Unlocked :
            (string.IsNullOrEmpty(_auth.Storage.LastLogin) ? Status.Unauthenticated : Status.Locked);
        return Task.FromResult<StatusDto?>(new(_auth.Storage.LastServer, null, _auth.Storage.LastLogin, null, status));
    }

    public Task<string?> GetTotp(Guid guid)
    {
        throw new NotImplementedException();
    }

    public Task<StatusDto?> Initialize()
    {
        return GetStatus();
    }

    public async Task<StatusDto?> Login()
    {
        _auth.Endpoint.DeviceName = $"{Environment.MachineName}_{nameof(VaultCommander)}";
        var (user, pw) = PasswordDialog.Show(Application.Current.MainWindow, _auth.Storage.LastLogin);
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

    public Task Sync()
    {
        throw new NotImplementedException();
    }

    public Task<ItemTemplate?> UpdateUris(Guid guid = default)
    {
        throw new NotImplementedException();
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
            // find email device approval channel.
            var emailChannel = channels
                .OfType<IDeviceApprovalPushInfo>()
                .FirstOrDefault(x => x.Channel == DeviceApprovalChannel.Email);
            if (emailChannel is not null)
            {
                // invoke send email action.
                if (ProgressBox is not null)
                {
                    ProgressBox.DetailText = "Email wurde versandt. Auf Genehmigung warten...";
                    ProgressBox.DetailProgress = double.NaN;
                }
                await emailChannel.InvokeDeviceApprovalPushAction();
                return true;
            }
            return false;
        }
        public async Task<bool> WaitForTwoFactorCode(ITwoFactorChannelInfo[] channels, CancellationToken token)
        {
            // find 2FA code channel.
            //var codeChannel = channels
            //    .OfType<ITwoFactorAppCodeInfo>()
            //    .FirstOrDefault();
            //if (codeChannel != null)
            //{
            //    Console.WriteLine("Enter 2FA code: ");
            //    var code = Console.ReadLine();
            //    await codeChannel.InvokeTwoFactorCodeAction(code);
            //    return true;
            //}
            return false;
        }
        public async Task<bool> WaitForUserPassword(IPasswordInfo info, CancellationToken token)
        {
            var (_,pw) = PasswordDialog.Show(Application.Current.MainWindow, info.Username);
            if (pw is null)
                return false;
            await info.InvokePasswordActionDelegate(pw.GetAsClearText());
            return true;
        }
    }
}
