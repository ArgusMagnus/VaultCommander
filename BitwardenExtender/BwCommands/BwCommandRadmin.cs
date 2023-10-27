using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WinForms = System.Windows.Forms;

namespace BitwardenExtender.BwCommands;

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
        var startInfo = new ProcessStartInfo
        {
            FileName = _exe,
            WorkingDirectory = Path.GetDirectoryName(_exe),
            ArgumentList = { $"/connect:{args.Host?.Trim()}" }
        };
        if (args.Gateway is not null)
            startInfo.ArgumentList.Add($"/through:{args.Gateway.Host?.Trim()}");

        switch(QueryBox.Show("Radmin Verbindungsart?", nameof(BitwardenExtender), "Vollsteuerung", "Nur Ansicht", "Dateiübertragung", "Terminal"))
        {
            case 0: break;
            case 1: startInfo.ArgumentList.Add("/noinput");break;
            case 2: startInfo.ArgumentList.Add("/file");break;
            case 3: startInfo.ArgumentList.Add("/telnet"); break;
            default: return;
        }        

        using var process = Process.Start(startInfo)!;

        while (!process.HasExited && process.MainWindowHandle is 0)
        {
            WindowHandle window;
            Match? match = null;
            if (WindowHandle.FindWindow(x => x.TryGetThreadAndProcessId(out _, out var pid) && pid == process.Id && (match = Regex.Match(x.Text ?? string.Empty, @"^Windows security:\s+(?<Host>.+)\s*$")).Success, out window))
            {
                WindowHandle tmp = new(process.MainWindowHandle);
                var host = match!.Groups["Host"].Value;
                if (args.Gateway is not null && string.Equals(host, args.Gateway?.Host))
                {
                    window.Focus();
                    WinForms.SendKeys.SendWait($"{args.Gateway.Username}{{TAB}}{args.Gateway.Password}{{TAB}}{{ENTER}}");
                }
                else if (string.Equals(host, args.Host))
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
