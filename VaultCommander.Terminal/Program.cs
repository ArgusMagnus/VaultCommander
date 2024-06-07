using VaultCommander.Commands;
using VaultCommander.Terminal;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

try
{
    if (string.Equals(args.LastOrDefault(), "/d", StringComparison.OrdinalIgnoreCase))
        Debugger.Launch();

    switch (args[0])
    {
        default: throw new ArgumentException($"Invalid verb: {args[0]}");

        case nameof(Verbs.Execute):
            {
                var assembly = args[1];
                var typeName = args[2];
                var argsPath = args[3];
                var salt = args[4];

                var buffer = await File.ReadAllBytesAsync(argsPath);
                File.Delete(argsPath);

                var type = Assembly.LoadFrom(assembly).GetType(typeName) ?? throw new InvalidOperationException($"Type '{typeName}' not found.");
                var command = (ICommand)Activator.CreateInstance(type)!;
                var commandArgs = JsonSerializer.Deserialize(Encoding.UTF8.GetString(ProtectedData.Unprotect(buffer, Convert.FromBase64String(salt), DataProtectionScope.CurrentUser)), command.ArgumentsType) ?? throw new FormatException();
                ICommand.IsInTerminal = true;
                await command.Execute(commandArgs);
                break;
            }

        case nameof(Verbs.Install):
            {
                var dest = args.ElementAtOrDefault(1) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), nameof(VaultCommander));
                if (int.TryParse(args.ElementAtOrDefault(2), out var processId))
                {
                    try { await Process.GetProcessById(processId).WaitForExitAsync(); }
                    catch { }
                }
                using (var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "robocopy.exe",
                    ArgumentList = { AppDomain.CurrentDomain.BaseDirectory, dest, "/MIR", "/XD", Constants.DataDirectory }
                })!)
                {
                    await process.WaitForExitAsync();
                    if (process is { ExitCode: >= 8})
                        throw new Exception("Installation failed");
                }
                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(dest, Path.GetFileName(Environment.ProcessPath)!),
                    ArgumentList = { nameof(Verbs.CleanUpdateCache), AppDomain.CurrentDomain.BaseDirectory, $"{Environment.ProcessId}" },
                };
                if (Debugger.IsAttached)
                    startInfo.ArgumentList.Add("/d");
                using (var process = Process.Start(startInfo)) { }
                break;
            }

        case nameof(Verbs.CleanUpdateCache):
            {
                var dir = args[1];
                var processId = int.Parse(args[2]);
                try { await Process.GetProcessById(processId).WaitForExitAsync(); }
                catch { }

                await Task.WhenAll(Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .Select(async x => { await Task.Yield(); File.Delete(x); }));
                Directory.Delete(dir, true);

                var exe = Environment.ProcessPath!.Replace($".{nameof(VaultCommander.Terminal)}.", ".");
                var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), $@"Microsoft\Windows\Start Menu\Programs\{nameof(VaultCommander)}.lnk");
                Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
                //var shell = new IWshRuntimeLibrary.WshShell();
                //var shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(shortcutPath);
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromCLSID(Guid.Parse("72C24DD5-D70A-438B-8A42-98424B88AFB8"))!)!;
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = exe;
                shortcut.Save();

                using var process = Process.Start(exe);

                shortcutPath = Path.Combine(Path.GetDirectoryName(shortcutPath)!, "$BitwardenExtender.lnk");
                if (File.Exists(shortcutPath))
                    File.Delete(shortcutPath);
                shortcutPath = Path.Combine(Path.GetDirectoryName(shortcutPath)!, "BitwardenExtender.lnk");
                if (File.Exists(shortcutPath))
                    File.Delete(shortcutPath);
                break;
            }

    }
}
catch(Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine(ex);
    Console.WriteLine("Press <Enter> to exit.");
    Console.ResetColor();
    Console.ReadLine();
    throw;
}