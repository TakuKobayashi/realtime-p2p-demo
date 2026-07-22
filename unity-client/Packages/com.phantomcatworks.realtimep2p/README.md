# com.phantomcatworks.realtimep2p

再利用可能なUnity向けリアルタイムP2Pライブラリ。マッチング → シグナリング → WebRTC接続 →
データ交換までを`P2PManager`シングルトン経由の一本のAPIにまとめています。
このファイルは **クイックスタート** と **全クラス・全メソッドのAPIリファレンス** です。

- 全体のセットアップ手順(サーバーデプロイ含む)はリポジトリルートの `README.md` を参照
- ここではあくまで「ライブラリとしてどう呼び出すか」だけに絞って説明します

---

## 目次

1. [クイックスタート(最小コード)](#クイックスタート最小コード)
2. [接続フローの全体像](#接続フローの全体像)
3. [APIリファレンス](#apiリファレンス)
   - [P2PManager](#p2pmanager-シングルトンエントリーポイント)
   - [P2PConfig](#p2pconfig-scriptableobject)
   - [P2PEndpoints / P2PEnvironment(接続先のLocal/Remote切り替え)](#p2pendpoints--p2penvironment接続先のlocalremote切り替え)
   - [P2PSessionInfo / P2PSessionState](#p2psessioninfo--p2psessionstate)
   - [PacketRouter](#packetrouter内部で自動的に使われる)
   - [IPayloadCodec / MessagePackPayloadCodec](#ipayloadcodec--messagepackpayloadcodec)
   - [IMatchmakingClient / HttpMatchmakingClient](#imatchmakingclient--httpmatchmakingclient)
   - [ISignalingClient / PartyKitSignalingClient / LobbyListener](#isignalingclient--partykitsignalingclient--lobbylistener)
   - [P2PLog / P2PNetworkLog(ログ設計)](#p2plog--p2pnetworklogログ設計)
4. [自前のパケット型を追加する](#自前のパケット型を追加する)
5. [差し替え可能なポイント(拡張方法)](#差し替え可能なポイント拡張方法)
6. [他プロジェクトへの持ち出し方](#他プロジェクトへの持ち出し方)
7. [トラブルシューティング](#トラブルシューティング)

---

## クイックスタート(最小コード)

```csharp
using PhantomCatWorks.RealtimeP2PKit;
using UnityEngine;

public class MyGameEntry : MonoBehaviour
{
    [SerializeField] private P2PConfig _config; // Inspectorで P2PConfig アセットを割り当てる

    private const byte PositionPacketId = 1;

    private void Start()
    {
        // 1. 初期化(最初に1回だけ)
        P2PManager.Instance.Initialize(_config);

        // 2. 受信ハンドラを登録(packetIdはアプリ側で自由に決める1バイトのID)
        P2PManager.Instance.RegisterPacketHandler<PositionPacket>(PositionPacketId, OnOpponentPosition);

        // 3. イベント購読
        P2PManager.Instance.StateChanged += state => Debug.Log($"state: {state}");
        P2PManager.Instance.Matched += info => Debug.Log($"matched with {info.OpponentId}");
        P2PManager.Instance.DataChannelReady += () => Debug.Log("connected! start playing");
        P2PManager.Instance.ConnectionClosed += reason => Debug.Log($"closed: {reason}");

        // 4. マッチング開始(playerIdは任意のユニーク文字列)
        var myPlayerId = "player-" + System.Guid.NewGuid().ToString("N")[..8];
        P2PManager.Instance.StartMatchmaking(myPlayerId);
    }

    private void Update()
    {
        // 5. 毎フレーム好きなタイミングで送信(接続前は自動的に無視される)
        P2PManager.Instance.Send(PositionPacketId, new PositionPacket
        {
            X = transform.position.x,
            Y = transform.position.y,
            Z = transform.position.z,
        });
    }

    private void OnOpponentPosition(PositionPacket packet)
    {
        // 受信データを反映する
    }

    private void OnDestroy()
    {
        P2PManager.Instance.Disconnect();
    }
}
```

これで「マッチング → シグナリング → WebRTC接続 → MessagePackでのデータ送受信」まで
すべて動きます。以降のセクションは、この5ステップそれぞれの詳細な仕様です。

> **接続先URL(サーバーのアドレス)は`P2PConfig`アセットには含まれません。** Unity Editorのメニュー
> `RealtimeP2PKit > Connection Settings` で Local/Remote を切り替えます(下記
> [P2PEndpoints / P2PEnvironment](#p2pendpoints--p2penvironment接続先のlocalremote切り替え) 参照)。
> ビルドしたアプリは常にハードコードされたRemoteの値を使うので、Editorでの設定作業は不要です。

---

## 接続フローの全体像

```
StartMatchmaking(playerId)
   │
   ├─ Lobby(自分のplayerId)のwebsocketに接続して待機
   ├─ POST /api/matchmaking/join { playerId }
   │     ├─ 相手がいなければ status=waiting → Lobbyへのpushを待つ
   │     └─ 相手が見つかれば status=matched がその場で返る(自分がisInitiator=false側)
   │
   ▼ (Lobby経由 or 即時レスポンスで) matched イベント発火
Matched イベント (roomId, opponentId, isInitiator)
   │
   ▼
Room の websocket に接続 (SignalingConnecting → Negotiating)
   │
   ├─ isInitiator=true  側 → Offer を作成して送信
   └─ isInitiator=false 側 → Offer を受信 → Answer を作成して送信
   │
   ├─ 双方向で ICE candidate を交換
   │
   ▼
DataChannel が Open
   │
   ▼
DataChannelReady イベント発火 (State = Connected)
   │
   ▼
Send<T>() / RegisterPacketHandler<T>() でゲームデータを送受信
```

状態は `P2PManager.Instance.Session.State` (`P2PSessionState`) で常に確認できます。
`StateChanged` イベントで遷移のたびに通知も受け取れます。

---

## APIリファレンス

### `P2PManager` (シングルトン、エントリーポイント)

名前空間: `PhantomCatWorks.RealtimeP2PKit`
**ゲーム側から直接触ってよいのはこのクラスだけです。** 他のクラスはすべてこの内部で使われる実装詳細です。

#### 静的プロパティ

| メンバー | 説明 |
|---|---|
| `static P2PManager Instance` | シングルトンインスタンス。初回アクセス時に`DontDestroyOnLoad`なGameObjectを自動生成します。Sceneに手動で配置する必要はありません(配置してもOKで、その場合はそのインスタンスが使われます)。 |

#### メソッド

| シグネチャ | 説明 |
|---|---|
| `void Initialize(P2PConfig config)` | **最初に必ず1回呼ぶ**。設定を読み込み、ログレベルを反映し、内部の`HttpMatchmakingClient`と`PacketRouter`を構築し、`Unity.WebRTC`の内部更新コルーチンを開始します。 |
| `void RegisterPacketHandler<T>(byte packetId, Action<T> handler)` | `packetId`(0〜255の任意のID、アプリ側で採番)に対応する受信ハンドラを登録します。データチャンネル経由で該当IDのパケットを受信すると、`T`にMessagePackでデシリアライズしてから`handler`が呼ばれます。 |
| `void UnregisterPacketHandler(byte packetId)` | 登録済みハンドラを解除します。`OnDisable`などで呼ぶことを推奨。 |
| `void Send<T>(byte packetId, T value)` | `value`をMessagePackでシリアライズし、`packetId`を1バイト付与してWebRTCデータチャンネルに送信します。`Session.State != Connected`の間は警告ログを出して黙って無視します(呼び出し側で状態チェックする必要はありません)。 |
| `void StartMatchmaking(string localPlayerId)` | マッチングキューに参加します。`localPlayerId`はサーバー上で一意である必要があります(重複するとサーバー側の`onConflictDoUpdate`で上書きされます)。非同期(`async void`)ですが、呼び出し側で`await`する必要はありません。 |
| `void Disconnect()` | 現在のセッションを終了します。キュー待機中であればサーバーから離脱し、WebRTC接続・シグナリング接続をすべて閉じ、`Session.State`を`Idle`に戻します。Sceneを抜けるときや対戦終了時に呼んでください。 |

#### プロパティ

| メンバー | 説明 |
|---|---|
| `P2PSessionInfo Session { get; }` | 現在(または直近)のセッション情報。`LocalPlayerId` / `OpponentId` / `RoomId` / `IsInitiator` / `State` を持つ通常のクラスです(詳細は後述)。 |

#### イベント

| シグネチャ | 発火タイミング |
|---|---|
| `event Action<P2PSessionState> StateChanged` | `Session.State`が変化するたび。 |
| `event Action<P2PSessionInfo> Matched` | マッチング成立時(WebRTC接続が確立する**前**)。`RoomId`/`OpponentId`/`IsInitiator`が確定したタイミング。 |
| `event Action DataChannelReady` | WebRTCのデータチャンネルがOpenになった瞬間。**送受信を開始してよい合図はこれ**(`Matched`ではなくこちら)。 |
| `event Action<string> ConnectionClosed` | データチャンネルが閉じた、ピア接続が切断/失敗した、または相手が退出した場合。引数は理由の文字列(例: `"peer-left"`, `"data channel closed"`, `RTCPeerConnectionState`名など)。 |

---

### `P2PConfig` (ScriptableObject)

`Assets > Create > RealtimeP2PKit > P2P Config` から作成するアセット。`P2PManager.Initialize()`に渡します。
**接続先URL(Web API / Signaling WebSocket / STUN)はここには含まれません** - `P2PEndpoints`(次項)を参照してください。

| フィールド | 型 | 説明 |
|---|---|---|
| `DataChannelLabel` | `string` | WebRTCデータチャンネルのラベル名。通常は変更不要。 |
| `Reliable` | `bool` | `true`=順序保証・再送あり(TCPライク、ロス時に遅延が出る)。`false`(推奨)=順序保証なし・即座に送りっぱなし。座標同期のような頻繁な状態送信には`false`を推奨。 |
| `MaxRetransmits` | `int` | `Reliable=false`のときのみ有効。再送回数の上限(`0`=再送なし)。 |
| `LogLevel` | `P2PLogLevel` | `None` / `Error` / `Warn` / `Info` / `Verbose`。開発中は`Verbose`、本番は`Info`以下推奨。`P2PLog.Level`(接続フローのログ)にのみ影響し、`P2PNetworkLog`(生データのログ)には影響しません。 |

---

### `P2PEndpoints` / `P2PEnvironment`(接続先のLocal/Remote切り替え)

接続先URLを解決する静的クラスです。`P2PManager`が内部で自動的に呼び出すため、
通常ゲーム側のコードから直接呼ぶ必要はありませんが、仕組みを理解しておくと設定ミスに気づきやすくなります。

```csharp
public enum P2PEnvironment { Local, Remote }

public static class P2PEndpoints
{
    public static P2PEnvironment GetCurrentEnvironment();
    public static string GetMatchmakingApiUrl();
    public static string GetSignalingWebSocketUrl();
    public static List<string> GetStunServerUrls(); // 上から順に使用される
}
```

**挙動がUnity Editorとビルドで異なります**:

- **Unity Editor上**: メニュー `RealtimeP2PKit > Connection Settings` で設定した値(PlayerPrefsに保存)
  が`GetCurrentEnvironment()`の値(`Local`/`Remote`)に応じて返されます。
- **ビルドしたアプリ(Editor外)**: `GetCurrentEnvironment()`は常に`Remote`を返し、各`Get*Url()`も
  PlayerPrefsを一切参照せず、`P2PEndpoints.cs`にハードコードされた`DefaultRemote*`定数を直接返します。
  本番のURLを変えるには、Editorの画面ではなく`P2PEndpoints.cs`の`DefaultRemote*`定数を書き換えて
  コミットする必要があります。

STUNサーバーは複数指定した場合、上から順に(実際にはWebRTCが全件からICE候補を収集する形で)使われます。
既定値はGoogleとMozillaの公開STUNです。

接続設定Editorウィンドウ(`P2PConnectionSettingsWindow`、`Editor/`フォルダ)は、この`P2PEndpoints`の
PlayerPrefsキーを読み書きするだけの薄いUIです。ゲーム固有のEditorツールを自作する場合も、
同じPlayerPrefsキー(`P2PEndpoints.PrefKey*`定数)を使えば設定を共有できます。

---

### `P2PSessionInfo` / `P2PSessionState`

`P2PManager.Instance.Session`から参照する、現在のセッション状態を表す通常のクラスです。

```csharp
public class P2PSessionInfo
{
    public string LocalPlayerId;
    public string OpponentId;
    public string RoomId;
    public bool IsInitiator;
    public P2PSessionState State;
}
```

`P2PSessionState` は次の順で遷移します:

```
Idle → Matchmaking → SignalingConnecting → Negotiating → Connected
                                                              │
                                                              ▼
                                                        Disconnected
```

(`Failed`は将来の拡張用に予約されていますが、現バージョンでは`Disconnected`に統一しています)

---

### `PacketRouter`(内部で自動的に使われる)

`P2PManager`が内部で1つだけ保持しており、`RegisterPacketHandler` / `Send`はすべてこれを経由します。
**通常、ゲーム側のコードがこのクラスを直接newしたり触ったりする必要はありません。**
ワイヤーフォーマットは `[1バイトpacketId][MessagePackエンコードされた本体]` です。

---

### `IPayloadCodec` / `MessagePackPayloadCodec`

送受信データのシリアライズ方式を差し替えるためのインターフェースです。
デフォルトの`MessagePackPayloadCodec`はMessagePack-CSharpを使い、`[MessagePackObject]` +
`[Key(n)]`を付けた型をシリアライズします。

```csharp
public interface IPayloadCodec
{
    byte[] Serialize<T>(T value);
    T Deserialize<T>(byte[] bytes);
}
```

---

### `IMatchmakingClient` / `HttpMatchmakingClient`

```csharp
public interface IMatchmakingClient
{
    Task<MatchmakingResult> JoinQueueAsync(string playerId);
    Task LeaveQueueAsync(string playerId);
}
```

`HttpMatchmakingClient`はサーバーの`POST /api/matchmaking/join`・`/leave`を叩く実装です。
`P2PManager.Initialize()`が`P2PEndpoints.GetMatchmakingApiUrl()`の値で自動生成するため、
通常は自分でnewする必要はありません。

`MatchmakingResult`:

```csharp
public class MatchmakingResult
{
    public string status;       // "waiting" | "matched"
    public string roomId;
    public string opponentId;
    public bool isInitiator;
}
```

---

### `ISignalingClient` / `PartyKitSignalingClient` / `LobbyListener`

WebRTCのSDP/ICE交換に使うシグナリング層です。`ISignalingClient`を実装すれば別のバックエンドに差し替えられます。

- `PartyKitSignalingClient`: `{P2PEndpoints.GetSignalingWebSocketUrl()}/parties/room/{roomId}` に接続し、offer/answer/ice-candidateを中継してもらいます。
- `LobbyListener`: `{P2PEndpoints.GetSignalingWebSocketUrl()}/parties/lobby/{playerId}` に接続し、マッチング成立のpush通知を待ちます。

いずれも`P2PManager`が内部で生成・破棄するため、通常は直接使いません。

---

### `P2PLog` / `P2PNetworkLog`(ログ設計)

**このライブラリはログ出力を1箇所に集約するラッパー(旧`P2PLogger`)を使いません。**
以前のバージョンは`P2PLogger.Info(...)`のような共有メソッド経由で`Debug.Log`を呼んでいましたが、
それだとUnityコンソールでログ行をダブルクリックしたときに常に`P2PLogger.cs`側にジャンプしてしまい
(Unityは実際に`Debug.Log`が呼ばれた場所にジャンプするため)、本来見たい呼び出し元に飛べませんでした。
そのため現在は **ライブラリ内の全ログ呼び出し箇所で`UnityEngine.Debug.Log`/`LogWarning`/`LogError`を
直接呼んでいます**。これでログ行のダブルクリックが常に正しい発生箇所にジャンプします。

冗長度合いを抑えるためのレベル制御だけは残していますが、これも「ログを出すメソッド」ではなく
「現在のレベルを保持するだけの入れ物」です:

```csharp
public static class P2PLog
{
    public static P2PLogLevel Level; // P2PConfig.LogLevel で自動設定される
    public static bool ShouldLog(P2PLogLevel level); // Level >= level
}
```

呼び出し側は必ずこの形で書きます(ライブラリ内はすべてこのパターンです):

```csharp
if (P2PLog.ShouldLog(P2PLogLevel.Info)) Debug.Log("[RealtimeP2PKit][MyClass] ...");
```

自前のコードでも同じ書式に合わせて直接`Debug.Log`を呼ぶことをおすすめします
(`P2PLog.ShouldLog(...)`でレベルだけ揃える形)。

---

`P2PNetworkLog` / `P2PNetworkLogFormat`はHTTP/WebSocket/WebRTC DataChannelの
**生の送受信内容**専用です。`P2PLog.Level`とは独立した、単一のON/OFFトグルで制御されます。
こちらも同じ設計で、`P2PNetworkLog`はON/OFFの状態を持つだけ、実際に表示する文字列を組み立てるのは
`P2PNetworkLogFormat`の各メソッド(いずれも`Debug.Log`を呼ばず、文字列を返すだけ)です:

```csharp
public static class P2PNetworkLog
{
    public static bool IsEnabled { get; set; } // Editor限定。ビルドでは常にfalseで、setも無視される
}

public static class P2PNetworkLogFormat
{
    public static string HttpRequest(string method, string url, string body);
    public static string HttpResponse(string method, string url, long statusCode, bool isError, string body, TimeSpan elapsed);
    public static string WebSocketSend(string context, string message);
    public static string WebSocketReceive(string context, string message);
    public static string WebRtcSend(byte[] payload);
    public static string WebRtcReceive(byte[] payload);
}
```

呼び出し側はやはりこの形です:

```csharp
if (P2PNetworkLog.IsEnabled) Debug.Log(P2PNetworkLogFormat.WebSocketSend("Room", json));
```

- **切り替え方法**: `RealtimeP2PKit > Connection Settings` の "Network Logging" トグル
  (PlayerPrefsキー: `P2PNetworkLog.PrefKeyEnabled`)。
- **Unity Editor上でのみ**ON/OFFを切り替えられます。ビルドしたアプリでは`IsEnabled`は常に`false`を
  返し(`set`も無視されます)、そもそもこの機能に対応するUIも存在しません。
- ONの間、HTTPリクエスト/レスポンス(RTT・サイズ・失敗時は赤字)、WebSocketの送受信メッセージ全文、
  WebRTC DataChannelの送受信バイト列(16進プレビュー)がすべてログに出力されます。

---

## 自前のパケット型を追加する

```csharp
using MessagePack;

[MessagePackObject]
public struct AttackPacket
{
    [Key(0)] public int SkillId;
    [Key(1)] public float Power;
}
```

```csharp
public const byte AttackPacketId = 2; // PositionPacketId=1 と重複しない値にする

P2PManager.Instance.RegisterPacketHandler<AttackPacket>(AttackPacketId, packet =>
{
    Debug.Log($"opponent used skill {packet.SkillId}");
});

// 送信側
P2PManager.Instance.Send(AttackPacketId, new AttackPacket { SkillId = 3, Power = 12.5f });
```

1つのデータチャンネルを複数のパケット種別で共有する設計なので、`packetId`はゲーム内で
重複しないようにだけ管理してください(enumで一元管理するのがおすすめです)。

---

## 差し替え可能なポイント(拡張方法)

| 差し替えたいもの | 実装するインターフェース |
|---|---|
| マッチングバックエンド(Hono以外にしたい) | `IMatchmakingClient` |
| シグナリングバックエンド(partyserver以外にしたい) | `ISignalingClient` |
| シリアライズ形式(MessagePack以外にしたい) | `IPayloadCodec` |

いずれも`P2PManager`はインターフェース越しにしか触っていないため、内部実装を差し替えても
呼び出し側(ゲームコード)のAPIには影響しません(ただし現状`P2PManager`のコンストラクタ相当部分が
`HttpMatchmakingClient`/`PartyKitSignalingClient`/`MessagePackPayloadCodec`を直接newしているため、
差し替える場合は`P2PManager.Initialize()`周辺を書き換えるか、DIできるようoverloadを追加してください)。

---

## 他プロジェクトへの持ち出し方

このフォルダ (`Packages/com.phantomcatworks.realtimep2p`) はUnityの **embedded package** です。

1. このフォルダを丸ごと別プロジェクトの `Packages/` 配下にコピーする(embedded package)
2. このフォルダだけを独立したgitリポジトリに切り出し、`Packages/manifest.json` に
   `"com.phantomcatworks.realtimep2p": "https://github.com/yourname/realtimep2pkit.git"`
   の形でUPM git依存として追加する(推奨・本来の想定形)

ゲーム固有のコード(`Assets/Scripts/Demo`以下)は一切参照していないので、そのまま持ち出せます。
依存パッケージ(`com.unity.webrtc` / `com.unity.nuget.newtonsoft-json` / NativeWebSocket / MessagePack)は
持ち出し先のプロジェクトにも同様にインストールしてください(手順はリポジトリルートのREADME参照)。

---

## トラブルシューティング

| 症状 | 原因・対処 |
|---|---|
| `RealtimeP2PKit`メニューが出てこない / `CS0246`系のコンパイルエラー(`Newtonsoft`/`NativeWebSocket`/`WebSocket`が見つからない等) | `MessagePack`と`Colyseus.NativeWebSocket`がNuGetForUnity経由でインストールされているか確認してください(リポジトリルートREADMEの2-2参照)。それでも直らない場合は`Packages/com.phantomcatworks.realtimep2p/Runtime/PhantomCatWorks.RealtimeP2PKit.asmdef`の`overrideReferences`が`false`になっているか確認してください(`true`のままだとDLLの自動参照が無効化され、明記していないDLLが一切参照されなくなります)。 |
| `Send`を呼んでも何も起きない | `Session.State`が`Connected`でない可能性。`DataChannelReady`イベントを待ってから`Send`してください。 |
| マッチングは成立するが接続しない | 両者が対称NAT(symmetric NAT)配下の可能性。本ライブラリはTURN未使用のため、既知の制限です。 |
| `[WebRTC] cannot send, data channel not open` の警告が出続ける | `DataChannelReady`前に`Send`を呼んでいます。上と同じ対処。 |
| ビルド後(IL2CPP)だけ動かない | MessagePackのAOT対応が必要です。ルートREADMEの「既知の制約」参照。 |
| 送受信データの中身(SDP本文やICE candidate、MessagePackのバイト列)を見たい | `RealtimeP2PKit > Connection Settings` の "Network Logging" トグルをONにしてください。Editor上でのみ有効です。 |
