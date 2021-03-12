using System;
using NUnit.Framework;
using System.Collections.Generic;

namespace Unity.Networking.QoS.Test
{
    class QosResultsTests
    {
        [Test]
        public void TestDefaultValues()
        {
            var result = new QosResult();
            Assert.AreEqual(0, result.RequestsSent);
            Assert.AreEqual(0, result.ResponsesReceived);
            Assert.AreEqual(QosResult.InvalidLatencyValue, result.AverageLatencyMs);
            Assert.AreEqual(QosResult.InvalidPacketLossValue, result.PacketLoss);
            Assert.AreEqual(0, result.InvalidRequests);
            Assert.AreEqual(0, result.InvalidResponses);
            Assert.AreEqual(0, result.DuplicateResponses);
            Assert.AreEqual(FcType.None, result.FcType);
            Assert.AreEqual(0, result.FcUnits);
        }

        [Test]
        public void TestInvalidSendToReceiveRatio()
        {
            var result = new QosResult {RequestsSent = 1, ResponsesReceived = 2,}; // Can't have more responses than requests
            Assert.AreEqual(QosResult.InvalidPacketLossValue, result.PacketLoss);
        }

        [Test]
        public void TestNoResponsesYieldsInvalidLatency()
        {
            var result = new QosResult {RequestsSent = 1, ResponsesReceived = 0,};                   // Likely outcome
            Assert.AreEqual(QosResult.InvalidLatencyValue, result.AverageLatencyMs); // Still considered invalid
        }

        [Test]
        public void TestPacketLossAutoUpdate()
        {
            var result = new QosResult();
            Assert.AreEqual(QosResult.InvalidPacketLossValue, result.PacketLoss);

            result.RequestsSent = 1;
            Assert.AreEqual(1.0f, result.PacketLoss);    // 100% packet loss

            result.ResponsesReceived = 1;
            Assert.AreEqual(0.0f, result.PacketLoss);    // 0% packet loss

            result.RequestsSent = 10;
            Assert.AreEqual(0.9f, result.PacketLoss);    // 90% packet loss
        }

        [Test]
        public void TestLatencyAutoUpdate()
        {
            var result = new QosResult();
            Assert.AreEqual(QosResult.InvalidLatencyValue, result.AverageLatencyMs);

            result.RequestsSent = 1;
            result.ResponsesReceived = 1;
            result.AddAggregateLatency(100);
            Assert.AreEqual(100, result.AverageLatencyMs);

            result.RequestsSent = 2;
            result.ResponsesReceived = 2;
            result.AddAggregateLatency(50);
            Assert.AreEqual(75, result.AverageLatencyMs);    // 150 aggregate latency / 2 responses
        }
    }
}
