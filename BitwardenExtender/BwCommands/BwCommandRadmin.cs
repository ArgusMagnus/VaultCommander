using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WinForms = System.Windows.Forms;

namespace VaultCommander.BwCommands;

sealed class BwCommandRadmin : BwCommand<BwCommandRadmin.Arguments>
{
    public record Arguments
    {
        public string? Host { get; init; }
        public string? Username { get; init; }
        public string? Password { get; init; }
        public Arguments? Gateway { get; init; }
    }

    readonly string _exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Radmin Viewer 3\Radmin.exe");

    public override string Name => "radmin";

    public override bool CanExecute => File.Exists(_exe);

    public override async Task Execute(Arguments args)
    {
        await Task.Delay(0).ConfigureAwait(false);

        var host = args.Host?.Trim() ?? string.Empty;
        var gateway = args.Gateway?.Host?.Trim() ?? string.Empty;
        var startInfo = new ProcessStartInfo
        {
            FileName = _exe,
            WorkingDirectory = Path.GetDirectoryName(_exe),
            ArgumentList = { $"/connect:{host}" }
        };
        if (args.Gateway is not null)
            startInfo.ArgumentList.Add($"/through:{gateway}");

        switch (QueryBox.Show("Radmin Verbindungsart?", nameof(VaultCommander), "Vollsteuerung", "Nur Ansicht", "Dateiübertragung", "Terminal"))
        {
            case 0: break;
            case 1: startInfo.ArgumentList.Add("/noinput"); break;
            case 2: startInfo.ArgumentList.Add("/file"); break;
            case 3: startInfo.ArgumentList.Add("/telnet"); break;
            default: return;
        }

        using var process = Process.Start(startInfo)!;

        var pattern = args.Gateway is null ? $@"^(?:Windows|Radmin)(?: security|-Sicherheit):\s*(?<Host>{Regex.Escape(host)})\s*$" : $@"^(?:Windows|Radmin)(?: security|-Sicherheit):\s*(?:(?<Host>{Regex.Escape(host)})|(?<GW>{Regex.Escape(gateway)}))\s*$";
        bool gatewaySent = false;

        while (!process.HasExited && process.MainWindowHandle is 0)
        {
            WindowHandle window;
            Match? match = null;
            if (WindowHandle.FindWindow(x => x.TryGetThreadAndProcessId(out _, out var pid) && pid == process.Id && (match = Regex.Match(x.Text ?? string.Empty, pattern)).Success, out window))
            {
                if (!gatewaySent && match!.Groups["GW"].Success)
                {
                    window.Focus();
                    WinForms.SendKeys.SendWait($"{args.Gateway!.Username}{{TAB}}{args.Gateway.Password}{{TAB}}{{ENTER}}");
                    gatewaySent = true;
                }
                else if (match!.Groups["Host"].Success)
                {
                    window.Focus();
                    WinForms.SendKeys.SendWait($"{args.Username}{{TAB}}{args.Password}{{TAB}}{{ENTER}}");
                    break;
                }
            }
            await Task.Delay(200);
        }

        await process.WaitForExitAsync();
    }
}
