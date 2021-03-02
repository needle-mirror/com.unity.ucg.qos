﻿using System;
using NUnit.Framework;
using System.Collections.Generic;

namespace Unity.Networking.QoS.Test
{
    class QosStatsTests
    {
        [Test]
        public void TestEmptyStatsNotNull()
        {
            var stats = new QosStats(1, 0.75f);
            bool rc = stats.TryGetAllResults("foo", out QosStatsResult[] outResults);
            Assert.IsFalse(rc); // This server doesn't exist
            Assert.IsNull(outResults);
        }

        [Test]
        public void TestEmptyStatsNotValid()
        {
            var stats = new QosStats(1, 0.75f);
            bool rc = stats.TryGetWeightedAverage("foo", out QosStatsResult outResult);
            Assert.IsFalse(rc); // This server doesn't exist
            Assert.IsNull(outResult);
        }

        [Test]
        public void TestStatsSingleResult()
        {
            var result = new QosResult { RequestsSent = 10, ResponsesReceived = 9, }; // So PacketLoss == 0.10f
            result.AddAggregateLatency(result.ResponsesReceived * 50);    // So average latency is 50

            var stats = new QosStats(1, 0.75f);
            stats.ProcessResult("foo", result);

            bool rc = stats.TryGetWeightedAverage("foo", out QosStatsResult outResult);
            Assert.IsTrue(rc);

            // Should retrieve the single result we just added. No weight applied since it's the only result
            Assert.IsNotNull(outResult);
            Assert.AreEqual(result.AverageLatencyMs, outResult.LatencyMs);
            Assert.AreEqual(result.PacketLoss, outResult.PacketLoss);
        }

        [Test]
        public void TestStatsWeightedLatency75()
        {
            var r1 = new QosResult { RequestsSent = 1, ResponsesReceived = 1 };
            r1.AddAggregateLatency(0);     // First result has 0 latency (by default, but make it explicit)
            var r2 = new QosResult { RequestsSent = 1, ResponsesReceived = 1 };
            r2.AddAggregateLatency(100);   // Second result has 100 latency (only 1 response)

            // Add to list so they can be referenced via index
            var results = new List<QosResult>() { r1, r2 };

            Assert.AreEqual(0, results[0].AverageLatencyMs);
            Assert.AreEqual(100, results[1].AverageLatencyMs);

            var stats = new QosStats(results.Count, 0.75f); // 75% weight to current result = 75 latency
            stats.ProcessResult("foo", results[0]);
            stats.ProcessResult("foo", results[1]);

            bool rc = stats.TryGetWeightedAverage("foo", out QosStatsResult outResult);
            Assert.IsTrue(rc);

            // Weight should be applied yielding 75ms avg latency
            Assert.IsNotNull(outResult);
            Assert.AreEqual(75, outResult.LatencyMs);
            Assert.AreEqual(0.0f, outResult.PacketLoss);
        }

        [Test]
        public void TestStatsWeighted100()
        {
            var results = new List<QosResult>()
            {
                new QosResult { RequestsSent = 2, ResponsesReceived = 2, },
                new QosResult { RequestsSent = 10, ResponsesReceived = 1, },
            };

            Assert.AreEqual(0.0f, results[0].PacketLoss);    // First result has 0% packet loss
            Assert.AreEqual(0.9f, results[1].PacketLoss);    // Second (current) result has 90% packet loss

            var stats = new QosStats(results.Count, 1.0f); // 100% weight to current result = 0% packet loss
            stats.ProcessResult("foo", results[0]);
            stats.ProcessResult("foo", results[1]);

            bool rc = stats.TryGetWeightedAverage("foo", out QosStatsResult outResult);
            Assert.IsTrue(rc);

            // Should have retrieved the weighted average of the two added results.
            Assert.IsNotNull(outResult);
            Assert.AreEqual(0.9f, outResult.PacketLoss); // 100% of weight to 90% packet loss result
        }

        [Test]
        public void TestStatsWeightedPacketLoss75()
        {
            var results = new List<QosResult>()
            {
                new QosResult { RequestsSent = 2, ResponsesReceived = 2, },
                new QosResult { RequestsSent = 10, ResponsesReceived = 1, },
            };

            Assert.AreEqual(0.0f, results[0].PacketLoss);    // First result has 0% packet loss
            Assert.AreEqual(0.9f, results[1].PacketLoss);    // Second (current) result has 90% packet loss

            var stats = new QosStats(results.Count, 0.75f); // 75% weight to current result = 67.5% packet loss
            stats.ProcessResult("foo", results[0]);
            stats.ProcessResult("foo", results[1]);

            bool rc = stats.TryGetWeightedAverage("foo", out QosStatsResult outResult);
            Assert.IsTrue(rc);

            // Should have retrieved the weighted average of the two added results.
            Assert.IsNotNull(outResult);
            Assert.AreEqual(results[1].PacketLoss * 0.75f, outResult.PacketLoss); // ((.75f × .9) + (.25f × 0)) / 1.0f = 0.675f
        }

        [Test]
        public void TestGetAllResultsNotExist()
        {
            var stats = new QosStats(1, 0.75f);
            bool rc = stats.TryGetAllResults("foo", out QosStatsResult[] outResults);
            Assert.IsFalse(rc);           // No records for "foo"
            Assert.IsNull(outResults);    // So no results returned
        }

        [Test]
        public void TestGetAllResultsSingleResult()
        {
            var result = new QosResult { RequestsSent = 10, ResponsesReceived = 9, }; // So PacketLoss == 0.10f
            result.AddAggregateLatency(result.ResponsesReceived * 50);    // So average latency is 50

            var stats = new QosStats(1, 0.75f);
            stats.ProcessResult("foo", result);

            bool rc = stats.TryGetAllResults("foo", out QosStatsResult[] outResults);
            Assert.IsTrue(rc);          // Results for "foo" exists
            Assert.IsNotNull(outResults);
            Assert.IsTrue(outResults.Length == 1);
            Assert.AreEqual(result.AverageLatencyMs, outResults[0].LatencyMs);
            Assert.AreEqual(result.PacketLoss, outResults[0].PacketLoss);
        }

        [Test]
        public void TestGetAllResultsMultipleResults()
        {
            var r1 = new QosResult { RequestsSent = 1, ResponsesReceived = 1, }; // 0% packet loss, 0ms latency
            var r2 = new QosResult { RequestsSent = 2, ResponsesReceived = 1, }; // 50% packet loss
            r2.AddAggregateLatency(100);                               // 100ms latency (only 1 response)

            var results = new List<QosResult>() { r1, r2 };

            var stats = new QosStats(2, 0.75f);
            stats.ProcessResult("foo", results[0]);
            stats.ProcessResult("foo", results[1]);

            bool rc = stats.TryGetAllResults("foo", out QosStatsResult[] outResults);
            Assert.IsTrue(rc);          // Results for "foo" exists
            Assert.IsNotNull(outResults);
            Assert.IsTrue(outResults.Length == 2);

            // Returned results are ordered newest->oldest
            for (int i = 0, j = results.Count - 1; i < results.Count; ++i, --j)
            {
                Assert.AreEqual(results[j].AverageLatencyMs, outResults[i].LatencyMs);
                Assert.AreEqual(results[j].PacketLoss, outResults[i].PacketLoss);
            }
        }

        [Test]
        public void TestExpireResults()
        {
            var r1 = new QosResult { RequestsSent = 1, ResponsesReceived = 1, }; // 0% packet loss, 0ms latency
            var r2 = new QosResult { RequestsSent = 2, ResponsesReceived = 1, }; // 50% packet loss
            r2.AddAggregateLatency(100);                               // 100ms latency (only 1 response)

            var results = new List<QosResult>() { r1, r2 };

            var stats = new QosStats(1, 0.75f); // Only room for 1 result
            stats.ProcessResult("foo", results[0]);
            stats.ProcessResult("foo", results[1]); // Should overwrite previous result since we only keep 1

            bool rc = stats.TryGetAllResults("foo", out QosStatsResult[] outResults);
            Assert.IsTrue(rc);
            Assert.IsNotNull(outResults);
            Assert.IsTrue(outResults.Length == 1);
            // Saved results are from the latest ProcessResult
            Assert.AreEqual(results[1].AverageLatencyMs, outResults[0].LatencyMs);
            Assert.AreEqual(results[1].PacketLoss, outResults[0].PacketLoss);
        }

        [Test]
        public void TestInvalidLatencyCantBeAddedToQosStats()
        {
            var stats = new QosStats(1, 0.75f);
            var invalidLatencyQosResult = new QosResult { RequestsSent = 1, ResponsesReceived = 0 };
            Assert.AreEqual(QosResult.InvalidLatencyValue, invalidLatencyQosResult.AverageLatencyMs);
            stats.ProcessResult("foo", invalidLatencyQosResult);

            var rc = stats.TryGetWeightedAverage("foo", out var weightedAverage);
            Assert.IsFalse(rc); // Was not added
            Assert.IsNull(weightedAverage);
        }

        [Test]
        public void TestInvalidPacketLossCantBeAddedToQosStats()
        {
            var stats = new QosStats(1, 0.75f);
            var invalidPacketLossQosResult = new QosResult { RequestsSent = 0, ResponsesReceived = 0 };
            Assert.AreEqual(QosResult.InvalidPacketLossValue, invalidPacketLossQosResult.PacketLoss);
            stats.ProcessResult("foo", invalidPacketLossQosResult);

            var rc = stats.TryGetWeightedAverage("foo", out var weightedAverage);
            Assert.IsFalse(rc); // Was not added
            Assert.IsNull(weightedAverage);
        }

        [Test]
        public void TestInvalidLatencyRemovesServerInQosStats()
        {
            var stats = new QosStats(1, 0.75f);
            var goodQosResult = new QosResult { RequestsSent = 1, ResponsesReceived = 1, };
            goodQosResult.AddAggregateLatency(100);

            stats.ProcessResult("foo", goodQosResult);

            bool rc = stats.TryGetWeightedAverage("foo", out var weightedAverage);
            Assert.IsTrue(rc);
            Assert.AreEqual(100, weightedAverage.LatencyMs);
            Assert.AreEqual(0.0f, weightedAverage.PacketLoss);

            var invalidLatencyQosResult = new QosResult { RequestsSent = 1, ResponsesReceived = 0 };
            Assert.AreEqual(QosResult.InvalidLatencyValue, invalidLatencyQosResult.AverageLatencyMs);
            stats.ProcessResult("foo", invalidLatencyQosResult);

            rc = stats.TryGetWeightedAverage("foo", out weightedAverage);
            Assert.IsFalse(rc); // No longer exists
            Assert.IsNull(weightedAverage);
        }

        [Test]
        public void TestInvalidPacketLossRemovesServerInQosStats()
        {
            var stats = new QosStats(1, 0.75f);
            var goodQosResult = new QosResult { RequestsSent = 1, ResponsesReceived = 1, };
            goodQosResult.AddAggregateLatency(100);

            stats.ProcessResult("foo", goodQosResult);

            bool rc = stats.TryGetWeightedAverage("foo", out var weightedAverage);
            Assert.IsTrue(rc);
            Assert.AreEqual(100, weightedAverage.LatencyMs);
            Assert.AreEqual(0.0f, weightedAverage.PacketLoss);

            var invalidPacketLossQosResult = new QosResult { RequestsSent = 0, ResponsesReceived = 0 };
            Assert.AreEqual(QosResult.InvalidPacketLossValue, invalidPacketLossQosResult.PacketLoss);
            stats.ProcessResult("foo", invalidPacketLossQosResult);

            rc = stats.TryGetWeightedAverage("foo", out weightedAverage);
            Assert.IsFalse(rc); // No longer exists
            Assert.IsNull(weightedAverage);
        }

        [Test]
        public void TestImpossibleQosResultRemovesServerInQosStats()
        {
            var stats = new QosStats(1, 0.75f);
            var goodQosResult = new QosResult { RequestsSent = 1, ResponsesReceived = 1, };
            goodQosResult.AddAggregateLatency(100);

            stats.ProcessResult("foo", goodQosResult);

            bool rc = stats.TryGetWeightedAverage("foo", out var weightedAverage);
            Assert.IsTrue(rc);
            Assert.AreEqual(100, weightedAverage.LatencyMs);
            Assert.AreEqual(0.0f, weightedAverage.PacketLoss);

            var impossibleQosResult = new QosResult { RequestsSent = 1, ResponsesReceived = 2, }; // Impossible, will result in invalid PacketLoss

            Assert.AreEqual(QosResult.InvalidPacketLossValue, impossibleQosResult.PacketLoss);
            stats.ProcessResult("foo", impossibleQosResult);

            rc = stats.TryGetWeightedAverage("foo", out weightedAverage);
            Assert.IsFalse(rc); // No longer exists
            Assert.IsNull(weightedAverage);
        }
    }
}
