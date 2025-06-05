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
using System.Xml.Linq;
using WinForms = System.Windows.Forms;

namespace VaultCommander.Commands;

sealed class CiscoAnyConnectCommand : Command<CiscoAnyConnectCommand.Arguments>
{
    public record Arguments : IArgumentsUsername, IArgumentsPassword, IArgumentsTotp
    {
        public string? Server { get; init; }
        public string? Group { get; init; }
        public string? Username { get; init; }
        public string? Password { get; init; }
        public string? Totp { get; init; }

        public PollEmailArguments? Mail { get; init; }
    }

    readonly string _vpncli;
    readonly string _vpnui;
    readonly string _config;

    public override string Name => "anyconnect";

    public override bool CanExecute => File.Exists(_vpncli) && File.Exists(_vpnui);
    protected override bool ExecuteInTerminal(Arguments args) => true;
    public override bool RequireDisconnect => true;

    public CiscoAnyConnectCommand()
    {
        string? dir = null;
        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Cisco\Cisco Secure Client"))
        {
            dir = (string?)key?.GetValue("InstallPathWithSlash");
            if (dir is not null)
            {
                using var subKey = key!.OpenSubKey("UI")!;
                _vpnui = (string)subKey.GetValue("ExePath")!;
                _config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Cisco\Cisco Secure Client\VPN\preferences.xml");
            }
        }

        if (dir is null)
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Cisco\Cisco AnyConnect Secure Mobility Client"))
                dir = (string?)key?.GetValue("InstallPathWithSlash");
        }

        dir ??= "";
        _vpncli = Path.Combine(dir, "vpncli.exe");
        _vpnui ??= Path.Combine(dir, "vpnui.exe");
        _config ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Cisco\Cisco AnyConnect Secure Mobility Client\preferences.xml");
    }

    public override async Task Execute(Arguments args)
    {
        await Task.Delay(0).ConfigureAwait(false);
        var maxEmailAge = DateTimeOffset.Now;
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = _vpncli,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            ArgumentList = {"connect", args.Server ?? throw new ArgumentNullException(nameof(args.Server)), "-s" }
        })!;
        try
        {
            StringBuilder sb = new();
            var group = -1;
            string? errorMsg = null;
            while (process.StandardOutput.Read() is var i && i is not -1)
            {
                var ch = (char)i;
                sb.Append(ch);
                if (ch is '\n')
                {
                    var line = sb.ToString().Trim();
                    var match = Regex.Match(line, @"^(?<Idx>\d+)\)\s+(?<Grp>.+?)$");
                    if (match.Success && string.Equals(args.Group, match.Groups["Grp"].Value, StringComparison.OrdinalIgnoreCase))
                        group = int.Parse(match.Groups["Idx"].Value);
                    var errorMatch = Regex.Match(line, @"^>>\s+error:\s*(?<Msg>.+)");
                    if (!errorMatch.Success)
                        WriteLine(line);
                    else
                    {
                        errorMsg = errorMatch.Groups["Msg"].Value;
                        WriteLine(line, ConsoleColor.Red);
                    }
                    sb.Clear();
                }
                else if (ch is ':')
                {
                    var line = sb.ToString().TrimStart();
                    if (line.StartsWith(">> "))
                        continue;
                    Write(line);
                    var writeNewLine = true;
                    switch (line)
                    {
                        case "accept? [y/n]:":
                        case "Connect Anyway? [y/n]:":
                        case "Change the setting that blocks untrusted connections? [y/n]:":
                        case "Always trust this server and import the certificate? [y/n]:":
                            await process.StandardInput.WriteLineAsync("y"); break;
                        case "Group:":
                            if (group is -1)
                                throw new ArgumentException($"Gruppe '{args.Group}' ist ungültig", nameof(args.Group));
                            await process.StandardInput.WriteLineAsync($"{group}");
                            break;
                        case "Username:": await process.StandardInput.WriteLineAsync(args.Username); break;
                        case "Password:": await process.StandardInput.WriteLineAsync(args.Password); break;
                        case "AnyConnect cannot verify server:": writeNewLine = false; break;
                        case "Answer:":
                        case "Second Password":
                            if (!string.IsNullOrEmpty(args.Totp))
                                await process.StandardInput.WriteLineAsync(args.Totp);
                            else if (args.Mail is not null)
                            {
                                Write(" Email wird abrufen...", ConsoleColor.Yellow);
                                CancellationTokenSource cts = new();
                                cts.CancelAfter(TimeSpan.FromMinutes(2));
                                var result = await Utils.PollEmail(args.Mail, maxEmailAge, cts.Token);
                                const string pattern = @"PASSCODE:\s*(?<T>[a-zA-Z\d]+)";
                                var match = Regex.Match(result.Subject, pattern);
                                if (!match.Success)
                                    match = Regex.Match(result.Body, pattern);
                                if (match.Success)
                                    await process.StandardInput.WriteLineAsync(match.Groups["T"].Value);
                                else
                                    throw new Exception("Token not found");
                            }
                            else
                            {
                                writeNewLine = false;
                                Write(" (Benutzereingabe erwartet): ", ConsoleColor.Green);
                                await process.StandardInput.WriteLineAsync(Console.ReadLine());
                            }
                            break;
                        default:
                            WriteLine($"Unbekannte Eingabe '{line}' erwartet", ConsoleColor.Yellow);
                            break;
                    }
                    sb.Clear();
                    if (writeNewLine)
                        WriteLine();
                }
            }

            var useUI = string.Equals(errorMsg, "The requested authentication type is not supported in AnyConnect CLI.");
            if (useUI)
            {
                XDocument config;
                if (File.Exists(_config))
                {
                    using (var stream = File.OpenRead(_config))
                        config = await XDocument.LoadAsync(stream, LoadOptions.None, default);
                }
                else
                {
                    config = new XDocument(new XElement("AnyConnectPreferences"));
                    Directory.CreateDirectory(Path.GetDirectoryName(_config)!);
                }

                AddOrUpdate(config.Root!, "DefaultUser", args.Username ?? string.Empty);
                AddOrUpdate(config.Root!, "DefaultHostName", args.Server ?? string.Empty);
                AddOrUpdate(config.Root!, "DefaultHostAddress", args.Server ?? string.Empty);
                AddOrUpdate(config.Root!, "DefaultGroup", args.Group ?? string.Empty);
                using (var stream = File.Open(_config, FileMode.Create, FileAccess.Write))
                    await config.SaveAsync(stream, SaveOptions.None, default);

                static void AddOrUpdate(XElement root, string name, string value)
                {
                    var element = root.Element(name);
                    if (element is null)
                    {
                        element = new XElement(name);
                        root.Add(element);
                    }
                    element.Value = value;
                }
            }

            using var vpnui = Process.Start(_vpnui);

            if (useUI)
            {
                while (vpnui.MainWindowHandle is 0)
                    await Task.Delay(200);
                WindowHandle window = new(vpnui.MainWindowHandle);
                window.Focus();
                Utils.SendKeysLiteral("{ENTER}");
                while ((window = WindowHandle.FindWindow(null, "Cisco AnyConnect Login")) == WindowHandle.Null)
                    await Task.Delay(200);
                await Task.Delay(5000);
                window.Focus();
                Utils.SendKeys($"{args.Username}{{ENTER}}");
                await Task.Delay(2000);
                window.Focus();
                Utils.SendKeys($"{args.Password}{{ENTER}}");
            }
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
        }
    }

    public override async Task<bool> Disconnect()
    {
        if (File.Exists(_vpnui))
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_vpnui)))
                process.Kill();
        }
        if (File.Exists(_vpncli))
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_vpncli)))
                process.Kill();

            {
                using var process = Process.Start(new ProcessStartInfo { FileName = _vpncli, ArgumentList = { "disconnect" }, CreateNoWindow = true });
                await process!.WaitForExitAsync();
            }
        }
        return true;
    }
}
