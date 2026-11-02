using System.Net;
using System.Net.Sockets;

namespace Gggs.BuildingBlocks.Web;

/// <summary>
/// Checks a URL a tenant typed in before Nuts and Bolts calls it: https only, and never an address
/// inside the hosting network (loopback, private ranges, link-local, the cloud metadata service).
/// Without this, a webhook URL could be pointed at internal services and used to probe them.
/// </summary>
public static class OutboundUrlGuard
{
    /// <summary>Null when the URL may be called; otherwise why not, in words a tenant can act on.</summary>
    public static async Task<string?> CheckAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return "The address must be an https URL.";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "The address must not carry a user name or password.";
        }

        return await CheckHostAsync(uri.IdnHost, cancellationToken);
    }

    /// <summary>Null when the host name resolves, and only to addresses outside the hosting network; otherwise why not.</summary>
    public static async Task<string?> CheckHostAsync(string? host, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return "The server's address is missing.";
        }

        host = host.Trim();
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(host, out IPAddress? literal) ? [literal] : await Dns.GetHostAddressesAsync(host, cancellationToken);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            return $"{host} could not be found.";
        }

        return addresses.Length == 0 ? $"{host} could not be found."
            : addresses.Any(IsInternal) ? "The address points inside the hosting network, which is not allowed."
            : null;
    }

    /// <summary>
    /// The handler for clients calling tenant-typed URLs. The check above runs before the request; this
    /// holds during it: redirects are not followed, and the address actually connected to is checked,
    /// so neither a redirect nor a DNS answer that changes after the check can reach the hosting network.
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        ConnectCallback = async (context, cancellationToken) =>
        {
            string host = context.DnsEndPoint.Host;
            IPAddress[] addresses = IPAddress.TryParse(host, out IPAddress? literal) ? [literal] : await Dns.GetHostAddressesAsync(host, cancellationToken);
            if (addresses.Length == 0 || addresses.Any(IsInternal))
            {
                throw new HttpRequestException($"{host} points inside the hosting network, which is not allowed.");
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    /// <summary>Loopback, private, link-local, carrier-grade NAT, unspecified and unique-local addresses.</summary>
    public static bool IsInternal(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
        }

        byte[] bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            10 => true,
            127 => true,
            0 => true,
            169 => bytes[1] == 254,
            172 => bytes[1] is >= 16 and <= 31,
            192 => bytes[1] == 168,
            100 => bytes[1] is >= 64 and <= 127,
            _ => false,
        };
    }
}
