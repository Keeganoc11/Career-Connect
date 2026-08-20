using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace CareerConnect.Api.Services;

/// <summary>
/// Fetches a user-supplied URL server-side, so this is a textbook SSRF
/// surface — the IP check runs inside the socket ConnectCallback itself
/// (the actual connect step), not as a separate resolve-then-check, which is
/// what closes the DNS-rebinding gap a "resolve, validate, then connect
/// again" approach would leave open.
/// </summary>
public partial class JobPostingFetcher : IJobPostingFetcher
{
    private const long MaxResponseBytes = 3_000_000;
    private const int MaxExtractedChars = 20_000;

    private readonly HttpClient _client;

    public JobPostingFetcher()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            ConnectCallback = async (context, cancellationToken) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
                var address = addresses.FirstOrDefault(a => !IsPrivateOrReserved(a))
                    ?? throw new InvalidOperationException("Refusing to connect to a private or reserved address.");

                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };

        _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("CareerConnectBot/1.0 (+personal job application tracker)");
    }

    public async Task<JobPostingFetchOutcome> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return new JobPostingFetchOutcome.Failed("Enter a valid http(s) URL.");
        }

        HttpResponseMessage response;
        try
        {
            response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return new JobPostingFetchOutcome.Failed("Couldn't reach that URL. Check the link and try again.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return new JobPostingFetchOutcome.Failed($"That page returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase) &&
                !contentType.Contains("text", StringComparison.OrdinalIgnoreCase))
            {
                return new JobPostingFetchOutcome.Failed("That URL doesn't look like a web page.");
            }

            var html = await ReadCappedAsync(response, cancellationToken);
            if (html is null)
            {
                return new JobPostingFetchOutcome.Failed("That page is too large to read.");
            }

            var text = HtmlToText(html);
            if (text.Length < 100)
            {
                return new JobPostingFetchOutcome.Failed("Couldn't find readable content on that page.");
            }

            return new JobPostingFetchOutcome.Success(text.Length > MaxExtractedChars ? text[..MaxExtractedChars] : text);
        }
    }

    /// <summary>Null if the response exceeds MaxResponseBytes before finishing.</summary>
    private static async Task<string?> ReadCappedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
            {
                return null;
            }
            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        using var reader = new StreamReader(buffer);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static string HtmlToText(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var boilerplate = doc.DocumentNode.SelectNodes("//script|//style|//noscript|//nav|//footer|//header");
        if (boilerplate is not null)
        {
            foreach (var node in boilerplate)
            {
                node.Remove();
            }
        }

        var text = WebUtility.HtmlDecode(doc.DocumentNode.InnerText);
        return WhitespaceRun().Replace(text, " ").Trim();
    }

    private static bool IsPrivateOrReserved(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] switch
            {
                0 => true,       // 0.0.0.0/8
                10 => true,      // 10.0.0.0/8
                127 => true,     // 127.0.0.0/8
                169 => b[1] == 254, // 169.254.0.0/16 — includes cloud metadata endpoints
                172 => b[1] is >= 16 and <= 31, // 172.16.0.0/12
                192 => b[1] == 168, // 192.168.0.0/16
                _ => false,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            {
                return true;
            }
            var b = address.GetAddressBytes();
            return b[0] is 0xfc or 0xfd; // fc00::/7 unique local
        }

        return true; // Unknown address family — refuse rather than guess.
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
