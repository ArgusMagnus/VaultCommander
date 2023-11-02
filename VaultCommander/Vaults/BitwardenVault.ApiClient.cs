using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace VaultCommander.Vaults;

sealed partial class BitwardenVault
{
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
        public Task<GetItemDto?> GetItem(string uid) => _client.GetFromJsonAsync<GetItemDto>($"/object/item/{uid}", _jsonOptions);
        public Task<GetListItemsDto?> GetItems() => _client.GetFromJsonAsync<GetListItemsDto>("/list/object/items", _jsonOptions);
        public async Task PutItem(ItemTemplate item) => await EnsureSuccessStatusCode(await _client.PutAsJsonAsync($"object/item/{item.Id}", item, _jsonOptions));
        public Task<GetTotpDto?> GetTotp(string uid) => _client.GetFromJsonAsync<GetTotpDto>($"/object/totp/{uid}", _jsonOptions);

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
}