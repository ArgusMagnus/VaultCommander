using System;
using System.IO;
using System.Security.Policy;
using System.Threading.Tasks;
using System.Windows;

namespace BitwardenExtender.Vaults;

sealed partial class BitwardenVault : IVault, IDisposable
{
    readonly CliClient _cli;
    EncryptedString? _savedPw;

    public string VaultName => "Bitwarden";
    public string UriScheme => "bw";

    public string UriFieldName => nameof(BitwardenExtender);

    BitwardenVault(string dataDirectoryRoot) => _cli = new(Path.Combine(dataDirectoryRoot, UriScheme, "bw.exe"));

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
        progressBox.DetailText = "Bitwarden CLI herunterladen...";

        void OnProgress(double progress)
        {
            progressBox.DetailProgress = progress * 2;
            if (progress is 0.5)
            {
                progressBox.StepText = "1 / 1";
                progressBox.StepProgress = 0.5;
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

    public void Dispose()
    {
        _cli.Dispose();
    }

    public Task Sync()
    {
        throw new NotImplementedException();
    }

    public Task UpdateUris()
    {
        throw new NotImplementedException();
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

    sealed class Factory : IVaultFactory
    {
        public IVault CreateVault(string dataDirectoryRoot) => new BitwardenVault(dataDirectoryRoot);
    }
}