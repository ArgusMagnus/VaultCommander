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

namespace BitwardenExtender.BwCommands;

public record PollEmailArguments
{
    public string? Host { get; init; }
    public int Port { get; init; } = 993;
    public bool UseSsl { get; init; } = true;
    public string? Username { get; init; }
    public string? Password { get; init; }
}

public record PollEmailResult(string Subject, string Body);

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
                    message = await inbox.GetMessageAsync(messageId,cancellationToken);
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
}
