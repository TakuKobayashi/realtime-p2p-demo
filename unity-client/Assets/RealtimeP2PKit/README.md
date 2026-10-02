# RealtimeP2PKit

This folder contains the reusable runtime and Editor window. `Assets/Example` is a separate consumer. To use the library in another Unity 6 project, copy this folder and `Assets/Packages` (MessagePack and NativeWebSocket DLLs with `.meta` files), then add Unity.WebRTC 3.0.0, Newtonsoft.Json 3.2.2, Burst 1.8.27, and Collections 2.6.2 to the target project's `Packages/manifest.json`.

Open **RealtimeP2PKit > Connection Settings** and choose Local or Remote from **Environment to edit**. Configure that environment's signaling WebSocket base URL and ordered STUN URL list. The window saves `Resources/P2PConnectionSettings.asset`, so the selected Player environment is available in builds. The Editor environment is stored separately in Editor PlayerPrefs.

Call `P2PManager.Instance.Initialize()` and register packet handlers. `P2PConfig` is optional: omitted or null config uses the `gameplay` data-channel label, unreliable delivery with no retransmits, and `Info` logging. To customize these values, create a `P2PConfig` asset and pass it to `Initialize(config)`. After your application chooses a room and initiator role, call `await P2PManager.Instance.ConnectToRoomAsync(playerId, roomId, opponentId, isInitiator)`. Wait for `DataChannelReady` before sending packets. Call `Disconnect()` during teardown. Matchmaking and HTTP code live in `Assets/Example/Matchmaking` and are not part of this library.

The signaling server must implement `/parties/room/{roomId}` as in the included `server` project. The STUN-only transport cannot connect every pair of NATs; a TURN relay is required for those cases.
