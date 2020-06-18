using System;
using Unity.Networking.Transport;

namespace Unity.Networking.QoS
{
    public static class QosHelper
    {
        public static bool WouldBlock(int errorcode)
        {
            // WSAEWOULDBLOCK == 10035 (windows)
            // WSAETIMEDOUT == 10060 (windows)
            // EAGAIN == 11 or 35 (supported POSIX platforms)
            // EWOULDBLOCK == 11 or 35 (supported POSIX platforms, generally an alias for EAGAIN)(*)
            //
            // (*)Could also be 54 on SUSv3, and 246 on AIX 4.3,5.1, but we don't support those platforms
            return errorcode == 10035 ||
                    errorcode == 10060 ||
                    errorcode == 11 ||
                    errorcode == 35;
        }

        public static bool ExpiredUtc(DateTime timeUtc)
        {
            return DateTime.UtcNow > timeUtc;
        }

        public static string Since(DateTime dt)
        {
            return $"{(DateTime.UtcNow - dt).TotalMilliseconds:F0}ms";
        }

        public unsafe static network_address NetworkAddress(NetworkInterfaceEndPoint endpoint)
        {
            return *(network_address*)endpoint.data;
        }

        public static NetworkInterfaceEndPoint CreateInterfaceEndPoint(NetworkEndPoint addr)
        {
            return new UDPNetworkInterface().CreateInterfaceEndPoint(addr);
        }

        public static string Address(NetworkInterfaceEndPoint addr)
        {
            return new UDPNetworkInterface().GetGenericEndPoint(addr).Address;
        }
    }
}
