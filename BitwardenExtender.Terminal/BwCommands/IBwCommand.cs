namespace VaultCommander.BwCommands;

public interface IBwCommand
{
    public string Name { get; }
    public Type ArgumentsType { get; }
    public Task Execute(object args);
    public bool CanExecute { get; }
    public bool RequireDisconnect { get; }
    public Task<bool> Disconnect();

    public static bool IsInTerminal { get; internal set; }
}