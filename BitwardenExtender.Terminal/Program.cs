using BitwardenExtender.BwCommands;
using BitwardenExtender.Terminal;
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

                var buffer = await File.ReadAllBytesAsync(argsPath);
                File.Delete(argsPath);

                var type = Assembly.LoadFrom(assembly).GetType(typeName) ?? throw new InvalidOperationException($"Type '{typeName}' not found.");
                var command = (IBwCommand)Activator.CreateInstance(type)!;
                var commandArgs = JsonSerializer.Deserialize(Encoding.UTF8.GetString(ProtectedData.Unprotect(buffer, null, DataProtectionScope.CurrentUser)), command.ArgumentsType) ?? throw new FormatException();
                IBwCommand.IsInTerminal = true;
                await command.Execute(commandArgs);
                break;
            }

        case nameof(Verbs.Install):
            {
                var dest = args.ElementAtOrDefault(1) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), nameof(BitwardenExtender));
                if (int.TryParse(args.ElementAtOrDefault(2), out var processId))
                {
                    try { await Process.GetProcessById(processId).WaitForExitAsync(); }
                    catch { }
                }
                using (var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "robocopy.exe",
                    ArgumentList = { AppDomain.CurrentDomain.BaseDirectory, dest, "/MIR", "/XD", Constants.CliDirectory },
                    CreateNoWindow = true
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
                    CreateNoWindow = true
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
                using var process = Process.Start(Environment.ProcessPath!.Replace($".{nameof(BitwardenExtender.Terminal)}.", "."));

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