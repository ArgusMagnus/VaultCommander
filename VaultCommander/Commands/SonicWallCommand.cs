using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using WinForms = System.Windows.Forms;

namespace VaultCommander.Commands;

sealed class SonicWallCommand : Command<SonicWallCommand.Arguments>
{
    public record Arguments
    {
        public string? Server { get; init; }
        public string? Group { get; init; }
        public string? Username { get; init; }
        public string? Password { get; init; }
        public string? Totp { get; init; }
    }

    readonly string _profiles;
    readonly string _config;
    readonly string _exe;

    public override string Name => "sonicwall";

    public override bool CanExecute => File.Exists(_profiles) && File.Exists(_config) && File.Exists(_exe);
    public override bool RequireDisconnect => true;

    public SonicWallCommand()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SonicWall");
        _profiles = Path.Combine(dir, @"SnwlConnect\Documents\profiles.xml");
        if (!Directory.Exists(dir))
            _config = string.Empty;
        else
        {
            _config = Directory.EnumerateDirectories(dir, "SnwlConnect*")
                .SelectMany(x => Directory.EnumerateFiles(x, "user.config", SearchOption.AllDirectories))
                .FirstOrDefault() ?? string.Empty;
        }
        _exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"SonicWall\Modern Connect Tunnel\SnwlConnect.exe");
    }

    public override async Task Execute(Arguments args)
    {
        XDocument profiles;
        using (var stream = File.OpenRead(_profiles))
            profiles = await XDocument.LoadAsync(stream, LoadOptions.None, default).ConfigureAwait(false);

        var ns = profiles.Root!.GetDefaultNamespace();
        var id = profiles.Root!.Elements(ns.GetName("VpnProfile"))
            .Where(x
            => string.Equals(x.Element(ns.GetName("HostAddress"))?.Value, args.Server, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Element(ns.GetName("LoginGroup"))?.Value, args.Group, StringComparison.OrdinalIgnoreCase))
            .Select(x => int.Parse(x.Element(ns.GetName("ID"))!.Value))
            .FirstOrDefault();

        if (id == default)
        {
            id = profiles.Root!.Elements(ns.GetName("VpnProfile")).Max(x => int.Parse(x.Element(ns.GetName("ID"))!.Value)) + 1;
            var profile = new XElement(ns.GetName("VpnProfile"), new XElement[]
            {
                new(ns.GetName("ID"), id),
                new(ns.GetName("AppType"), 0),
                new(ns.GetName("ConfigType"), 0),
                new(ns.GetName("Name"), args.Server),
                new(ns.GetName("HostAddress"), args.Server),
                new(ns.GetName("AutoCredType"), 0),
                new(ns.GetName("VpnProtocolType"), 0),
                new(ns.GetName("ProfileId"), Guid.NewGuid())
            });
            if (!string.IsNullOrEmpty(args.Group))
                profile.Add(new XElement(ns.GetName("LoginGroup"), args.Group));
            profiles.Root!.Add(profile);
            using (var stream = File.Open(_profiles, FileMode.Create, FileAccess.Write))
                await profiles.SaveAsync(stream, SaveOptions.None, default);
        }

        XDocument config;
        using (var stream = File.OpenRead(_config))
            config = await XDocument.LoadAsync(stream, LoadOptions.None, default);

        var element = config.Root!.Descendants("setting").First(x => x.Attribute("name")?.Value == "VpnProfileId").Element("value")!;
        element.Value = $"{id}";
        using (var stream = File.Open(_config, FileMode.Create, FileAccess.Write))
            await config.SaveAsync(stream, SaveOptions.None, default);

        using var process = Process.Start(_exe);
        process.WaitForInputIdle();
        var window = new WindowHandle(process.MainWindowHandle);
        window.Focus();
        WinForms.SendKeys.SendWait("{ENTER}");
    }

    public override Task<bool> Disconnect()
    {
        foreach (var process in Process.GetProcessesByName("SnwlConnect"))
            process.CloseMainWindow();
        return Task.FromResult(true);
    }
}
