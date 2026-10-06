# RealtimeP2PKit

The public namespace and runtime assembly are `net.taptappun.RealtimeP2PKit`. This folder contains the reusable Runtime and Editor assemblies. `Assets/Example` and `Assets/Editor/Example` are application code and are excluded from library distribution.

The library manages WebRTC negotiation and DataChannels. It does not prescribe a server URL, route, room model, authentication method, or JSON protocol. Room creation, listing, reservation, and HTTP membership APIs belong to the application.

Initialize `P2PManager.Instance.Initialize(optionalConfig)`, register packet handlers, and call `await manager.ConnectAsync(localPeerId, signalingClient)`. `signalingClient` is an application-provided `ISignalingClient`; the manager owns and disposes it when disconnecting or replacing a session. Supply a new client for each connection.

The adapter implements this contract:

- `ConnectAsync()` completes when the application's signaling session is ready, including any authentication/setup it requires. Waiting alone is valid.
- `PeerJoined(peerId, isInitiator)` identifies a remote peer and which side creates the offer. Initial peers can be reported before `ConnectAsync()` completes. The adapter must coordinate complementary offerer roles with its backend.
- `PeerLeft(peerId)` reports a departure. `MessageReceived(senderId, message)` supplies typed Offer, Answer, or IceCandidate data from an identified peer. The adapter is responsible for authenticating identities and checking recipients.
- `Send(peerId, message)` converts typed WebRTC data to the backend's wire format. `DispatchMessageQueue()` is called each frame and can drive transport events or adapter-specific keepalive. `Disconnected(reason)` reports loss of signaling.
- `Dispose()` releases the adapter's connections/subscriptions and should cancel pending work. The manager also cancels its own pending connection and ignores callbacks from replaced clients.

`WebSocketSignalingTransport` is an optional `ISignalingTransport` implementation using NativeWebSocket. Pass a complete `ws://` or `wss://` URL. It does not append a path, query parameters, credentials, or messages. An adapter can wrap it to implement any text protocol, or use another transport entirely. Query/user information is excluded from URL logs.

Open **RealtimeP2PKit > Connection Settings** to configure complete signaling URLs and ordered STUN lists. Settings are saved to the application's `Assets/Resources/P2PConnectionSettings.asset`, outside the library folder. Editor selection uses PlayerPrefs; Player builds always use Remote. Adapters can obtain the configured URL from `P2PEndpoints.GetSignalingWebSocketUrl()`. The manager reads STUN settings but never creates a signaling implementation or consumes a signaling URL itself.

`P2PConfig` is optional. Defaults use the `gameplay` DataChannel label, unreliable delivery with no retransmits, and Info logging. `PeerConnectionTimeoutSeconds` is an optional application-selected negotiation deadline; 0 disables this additional deadline and leaves failure detection to WebRTC. The core does not impose a signaling heartbeat or membership lease.

`SignalingReady` reports completed signaling setup. `PeerConnected` reports an open DataChannel, `PeerLeft` a departure, `PeerConnectionFailed` an individual link failure, and `ConnectionClosed` loss of signaling. `Session` contains only `LocalPeerId`, `IsSignalingReady`, and connection state. Call `Disconnect()` during teardown.

The manager maintains one WebRTC connection per discovered peer. `Send(packetId, value)` broadcasts; `SendTo(peerId, packetId, value)` targets a peer. Register `RegisterPacketHandler<T>(packetId, (senderId, value) => ...)` to identify the sending connection. No application participant limit is built in. Full mesh requires n−1 links per participant, so application capacity should account for hardware and bandwidth.

The Demo's `Assets/Example/Signaling/PartyKitSignalingClient.cs` implements the included partyserver protocol: room acknowledgement, authenticated query parameters, JSON envelopes, peer discovery, and lease heartbeat. `ExampleEndpoints` constructs its room URL. These choices are Demo-specific and are not library requirements. The server protocol is unchanged.

To use this source in another Unity project, include this folder and its MessagePack/NativeWebSocket dependencies from `Assets/Packages`, plus Unity.WebRTC, Newtonsoft.Json (payload logging), Burst, and Collections. Localization is used only by the Demo. Dependency packaging for UPM distribution must include or declare the runtime dependencies.
