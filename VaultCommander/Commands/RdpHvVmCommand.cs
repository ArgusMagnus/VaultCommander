﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VaultCommander.Commands;

sealed class RdpHvVmCommand : Command<RdpHvVmCommand.Arguments>
{
    public sealed record Arguments : IArgumentsHost, IArgumentsUsername, IArgumentsPassword
    {
        public string? Host { get; init; }
        public string? VmId { get; init; }
        public string? Username { get; init; }
        public string? Password { get; init; }
    }

    public override string Name => "rdp-hvvm";

    public override bool CanExecute => true;

    public override async Task Execute(Arguments args)
    {
        using var rdpPath = new TempFile();
        await File.WriteAllLinesAsync(rdpPath.FullName, new[]
        {
            $"full address:s:{args.Host}",
            $"pcb:s:{args.VmId}",
            "server port:i:2179",
            "negotiate security layer:i:0"
        });

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
