# Changelog
## [0.3.1-preview.1] - 2021-03-12
### Changed
- The QoS package no longer has a dependency on the Transport Package.
- Calls to NativeBindings no longer supported.
- Removed requirement to initialize network before performing a QoS check.

## [0.3.0-preview.1] - 2021-03-02
### Fixed
- Obsolete usage of UnityWebRequest Errors

### Changed
- Minimum supported Unity version has been bumped to 2020.2

## [0.2.0-preview] - 2020-09-10
### Added
- QosResult.AddAggregateLatency() replaces QosResult.AverageLatencyMs to generate the aggregate sum of all latencies that is used to compute the average. Instead of directly adding to QosResult.AverageLatencyMs, call QosResult.AddAggregateLatency.
### Changed
- Average latency when no responses have been received changed from 0 to QosResult.InvalidLatencyValue (which is uint.MaxValue) so sorting by latency does not make invalid results appear first.
- Packet loss when no requests have been sent changed from 0.0f to QosResult.InvalidPacketLossValue (which is float.MaxValue) so sorting by packet loss does not make invalid results appear first.
- QosStats.AddResult() renamed to QosStats.ProcessResult(). Additionally, calling QosStats.ProcessResult() with QosResult.AverageLatencyMs == QosResult.InvalidLatencyValue or QosResult.PacketLoss == QosResult.InvalidPacketLossValue will remove the server from stats-tracking since an invalid latency or packet loss indicates a server that is not currently reachable. Adding a valid result will again start tracking the server's results.
- QosResult.AverageLatencyMs is now read-only and is computed when accessed.
- QosResult.PacketLoss is now read-only and is computed when accessed.
- Some methods that have an out parameter will now set that parameter to null if it returns false (instead of using a default value). This affects QosStats.TryGetWeightedAverage(), QosStats.TryGetAllResults(), and WeightedMovingAverage.AllResults().
- WeightedMovingAverage.AllResults() now returns results ordered newest-to-oldest.
- QosStats.TryGetAllResults() now returns results ordered newest-to-oldest.
- com.unity.collections dependency updated to 0.9.0-preview.6.
### Deprecated
- QosStatsResult.IsValid() is deprecated. It is no longer possible to submit an invalid QosResult to QosStats, so having a validity check is unnecessary.
- QosStats.AddResult() is deprecated. It will call QosStats.ProcessResult() for the nonce.
### Removed
- QosResult.Update() removed. Average latency and packet loss are now computed on access.
### Fixed
- It is no longer possible to add invalid results to QosStats.

## [0.1.1-preview.4] - 2020-06-18
### Fixed
- TimeoutMs initialization fixed to be correct value.

## [0.1.1-preview.3] - 2020-06-11
### Changed
- com.unity.collections dependency updated to 0.9.0-preview.5.
- com.unity.transport dependency updated to 0.3.1-preview.4.
- Minor reformatting and flattening of fixed blocks in QosRequest.Send() & QosResponse.Recv().
- Updated copyright to 2020 in LICENSE.md.

## [0.1.1-preview.2] - 2020-05-15
### Fixed
- Register OnSuccess and OnError handlers in QosDiscovery.Start() if present.

## [0.1.1-preview.1] - 2020-04-23
### Changed
- Updated Unity required version to 2019.3.
- com.unity.transport dependency updated to 0.3.1-preview.1.
- com.unity.collections dependency updated to 0.6.0-preview.9.
- Improve the resilience of discovery by adding request retry support.
- Improve the resilience of QoS results, mainly due to invalid timeouts, by eliminating serial query of servers.
- Improved logging from response verification failure adding the source address.
- Add error reporting on discovery failures.

## [0.1.0-preview.3] - 2020-03-28
### Changed
- com.unity.transport dependency updated to 0.2.4-preview.0.
- Unity version dependency updated to 2019.3.

## [0.1.0-preview.2] - 2019-07-24
### Changed
- Fix QosStats to update weighted average on first call to TryGetWeightedAverage.

## [0.1.0-preview] - 2019-06-10
### This is the first release of *Unity Package \<Multiplay QoS Client\>*
