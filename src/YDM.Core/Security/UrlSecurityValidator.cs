using System;
using System.Net;
using System.Net.Sockets;

namespace YDM.Core.Security;

public static class UrlSecurityValidator
{
    public static bool IsValidDownloadUrl(string? rawUrl, bool allowLocal = false)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)) return false;

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        if (allowLocal) return true;

        string host = uri.Host;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return false;

        if (IPAddress.TryParse(host, out var ipAddress))
        {
            if (IsPrivateOrLoopback(ipAddress))
                return false;
        }

        return true;
    }

    public static bool IsPrivateOrLoopback(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] bytes = ip.GetAddressBytes();
            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            // Link-local 169.254.0.0/16
            if (bytes[0] == 169 && bytes[1] == 254) return true;
            // Broadcast
            if (bytes[0] == 255 && bytes[1] == 255 && bytes[2] == 255 && bytes[3] == 255) return true;
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
        }

        return false;
    }
}
