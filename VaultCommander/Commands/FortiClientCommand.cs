using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace VaultCommander.Commands;

sealed class FortiClientCommand : Command<FortiClientCommand.Arguments>
{
    public record Arguments : IArgumentsUsername, IArgumentsPassword, IArgumentsTotp
    {
        public string? VpnName { get; init; }
        public string? Username { get; init; }
        public string? Password { get; init; }
        public string? Totp { get; init; }
        public PollEmailArguments? Mail { get; init; }
    }

    readonly string _exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Fortinet\FortiClient\FortiClient.exe");

    public override string Name => "forticlient";
    public override bool CanExecute => true;// File.Exists(_exe);
    public override bool RequireDisconnect => true;

    public override async Task Execute(Arguments args)
    {
        await Task.Delay(0).ConfigureAwait(false);
        //using (var regKey = Registry.CurrentUser.CreateSubKey(@"Software\Fortinet\FortiClient\FA_VPN", true))
        //    regKey.SetValue("connection", args.VpnName ?? string.Empty);

        //using var process = Process.Start(new ProcessStartInfo { FileName = _exe, WorkingDirectory = Path.GetDirectoryName(_exe) })!;
        //await Utils.WaitForProcessReady(process);
        //WindowHandle windowHandle = new(process.MainWindowHandle);
        //windowHandle.Focus();
        //var maxEmailAge = DateTimeOffset.Now;
        Utils.SendKeys($"{{TAB}}{{TAB}}{args.Username}{{TAB}}{args.Password}{{ENTER}}");

        //var totp = args.Totp;
        //if (args.Mail is not null)
        //{
        //    CancellationTokenSource cts = new();
        //    cts.CancelAfter(TimeSpan.FromMinutes(2));
        //    var result = await Utils.PollEmail(args.Mail, maxEmailAge, cts.Token);
        //    totp = Regex.Match(result.Subject, @"\d+").Value;
        //}

        //if (!string.IsNullOrEmpty(totp))
        //{
        //    Clipboard.SetText(totp);
        //    MessageBox.Show("Token wurde in die Zwischenablage kopiert", nameof(VaultCommander), MessageBoxButton.OK, MessageBoxImage.Information);
        //}
    }

    public override async Task<bool> Disconnect()
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_exe)))
        {
            if (process.MainWindowHandle is 0)
                process.Kill();
            else if (process.CloseMainWindow())
                await process.WaitForExitAsync();
        }
        if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_exe)).Length is 0)
            return true;
        MessageBox.Show("FortiClient läuft bereits. Bitte beenden und erneut versuchen.", nameof(VaultCommander), MessageBoxButton.OK, MessageBoxImage.Error);
        return false;
    }
}
