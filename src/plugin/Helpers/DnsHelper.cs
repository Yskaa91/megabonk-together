using System.Net;

namespace MegabonkTogether.Helpers
{
    public static class DnsHelper
    {
        public static string ResolveDomainToIp(string domainName)
        {
            var addresses = Dns.GetHostAddresses(domainName);
            return addresses.Length > 0 ? addresses[0].ToString() : null;
        }

        /// <summary>
        /// Returns true when <paramref name="address"/> is one of the addresses the given
        /// host name resolves to. IPv4-mapped IPv6 addresses are normalized so a relay
        /// connection is not mistaken for a direct peer (or vice versa) depending on
        /// which address family the OS picked.
        /// </summary>
        public static bool IsAddressOf(string domainName, IPAddress address)
        {
            if (address == null || string.IsNullOrWhiteSpace(domainName))
            {
                return false;
            }

            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            try
            {
                foreach (var resolved in Dns.GetHostAddresses(domainName))
                {
                    var candidate = resolved.IsIPv4MappedToIPv6 ? resolved.MapToIPv4() : resolved;
                    if (candidate.Equals(address))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // DNS failure: fall back to a plain string comparison.
                return string.Equals(address.ToString(), ResolveDomainToIp(domainName));
            }

            return false;
        }
    }
}
