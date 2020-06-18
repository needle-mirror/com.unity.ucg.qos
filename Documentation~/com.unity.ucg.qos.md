# About Multiplay QoS

The Multiplay Quality of Service (QoS) package provides a Unity implementation of the Multiplay QoS protocol for communicating with the Multiplay QoS service. This implementation lets a Unity client determine their network pings to different regions where a Multiplay fleet is deployed.

## Preview package

This package is available as a preview, so it is not ready for production use. The features and documentation in this package might change before it is verified for release.

## Installation

To install this package, follow the instructions in the [Package Manager documentation](https://docs.unity3d.com/Packages/com.unity.package-manager-ui@latest/index.html).

## Requirements

This version of Multiplay QoS is compatible with the following versions of the Unity Editor:

* 2019.3 and later

## Known limitations

Multiplay QoS version 0.1.0-preview.1 includes the following known limitations:

* Uses one job thread for the duration of a set of QoS pings

## Helpful links

This package assumes familiarity with terms and concepts outlined by Multiplay, such as fleets, locations, and regions. For more information, see the [Multiplay documentation](https://docs.multiplay.com/).

## Using Multiplay QoS

The Multiplay QoS service dynamically determines which available region should provide the best connection quality to the client for an online session.

The service is composed of two main components:

* Discovery
  * The Discovery service lets a client determine, at runtime, which Multiplay regions are available for a fleet to allocate servers from. The client can then test each region for connection quality using QoS pings.
  * Calls to the Discovery service return a list of servers (with region information) to send QoS pings to.
* QoS
  * QoS pings let the client test the connection quality to each available region, using the User Datagram Protocol (UDP).

## Multiplay QoS workflows

 1. Initialize the network.<br />
     `NativeBindings.network_initialize();`
 2. Call the discovery service using your fleet ID to retrieve a list of servers.
    1. Create a new `QosDiscovery` object.
    2. Call `<QosDiscovery>.StartDiscovery()`.
    3. Provide a completion handler to get the `QosServer[]` results.
 3. Ping the list of servers to get QoS results.
    1. Create a new `QosJob`, passing in the `QosServer[]` from Discovery.
    2. Wait for the job to complete.
    3. Create a new `QosStats` object to handle storing and processing stats from the qos results.
    4. Iterate through the list of `QosResults` on the completed `QosJob`.
       1. Add each Qos result to the stats object for tracking.<br />
       See [Example Consuming QoS job results](#example-consuming-qos-job-results).
 4. Handle flow control.<br />
     See the note on flow control under [Example Consuming QoS job results](#example-consuming-qos-job-results).
 5. Dispose of network when no longer needed.<br />
    `NativeBindings.network_terminate();`.

### Example: Consuming QoS job results

```csharp
// Populate in Qos job
QosServer[] qosServers;

// Only operate if the Qos job returned results
if (!myQosJob.QosResults.IsCreated)
  return;

// Get results from the Qos job
var results = myQosJob.QosResults.ToArray();

// Set up a new object to hold stats about Qos results
var stats = new QosStats(5, 0.75f);

// Add each Qos result to Qos stats object
for (var i = 0; i < results.Length; ++i)
{
  var ipAndPort = qosServers[i].ToString();
  var result = results[i];
  stats.AddResult(ipAndPort, result);

  // Update flow control for each Qos server
  if (result.ResponsesReceived > 0 && result.FcType != FcType.None)
  {
      qosServers[i].BackoffUntilUtc = GetBackoffUntilTime(result.FcUnits);
  }
}

// Dispose the results now that we're done with them
myQosJob.QosResults.Dispose();
```
