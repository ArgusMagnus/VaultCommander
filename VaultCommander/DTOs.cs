using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VaultCommander;

[JsonConverter(typeof(JsonStringEnumConverter))]
enum Status
{
    Unauthenticated,
    Locked,
    Unlocked
}

sealed record StatusDto(string? ServerUrl, DateTime? LastSync, string? UserEmail, Guid? UserId, Status Status);

sealed record Record(string Id, string? Name, IReadOnlyList<RecordField> Fields);
sealed record RecordField(string Name, string? Value);