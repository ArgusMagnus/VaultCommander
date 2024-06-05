using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;

namespace VaultCommander.Commands;

sealed class ECatcherCommand : Command<ECatcherCommand.Arguments>
{
    public sealed record Arguments : IArgumentsUsername, IArgumentsPassword
    {
        public string? Account { get; init; }
        public string? Username { get; init; }
        public string? Password { get; init; }
    }

    readonly string _exe;
    readonly string _cfg;
    readonly string _lock;

    public override string Name => "eCatcher";
    public override bool CanExecute => File.Exists(_exe);
    public override bool RequireDisconnect => true;

    public ECatcherCommand()
    {
        _exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "eCatcher-Talk2M", "eCatcher.exe");
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".talk2M");
        _cfg = Path.Combine(dir, "ecatcherConfig.xml");
        _lock = Path.Combine(dir, "eCatcher.tmp");
    }

    public override async Task Execute(Arguments args)
    {
        XDocument cfg;
        using (var stream = File.OpenRead(_cfg))
            cfg = await XDocument.LoadAsync(stream, LoadOptions.None, default).ConfigureAwait(false);
        var login = cfg.Root!.Element("login")!;
        login.Element("account")!.Value = args.Account ?? string.Empty;
        if (args.Username is not null)
        {
            login.Element("user")!.Value = args.Username ?? string.Empty;
            login.Element("rememberMe")!.Value = "true";
        }
        using (var stream = File.Open(_cfg, FileMode.Create, FileAccess.Write))
            await cfg.SaveAsync(stream, SaveOptions.None, default);

        using (var process = Process.Start(_exe))
            process.WaitForInputIdle();

        if (args.Username is null)
            return;

        using var searcher = new ManagementObjectSearcher("SELECT ProcessId FROM Win32_Process WHERE Name LIKE 'java%.exe' AND CommandLine LIKE '%eCatcher-%.jar%'");
        using var objects = searcher.Get();
        var obj = objects.Cast<ManagementBaseObject>().FirstOrDefault();
        if (obj is null)
            return;

        using (var process = Process.GetProcessById((int)(uint)obj["ProcessId"]))
        {
            await Utils.WaitForProcessReady(process);
            var handle = new WindowHandle(process.MainWindowHandle);
            handle.Focus();
        }
        Utils.SendKeys($"{args.Password}{{ENTER}}");
    }

    public override Task<bool> Disconnect()
    {
        if (!File.Exists(_lock))
            return Task.FromResult(true);

        MessageBox.Show("eCatcher läuft, bitte schliessen und erneut versuchen.", string.Empty, MessageBoxButton.OK, MessageBoxImage.Warning);
        return Task.FromResult(false);
    }
}
