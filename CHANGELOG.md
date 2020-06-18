# Changelog

## [0.1.1-preview.4] - 2020-06-18
### Fixed
- TimeoutMs initialization fixed to be correct value

## [0.1.1-preview.3] - 2020-06-11
### Changed
- com.unity.collections dependency updated to 0.9.0-preview.5
- com.unity.transport dependency updated to 0.3.1-preview.4
- Minor reformatting and flattening of fixed blocks in QosRequest.Send & QosResponse.Recv
- Updated copyright to 2020 in LICENSE.md

## [0.1.1-preview.2] - 2020-05-15
### Fixed
- Register OnSuccess and OnError handlers in QosDiscovery.Start if present

## [0.1.1-preview.1] - 2020-04-23
### Changed
- Updated Unity required version to 2019.3
- com.unity.transport dependency updated to 0.3.1-preview.1
- com.unity.collections dependency updated to 0.6.0-preview.9
- Improve the resilience of discovery by adding request retry support.
- Improve the resilience of QoS results, mainly due to invalid timeouts, by eliminating serial query of servers.
- Improved logging from response verification failure adding the source address.
- Add error reporting on discovery failures.

## [0.1.0-preview.3] - 2020-03-28
### Changed
- com.unity.transport dependency updated to 0.2.4-preview.0
- Unity version dependency updated to 2019.3

## [0.1.0-preview.2] - 2019-07-24
### Changed
- Fix QosStats to update weighted average on first call to TryGetWeightedAverage

## [0.1.0-preview] - 2019-06-10
### This is the first release of *Unity Package \<Multiplay QoS Client\>*
