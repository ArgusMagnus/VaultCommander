using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BitwardenExtender.BwCommands;

sealed class BwCommandStartProcess : BwCommand<BwCommandStartProcess.Arguments>
{
    public record Arguments
    {
        public string? FileName { get => _startInfo.FileName; set => _startInfo.FileName = value; }
        public Collection<string> ArgumentList => _startInfo.ArgumentList;
        public string? WorkingDirectory { get => _startInfo.WorkingDirectory; set => _startInfo.WorkingDirectory = value; }
        public bool UseShellExecute { get => _startInfo.UseShellExecute; set => _startInfo.UseShellExecute = value; }
        public string? Verb { get => _startInfo.Verb; set => _startInfo.Verb = value; }
        public bool CreateNoWindow { get => _startInfo.CreateNoWindow; set => _startInfo.CreateNoWindow = value; }
        public ProcessWindowStyle WindowStyle { get => _startInfo.WindowStyle; set => _startInfo.WindowStyle = value; }
        public IDictionary<string, string?> Environment => _startInfo.Environment;
        public ImpersonationArguments Impersonation { get; }
        public bool WaitForExit { get; init; }


        readonly ProcessStartInfo _startInfo = new();
        public Arguments() => Impersonation = new(_startInfo);
        public ProcessStartInfo GetStartInfo() => _startInfo;

        public record ImpersonationArguments
        {
            readonly ProcessStartInfo _startInfo;

            public ImpersonationArguments(ProcessStartInfo startInfo) => _startInfo = startInfo;

            public string? Domain { get => _startInfo.Domain; set => _startInfo.Domain = value; }
            public string? Username { get => _startInfo.UserName; set => _startInfo.UserName = value; }
            public string? Password { get => _startInfo.PasswordInClearText; set => _startInfo.PasswordInClearText = value; }
            public bool LoadUserProfile { get => _startInfo.LoadUserProfile; set => _startInfo.LoadUserProfile = value; }
        }
    }

    public override string Name => "Start-Process";

    public override bool CanExecute => true;

    public override async Task Execute(Arguments args)
    {
        using var process = Process.Start(args.GetStartInfo());
        if (args.WaitForExit)
            await process!.WaitForExitAsync();
    }

}
