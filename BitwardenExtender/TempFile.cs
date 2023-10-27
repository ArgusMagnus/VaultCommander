using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BitwardenExtender;

sealed class TempFile : IDisposable
{
    public string FullName { get; }

    public TempFile()
    {
        FullName = Path.GetTempFileName();
    }

    public void Dispose()
    {
        try { File.Delete(FullName); } catch { }
    }
}
