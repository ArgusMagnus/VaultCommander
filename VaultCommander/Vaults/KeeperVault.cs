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

sealed class KeeperVault : IVault
{
    // https://keeper-security.github.io/gitbook-keeper-sdk/CSharp/html/R_Project_Documentation.htm

    public string VaultName => "Keeper";

    public string UriScheme => "KeeperCmd";

    public string UriFieldName => nameof(VaultCommander);

    readonly string _dataDirectory;
    readonly IConfigurationStorage _storage;

    KeeperVault(string dataDirectoryRoot)
    {
        _dataDirectory = Path.Combine(dataDirectoryRoot, VaultName);
        Directory.CreateDirectory(_dataDirectory);
        _storage = new JsonConfigurationStorage(new JsonConfigurationCache(new JsonConfigurationFileLoader(Path.Combine(_dataDirectory, "storage.json")))
        {
            ConfigurationProtection = new ConfigurationProtectionFactory()
        });
    }

    public Task<ItemTemplate?> GetItem(Guid guid)
    {
        throw new NotImplementedException();
    }

    public Task<StatusDto?> GetStatus()
    {
        throw new NotImplementedException();
    }

    public Task<string?> GetTotp(Guid guid)
    {
        throw new NotImplementedException();
    }

    public async Task<StatusDto?> Initialize()
    {
        return null;
    }

    public async Task<StatusDto?> Login()
    {
        using Auth auth = new(new AuthUi(), _storage);
        auth.Endpoint.DeviceName = $"{Environment.MachineName}_{nameof(VaultCommander)}";
        var (user, pw) = PasswordDialog.Show(Application.Current.MainWindow, auth.Storage.LastLogin);
        if (pw is not null)
            await auth.Login(user, pw.GetAsClearText());
        if (auth.IsAuthenticated())
            return new StatusDto(Uri.TryCreate(auth.Storage.LastServer, UriKind.Absolute, out var uri) ? uri : null, null, auth.Username, null, Status.Unlocked);
        return null;
    }

    public Task Logout()
    {
        throw new NotImplementedException();
    }

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
        public async Task<bool> WaitForDeviceApproval(IDeviceApprovalChannelInfo[] channels, CancellationToken token)
        {
            // find email device approval channel.
            var emailChannel = channels
                .OfType<IDeviceApprovalPushInfo>()
                .FirstOrDefault(x => x.Channel == DeviceApprovalChannel.Email);
            if (emailChannel is not null)
            {
                // invoke send email action.
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
            await info.InvokePasswordActionDelegate(pw.GetAsClearText());
            return true;
        }
    }
}
