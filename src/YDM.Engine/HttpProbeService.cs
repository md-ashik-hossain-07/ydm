using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using YDM.Core.Interfaces;
using YDM.Core.Models;
using YDM.Core.Security;

namespace YDM.Engine;

public class HttpProbeService : IHttpProbeService
{
    private readonly HttpClient _httpClient;

    public HttpProbeService(HttpClient? httpClient = null)
    {
        if (httpClient != null)
        {
            _httpClient = httpClient;
        }
        else
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false, // Manual redirect following for security and header capture
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36 YDM/1.0");
        }
    }

    public async Task<ProbeResult> ProbeAsync(
        string url, 
        string? referrer = null, 
        string? cookies = null, 
        string? userAgent = null, 
        CancellationToken ct = default)
    {
        var result = new ProbeResult
        {
            FinalUrl = url
        };

        string currentUrl = url;
        string? capturedFileName = null;
        int redirectCount = 0;
        const int maxRedirects = 10;

        while (redirectCount <= maxRedirects)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
            request.Headers.Range = new RangeHeaderValue(0, 0);

            if (!string.IsNullOrWhiteSpace(referrer))
            {
                request.Headers.Referrer = new Uri(referrer);
            }

            if (!string.IsNullOrWhiteSpace(cookies))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookies);
            }

            if (!string.IsNullOrWhiteSpace(userAgent))
            {
                request.Headers.UserAgent.Clear();
                request.Headers.UserAgent.ParseAdd(userAgent);
            }

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            result.StatusCode = (int)response.StatusCode;
            result.FinalUrl = currentUrl;

            // Check for Content-Disposition in current hop
            if (response.Content.Headers.ContentDisposition != null)
            {
                string parsedName = ExtractFileName(response.Content.Headers.ContentDisposition, currentUrl);
                if (!string.IsNullOrWhiteSpace(parsedName) && parsedName != "download.dat")
                {
                    capturedFileName = parsedName;
                }
            }

            // Handle 3xx redirects
            if (response.StatusCode is HttpStatusCode.MovedPermanently 
                or HttpStatusCode.Found 
                or HttpStatusCode.SeeOther 
                or HttpStatusCode.TemporaryRedirect 
                or (HttpStatusCode)308)
            {
                var location = response.Headers.Location;
                if (location == null)
                {
                    break;
                }

                // Resolve relative redirect
                var baseUri = new Uri(currentUrl);
                var resolvedUri = new Uri(baseUri, location);
                currentUrl = resolvedUri.ToString();
                redirectCount++;
                continue;
            }

            // Terminal response reached
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                result.SupportsRange = true;
                if (response.Content.Headers.ContentRange?.Length.HasValue == true)
                {
                    result.ContentLength = response.Content.Headers.ContentRange.Length.Value;
                }
            }
            else if (response.StatusCode == HttpStatusCode.OK)
            {
                result.SupportsRange = response.Headers.AcceptRanges.Contains("bytes");
                if (response.Content.Headers.ContentLength.HasValue)
                {
                    result.ContentLength = response.Content.Headers.ContentLength.Value;
                }
            }

            result.ETag = response.Headers.ETag?.Tag;
            result.LastModified = response.Content.Headers.LastModified;
            result.ContentType = response.Content.Headers.ContentType?.MediaType;

            result.SuggestedFileName = capturedFileName ?? ExtractFileName(response.Content.Headers.ContentDisposition, result.FinalUrl);
            break;
        }

        return result;
    }

    public static string ExtractFileName(ContentDispositionHeaderValue? disposition, string url)
    {
        if (disposition != null)
        {
            // Try RFC 5987 / RFC 6266 filename* first
            if (!string.IsNullOrWhiteSpace(disposition.FileNameStar))
            {
                string rawStar = disposition.FileNameStar.Trim();
                if (rawStar.StartsWith("UTF-8''", StringComparison.OrdinalIgnoreCase))
                {
                    string encoded = rawStar.Substring(7);
                    try
                    {
                        string decoded = Uri.UnescapeDataString(encoded);
                        return PathSanitizer.SanitizeFileName(decoded);
                    }
                    catch { }
                }
                return PathSanitizer.SanitizeFileName(rawStar);
            }

            // Try standard filename
            if (!string.IsNullOrWhiteSpace(disposition.FileName))
            {
                string raw = disposition.FileName.Trim('"', ' ');
                return PathSanitizer.SanitizeFileName(raw);
            }
        }

        // Fallback to URL path
        try
        {
            var uri = new Uri(url);
            string lastSegment = uri.Segments.LastOrDefault()?.Trim('/') ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(lastSegment))
            {
                string unescaped = Uri.UnescapeDataString(lastSegment);
                return PathSanitizer.SanitizeFileName(unescaped);
            }
        }
        catch { }

        return "download.dat";
    }
}
