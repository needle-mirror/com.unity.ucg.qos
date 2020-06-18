using System;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace Unity.Networking.QoS
{
    public enum DiscoveryState
    {
        NotStarted = 0,
        Running,
        Done,
        Failed
    }

    public class QosDiscovery
    {
        const string k_DefaultDiscoveryServiceUri = "https://qos.multiplay.com/v1/fleets/{0}/servers";
        const int k_DefaultRequestTimeoutSeconds = 5;
        const int k_DefaultRequestRetries = 2;
        const int k_DefaultFailureCacheTimeMs = 1 * 1000;
        const int k_DefaultSuccessCacheTimeMs = 30 * 1000;

        // Used to track whether code execution is happening on main thread
        static readonly Thread k_MainThread = Thread.CurrentThread;

        DateTime m_CacheExpireTimeUtc = DateTime.MinValue;
        UnityWebRequestAsyncOperation m_DiscoverQosAsyncOp;
        string m_DiscoveryServiceUriPattern = k_DefaultDiscoveryServiceUri;
        string m_Etag;
        string m_FleetId;
        string m_URL;
        int m_Retries;
        QosServer[] m_QosServersCache;

        public QosDiscovery(string fleetId)
        {
            if (string.IsNullOrEmpty(fleetId))
                throw new ArgumentNullException(nameof(fleetId), $"{nameof(fleetId)} cannot be null or empty.");

            m_FleetId = fleetId;
        }

        /// <summary>
        ///     The time (in seconds) to wait before timing out a single call to the discovery service.
        /// </summary>
        public int RequestTimeoutSeconds { get; set; } = k_DefaultRequestTimeoutSeconds;

        /// <summary>
        ///     The number of request retries performed in the event of a failure.
        /// </summary>
        public int RequestRetries { get; set; } = k_DefaultRequestRetries;

        /// <summary>
        ///     The time (in milliseconds) to cache a failure state
        /// </summary>
        public int FailureCacheTimeMs { get; set; } = k_DefaultFailureCacheTimeMs;

        /// <summary>
        ///     The time (in milliseconds) to cache a set of successful results.
        ///     Can be overridden by cache-control information received from the service.
        /// </summary>
        public int SuccessCacheTimeMs { get; set; } = k_DefaultSuccessCacheTimeMs;

        /// <summary>
        ///     The callback to invoke when a call to the discovery service is successful
        /// </summary>
        public Action<QosServer[]> OnSuccess { get; set; }

        /// <summary>
        ///     The callback to invoke when a call to the discovery service fails
        /// </summary>
        public Action<string> OnError { get; set; }

        /// <summary>
        ///     The pattern for building the URI to the discovery service.
        ///     For use in a formatter; replace your Fleet ID with {0} and set the FleetId separately using the FleetId property.
        /// </summary>
        public string DiscoveryServiceUri
        {
            get => m_DiscoveryServiceUriPattern;
            set
            {
                if (string.IsNullOrEmpty(value))
                    throw new ArgumentNullException(nameof(value), $"{nameof(DiscoveryServiceUri)} cannot be null or empty.");

                // If trying to set the discovery URI to a new value,
                //  old / in-flight results are no longer valid
                if (m_DiscoveryServiceUriPattern != null
                    && !value.Equals(m_DiscoveryServiceUriPattern, StringComparison.CurrentCultureIgnoreCase))
                {
                    Debug.LogWarning($"{nameof(DiscoveryServiceUri)} changed; resetting discovery service");
                    Reset();
                }

                m_DiscoveryServiceUriPattern = value;
            }
        }

        /// <summary>
        ///     The ID of your multiplay fleet
        /// </summary>
        public string FleetId
        {
            get => m_FleetId;
            set
            {
                if (string.IsNullOrEmpty(value))
                    throw new ArgumentNullException(nameof(value), $"{nameof(m_FleetId)} cannot be null or empty.");

                // If trying to set the fleet ID to a new value,
                //  old / in-flight results are no longer valid
                if (m_FleetId != null
                    && !value.Equals(m_FleetId, StringComparison.CurrentCultureIgnoreCase))
                {
                    Debug.LogWarning($"{nameof(FleetId)} changed; resetting discovery service");
                    Reset();
                }

                m_FleetId = value;
            }
        }

        /// <summary>
        ///     The internal state of the qos request
        /// </summary>
        public DiscoveryState State { get; private set; } = DiscoveryState.NotStarted;

        /// <summary>
        ///     Whether or not the qos request is in a "done" state (success or failure)
        /// </summary>
        public bool IsDone => State == DiscoveryState.Done || State == DiscoveryState.Failed;

        /// <summary>
        ///     Populated with an error if the request has failed
        /// </summary>
        public string ErrorString { get; private set; }

        /// <summary>
        ///     Get a new copy of the cached QosServers
        /// </summary>
        public QosServer[] QosServers => GetCopyOfCachedQosServers();

        /// <summary>
        ///     Starts the QoS server discovery process
        /// </summary>
        /// <remarks>
        ///     * QosDiscovery is not thread safe and does not support concurrent
        ///     Discovery requests. Calling Start while another discovery
        ///     is outstanding will cancel the existing request and not trigger handlers.
        /// </remarks>
        public void Start(Action<QosServer[]> successHandler = null, Action<string> errorHandler = null)
        {
            // Fail fast - UnityWebRequest's SendWebRequest() only works on main thread
            ThrowExceptionIfNotOnMainThread();

            if (successHandler != null)
                OnSuccess = successHandler;

            if (errorHandler != null)
                OnError = errorHandler;

            State = DiscoveryState.Running;

            m_URL = string.Format(DiscoveryServiceUri, UnityWebRequest.EscapeURL(FleetId));

            // Deliver cached results if valid
            if (DateTime.UtcNow <= m_CacheExpireTimeUtc)
            {
                InvokeSuccessHandler();
                State = DiscoveryState.Done;
                return;
            }

            // A new request needs to be sent, so cancel any old requests in-flight
            DisposeDiscoveryRequest();
            ErrorString = null;
            m_Retries = 0;

            // Set up a new request
            SingleRequest();
        }

        /// <summary>
        ///     Cancel the current in-progress or completed discovery
        /// </summary>
        /// <remarks>
        ///     * Clears the UnityWebRequestAsyncOperation, all callbacks, and sets the state back to NotStarted.
        ///     * Leaves the cache/etag values so starting a new discovery can take advantage of those values.
        /// </remarks>
        public void Cancel()
        {
            // Remove handlers
            OnSuccess = null;
            OnError = null;

            // Dispose of any discovery requests in-flight
            DisposeDiscoveryRequest();

            ErrorString = null;

            State = DiscoveryState.NotStarted;
        }

        /// <summary>
        ///     Cancel the current in-progress or completed discovery with cache disposal
        /// </summary>
        /// <remarks>
        ///     * Does everything Cancel() does, plus purges the cache
        /// </remarks>
        public void Reset()
        {
            Cancel();
            PurgeCache();
        }

        /// <summary>
        /// Perform a single web request to obtain the servers.
        /// </summary>
        private void SingleRequest()
        {
            m_DiscoverQosAsyncOp = QosDiscoveryClient.GetQosServersAsync(m_URL, RequestTimeoutSeconds, m_Etag);

            // Register completed handler for the discovery web request
            // Not a race condition (see https://docs.unity3d.com/ScriptReference/Networking.UnityWebRequestAsyncOperation.html)
            m_DiscoverQosAsyncOp.completed += OnDiscoveryCompleted;
        }

        void PurgeCache()
        {
            m_Etag = null;
            m_QosServersCache = null;
            m_CacheExpireTimeUtc = DateTime.MinValue;
        }

        // Dispose of any discovery requests currently in-flight
        void DisposeDiscoveryRequest()
        {
            if (m_DiscoverQosAsyncOp != null)
            {
                m_DiscoverQosAsyncOp.completed -= OnDiscoveryCompleted;
                m_DiscoverQosAsyncOp.webRequest?.Dispose();
                m_DiscoverQosAsyncOp = null;
            }
        }

        void OnDiscoveryCompleted(AsyncOperation obj)
        {
            if (!(obj is UnityWebRequestAsyncOperation discoveryRequestOperation))
                throw new Exception($"QosDiscovery: Wrong AsyncOperation type {obj.GetType()} in callback");

            var discoveryRequest = discoveryRequestOperation.webRequest;
            if (discoveryRequestOperation != m_DiscoverQosAsyncOp)
            {
                // Not our current request clean up.
                discoveryRequest?.Dispose();
                return;
            }

            if (m_DiscoverQosAsyncOp == null)
                return;  // Request was cancelled.

            bool cleanup = true;
            try
            {
                if (QosDiscoveryClient.IsWebRequestNullOrFailed(discoveryRequest))
                {
                    // Request failed.
                    if (QosDiscoveryClient.IsWebRequestRetryable(discoveryRequest) && m_Retries < RequestRetries)
                    {
                        // Retry, so skip full dispose.
                        m_Retries++;
                        cleanup = false;
                        Debug.LogWarning($"QosDiscovery: Failed to request servers error: '{discoveryRequest.error}' attempt {m_Retries} retrying...");
                        discoveryRequest.Dispose();
                        SingleRequest();
                        return;
                    }

                    m_CacheExpireTimeUtc = DateTime.UtcNow.AddMilliseconds(FailureCacheTimeMs);
                    InvokeErrorHandler($"QosDiscovery: Failed to request servers {discoveryRequest.error} after {m_Retries + 1} tries");
                    return;
                }

                // Extract max-age and update the cache time
                if (QosDiscoveryClient.TryGetMaxAge(discoveryRequest, out var maxAgeSeconds))
                    m_CacheExpireTimeUtc = DateTime.UtcNow.AddSeconds(maxAgeSeconds);
                else
                    m_CacheExpireTimeUtc = DateTime.UtcNow.AddMilliseconds(SuccessCacheTimeMs);

                if (discoveryRequest.responseCode != (long)HttpStatusCode.NotModified)
                {
                    // Update the cached server array as its changed or new.
                    if (QosDiscoveryClient.TryGetEtag(discoveryRequest, out var etag))
                        m_Etag = etag;

                    string error = null;
                    if (QosDiscoveryClient.TryGetQosServersFromRequest(discoveryRequest, out m_QosServersCache, out error))
                    {
                        InvokeSuccessHandler();
                        return;
                    }

                    // Ignore cache control and reset m_CacheExpireTimeUtc to failure value
                    m_CacheExpireTimeUtc = DateTime.UtcNow.AddMilliseconds(FailureCacheTimeMs);
                    InvokeErrorHandler($"QosDiscovery: Failed to process discovery response {error}");
                    return;
                }

                InvokeSuccessHandler();
            }
            finally
            {
                if (cleanup)
                    DisposeDiscoveryRequest();
            }
        }

        void InvokeSuccessHandler()
        {
            State = DiscoveryState.Done;

            // Always return a copy of the results. The next discovery may return a 304 which expects us to have
            // the previous results available.  We don't want to have our cached data dirtied.
            OnSuccess?.Invoke(GetCopyOfCachedQosServers());
        }

        QosServer[] GetCopyOfCachedQosServers()
        {
            var qosServersCopy = new QosServer[m_QosServersCache?.Length ?? 0];
            m_QosServersCache?.CopyTo(qosServersCopy, 0);

            return qosServersCopy;
        }

        void InvokeErrorHandler(string error)
        {
            State = DiscoveryState.Failed;

            // Don't log here.  Let callback log the error if it wants.
            ErrorString = error;
            OnError?.Invoke(error);
        }

        // Throw an exception if not on the main thread
        // Many Unity methods can only be used from the main thread; this allows code to fail fast
        static void ThrowExceptionIfNotOnMainThread([CallerMemberName] string memberName = "")
        {
            if (Thread.CurrentThread != k_MainThread)
                throw new InvalidOperationException($"{memberName} must be called from the main thread.");
        }
    }
}
