using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ImpiousBonum.Core.Remote;

/// <summary>Who may watch the tablet view: holders of the link's secret, on the local network.</summary>
public static class TabletAccess
{
    /// <summary>A fresh link secret: 128 random bits, URL-safe.</summary>
    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Compares in constant time, so the secret can't be guessed a character at a time.</summary>
    public static bool TokenMatches(string? given, string expected) =>
        given is not null && expected.Length > 0
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected));

    /// <summary>
    /// Private (RFC 1918), link-local and loopback addresses for IPv4; loopback, link-local and unique local for IPv6.
    /// Anything else could only be the internet, which the tablet view never answers.
    /// </summary>
    public static bool IsLocalNetwork(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal;

        return false;
    }

    /// <summary>
    /// This PC's addresses a tablet could reach it on: private IPv4 addresses of connected adapters, those with a
    /// default gateway (the home network) first.
    /// </summary>
    public static IReadOnlyList<IPAddress> LocalAddresses()
    {
        var found = new List<(IPAddress Address, bool HasGateway)>();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up
                    || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                var properties = adapter.GetIPProperties();
                var hasGateway = properties.GatewayAddresses.Any(g => !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any));
                foreach (var unicast in properties.UnicastAddresses)
                {
                    var address = unicast.Address;
                    if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address) && IsLocalNetwork(address))
                        found.Add((address, hasGateway));
                }
            }
        }
        catch (NetworkInformationException)
        {
            // No adapter information: the caller shows "no network" rather than a link.
        }

        return found.OrderByDescending(f => f.HasGateway).Select(f => f.Address).Distinct().ToList();
    }

    public static string Link(IPAddress address, int port, string token) => $"http://{address}:{port}/?k={token}";
}
