# RealtimeP2PKit

This folder contains the reusable runtime and Editor window. `Assets/Example` is a separate consumer. To use the library in another Unity 6 project, copy this folder and `Assets/Packages` (MessagePack and NativeWebSocket DLLs with `.meta` files), then add Unity.WebRTC 3.0.0, Newtonsoft.Json 3.2.2, Burst 1.8.27, and Collections 2.6.2 to the target project's `Packages/manifest.json`.

Open **RealtimeP2PKit > Connection Settings** to edit the matchmaking HTTP API URL, signaling WebSocket base URL, and ordered STUN URL list for Local and Remote environments. The window saves `Resources/P2PConnectionSettings.asset`, so the selected Player environment is available in builds. The Editor environment is chosen separately and stored in Editor PlayerPrefs.

Create a `P2PConfig` asset, then call `P2PManager.Instance.Initialize(config)`, register packet handlers, and call `StartMatchmaking(playerId)`. Wait for `DataChannelReady` before sending packets. Call `Disconnect()` during teardown. Packets use MessagePack; see `Assets/Example/PositionPacket.cs`.

The server must implement the matching and `/parties/lobby/{playerId}` and `/parties/room/{roomId}` signaling protocol used by the included `server` project. The STUN-only transport cannot connect every pair of NATs; a TURN relay is required for those cases.
