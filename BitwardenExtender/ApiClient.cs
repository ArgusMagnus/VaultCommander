using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BitwardenExtender;

sealed class ApiClient
{
    readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    readonly HttpClient _client = new();

    public void SetPort(int port) => _client.BaseAddress = new($"http://{IPAddress.Loopback}:{port}");

    public async Task<StatusDto?> GetStatus()
    {
        var status = (await _client.GetFromJsonAsync<StatusResponse>("/status", _jsonOptions))?.Data.Template;
        if (status?.LastSync is not null)
            status = status with { LastSync = status.LastSync.Value.ToLocalTime() };
        return status;
    }

    public async Task Lock() => await EnsureSuccessStatusCode(await _client.PostAsync("/lock", null));
    public async Task<bool> Unlock(EncryptedString password) => (await _client.PostAsJsonAsync("/unlock", new Dictionary<string, EncryptedString> { ["password"] = password }, _jsonOptions)).IsSuccessStatusCode;
    public async Task Sync() => await EnsureSuccessStatusCode(await _client.PostAsync("/sync", null));
    public Task<GetItemDto?> GetItem(Guid guid) => _client.GetFromJsonAsync<GetItemDto>($"/object/item/{guid}", _jsonOptions);
    public Task<GetListItemsDto?> GetItems() => _client.GetFromJsonAsync<GetListItemsDto>("/list/object/items", _jsonOptions);
    public async Task PutItem(ItemTemplate item) => await EnsureSuccessStatusCode(await _client.PutAsJsonAsync($"object/item/{item.Id}", item, _jsonOptions));
    public Task<GetTotpDto?> GetTotp(Guid guid) => _client.GetFromJsonAsync<GetTotpDto>($"/object/totp/{guid}", _jsonOptions);

    static async Task EnsureSuccessStatusCode(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var str = await response.Content.ReadAsStringAsync();
            Exception? inner = null;
            try { response.EnsureSuccessStatusCode(); }
            catch (Exception ex) { inner = ex; }
            throw new HttpRequestException(str, inner, response.StatusCode);
        }
    }

    sealed record StatusResponse
    {
        public bool Success { get; init; }
        public DataRecord Data { get; init; } = new();

        public sealed record DataRecord
        {
            public string? Object { get; init; }
            public StatusDto? Template { get; init; }
        }
    }
}


[JsonConverter(typeof(JsonStringEnumConverter))]
enum Status
{
    Unauthenticated,
    Locked,
    Unlocked
}

sealed record StatusDto(Uri? ServerUrl, DateTime? LastSync, string? UserEmail, Guid? UserId, Status Status);

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
    public Guid Id { get; init; }
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