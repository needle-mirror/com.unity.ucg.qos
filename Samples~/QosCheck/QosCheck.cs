using System;
using System.Collections;
using System.Linq;
using System.Text;
using Unity.Jobs;
using Unity.Networking.QoS;
using Unity.Networking.Transport;
using UnityEngine;

public class QosCheck : MonoBehaviour
{
    Coroutine m_QosCoroutine;
    QosDiscovery m_Discovery;
    QosJob m_Job;
    QosStats m_Stats;
    JobHandle m_UpdateHandle;

    // Properties set in editor
    [Header("QoS Check Settings")]
    [Tooltip("Title to include in QoS Request.  Must be specified.")]
    public string title;

    [Tooltip("Milliseconds for the job to complete.  Must be non-zero.")]
    public ulong timeoutMs = 5000;

    [Tooltip("How many requests to send (and responses to expect) per QoS Server. Must be [1..256].")]
    public uint requestsPerEndpoint = 10;

    [Tooltip("Time in milliseconds to wait between QoS checks.")]
    public ulong qosCheckIntervalMs = 30000;

    [Tooltip("Maximum response wait in milliseconds.  Responses which take longer than this may not be reported. Must be non-zero.")]
    public ulong maxWaitMs = 400;

    [Tooltip("How many requests to send before pausing. Must be non-zero.")]
    public uint requestsBetweenPause = 10;

    [Tooltip("How long to pause for after the given number requests in milliseconds.  This help prevent avoid network overload and spaces requests out over time . Must be non-zero.")]
    public uint requestPauseMs = 1;

    [Tooltip("QoS Servers to test for the QoS Check.  If not specified, Discovery will be used.")]
    public QosServer[] qosServers;

    [Header("QoS Result Settings")]
    [Tooltip("Weight to give the most recent QoS results in the overall stats [0.0..1.0].  The historic results share the remainder of the weight equally.")]
    public float weightOfCurrentResult = 0.75f;

    [Tooltip("How many QoS results to save per server. The historic results are used in the computation of the overall weighted rolling average.  Must be non-zero.")]
    public uint qosResultHistory = 5;

    [Header("QoS Discovery Settings")]
    [Tooltip("If true, queries Multiplay for a list of QoS servers for the locations a Fleet is deployed to.")]
    public bool useQosDiscoveryService = true;

    [Tooltip("Multiplay Fleet ID where QoS server(s) are running.  Required for QoS Discovery.")]
    public string fleetId;

    [Tooltip("Seconds to wait for a single response from Discovery service before timing out. Default is 5 seconds.")]
    public int requestTimeoutSec = 5;

    [Tooltip("Number of retires the Discovery service will make. Default is 2 seconds.")]
    public int requestRetries = 2;

    void Awake()
    {
        NativeBindings.network_initialize();
    }

    void Update()
    {
        if (m_QosCoroutine == null)
        {
            // QoS check not started yet.
            m_QosCoroutine = StartCoroutine(PeriodicQosPingCoroutine());
            return;
        }

        if (!m_UpdateHandle.IsCompleted)
            return; // QoS job is still processing, nothing to do.

        // Ensure the job results are safe to read, needed even though we know it
        // has completed.
        m_UpdateHandle.Complete();

        if (m_Job.QosResults.IsCreated)
        {
            // Extract the results of the last completed Qos ping job and log them
            // Update the history of QoS results
            UpdateStats();
            PrintStats();

            // Dispose the results and job temporary structures now we're done with them.
            // These are seperate calls so the caller can keep the QosResults.
            m_Job.QosResults.Dispose();
            m_Job.Dispose();
        }
    }

    void OnDestroy()
    {
        m_UpdateHandle.Complete();

        if (m_Job.QosResults.IsCreated)
        {
            m_Job.QosResults.Dispose();
            m_Job.Dispose();
        }

        m_Discovery?.Reset();

        NativeBindings.network_terminate();
    }

    // A coroutine that perioidically triggers qos checks
    IEnumerator PeriodicQosPingCoroutine()
    {
        // Attempt a qos query once every qosCheckIntervalMs
        while (isActiveAndEnabled)
        {
            if (timeoutMs + (ulong)requestTimeoutSec * (ulong)requestRetries * 1000 > qosCheckIntervalMs)
                Debug.LogWarning("The combination of discovery timeout and qos timeout are longer than the interval between Qos checks." +
                    "  This may result in overlapped discovery and/or qos checks being canceled before completion.");

            var qosCheck = StartCoroutine(ExecuteSingleQosQueryCoroutine());

            // Delay the next QoS check until qosCheckIntervalMs has elapsed
            Debug.Log($"QoS check will run again in {qosCheckIntervalMs / 1000:F} seconds.");
            yield return new WaitForSeconds(qosCheckIntervalMs / 1000f);

            // Ensure that last coroutine completed before moving on
            if (qosCheck != null)
                StopCoroutine(qosCheck);
        }
    }

    // A coroutine that runs a single qos discovery + qos check
    //  Note - exceptions fired in this method do not affect / get captured by PeriodicQosPingCoroutine()
    IEnumerator ExecuteSingleQosQueryCoroutine()
    {
        if (string.IsNullOrEmpty(title))
            throw new ArgumentNullException(nameof(title), "Title must be set");

        if (timeoutMs == 0)
            throw new ArgumentOutOfRangeException(nameof(timeoutMs), "TimeoutMs must be non-zero");

        if (qosResultHistory == 0)
            throw new ArgumentOutOfRangeException(nameof(qosResultHistory), "Must keep at least 1 QoS result");

        if (weightOfCurrentResult < 0.0f || weightOfCurrentResult > 1.0f)
            throw new ArgumentOutOfRangeException(nameof(weightOfCurrentResult), "Weight must be in the range [0.0..1.0]");

        // Try to use the discovery service to find qos servers
        if (useQosDiscoveryService)
        {
            PopulateQosServerListFromService();

            while (m_Discovery != null && !m_Discovery.IsDone)
                yield return null;
        }

        // Kick off a new qos job if possible
        if (qosServers?.Length > 0)
            ScheduleNewQosJob();
        else
            Debug.LogWarning($"{nameof(QosCheck)} tried to update results, but no servers have been specified or discovered");
    }

    // Make a discovery call if requirements are met
    void PopulateQosServerListFromService()
    {
        if (!useQosDiscoveryService)
            return;

        m_Discovery = m_Discovery ?? new QosDiscovery(fleetId?.Trim()){
            RequestTimeoutSeconds = requestTimeoutSec,
            RequestRetries = requestRetries,
            OnSuccess = DiscoverySuccess,
            OnError = DiscoveryError,
        };

        if (m_Discovery.State != DiscoveryState.Running)
            m_Discovery.Start();
    }

    // Handle a successful discovery service call
    void DiscoverySuccess(QosServer[] servers)
    {
        // Update the list of available QoS servers
        qosServers = servers ?? new QosServer[0];

        if (qosServers.Length == 0)
        {
            Debug.LogWarning("Discovery found no QoS servers");
            return;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var sb = new StringBuilder(qosServers.Length);
        sb.AppendLine($"Discovery found {qosServers.Length} QoS servers:");

        foreach (var server in qosServers)
            sb.AppendLine(server.ToString());

        Debug.Log(sb.ToString());
#endif
    }

    // Handle a failed discovery service call
    void DiscoveryError(string error)
    {
        Debug.LogError(error);
    }

    // Start up a job that pings QoS endpoints
    void ScheduleNewQosJob()
    {
        m_Job = new QosJob(qosServers, title)
        {
            RequestsPerEndpoint = requestsPerEndpoint,
            TimeoutMs = timeoutMs
        };

        if (maxWaitMs > 0)
            m_Job.MaxWaitMs = maxWaitMs;

        if (requestsBetweenPause > 0)
            m_Job.RequestsBetweenPause = requestsBetweenPause;

        if (requestPauseMs > 0)
            m_Job.RequestPauseMs = requestPauseMs;

        m_UpdateHandle = m_Job.Schedule();
        JobHandle.ScheduleBatchedJobs();
    }

    void UpdateStats()
    {
        var results = m_Job.QosResults.ToArray();

        if (results.Length == 0)
        {
            Debug.LogWarning("No QoS results available because no QoS servers contacted.");
            return;
        }

        // We've got stats to record, so create a new stats tracker if we don't have one yet
        m_Stats = m_Stats ?? new QosStats(qosResultHistory, weightOfCurrentResult);

        // Add stats for each endpoint to the stats tracker
        for (var i = 0; i < results.Length; ++i)
        {
            var ipAndPort = qosServers[i].ToString();
            var r = results[i];
            m_Stats.AddResult(ipAndPort, r);

            if (r.RequestsSent == 0)
                Debug.Log($"{ipAndPort}: Sent/Received: 0");
            else
                Debug.Log($"{ipAndPort}: " +
                    $"Received/Sent: {r.ResponsesReceived}/{r.RequestsSent}, " +
                    $"Latency: {r.AverageLatencyMs}ms, " +
                    $"Packet Loss: {r.PacketLoss * 100.0f:F1}%, " +
                    $"Flow Control Type: {r.FcType}, " +
                    $"Flow Control Units: {r.FcUnits}, " +
                    $"Duplicate responses: {r.DuplicateResponses}, " +
                    $"Invalid responses: {r.InvalidResponses}");

            // Deal with flow control in results (must have gotten at least one response back)
            if (r.ResponsesReceived > 0 && r.FcType != FcType.None)
            {
                qosServers[i].BackoffUntilUtc = GetBackoffUntilTime(r.FcUnits);
                Debug.Log($"{ipAndPort}: Server applied flow control and will no longer respond until {qosServers[i].BackoffUntilUtc}.");
            }
        }
    }

    void PrintStats()
    {
        // Print out all the aggregate stats
        for (var i = 0; i < qosServers?.Length; ++i)
        {
            var ipAndPort = qosServers[i].ToString();

            if (m_Stats.TryGetWeightedAverage(ipAndPort, out var result))
            {
                m_Stats.TryGetAllResults(ipAndPort, out var allResults);

                // NOTE:  You probably don't want Linq in your game, but it's convenient here to filter out the invalid results.
                Debug.Log($"Weighted average QoS report for {ipAndPort}: " +
                    $"Latency: {result.LatencyMs}ms, " +
                    $"Packet Loss: {result.PacketLoss * 100.0f:F1}%, " +
                    $"All Results: {string.Join(", ", allResults.Select(x => x.IsValid() ? x.LatencyMs : 0))}");
            }
            else
            {
                Debug.Log($"No results for {ipAndPort}.");
            }
        }
    }

    // Do not modify - Contract with server
    static DateTime GetBackoffUntilTime(byte fcUnits)
    {
        return DateTime.UtcNow.AddMinutes(2 * fcUnits + 0.5f); // 2 minutes for each unit, plus 30 seconds buffer
    }
}
