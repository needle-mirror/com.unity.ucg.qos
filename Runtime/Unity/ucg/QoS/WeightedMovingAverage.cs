using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Unity.Networking.QoS
{
    // Weighted moving average of QoS Results.  A single weight can be specified, which is the weight to apply
    // to the newest QoS results.  The rest of the results equally share the remainder of the weight.
    public class WeightedMovingAverage
    {
        public QosStatsResult WeightedResult { get; private set; }

        private LinkedList<QosStatsResult> m_Results;   // List of results used to compute the moving average
        private readonly int m_MaxResults;              // How many results to track in moving average
        private readonly float m_Weight;                // Weight of the most recent result [0.0..1.0]

        // No default construction allowed
        private WeightedMovingAverage() { }

        public WeightedMovingAverage(int numResults, float weightOfCurrentResult)
        {
            if (numResults <= 0)
                throw new ArgumentException("Number of results to track must be positive", nameof(numResults));
            if (!QosStats.InRangeInclusive(weightOfCurrentResult, 0.0f, 1.0f))
                throw new ArgumentException("Weight of must be in the range [0.0f..1.0f]", nameof(weightOfCurrentResult));

            m_MaxResults = numResults;
            m_Weight = weightOfCurrentResult;
        }

        /// <summary>
        /// Add a QosStatsResult to the list of results used to compute the weighted moving average.
        /// </summary>
        /// <param name="result">Result to add to the list of results</param>
        /// <param name="updateAfterAdd">If true, update the weighted moving average after adding the result</param>
        /// <remarks>
        /// * If <paramref name="updateAfterAdd"/> is false, <see cref="Unity.Networking.QoS.WeightedMovingAverage.Update()"/> must be called to correctly update the weighted moving average.
        public void ProcessResult(QosStatsResult result, bool updateAfterAdd = false)
        {
            if (m_Results == null)
                m_Results = new LinkedList<QosStatsResult>();

            // If full, drop oldest result (tail) to make room for new result
            if (m_Results.Count == m_MaxResults)
                m_Results.RemoveLast();

            m_Results.AddFirst(result);

            if (updateAfterAdd)
                Update();
        }

        /// <summary>
        /// Get a copy of all stored results (unweighted)
        /// </summary>
        /// <param name="results">out param containing copy of results</param>
        /// <remarks>
        /// * <paramref name="results"/> can be null if no results have been added.
        /// </remarks>
        public void AllResults(out QosStatsResult[] results)
        {
            results = (AllResults() as List<QosStatsResult>)?.ToArray();
        }

        /// <summary>
        /// Get a copy of all stored results (unweighted)
        /// </summary>
        /// <returns>
        /// Copy of the stored results as a list, or null if there is no stored results.
        /// </returns>
        public IList<QosStatsResult> AllResults()
        {
            return (m_Results != null) ? new List<QosStatsResult>(m_Results) : null;
        }

        /// <summary>
        /// Update the weighted moving average for the available results.
        /// </summary>
        /// <remarks>
        /// Results are stored in <see cref="WeightedResult"/> if there are results to compute.
        /// </remarks>
        public void Update()
        {
            if (m_Results == null)
            {
                return; // Nothing to do
            }

            float weightedLatencyMs = 0.0f;
            float weightedPacketLoss = 0.0f;
            int numResults = m_Results.Count;

            // The specified weight is a percentage [0.0f..1.0f], so we'll use
            // 1.0 as our "arbitrary" weighing value. Do not use a weight if
            // there is only one result.
            float initialWeight = (numResults == 1) ? 1.0f : m_Weight;
            float remainingWeight = (numResults > 1) ? (1.0f - initialWeight) / (numResults - 1) : 0;

            // List is ordered from newest to oldest, so computing weighted average means
            // iterating forwards, applying the initial weight to the newest result, and dividing up
            // the remaining weight equally among the other results.
            bool newestResult = true;
            foreach (var result in m_Results)
            {
                if (newestResult)
                {
                    weightedLatencyMs = result.LatencyMs * initialWeight;
                    weightedPacketLoss = result.PacketLoss * initialWeight;
                    newestResult = false;
                }
                else
                {
                    weightedLatencyMs += result.LatencyMs * remainingWeight;
                    weightedPacketLoss += result.PacketLoss * remainingWeight;
                }
            }

            // Because the arbitrary weighing value is 1.0, there's no need to do the final division since we'd just be
            // dividing by 1.0.

            WeightedResult = new QosStatsResult(
                (uint)Mathf.RoundToInt(weightedLatencyMs),
                Mathf.Min(weightedPacketLoss, 1.0f));  // Make sure precision issues don't yield >100% packet loss
        }
    }
}