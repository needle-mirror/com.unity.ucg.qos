using System;
using Unity.Networking.Transport;
using UnityEngine;

namespace Unity.Networking.QoS
{
    public enum FcType
    {
        None = 0,
        Throttle = 1,
        Ban = 2
    }

    public class QosResponse
    {
        public const int MinPacketLen = 13;
        public const int MaxPacketLen = 1500;
        public const byte ResponseMagic = 0x95;
        public const byte ResponseVersion = 0;

        // Not making these public properties because we need to be able to take the address of them in Recv()
        private byte m_Magic;
        private byte m_VerAndFlow;
        private byte m_Sequence;
        private ushort m_Identifier;
        private ulong m_Timestamp;

        private int m_LatencyMs;

        private ushort m_PacketLength = 0;

        // Can only read the response data.
        public byte Magic => m_Magic;
        public byte Version => (byte)((m_VerAndFlow >> 4) & 0x0F);
        public byte FlowControl => (byte)(m_VerAndFlow & 0x0F);
        public byte Sequence => m_Sequence;
        public ushort Identifier => m_Identifier;
        public ulong Timestamp => m_Timestamp;
        public ushort Length => m_PacketLength;

        public int LatencyMs => m_LatencyMs;

        /// <summary>
        /// Receive a QosResponse if one is available
        /// </summary>
        /// <param name="socket">Native socket descriptor</param>
        /// <param name="endpoint">Remote endpoint from which to receive the response</param>
        /// <param name="expireTimeUtc">When to stop waiting for a response</param>
        /// <returns>
        /// (received, errorcode) where received is the number of bytes received and errorcode is the error code if any.
        /// 0 means no error.
        /// </returns>
        public (int received, int errorcode) Recv(long socket, bool wait, DateTime expireTimeUtc, ref NetworkInterfaceEndPoint endPoint)
        {
            m_PacketLength = 0;
            int errorcode = 0;
            int received = -1;
            var startTime = DateTime.UtcNow;
            unsafe
            {
                fixed (
                    void* pMagic = &m_Magic,
                    pVerAndFlow = &m_VerAndFlow,
                    pSequence = &m_Sequence,
                    pIdentifier = &m_Identifier,
                    pTimestamp = &m_Timestamp,
                    pSrcAddr = endPoint.data
                )
                {
                    var iov = stackalloc network_iovec[5];
                    iov[0].buf = pMagic;
                    iov[0].len = sizeof(byte);

                    iov[1].buf = pVerAndFlow;
                    iov[1].len = sizeof(byte);

                    iov[2].buf = pSequence;
                    iov[2].len = sizeof(byte);

                    iov[3].buf = pIdentifier;
                    iov[3].len = sizeof(ushort);

                    // Everything below here is user-specified data and not part of the QosResponse header
                    iov[4].buf = pTimestamp;
                    iov[4].len = sizeof(ulong);

                    int tries = 0;
                    while (!QosHelper.ExpiredUtc(expireTimeUtc))
                    {
                        errorcode = 0;
                        tries++;

                        received = NativeBindings.network_recvmsg(socket, iov, 5, ref *(network_address*)pSrcAddr, ref errorcode);
                        if (received == -1)
                        {
                            if (QosHelper.WouldBlock(errorcode))
                            {
                                if (!wait)
                                {
                                    return (0, 0);
                                }

                                continue;
                            }
                        }
                        break; // Got a response, or a non-retryable error
                    }

                    if (received == -1)
                    {
                        if (!QosHelper.WouldBlock(errorcode))
                        {
                            Debug.LogError($"QosResponse: recv failed after {tries} tries in {QosHelper.Since(startTime)} (errorcode {errorcode})");
                        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        else
                        {
                            // Don't treat as an error as it's expected behaviour if we're seeing any
                            // level of packet loss.
                            Debug.Log($"QosResponse: no response after {tries} tries in {QosHelper.Since(startTime)} (errorcode {errorcode})");
                        }
#endif

                        return (received, errorcode);
                    }

                    m_PacketLength = (ushort)received;
                }
            }

            m_LatencyMs = (Length >= MinPacketLen) ? (int)((ulong)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond) - m_Timestamp) : -1;
            return (received, errorcode);
        }

        /// <summary>
        /// Verifies the QosResponse contains valid required fields
        /// </summary>
        /// <param name="error">Contains the description of the first validation failure if valiation failed</param>
        /// <returns>true if basic validation passes, false otherwise</returns>
        public bool Verify(uint maxSequence, ref string error)
        {
            if (Length < MinPacketLen)
            {
                error = $"response is too small got {Length} bytes min expected {MinPacketLen} bytes";
                return false;
            }
            else if (Magic != ResponseMagic)
            {
                error = $"response contains an invalid signature 0x{Magic:X} expected 0x{ResponseMagic:X}";
                return false;
            }
            else if (Version != ResponseVersion)
            {
                error = $"response contains an invalid version {Version} expected {ResponseVersion}";
                return false;
            }
            else if (Sequence > maxSequence)
            {
                error = $"response contains an invalid sequence {Sequence} max expected {maxSequence}";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Parses the FlowControl 4-bit value into the type and number of units (duration) that have been applied.
        /// </summary>
        /// <returns>
        /// (type, units) where type is the flow control type (FcType.None for no flow control), and units is the
        /// number of units of that type of flow control that the server has applied (and we should adhere to).
        /// </returns>
        public (FcType type, byte units) ParseFlowControl()
        {
            if (FlowControl == 0)
            {
                return (FcType.None, 0);
            }

            FcType type = ((FlowControl & 0x8) != 0) ? FcType.Ban : FcType.Throttle;
            byte units = (byte)(FlowControl & 0x7);

            // Units are the lower 3 bits of the flow control nibble.  For throttles, the unit counts start at 1
            // (001b..111b). For bans, the unit count starts at 0 (000b..111b), so 0 is 1 unit, 1 is 2 units, etc.
            // So for bans, add 1 to get the number of units.
            if (type == FcType.Ban)
            {
                ++units;
            }

            return (type, units);
        }
    }
}
