using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Unity.Networking.QoS
{
    public class QosStats
    {
        private ReaderWriterLockSlim m_ResultsLock = new ReaderWriterLockSlim();

        // IP:Port => Weighted results
        private IDictionary<string, WeightedMovingAverage> m_Results = new Dictionary<string, WeightedMovingAverage>();

        private readonly int m_NumResults;
        private readonly float m_Weight;

        // Restrict default construction
        private QosStats() { }

        public QosStats(int numResults, float weightOfCurrentResult)
        {
            if (numResults <= 0) throw new ArgumentException("Number of results to track must be positive", nameof(numResults));
            if (!InRangeInclusive(weightOfCurrentResult, 0.0f, 1.0f)) throw new ArgumentException("Weight must be in the range [0.0f..1.0f]", nameof(weightOfCurrentResult));

            m_NumResults = numResults;
            m_Weight = weightOfCurrentResult;
        }

        /// <summary>
        /// Process a QosResult, udpating the weighted rolling average for the
        /// provided server.
        /// </summary>
        /// <param name="key">Key to identify the server (e.g. IP:port)</param>
        /// <param name="result">The result to include in the history. If added,
        /// this result becomes the current result.</param>
        /// <remarks>
        /// * Processing a result with either invalid latency (meaning no
        /// responses received) or invalid packet loss (meaning no requests
        /// sent) will instead remove the specified server from the stats
        /// history so the server is not accidentally chosen as a good match
        /// based on previous results.
        /// * To start tracking a removed server, simply submit a valid result
        /// on the next QoS check.
        public void ProcessResult(string key, QosResult result)
        {
            if (result.AverageLatencyMs == QosResult.InvalidLatencyValue || result.PacketLoss == QosResult.InvalidPacketLossValue)
            {
                // This is no longer a good server, remove it from the history.
                m_Results.Remove(key);
                return;
            }

            m_ResultsLock.EnterWriteLock();
            try
            {
                WeightedMovingAverage wma = null;
                if (!m_Results.TryGetValue(key, out wma))
                {
                    // Tracking new server
                    wma = new WeightedMovingAverage(m_NumResults, m_Weight);
                }

                wma.ProcessResult(new QosStatsResult(result.AverageLatencyMs, result.PacketLoss));
                m_Results[key] = wma;
            }
            finally
            {
                m_ResultsLock.ExitWriteLock();
            }
        }

        [Obsolete("Use QosStats.ProcessResult() instead.")]
        public void AddResult(string key, QosResult result)
        {
            ProcessResult(key, result);
        }

        /// <summary>
        /// Get the weighted rolling average for the given key.
        /// </summary>
        /// <param name="key">Key to identify the server (e.g. IP:port)</param>
        /// <param name="result">The weighted rolling average for the given key. null if server not found.</param>
        /// <returns>true if the record was found, false otherwise</returns>
        public bool TryGetWeightedAverage(string key, out QosStatsResult result)
        {
            m_ResultsLock.EnterReadLock();
            try
            {
                if (!m_Results.TryGetValue(key, out WeightedMovingAverage wma))
                {
                    result = null;
                    return false;
                }

                wma.Update();
                result = wma.WeightedResult;
                return true;
            }
            finally
            {
                m_ResultsLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Get an array of all the results currently being tracked for the given key.
        /// </summary>
        /// <param name="key">Key to identify the server (e.g. IP:port)</param>
        /// <param name="results">Array of results (unweighted) used to compute the weighted average.</param>
        /// <returns>
        /// true if record(s) were found, false if key does not exist or no valid records found.
        /// If false, <paramref name="results"/> will be null, else it will contain the array of results.
        /// </returns>
        /// <remarks>
        /// * The results, if present, are ordered newest to oldest.
        ///</remarks>
        public bool TryGetAllResults(string key, out QosStatsResult[] results)
        {
            m_ResultsLock.EnterReadLock();
            try
            {
                if (!m_Results.TryGetValue(key, out WeightedMovingAverage wma))
                {
                    results = null;
                    return false;
                }

                wma.AllResults(out results);
                return results != null;
            }
            finally
            {
                m_ResultsLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Test if float value is in the given range, inclusively
        /// <param name="value">Value to test</param>
        /// <param name="min">Minimum value that <paramref name="value"/> must be greater than</param>
        /// <param name="max">Maximum value that <paramref name="value"/> must be less than</param>
        /// <returns> true if value is in inclusive range, false otherwise</returns>
        /// <remarks>
        /// * Epsilon is applied to value to account for FP precision issues
        /// * As noted in the name, this is an inclusive range [min..max], not (min..max)
        public static bool InRangeInclusive(float value, float min, float max)
        {
            return (value + Mathf.Epsilon) >= min && (value - Mathf.Epsilon) <= max;
        }
    }
}