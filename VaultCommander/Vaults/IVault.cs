using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace VaultCommander.Vaults;

interface IVaultFactory
{
    public IVault CreateVault(string dataDirectoryRoot);

    public static IReadOnlyList<IVault> CreateVaults(string dataDirectoryRoot) => typeof(IVaultFactory).Assembly.DefinedTypes
        .Where(x => !x.IsInterface && !x.IsAbstract && x.ImplementedInterfaces.Contains(typeof(IVaultFactory)))
        .Select(x => ((IVaultFactory)Activator.CreateInstance(x)!).CreateVault(dataDirectoryRoot))
        .ToList();
}

interface IVault
{
    public string VaultName { get; }
    public string UriScheme { get; }
    public string UriFieldName { get; }
    public int? UidLength { get; }

    public Task<StatusDto?> Initialize();
    public Task<StatusDto?> Login();
    public Task<StatusDto?> GetStatus();
    public Task Sync();
    public Task<Record?> UpdateUris(IEnumerable<string> validCommandSchemes, string? uid = null);
    public Task Logout();
    public Task<Record?> GetItem(string uid, bool includeTotp = false);
}
