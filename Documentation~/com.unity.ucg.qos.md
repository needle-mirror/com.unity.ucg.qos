# About Multiplay QoS

The Multiplay Quality of Service (QoS) package provides a Unity implementation of the Multiplay QoS protocol for communicating with the Multiplay QoS service. This implementation lets a Unity client determine their network latency to different regions where a Multiplay fleet is deployed.

## Preview package

This package is available as a preview, so it is not ready for production use. The features and documentation in this package might change before it is verified for release.

## Installation

While the preview package has been released for preview, it cannot be installed directly from the Package Manager window in the Unity editor. Instead, add a dependency to the QoS package in your Unity Project directly, and the QoS package will be downloaded and installed the next time the project is opened in the editor. The QoS package can be added to any Unity Project by inserting a dependency into the project's manifest.json file with the desired version. For example:
```json
{
  "dependencies": {
    ...
    "com.unity.ucg.qos": "0.2.0-preview",
    ...
  }
}
```
Additional information about Package Manager and the project manifest can be found in the [Unity Package Manager documentation](https://docs.unity3d.com/Packages/com.unity.package-manager-ui@latest/index.html).

## Requirements

This version of Multiplay QoS is compatible with the following versions of the Unity Editor:

* 2019.3 and later

## Known limitations

## Helpful links

This package assumes familiarity with terms and concepts outlined by Multiplay, such as fleets, locations, and regions. For more information, see the [Multiplay documentation](https://docs.multiplay.com/).

## Using Multiplay QoS

The Multiplay QoS service dynamically determines which available regions should provide the best connection quality to the client for an online session.

The service is composed of two main components:

* **Discovery** - The Discovery service allows a client to determine which Multiplay region(s) is/are available to allocate or use servers from at runtime. It also provides endpoints that are used in the QoS service (outlined below).
* **QoS** - The QoS service allows a client to determine which region(s) is/are likely to provide the best connection quality for an online game session. It does this by allowing the client to send a number of UDP (User Datagram Protocol) requests to endpoints provided by the discovery service, and recording both the time it takes for responses to come back (latency), and how many requests were lost (packet loss).

The discovery and QoS protocols are documented on the [Unity Matchmaking Documentation](https://unity-technologies.github.io/ucg-matchmaking-docs/qos) portal.

This package includes a sample called QosCheck that can be used as a starting point for integrating QoS into your project. The remainder of this document will go over the Discovery & QoS APIs and related workflows.

### Initialization
The network stack must be initialized once before any Discovery or QoS APIs can be used. This is typically done in the Awake() method of the class that implements Discovery and QoS.

```csharp
using Unity.Networking.QoS;
using Unity.Networking.Transport;

public class YourQosClass : MonoBehaviour
{
    void Awake()
    {
        NativeBindings.network_initialize();
    }
}
```
### Discovery
Recall that Discovery determines which regions are active for any given Multiplay fleet, and provides endpoints to contact to determine connection quality to those regions.

Discovery is started by creating a QosDiscovery object.

#### Constructor
```csharp
public QosDiscovery(string fleetId);
```
* `fleetId` - The Multiplay Fleet ID

#### Properties
Several properties can be retrieved and/or set in the QosDiscovery object. If setting is not allowed, it will be noted below.

| Type | Name                  | Default | Notes |
| ---- | --------------------- | ------- | ------|
| int  | RequestTimeoutSeconds | 5 sec   | How long to wait for a discovery response before a request is considered timed out. |
| int  | RequestRetries        | 2       | How many times to retry the request on failure. |
| int  | FailureCacheTimeMs    | 1000 ms | How long to keep failed results in the cache |
| int  | SuccessCacheTimeMs    | 30000 ms | How long to keep successful results in the cache |
| Action\<QosServer[]\> | OnSuccess | null | Action to call when response comes back indicating a successful result |
| Action\<string\> | OnError | null | Action to call when response comes back indicating an error result |
| string | DiscoveryServiceUri | https://qos.multiplay.com/v1/fleets/{0}/servers | URI to get Discovery results {0} is replaced with the `fleetId` in the constuctor. |
| string | FleetId | See QosDiscovery constructor | Multiplay Fleet ID. Normally specified in the constructor, but can be overridden. Resets current in-flight discovery if changed. |
| DiscoveryState | State | DiscoveryState.NotStarted | The current DiscoveryState. Updated after discovery starts. Read-only property. |
| bool | IsDone | false | True if discovery is done (or failed). False otherwise. Read-only property. |
| string | ErrorString | null | Populated with the reason if the discovery request fails. |
| QosServer[] | QosServers | See notes | Currently cached QoS server array. Empty (0-length, but not null) if unavailable. See [Unity Matchmaking Documentation](https://unity-technologies.github.io/ucg-matchmaking-docs/qos) for `QosServer` details. Read-only property. |

#### Methods
```csharp
public void Start(Action<QosServer[]> successHandler = null, Action<string> errorHandler = null)
```
* `successHandler` - Action to invoke on successful discovery. Will override the `OnSuccess` property if specified.
* `errorHandler` - Action to invoke on failed discovery. Will override the `OnError` property if specified.

Start will transition a QosDiscovery from `DiscoveryState.NotStarted` to `DiscoveryState.Running` and send the discovery request to the `DiscoveryServiceUri`. The client can get the current state by accessing the `QosDiscovery.State` property.

NOTE: QosDiscovery is not thread safe and does not support concurrent Discovery requests. Calling Start while another discovery is outstanding will cancel the existing request and will not trigger completion handlers.

```csharp
public void Cancel()
```
Cancel will stop the current in-progress or completed discovery. It will clear all callbacks and set the state back to DiscoveryState.NotStarted. However, it will leave the current cache/etag values so starting a new discovery can take advantage of those values.

```csharp
public void Reset()
```
Reset will stop the current in-progress or completed discovery. It functions the same as `Cancel()` except that it will also purge the cache.

#### Workflow
To perform Discovery, the client should create a QosDiscovery object and provide the Multiplay Fleet ID in the constructor. Additional properties can be set, such as the callback for the success or failure case, or any adjustments to timeouts.

For example:
```csharp
using Unity.Networking.QoS;
using Unity.Networking.Transport;

public class YourQosClass : MonoBehaviour
{
    public string fleetId;

    QosDiscovery _qosDiscovery;
    QosServer[] _qosServers;

    void DiscoverySuccess(QosServer[] servers)
    {
        _qosServers = servers;
    }

    void DiscoveryError(string error)
    {
        Debug.Log($"Got error '{error}' on Discovery");
    }

    void Awake()
    {
        NativeBindings.network_initialize();
    }

    void Start()
    {
        _qosDiscovery = new QosDiscovery(fleetId)
        {
            OnSuccess = DiscoverySuccess,
            OnError   = DiscoveryError,
        };
        _qosDiscovery.Start(); // Could also have specified the callbacks here
    }

    void Update()
    {
        if (_qosDiscovery.IsDone && _qosServers != null)
        {
            // Do something with the results, like start a QosJob
        }
    }
}
```

Once discovery is started, it can be treated as a simple state machine by checking the State on each Update(), or even just checking if it is done. The DiscoverySuccess or DiscoveryError will get invoked when it finishes, providing another way to determine when the task has completed.

### QosJob
QosJob uses the [Unity Job System](https://docs.unity3d.com/Manual/JobSystem.html) to perform network latency and stability (packet loss) checks for the QosServer array that is returned on a successful Discovery.

#### Constructor
```csharp
public QosJob(IList<QosServer> qosServers, string title, uint requestsPerEndpoint = 5, ulong timeoutMs = 10000, ulong maxWaitMs = 500, uint requestsBetweenPause = 10, uint requestPauseMs = 1, uint receiveWaitMs = 10)
```
* `qosServers` - List of QoS servers to contact. This is usually going to be the list that is returned from Discovery.
* `title` - Your game title. This is sent in the request packets to identify the game requesting QoS responses.
* `requestsPerEndpoint` - How many requests to send to each of the endpoints in the `qosServers` list.
* `timeoutMs` - Maximum number of milliseconds the job is allowed to run. If the job times out, whatever results are available can still be collected.
* `maxWaitMs` - Maximum number of milliseconds to wait for responses from each endpoint.
* `requestsBetweenPause` - How many requests to send before pausing to prevent overloading the network.
* `requestPauseMs` - How many milliseconds to pause when `requestsBetweenPause` count is reached.
* `receiveWaitMs` - How many milliseconds to wait for a response in a single receive.

QosJob is a struct, so the constructor is technically optional. However, if it is not used, the job will not be initialized with the required data to run the job, so for all intents and purposes, constructing the QosJob with this constructor should be considered mandatory.

#### Values
Several of the parameters in the constructor will set values in the QosJob struct. These values can be overridden before the job is scheduled if desired. Each corresponds to parameters in the QosJob constructor above. They are:
* `RequestsPerEndpoint`
* `TimeoutMs`
* `MaxWaitMs`
* `RequestsBetweenPause`
* `RequestPauseMs`
* `ReceiveWaitMs`

When the job has finished, the results will be available in the QosJob struct in QosResults [NativeArray](https://docs.unity3d.com/ScriptReference/Unity.Collections.NativeArray_1.html).
```csharp
public NativeArray<QosResult> QosResults;
```
The order and count of the results in the NativeArray will correspond to the order and count of the list of QosServer that is passed into the constructor. There will always be the same number of results as there are QoS servers, even if the job is unable to contact one or more servers in the list.

The results must be copied out of the NativeArray before the job is disposed. One simple way to do this is to utilize the `NativeArray.ToArray()` method which will copy the NativeArray to a regular managed array.

Each QosResult contains details about communicating with the server. QosResult is discussed below.

#### QosResult
The QosResult is a struct that contains the following values:
| Type | Name | Notes |
| ---- | ---- | ----- |
| uint | RequestsSent | How many requests were sent to this server. |
| uint | ResponsesReceived | How many responses were received from this server. |
| uint | AverageLatencyMs | Average network latency in milliseconds over all the responses received. |
| float | PacketLoss | Percentage of packet loss to this server in the range [0.0f..1.0f] (0% - 100%). |
| uint | InvalidRequests | Number of discarded requests that were considered invalid (e.g. request size too small or too large). |
| uint | InvalidResponses | Number of discarded responses that were considered invalid (e.g. bad data, too small, or too large). |
| uint | DuplicateResponses | Number of responses that were duplicated. |
| FcType | FcType | Type of Flow Control set on the response, if any. |
| byte | FcUnits | Units of Flow Control set on the response, if any. |

Flow control is documented in the [QoS API documentation](https://unity-technologies.github.io/ucg-matchmaking-docs/qos).

In addition to the values above, there are two constant values declared in QosResult:
* `QosResult.InvalidLatencyValue` is a value used to represent a result that has zero responses received. Internally it is defined as `uint.MaxValue`, so ordering QosResults by AverageLatencyMs will shift all the invalid results to the end of the list.
* `QosResult.InvalidPacketLossValue` is a value used to represent a result that has zero requests sent. Internally it is defined as `float.MaxValue`, so ordering QosResults by PacketLoss will shift all the invalid results to the end of the list.

`AverageLatencyMs` will be set to `QosResult.InvalidLatencyValue` when no responses have been received. Likewise, `PacketLoss` will be set to `QosResultlInvalidPacketLossValue` when no requests were sent.

#### Workflow
A QosJob is run by creating and initializing the struct, scheduling the job with the Job System, then waiting for it to complete. For example:

```csharp
using Unity.Networking.QoS;
using Unity.Jobs;

public class YourQosClass : MonoBehaviour
{
    QosJob _job;
    JobHandle _updateHandle;
    QosServer[] _qosServers; // Populated in discovery, for example

    void Start()
    {
        _job = new QosJob(_qosServers, "My Title")
        {
            // Override the values here or in the ctor parameters
            RequestsPerEndpoint = 10,
            TimeoutMs = 5000
        };

        _updateHandle = _job.Schedule();
        JobHandle.ScheduleBatchedJobs();
    }

    void Update()
    {
        if (!_updateHandle.IsCompleted)
            return; // QoS job is still processing, nothing to do.

        // Ensure the job results are safe to read, needed even though we know it has completed.
        _updateHandle.Complete();

        if (_job?.QosResults.IsCreated)
        {
            var qosResults = _job.QosResults.ToArray();
            // Do whatever you need with the results, e.g. add to QosStats.

            // Dispose the results and job temporary structures now we're done with them.
            // These are seperate calls so the caller can keep the QosResults.
            _job.QosResults.Dispose();
            _job.Dispose();
        }
    }
```

### QosStats
QosStats is a way to record a history of QoS checks, distilling the information down to just the latency and packet loss. Additionally, it allows the user to compute a weighted moving average of all results, weighted toward the most recent result. This can be used to determine the best overall region for matchmaking when considering results over several checks.

#### Constructor
```csharp
public QosStats(int numResults, float weightOfCurrentResult)
```
* `numResults` - Number of results to keep in the history. Must be a positive number.
* `weightOfCurrentResult` - How much weight to apply to the current (newest) result when computing the weighted moving average. Must be in the range [0.0f..1.0f] (0% - 100%). There is 1.0f (100%) total weight avaialble, so setting this to 1.0f would mean only the current result would count toward the weighted average.

The rest of the results share the remainder of the remaining weight equally. For example, if there are three results with a 0.75f weight on the first result, the remaining two results will each get a weight of 0.125f (12.5%). This is because 1.0f - 0.75f = 0.25f remaining weight. Since there are two other results, they each share half the remaining weight 0.25f / 2 = 0.125f.

#### Methods
```csharp
public void ProcessResult(string key, QosResult result)
```
* `key` - Key to identify the QoS server. This is specified by the caller and can be any string. For example, using the server IP:port.
* `result` - QosResult from the QosJob for that server.

ProcessResult attempts to add the given QosResult to the history for the specified QoS server, represented by `key`. If QosStats is already tracking `numResults` results for that QoS server, the oldest result will be removed before the newest result is added. If the result contains an invalid latency or invalid packet loss, instead of adding the result to the history, the specified `key` will be removed from the QosStats history. This is because any result that is invalid likely indicates a server that should no longer be considered, at least until the next QoS check starts producing valid results again. If the history for a QoS server is considered important, check your results for any invalid values before calling ProcessResult to prevent the history from being removed. Regardless, the data in a QosStats object is ephemeral, so it will only remain as long as the object exists.

TIP: `QosServer.ToString()` will generate "IP:port" as a string, preferring the IPv6 address if available, but falling back to the IPv4 address if necessary. This is an easy way to generate a unique key for each distinct QosServer.

---

```csharp
public bool TryGetWeightedAverage(string key, out QosStatsResult result)
```
* `key` - Key to identify the QoS server.
* `result` - Where to write the result; null if key not found.

TryGetWeightedAverage will get the weighted moving average for the given server key. If the key is found, the result will be written to the provided out param and the method will return true. If the key is not found, `result` will be null and the method will return false.

---

```csharp
public bool TryGetAllResults(string key, out QosStatsResult[] results)
```
* `key` - Key to identify the QoS server.
* `results` - Where to write the results; null if key not found.

TryGetAllResults returns an array of all stored results for a given key. The returned results are raw, meaning they will not have the weighted moving average applied. If the key is found, the results will be written to the provided out param and the method will return true. If the key is not found, `results` will be null and the method will return false.

The results array will be ordered from the most recently added result to the oldest result.

#### QosStatsResult
The QosStatsResult is a distilled version of QosResult that contains only the latency and packet loss. The remaining values from the QosResult are not stored. If you need access to those values, be sure to store the QosResult from the job.

| Type | Name | Notes |
| ---- | ---- | ----- |
| uint | LatencyMs | Latency in milliseconds. |
| float | PacketLoss | Packet loss in the range [0.0f..1.0f] (0% - 100%) |

#### Workflow
This example demonstrates how the developer might submit results to a QosStats object.

```csharp
using Unity.Networking.QoS;

public class YourQosClass
{
    QosServer[] _qosServers; // Populated by Discovery
    QosResult[] _qosResults; // Populated by the QosJob
    QosStats _qosStats = new QosStats(5, 0.75f);

    void UpdateQosStats()
    {
        for (int i = 0 ; i < _qosServers.Count ; ++i)
        {
            // ToString() on a QosServer will return "IP:port" string
            _qosStats.ProcessResult($"{_qosServer[i]}", _qosResult[i]);
        }

        if (_qosStats.TryGetWeightedAverage($"{_qosServer[0]}", out QosStatsResult result))
        {
            Debug.Log(
                $"Latency: {result.LatencyMs}\n"
                $"Packet Loss: {result.PacketLoss}\n"
            );
        }
    }
```