using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Unity.Networking.QoS
{
    public class QosStatsResult
    {
        public uint LatencyMs { get; set; }
        public float PacketLoss { get; set; }

        public QosStatsResult(uint latencyMs, float packetLoss)
        {
            if (latencyMs == QosResult.InvalidLatencyValue) throw new ArgumentException($"Latency must be in the range [0..{QosResult.InvalidLatencyValue - 1}", nameof(latencyMs));
            if (!QosStats.InRangeInclusive(packetLoss, 0.0f, 1.0f)) throw new ArgumentException("Packet loss must be in the range [0.0..1.0]", nameof(packetLoss));

            LatencyMs = latencyMs;
            PacketLoss = packetLoss;
        }

        private QosStatsResult() { }

        [Obsolete("It is no longer possible to submit invalid results to QosStats, making this method superfluous.")]
        public bool IsValid() => true;
    }
}