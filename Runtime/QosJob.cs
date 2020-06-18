using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine;
using Random = System.Random;

namespace Unity.Networking.QoS
{
    public partial struct QosJob : IJob
    {
        public uint RequestsPerEndpoint;
        public ulong TimeoutMs;
        public ulong MaxWaitMs;
        public uint RequestsBetweenPause;
        public uint RequestPauseMs;
        public uint ReceiveWaitMs;

        // Leave the results allocated after the job is done.  It's the user's responsibility to dispose it.
        public NativeArray<QosResult> QosResults;

        [DeallocateOnJobCompletion] private NativeArray<InternalQosServer> m_QosServers;
        [DeallocateOnJobCompletion] private NativeArray<byte> m_TitleBytesUtf8;
        private NativeHashMap<network_address,int> m_AddressIndexes;
        private long m_Socket;
        private DateTime m_JobExpireTimeUtc;
        private int m_Requests;
        private int m_Responses;

        public QosJob(IList<QosServer> qosServers, string title,
            uint requestsPerEndpoint = 5, ulong timeoutMs = 10000, ulong maxWaitMs = 500,
            uint requestsBetweenPause = 10, uint requestPauseMs = 1, uint receiveWaitMs = 10) : this()
        {
            RequestsPerEndpoint = requestsPerEndpoint;
            TimeoutMs = timeoutMs;
            MaxWaitMs = maxWaitMs;
            RequestsBetweenPause = requestsBetweenPause;
            RequestPauseMs = requestPauseMs;
            ReceiveWaitMs = receiveWaitMs;


            var networkInterface = new UDPNetworkInterface();
            NetworkEndPoint remote;

            // Copy the QoS Servers into the job, converting all the IP/Port to NetworkEndPoint and DateTime to ticks.
            m_AddressIndexes = new NativeHashMap<network_address,int>(qosServers?.Count ?? 0, Allocator.Persistent);
            m_QosServers = new NativeArray<InternalQosServer>(qosServers?.Count ?? 0, Allocator.Persistent);
            if (qosServers != null)
            {
                int i = 0;
                foreach (var s in qosServers)
                {
                    if (!NetworkEndPoint.TryParse(s.ipv4, s.port, out remote))
                    {
                        Debug.LogError($"QosJob: Invalid IP address {s.ipv4} in QoS Servers list");
                        continue;
                    }

                    var server = new InternalQosServer(networkInterface.CreateInterfaceEndPoint(remote), s.BackoffUntilUtc, i);
                    if (!m_AddressIndexes.TryAdd(server.NetworkAddress, i))
                    {
                        // Duplicate server.
                        server.FirstIdx = m_AddressIndexes[server.NetworkAddress];
                    }

                    StoreServer(server);
                    i++;
                }

                if (i < m_QosServers.Length)
                {
                    // We had some bad addresses, resize m_QosServers to reduce storage but more importantly
                    // so iterations can be used without checking. m_AddressIndexes isn't resized as its not
                    // worth the effort for the small amount of memory which would saved.
                    NativeArray<InternalQosServer> t = new NativeArray<InternalQosServer>(i, Allocator.Persistent);
                    m_QosServers.GetSubArray(0, t.Length).CopyTo(t);
                    m_QosServers.Dispose();
                    m_QosServers = t;
                }
            }

            // Indexes into QosResults correspond to indexes into qosServers/m_QosServers
            QosResults = new NativeArray<QosResult>(m_QosServers.Length, Allocator.Persistent);

            // Convert the title to a NativeArray of bytes (since everything in the job has to be a value-type)
            byte[] utf8Title = Encoding.UTF8.GetBytes(title);
            m_TitleBytesUtf8 = new NativeArray<byte>(utf8Title.Length, Allocator.Persistent);
            m_TitleBytesUtf8.CopyFrom(utf8Title);
        }

        /// <summary>
        /// Disposes of any internal structures.
        /// Caller should also call QosResults.Dispose().
        /// </summary>
        public void Dispose()
        {
            if (m_AddressIndexes.IsCreated)
                m_AddressIndexes.Dispose();
        }

        /// <summary>
        // Execute implements IJob
        /// <summary>
        public void Execute()
        {
            if (m_QosServers.Length == 0)
                return;    // Nothing to do.

            m_Requests = 0;
            m_Responses = 0;
            var startTime = DateTime.UtcNow;
            m_JobExpireTimeUtc = startTime.AddMilliseconds(TimeoutMs);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"QosJob: executing job with {TimeoutMs}ms timeout");
#endif

            // Create the local socket
            int errorcode = 0;
            (m_Socket, errorcode) = CreateAndBindSocket();
            if (m_Socket == -1 || errorcode != 0)
            {
                // Can't run the job
                Debug.LogError($"QosJob: failed to create and bind the local socket (errorcode {errorcode})");
                return;
            }

            ProcessServers();

            if (NativeBindings.network_close(ref m_Socket, ref errorcode) != 0)
            {
                Debug.LogError($"QosJob: failed to close socket (errorcode {errorcode})");
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"QosJob: took {QosHelper.Since(startTime)} to process {m_QosServers.Length} servers");
#endif
        }

        /// <summary>
        /// Sends QoS requests to all servers and receives the responses.
        /// </summary>
        private void ProcessServers()
        {
            var startTime = DateTime.UtcNow;
            NetworkInterfaceEndPoint addr = QosHelper.CreateInterfaceEndPoint(NetworkEndPoint.AnyIpv4); // TODO(steve): IPv6 support
            foreach (var s in m_QosServers)
            {
                if (s.Duplicate)
                {
                    // Skip, we will copy the result before we return.
                    continue;
                }

                ProcessServer(s);

                // To get as accurate results as possible check to see if we have pending responses.
                RecvQosResponsesTimed(addr, m_JobExpireTimeUtc, false);
            }

            // Receive remaining responses.
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(MaxWaitMs);
            if (m_JobExpireTimeUtc < deadline)
            {
                deadline = m_JobExpireTimeUtc;
            }

            var error = EnableReceiveWait();
            if (error != "")
            {
                Debug.LogError(error);
                return;
            }
            RecvQosResponsesTimed(addr, deadline, true);

            QosResult r;
            foreach (var s in m_QosServers)
            {
                if (s.Duplicate)
                {
                    // Duplicate server so just copy the result.
                    r = QosResults[s.FirstIdx];
                }
                else
                {
                    r = QosResults[s.Idx];
                    r.Update();
                }

                StoreResult(s.Idx, r);
            }
        }

        /// <summary>
        /// Sends QoS requests to a server and receives any responses that are ready.
        /// </summary>
        /// <param name="server">Server that QoS requests should be sent to</param>
        private void ProcessServer(InternalQosServer server)
        {
            if (QosHelper.ExpiredUtc(m_JobExpireTimeUtc))
            {
                Debug.LogWarning($"QosJob: not enough time to process {server.Address}.");
                return;
            }
            else if (DateTime.UtcNow < server.BackoffUntilUtc)
            {
                Debug.LogWarning($"QosJob: skipping {server.Address} due to backoff restrictions");
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"QosJob: processing {server.Address}");
            DateTime startTime = DateTime.UtcNow;
#endif

            QosResult r = QosResults[server.Idx];
            int errorcode = SendQosRequests(server, ref r);
            if (errorcode != 0)
            {
                Debug.LogError($"QosJob: failed to send to {server.Address} (errorcode {errorcode})");
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"QosJob: send to {server.Address} took {QosHelper.Since(startTime)}");
#endif
            StoreResult(server.Idx, r);
        }

        /// <summary>
        /// Sends QoS requests to the given server.
        /// </summary>
        /// <param name="server">Server that QoS requests should be sent to</param>
        /// <param name="result">Results from the send side of the check (packets sent)</param>
        /// <returns>
        /// errorcode - the last error code generated (if any).  0 indicates no error.
        /// </returns>
        private int SendQosRequests(InternalQosServer server, ref QosResult result)
        {
            QosRequest request = new QosRequest
            {
                Title = m_TitleBytesUtf8.ToArray(),
                Identifier = (ushort)new Random().Next(ushort.MinValue, ushort.MaxValue),
            };
            server.RequestIdentifier = request.Identifier;
            StoreServer(server);

            // Send all requests.
            NetworkInterfaceEndPoint remote = server.RemoteEndpoint;
            result.RequestsSent = 0;
            do
            {
                if (QosHelper.ExpiredUtc(m_JobExpireTimeUtc))
                {
                    Debug.LogWarning($"QosJob: not enough time to complete {RequestsPerEndpoint - result.RequestsSent} sends to {QosHelper.Address(remote)} ");
                    return 0;
                }

                request.Timestamp = (ulong)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond);
                request.Sequence = (byte)result.RequestsSent;

                int errorcode = 0;
                int sent = 0;
                (sent, errorcode) = request.Send(m_Socket, ref remote, m_JobExpireTimeUtc);
                if (errorcode != 0)
                {
                    Debug.LogError($"QosJob: send returned error code {errorcode}, can't continue");
                    return errorcode;
                }
                else if (sent != request.Length)
                {
                    Debug.LogWarning($"QosJob: sent {sent} of {request.Length} bytes, ignoring this request");
                    result.InvalidRequests++;
                }
                else
                {
                    m_Requests++;
                    result.RequestsSent++;
                    if (RequestsBetweenPause > 0 && RequestPauseMs > 0 && m_Requests%RequestsBetweenPause == 0)
                    {
                        // Inject a delay to help avoid network overload and to space requests out over time
                        // which can improve the accuracy of the results.
                        System.Threading.Thread.Sleep((int)RequestPauseMs);
                    }
                }
            } while (result.RequestsSent < RequestsPerEndpoint);

            return 0;
        }

        /// <summary>
        /// Stores the updated server.
        /// </summary>
        /// <param name="server">The server to store</param>
        private void StoreServer(InternalQosServer server)
        {
            m_QosServers[server.Idx] = server;
        }

        /// <summary>
        /// Stores the updated result.
        /// </summary>
        /// <param name="idx">The index to store the result at</param>
        /// <param name="result">The result to store</param>
        private void StoreResult(int idx, QosResult result)
        {
            QosResults[idx] = result;
        }

        /// <summary>
        /// Receive QoS responses outputing timing information to the log if in the editor or development mode.
        /// </summary>
        /// <param name="addr">The interface address for storage</param>
        /// <param name="deadline">Responses after this point in time may not be processed</param>
        /// <param name="wait">If true waits for all pending responses to be received, otherwise returns early if no response is received</param>
        /// <returns>
        /// errorcode - the last error code (if any). 0 means no error.
        /// </returns>
        private int RecvQosResponsesTimed(NetworkInterfaceEndPoint addr, DateTime deadline, bool wait)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            int hadResponses = m_Responses;
            DateTime startTime = DateTime.UtcNow;
#endif
            int rc = RecvQosResponses(addr, deadline, wait);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var t = (DateTime.UtcNow - startTime).TotalMilliseconds;
            var p = m_Responses - hadResponses;
            string avgTime = "";
            if (p > 0)
                avgTime = $" avg {t/p:F0}ms per response";
            string w = wait? "waiting" : "";
            Debug.Log($"QosJob: received {p} responses of {m_Responses}/{m_Requests} in {QosHelper.Since(startTime)} {w}{avgTime}");
#endif
            return rc;
        }

        /// <summary>
        /// Receive QoS responses
        /// </summary>
        /// <param name="addr">The interface address for storage</param>
        /// <param name="deadline">Responses after this point in time may not be processed</param>
        /// <param name="wait">If true waits for all pending responses to be received, otherwise returns as soon as no more responses are received</param>
        /// <returns>
        /// errorcode - the last error code (if any). 0 means no error.
        /// </returns>
        private unsafe int RecvQosResponses(NetworkInterfaceEndPoint addr, DateTime deadline, bool wait)
        {
            if (m_Requests == m_Responses)
                return 0;

            QosResponse response = new QosResponse();
            QosResult result = QosResults[0];
            int errorcode = 0;
            int received = 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            int tries = 0;
#endif

            while (m_Requests > m_Responses)
            {
                if (QosHelper.ExpiredUtc(deadline))
                {
                    // Even though this could indicate a config issue the most common cause
                    // will be packet loss so use debug not warning.
                    Debug.Log($"QosJob: not enough time to receive {m_Requests-m_Responses} responses still outstanding");
                    return 0;
                }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                DateTime startTime = DateTime.UtcNow;
#endif
                (received, errorcode) = response.Recv(m_Socket, wait, deadline, ref addr);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                tries++;
#endif
                if (received == 0)
                {
                    if (!wait)
                        return 0; // Wait disabled so just return.

                    continue; // Timeout so just retry.
                }
                else if (received == -1)
                {
                    // Error was logged by Recv so just retry.
                    continue;
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                string w = wait ? " with wait" : "";
                Debug.Log($"QosJob: recv took {QosHelper.Since(startTime)} with {tries} tries pending {m_Requests - m_Responses}{w}");
#endif
                int idx = LookupResult(addr, response, ref result);
                if (idx < 0)
                {
                    // Not found.
                    continue;
                }

                string error = "";
                if (!response.Verify(result.RequestsSent, ref error))
                {
                    Debug.LogWarning($"QosJob: ignoring response from {QosHelper.Address(addr)} verify failed with {error}");
                    result.InvalidResponses++;
                }
                else
                {
                    m_Responses++;
                    result.ResponsesReceived++;
                    result.AverageLatencyMs += (uint)response.LatencyMs;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"QosJob: response from {QosHelper.Address(addr)} with latency {response.LatencyMs}ms");
#endif

                    // Determine if we've had flow control applied to us. If so, save the most significant result based
                    // on the unit count.  In this version, both Ban and Throttle have the same result: client back-off.
                    var fc = response.ParseFlowControl();
                    if (fc.type != FcType.None && fc.units > result.FcUnits)
                    {
                        result.FcType = fc.type;
                        result.FcUnits = fc.units;
                    }
                }

                StoreResult(idx, result);
            }

            return 0;
        }

        /// <summary>
        /// Enables receive waiting and disables non blocking socket operations.
        /// This reduces the overhead of receiving pending responses after all sends have completed.
        /// </summary>
        /// <returns>
        /// error - string detailing on failure, otherwise and empty string.
        /// </returns>
        private string EnableReceiveWait()
        {
            int rc;
            int errorcode = 0;

            if ((rc = NativeBindings.network_set_receive_timeout(m_Socket, ReceiveWaitMs, ref errorcode)) != 0)
            {
                return $"QosJob: failed to set receive timeout (errorcode {errorcode})";
            }
            else if ((rc = NativeBindings.network_set_blocking(m_Socket, ref errorcode)) != 0)
            {
                return $"QosJob: failed to set blocking (errorcode {errorcode})";
            }
            return "";
        }

        /// <summary>
        /// Returns the index of the server which matches both the address of endPoint and identified of response.
        /// </summary>
        /// <returns>
        /// index - the index of server if found, otherwise -1.
        /// </returns>
        private int LookupResult(NetworkInterfaceEndPoint endPoint, QosResponse response, ref QosResult result)
        {
            int idx = 0;

            // TODO(steve): Connecting to loopback at nonstandard (but technically correct) addresses like
            // 127.0.0.2 can return a remote address of 127.0.0.1, resulting in a mismatch which we could fix.
            if (m_AddressIndexes.TryGetValue(QosHelper.NetworkAddress(endPoint), out idx))
            {
                result = QosResults[idx];
                var s = m_QosServers[idx];
                if (response.Identifier != s.RequestIdentifier)
                {
                    Debug.LogWarning($"QosJob: invalid identifier from {QosHelper.Address(endPoint)} 0x{response.Identifier:X4} != 0x{s.RequestIdentifier:X4} ignoring");
                    result.InvalidResponses++;
                    return -1;
                }

                return idx;
            }

            Debug.LogWarning($"QosJob: ignoring unexpected response from {QosHelper.Address(endPoint)}");

            return -1;
        }

        /// <summary>
        /// Create and bind the local UDP socket for QoS checks. Also sets appropriate options on the socket such as
        /// non-blocking and buffer sizes.
        /// </summary>
        /// <returns>
        /// (socketfd, errorcode) where socketfd is a native socket descriptor and errorcode is the error code (if any)
        /// errorcode is 0 on no error.
        /// </returns>
        private unsafe static (long, int) CreateAndBindSocket()
        {
            // Create the local socket.
            NetworkInterfaceEndPoint addr = QosHelper.CreateInterfaceEndPoint(NetworkEndPoint.AnyIpv4); // TODO(steve): IPv6 support
            int errorcode = 0;
            long socket = -1;
            int rc = NativeBindings.network_create_and_bind(ref socket, ref *(network_address*)addr.data, ref errorcode);
            if (rc != 0)
            {
                Debug.LogError($"QosJob: failed to create and bind (errorcode {errorcode} rc {rc})");
                return (rc, errorcode);
            }

            if ((rc = NativeBindings.network_set_nonblocking(socket, ref errorcode)) != 0)
            {
                Debug.LogError($"QosJob: failed to set non blocking (errorcode {errorcode})");
                int ec = 0;
                if (NativeBindings.network_close(ref socket, ref ec) != 0)
                {
                     Debug.LogError($"QosJob: failed to close socket (errorcode {ec})");
                }
                return (rc, errorcode);
            }

            if ((rc = NativeBindings.network_set_send_buffer_size(socket, ushort.MaxValue, ref errorcode)) != 0)
            {
                Debug.LogError($"QosJob: failed to set send buffer (errorcode {errorcode})");
            }
            else
            {
                int len = 0;
                if ((rc = NativeBindings.network_get_send_buffer_size(socket, ref len, ref errorcode)) != 0)
                {
                    Debug.LogError($"QosJob: failed to get send buffer (errorcode {errorcode})");
                }
                else if (len < ushort.MaxValue)
                {
                    Debug.LogWarning($"QosJob: send buffer {len} is less than requested {ushort.MaxValue}");
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                else
                {
                    Debug.Log($"QosJob: send buffer is {len} requested {ushort.MaxValue}");
                }
#endif
            }

            if ((rc = NativeBindings.network_set_receive_buffer_size(socket, ushort.MaxValue, ref errorcode)) != 0)
            {
                Debug.LogError($"QosJob: failed to set receive buffer (errorcode {errorcode})");
            }
            else
            {
                int len = 0;
                if ((rc = NativeBindings.network_get_receive_buffer_size(socket, ref len, ref errorcode)) != 0)
                {
                    Debug.LogError($"QosJob: failed to get receive buffer (errorcode {errorcode})");
                }
                else if (len < ushort.MaxValue)
                {
                    Debug.LogWarning($"QosJob: receive buffer {len} is less than requested {ushort.MaxValue}");
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                else
                {
                    Debug.Log($"QosJob: receive buffer is {len} requested {ushort.MaxValue}");
                }
#endif
            }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            // Avoid WSAECONNRESET errors when sending to an endpoint which isn't open yet (unclean connect/disconnects)
            NativeBindings.network_set_connection_reset(socket, 0);
#endif
            // Don't report non fatal buffer size changes as an error.
            errorcode = 0;

            return (socket, errorcode);
        }
    }
}
