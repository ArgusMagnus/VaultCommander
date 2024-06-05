using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VaultCommander.Commands;

sealed record ScreenInfo(int Index, bool IsPrimary, int Left, int Top, int Width, int Height);

sealed class RdpCommand : Command<RdpCommand.Arguments>
{
    public sealed record Arguments : IArgumentsHost, IArgumentsUsername, IArgumentsPassword
    {
        public string? Host { get; init; }
        public string? Username { get; init; }
        public string? Password { get; init; }
    }

    public override string Name => "rdp";

    public override bool CanExecute => true;

    public override async Task Execute(Arguments args)
    {
        IReadOnlyList<ScreenInfo> screens = Screen.AllScreens
            .Select(x => new ScreenInfo(int.Parse(Regex.Match(x.DeviceName, @"^\\\\.\\DISPLAY(?<No>\d+)$").Groups["No"].Value) - 1, x.Primary, x.Bounds.Left, x.Bounds.Top, x.Bounds.Width, x.Bounds.Height))
            .ToList();

        if (screens.Count > 1)
        {
            var dlg = new ConfigureRdpDialog(screens);
            if (dlg.ShowDialog() is not true)
                return;
            screens = dlg.SelectedScreens.OrderBy(x => x.IsPrimary ? 0 : 1).ThenBy(x => x.Index).ToList();
        }

        var defaultRdpPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Default.rdp");
        var lines = (File.Exists(defaultRdpPath) ? await File.ReadAllLinesAsync(defaultRdpPath) : Enumerable.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith("full address:s:") && !x.StartsWith("selectedmonitors:s:") && !x.StartsWith("use multimon:i:") && !x.StartsWith("screen mode id:i:"))
            .Append($"full address:s:{args.Host}")
            .Append($"selectedmonitors:s:{string.Join(',', screens.Select(x => x.Index))}")
            .Append($"use multimon:i:{(screens.Count is 0 ? '0' : '1')}")
            .Append("screen mode id:i:2") // enable fullscreen
            .ToList();

        using var rdpPath = new TempFile();
        await File.WriteAllLinesAsync(rdpPath.FullName, lines);

        var hasUsername = !string.IsNullOrWhiteSpace(args.Username);
        var username = args.Username ?? "";
        if (hasUsername)
        {
            username = username.Contains('\\') ? username : $"{args.Host}\\{username}";
            using (var process = Process.Start(new ProcessStartInfo { FileName = "cmdkey", ArgumentList = { $"/generic:{args.Host}", $"/user:{username}", $"/pass:{args.Password}" }, UseShellExecute = false, CreateNoWindow = true }))
                await process!.WaitForExitAsync();
        }
        using (var process = Process.Start(new ProcessStartInfo { FileName = "mstsc", ArgumentList = { rdpPath.FullName } }))
            await process!.WaitForExitAsync();
        if (hasUsername)
        {
            using (var process = Process.Start(new ProcessStartInfo { FileName = "cmdkey", ArgumentList = { $"/delete:{args.Host}" }, UseShellExecute = false, CreateNoWindow = true }))
                await process!.WaitForExitAsync();
        }
    }
}