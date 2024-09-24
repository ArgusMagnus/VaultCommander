using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VaultCommander;

sealed class TempFile : IDisposable
{
    public string FullName { get; }

    public TempFile(string? extension = null)
    {
        if (string.IsNullOrEmpty(extension))
            FullName = Path.GetTempFileName();
        else
            FullName = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + extension);
    }

    public void Dispose()
    {
        try { File.Delete(FullName); } catch { }
    }
}
