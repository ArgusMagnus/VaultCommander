using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VaultCommander;

[JsonConverter(typeof(Converter))]
sealed class EncryptedString
{
    readonly byte[] _data;

    public EncryptedString(string str)
    {
        _data = ProtectedData.Protect(Encoding.Unicode.GetBytes(str), null, DataProtectionScope.CurrentUser);
    }

    public string GetAsClearText() => Encoding.Unicode.GetString(ProtectedData.Unprotect(_data, null, DataProtectionScope.CurrentUser));

    sealed class Converter : JsonConverter<EncryptedString>
    {
        public override EncryptedString? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.GetString() is string str ? new(str) : null;
        }

        public override void Write(Utf8JsonWriter writer, EncryptedString value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.GetAsClearText());
        }
    }
}
