using MailKit.Net.Imap;
using MailKit;
using MimeKit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.IO.Compression;
using System.IO;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BitwardenExtender;

sealed record PollEmailArguments
{
    public string? Host { get; init; }
    public int Port { get; init; } = 993;
    public bool UseSsl { get; init; } = true;
    public string? Username { get; init; }
    public string? Password { get; init; }
}

sealed record PollEmailResult(string Subject, string Body);

sealed record ReleaseInfo
{
    [JsonPropertyName("tag_name")]
    public string? Version { get; set; }

    [JsonPropertyName("html_url")]
    public Uri? Url { get; set; }

    [JsonPropertyName("assets")]
    public IReadOnlyList<Asset> Assets { get; set; } = Array.Empty<Asset>();

    public record Asset
    {
        [JsonPropertyName("browser_download_url")]
        public Uri? DownloadUrl { get; set; }
    }
}

static class Utils
{
    public static async Task<PollEmailResult> PollEmail(PollEmailArguments args, DateTimeOffset maxAge, CancellationToken cancellationToken)
    {
        using var client = new ImapClient();
        await client.ConnectAsync(args.Host ?? throw new ArgumentNullException(nameof(args.Host)), args.Port, args.UseSsl, cancellationToken);
        await client.AuthenticateAsync(args.Username ?? throw new ArgumentNullException(nameof(args.Username)), args.Password ?? throw new ArgumentNullException(nameof(args.Password)), cancellationToken);

        UniqueId messageId = default;
        MimeMessage? message = null;
        while (true)
        {
            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            if (inbox.Count > 0)
            {
                var summary = (await inbox.FetchAsync(inbox.Count - 1, -1, MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate | MessageSummaryItems.SaveDate | MessageSummaryItems.Flags, cancellationToken)).FirstOrDefault();
                if (summary is not null && !summary.Flags!.Value.HasFlag(MessageFlags.Seen) && summary.Date >= maxAge)
                {
                    messageId = summary.UniqueId;
                    message = await inbox.GetMessageAsync(messageId, cancellationToken);
                }
            }

            await inbox.CloseAsync(false, cancellationToken);

            if (message is not null)
                break;

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        {
            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
            await inbox.AddFlagsAsync(messageId, MessageFlags.Seen, true, cancellationToken);
            await inbox.CloseAsync(false, cancellationToken);
        }

        await client.DisconnectAsync(true);
        return new(message.Subject, message.TextBody);
    }

    public static async Task WaitForProcessReady(Process process)
    {
        await Task.Delay(0).ConfigureAwait(false);
        process.WaitForInputIdle();
        using var perfCounter = new PerformanceCounter("Process", "% Processor Time", process.ProcessName, true);
        float prevCpu = 0;
        var readyCounter = 0;
        while (readyCounter < 2)
        {
            await Task.Delay(200);
            var cpu = perfCounter.NextValue() / Environment.ProcessorCount;
            if (prevCpu == cpu)
                readyCounter++;
            else
                readyCounter = 0;
            prevCpu = cpu;
            Debug.WriteLine(cpu);
        }
    }

    public static string ConvertToRegexPattern(string searchPattern)
    {
        searchPattern = Regex.Escape(searchPattern);
        searchPattern = searchPattern.Replace("\\*", ".*").Replace("\\?", ".?");
        return $"(?i)^{searchPattern}$";
    }

    public static async Task DownloadAndExpandZipArchive(Uri uri, Func<string,string?> getDestination, Action<double>? progress)
    {
        progress?.Invoke(0);
        byte[] buffer;
        using (var httpClient = new HttpClient())
        using (var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var contentLength = response.Content.Headers.ContentLength ?? 0;
            buffer = new byte[contentLength];
            var memory = new Memory<byte>(buffer);
            int bytesRead;
            using var stream = await response.Content.ReadAsStreamAsync();
            while ((bytesRead = await stream.ReadAsync(memory.Slice(0, Math.Min(memory.Length, 1024)))) > 0)
            {
                memory = memory.Slice(bytesRead);
                progress?.Invoke((contentLength - memory.Length) / (double)contentLength / 2);
            }
        }

        progress?.Invoke(0.5);
        using (var archive = new ZipArchive(new MemoryStream(buffer), ZipArchiveMode.Read, false))
        {
            double total = progress is null ? 0 : archive.Entries.Sum(x => x.Length);
            long done = 0;
            foreach (var entry in archive.Entries)
            {
                var destPath = getDestination(entry.FullName);
                if (destPath is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    using (var dest = File.Open(destPath, FileMode.Create, FileAccess.Write))
                    using (var source = entry.Open())
                        await source.CopyToAsync(dest);
                }
                done += entry.Length;
                progress?.Invoke(done / total / 2 + 0.5);
            }
        }
    }

    public static async Task<ReleaseInfo?> GetLatestRelease()
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Accept.Clear();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.Add("User-Agent", nameof(BitwardenExtender));
        return await httpClient.GetFromJsonAsync<ReleaseInfo>("https://api.github.com/repos/ArgusMagnus/BitwardenExtender/releases/latest");
    }

    public static async Task DownloadRelease(ReleaseInfo release, string destinationDirectory, Action<double>? progress)
    {
        var downloadUrl = release.Assets.Select(x => x.DownloadUrl).FirstOrDefault(x => x?.OriginalString.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) is true);
        if (downloadUrl is null)
            throw new ArgumentException("Download uri for zip archive not found", nameof(release));
        await DownloadAndExpandZipArchive(downloadUrl, name => Path.Combine(destinationDirectory, name), progress);
    }
}
