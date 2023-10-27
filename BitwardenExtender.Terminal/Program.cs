using BitwardenExtender.BwCommands;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var assembly = args[0];
var typeName = args[1];
var argsPath = args[2];
if (args.Length > 3 && string.Equals(args[3], "/d", StringComparison.OrdinalIgnoreCase))
    Debugger.Launch();

var buffer = await File.ReadAllBytesAsync(argsPath);
File.Delete(argsPath);

var type = Assembly.LoadFrom(assembly).GetType(typeName) ?? throw new InvalidOperationException($"Type '{typeName}' not found.");
var command = (IBwCommand)Activator.CreateInstance(type)!;
var commandArgs = JsonSerializer.Deserialize(Encoding.UTF8.GetString(ProtectedData.Unprotect(buffer, null, DataProtectionScope.CurrentUser)), command.ArgumentsType) ?? throw new FormatException();
IBwCommand.IsInTerminal = true;
await command.Execute(commandArgs);