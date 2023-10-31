using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WinForms = System.Windows.Forms;

namespace VaultCommander.BwCommands;

sealed class BwCommandNetSupport : BwCommand<BwCommandNetSupport.Arguments>
{
    public record Arguments
    {
        public string? Host { get; init; }
        public string? Username { get; init; }
        public string? Password { get; init; }
    }

    readonly string _exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"NetSupport\NetSupport Manager\PCICTLUI.EXE");

    public override string Name => "NetSupport";

    public override bool CanExecute => File.Exists(_exe);

    public override async Task Execute(Arguments args)
    {
        await Task.Delay(0).ConfigureAwait(false);
        char mode;
        switch (QueryBox.Show("NetSupport Verbindungsart?", nameof(VaultCommander), "Control", "Watch", "Share"))
        {
            case 0: mode = 'C'; break;
            case 1: mode = 'W'; break;
            case 2: mode = 'S'; break;
            default: return;
        }

        // https://kb.netsupportsoftware.com/knowledge-base/netsupport-manager-control-command-line-options/
        using var process = Process.Start(new ProcessStartInfo { FileName = _exe, ArgumentList = { "/E", $"/V{mode}", $"/C>{args.Host}" } })!;
        WindowHandle window;
        while (!WindowHandle.FindWindow(x => x.TryGetThreadAndProcessId(out _, out var pid) && pid == process.Id && x.Text is "Sicherheit", out window))
            await Task.Delay(200);
        window.Focus();
        WinForms.SendKeys.SendWait($"{args.Username}{{TAB}}{args.Password}{{ENTER}}");
        await process.WaitForExitAsync();
    }

}
