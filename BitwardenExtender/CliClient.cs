using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BitwardenExtender;

sealed class CliClient : IDisposable
{
    const string EnvAppDataDir = "BITWARDENCLI_APPDATA_DIR";
    readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    readonly ApiClient _apiClient = new();
    readonly TaskCompletionSource<ApiClient> _apiTcs = new();
    Process? _apiServerProcess;
    int _apiServerPort;

    public string ExePath { get; }
    public string AppDataDir { get; }

    public CliClient(string exePath)
    {
        ExePath = exePath;
        //AppDataDir = Enumerable.Empty<string>()
        //    .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Packages\8bitSolutionsLLC.bitwardendesktop_h4e712dmw3xyy\LocalCache\Roaming\Bitwarden"))
        //    .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Bitwarden"))
        //    .FirstOrDefault(x => Directory.Exists(x)) ?? string.Empty;
        AppDataDir = Path.Combine(Path.GetDirectoryName(exePath)!, "Cache");
        Directory.CreateDirectory(AppDataDir);
    }

    async Task<string?> InvokeCli(bool showTerminal, params string[] arguments)
    {
        if (!File.Exists(ExePath))
            return null;
        var startInfo = new ProcessStartInfo(ExePath) { UseShellExecute = false, CreateNoWindow = !showTerminal, RedirectStandardOutput = true };
        startInfo.Environment.Add(EnvAppDataDir, AppDataDir);
        foreach (var arg in arguments)
            startInfo.ArgumentList.Add(arg);
        using var process = Process.Start(startInfo);
        return (await process!.StandardOutput.ReadToEndAsync()).Trim();
    }

    Task<string?> InvokeCli(params string[] arguments) => InvokeCli(false, arguments);

    async Task<T?> InvokeCli<T>(bool showTerminal, params string[] arguments)
    {
        var json = await InvokeCli(showTerminal, arguments);
        if (json is null)
            return default;
        return JsonSerializer.Deserialize<T>(json, _jsonOptions);
    }

    Task<T?> InvokeCli<T>(params string[] arguments) => InvokeCli<T>(false, arguments);

    public async Task<StatusDto?> GetStatus()
    {
        var status = await InvokeCli<StatusDto>("status");
        if (status?.LastSync is not null)
            status = status with { LastSync = status.LastSync.Value.ToLocalTime() };
        return status;
    }

    public Task<string?> Login(string userEmail, EncryptedString password) => InvokeCli(true, "login", userEmail, password.GetAsClearText(), "--raw");
    public Task Logout() => InvokeCli("logout");

    public Task<ApiClient> GetApiClient() => _apiTcs.Task;

    public async Task<bool> StartApiServer(string? sessionToken)
    {
        if (TryAttachToApiServer())
            return true;

        var startInfo = new ProcessStartInfo(ExePath)
        {
            FileName = ExePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            ArgumentList = { "serve", "--hostname", $"{IPAddress.Loopback}", "--port", "0", "--response" }
        };
        startInfo.Environment.Add(EnvAppDataDir, AppDataDir);

        if (!string.IsNullOrEmpty(sessionToken))
        {
            startInfo.ArgumentList.Add("--session");
            startInfo.ArgumentList.Add(sessionToken);
        }

        _apiServerProcess = Process.Start(startInfo) ?? throw new InvalidOperationException();
        while (!FindServerProcess(_apiServerProcess.Id, null, out var _, out _apiServerPort))
        {
            var cts = new CancellationTokenSource();
            cts.CancelAfter(200);
            try { await _apiServerProcess.WaitForExitAsync(cts.Token); }
            catch (TaskCanceledException) { }
            if (_apiServerProcess.HasExited)
                return false;
        }

        _apiClient.SetPort(_apiServerPort);
        _apiTcs.TrySetResult(_apiClient);
        return true;
    }

    public bool TryAttachToApiServer()
    {
        if (_apiServerProcess?.HasExited is false && _apiServerPort is not 0)
            return true;

        if (FindServerProcess(0, ExePath, out _apiServerProcess, out _apiServerPort))
        {
            _apiClient.SetPort(_apiServerPort);
            _apiTcs.TrySetResult(_apiClient);
            return true;
        }
        return false;
    }

    public void KillServer()
    {
        if (_apiServerProcess?.HasExited is false)
            _apiServerProcess.Kill();
    }

    static bool FindServerProcess(int processId, string? exePath, out Process? process, out int port)
    {
        [DllImport("Iphlpapi")]
        static extern uint GetExtendedTcpTable(ref byte buffer, ref uint dwSize, bool bOrder, uint ulAf, TCP_TABLE_CLASS tableClass, uint reserved = 0);

        const uint NO_ERROR = 0;
        const uint ERROR_INSUFFICIENT_BUFFER = 122;
        const uint AF_INET = 2;
        Memory<byte> buffer = new byte[1];
        uint dwSize = (uint)buffer.Length;
        var error = GetExtendedTcpTable(ref buffer.Span[0], ref dwSize, false, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_LISTENER);
        if (error is ERROR_INSUFFICIENT_BUFFER)
        {
            buffer = new byte[dwSize];
            error = GetExtendedTcpTable(ref buffer.Span[0], ref dwSize, false, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_LISTENER);
        }
        if (error is not NO_ERROR)
            throw new Win32Exception();

        var span = buffer.Span;

        ref var table = ref MemoryMarshal.AsRef<MIB_TCPTABLE_OWNER_PID>(span);
        for (int i = 0; i < table.dwNumEntries; i++)
        {
            ref var row = ref MemoryMarshal.AsRef<MIB_TCPROW_OWNER_PID>(span.Slice(Unsafe.SizeOf<MIB_TCPTABLE_OWNER_PID>() + i * Unsafe.SizeOf<MIB_TCPROW_OWNER_PID>()));
            if ((row.bLocalAddr1, row.bLocalAddr2, row.bLocalAddr3, row.bLocalAddr4) != (127, 0, 0, 1))
                continue;
            if (processId is not 0)
            {
                if (unchecked((int)row.dwOwningPid) == processId)
                {
                    process = null;
                    port = unchecked((ushort)IPAddress.NetworkToHostOrder((short)(ushort)row.dwLocalPort));
                    return true;
                }
            }
            else
            {
                process = Process.GetProcessById(unchecked((int)row.dwOwningPid));
                try
                {
                    if (string.Equals(exePath, process.MainModule?.FileName))
                    {
                        port = unchecked((ushort)IPAddress.NetworkToHostOrder((short)(ushort)row.dwLocalPort));
                        return true;
                    }
                }
                catch { }
            }
        }
        process = null;
        port = 0;
        return false;
    }

    public async Task<Uri?> GetUpdateUri()
    {
        var uri = await InvokeCli("update", "--raw") ?? "https://vault.bitwarden.com/download/?app=cli&platform=windows";
        return string.IsNullOrEmpty(uri) ? null : new(uri);
    }

    public void Dispose()
    {
        if (_apiServerProcess is not null)
        {
            //_apiServerProcess.Kill();
            _apiServerProcess.Dispose();
            _apiServerProcess = null;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    unsafe struct MIB_TCPTABLE_OWNER_PID
    {
        public uint dwNumEntries { get; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    readonly struct MIB_TCPROW_OWNER_PID
    {
        public uint dwState { get; }
        public byte bLocalAddr1 { get; }
        public byte bLocalAddr2 { get; }
        public byte bLocalAddr3 { get; }
        public byte bLocalAddr4 { get; }
        public uint dwLocalPort { get; }
        public byte bRemoteAddr1 { get; }
        public byte bRemoteAddr2 { get; }
        public byte bRemoteAddr3 { get; }
        public byte bRemoteAddr4 { get; }
        public uint dwRemotePort { get; }
        public uint dwOwningPid { get; }
    }

    enum TCP_TABLE_CLASS
    {
        TCP_TABLE_BASIC_LISTENER,
        TCP_TABLE_BASIC_CONNECTIONS,
        TCP_TABLE_BASIC_ALL,
        TCP_TABLE_OWNER_PID_LISTENER,
        TCP_TABLE_OWNER_PID_CONNECTIONS,
        TCP_TABLE_OWNER_PID_ALL,
        TCP_TABLE_OWNER_MODULE_LISTENER,
        TCP_TABLE_OWNER_MODULE_CONNECTIONS,
        TCP_TABLE_OWNER_MODULE_ALL
    }
}
