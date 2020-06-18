using System;
using Unity.Networking.Transport;

namespace Unity.Networking.QoS
{
    public partial struct QosJob
    {
        /// <summary>
        /// Internal version of the QosServer struct that trades IP/Port for RemoteEndPoint since NativeArrays can only
        /// have value types.
        /// </summary>
        private struct InternalQosServer
        {
            public readonly NetworkInterfaceEndPoint RemoteEndpoint;
            public readonly DateTime BackoffUntilUtc;
            public readonly int Idx;

            private int m_FirstIdx;
            private ushort m_RequestIdentifier;

            /// <summary>
            /// InternalQosServer is the internal representation of a QoS server use by QosJob.
            /// </summary>
            public InternalQosServer(NetworkInterfaceEndPoint remote, DateTime backoffUntilUtc, int idx)
            {
                RemoteEndpoint = remote;
                BackoffUntilUtc = backoffUntilUtc;

                Idx = idx;
                m_FirstIdx = idx;
                m_RequestIdentifier = 0;
            }

            /// <summary>
            /// NetworkAddress returns the network address of this server.
            /// </summary>
            public network_address NetworkAddress => QosHelper.NetworkAddress(RemoteEndpoint);

            /// <summary>
            /// FirstIdx is the index of the first server which matches the address of this server.
            /// </summary>
            public int FirstIdx
            {
                get => m_FirstIdx;
                set => m_FirstIdx = value;
            }

            /// <summary>
            /// RequestIdentifier is identifier sent to the server.
            /// </summary>
            public ushort RequestIdentifier
            {
                get => m_RequestIdentifier;
                set => m_RequestIdentifier = value;
            }

            /// <summary>
            /// Duplicate returns true if the server uses a duplicate address, false otherwise.
            /// </summary>
            public bool Duplicate => m_FirstIdx != Idx;

            /// <summary>
            /// Address returns the address string of the server.
            /// </summary>
            public string Address => QosHelper.Address(RemoteEndpoint);
        }
    }
}