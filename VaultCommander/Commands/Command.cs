using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace VaultCommander.Commands;

abstract class Command<T> : ICommand
    where T : new()
{
    public abstract string Name { get; }
    public abstract bool CanExecute { get; }
    public virtual bool RequireDisconnect => false;

    public Type ArgumentsType => typeof(T);
    protected virtual bool ExecuteInTerminal(T args) => false;

    async Task ICommand.Execute(object _args)
    {
        var args = (T)_args;
        var executeInTerminal = ExecuteInTerminal(args);
        if (executeInTerminal && !ICommand.IsInTerminal)
        {
            var salt = RandomNumberGenerator.GetBytes(32);
            var json = ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(args)), salt, DataProtectionScope.CurrentUser);
            using TempFile tmpFile = new();
            await File.WriteAllBytesAsync(tmpFile.FullName, json);
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.ChangeExtension(typeof(ICommand).Assembly.Location, ".exe"),
                UseShellExecute = false,
                ArgumentList = { nameof(Terminal.Verbs.Execute), typeof(Command<T>).Assembly.Location, GetType().FullName!, tmpFile.FullName, Convert.ToBase64String(salt) }
            };
            if (Debugger.IsAttached)
                startInfo.ArgumentList.Add("/d");
            using var process = Process.Start(startInfo);
            await process!.WaitForExitAsync();
        }
        else
        {
            try { await Execute(args); }
            catch (Exception ex) when (!Debugger.IsAttached)
            {
                if (!ICommand.IsInTerminal)
                    MessageBox.Show($"Unerwarteter Fehler ({ex.GetType().Name}): {ex.Message}", nameof(VaultCommander), MessageBoxButton.OK, MessageBoxImage.Error);
                else
                {
                    WriteLine(ex.ToString(), ConsoleColor.Red);
                    Console.ReadLine();
                }
            }
        }
    }

    public abstract Task Execute(T args);

    public virtual Task<bool> Disconnect() => Task.FromResult(true);

    const ConsoleColor DefaultConsoleColor = (ConsoleColor)(-1);

    protected void Write(string text, ConsoleColor color = DefaultConsoleColor)
    {
        if (color is not DefaultConsoleColor)
            Console.ForegroundColor = color;
        Debug.Write(text);
        Console.Write(text);
        if (color is not DefaultConsoleColor)
            Console.ResetColor();
    }

    protected void WriteLine(string text, ConsoleColor color = DefaultConsoleColor)
    {
        if (color is not DefaultConsoleColor)
            Console.ForegroundColor = color;
        Debug.WriteLine(text);
        Console.WriteLine(text);
        if (color is not DefaultConsoleColor)
            Console.ResetColor();
    }

    protected void WriteLine()
    {
        Debug.WriteLine(string.Empty);
        Console.WriteLine();
    }
}
