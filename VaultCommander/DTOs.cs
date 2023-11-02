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

enum ItemType
{
    Login = 1,
    SecureNote,
    Card,
    Identity
}

enum FieldType
{
    Text = 0,
    Hidden,
    Boolean,
    Link
}

enum UriMatchType
{
    Domain = 0,
    Host,
    StartsWith,
    Exact,
    Regex,
    Never
}

sealed record Field
{
    public string Name { get; init; } = string.Empty;
    public string? Value { get; init; }
    public FieldType Type { get; init; }
    public string? LinkedId { get; init; }
}

sealed record ItemTemplate
{
    public string Id { get; init; } = null!;
    public Guid? OrganizationId { get; init; }
    public IReadOnlyList<Guid> CollectionIds { get; init; } = Array.Empty<Guid>();
    public Guid? FolderId { get; init; }
    public ItemType Type { get; init; }
    public string? Name { get; init; }
    public string? Notes { get; init; }
    public bool Favorite { get; init; }
    public IList<Field> Fields { get; init; } = new List<Field>();
    public ItemLogin? Login { get; init; }
    public ItemSecureNote? SecureNote { get; init; }
    public ItemCard? Card { get; init; }
    public IReadOnlyDictionary<string, string>? Identity { get; init; }
    public int Reprompt { get; init; }
}

sealed record ItemLogin
{
    public IList<ItemUri> Uris { get; init; } = new List<ItemUri>();
    public string? Username { get; init; }
    public EncryptedString? Password { get; init; }
    public string? Totp { get; init; }
}

sealed record ItemSecureNote
{
    public int Type { get; init; }
}

sealed record ItemCard
{
    public string CardholderName { get; init; } = string.Empty;
    public string Brand { get; init; } = string.Empty;
    public string Number { get; init; } = string.Empty;
    public string ExpMonth { get; init; } = string.Empty;
    public string ExpYear { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
}

sealed record ItemUri
{
    public UriMatchType? Match { get; init; }
    public string? Uri { get; init; }
}

abstract record ObjectResponse<T>
{
    public bool Success { get; init; }
    public DataDto? Data { get; init; }

    public sealed record DataDto
    {
        public string Object { get; init; } = string.Empty;
        public T? Data { get; init; }
    }
}

sealed record GetListItemsDto : ObjectResponse<IReadOnlyList<ItemTemplate>>;
sealed record GetTotpDto : ObjectResponse<string>;

sealed record GetItemDto
{
    public bool Success { get; init; }
    public ItemTemplate Data { get; init; } = new();
}