# realtime-p2p demo (Unity + WebRTC + partyserver/Cloudflare)

1対1リアルタイム対戦ゲームの実証実験。座標(xyz)をWebRTC DataChannel経由でP2P直接送信し、
マッチング/シグナリングは **1つのCloudflare Worker** で行う構成です。

```
[Unity A]                                        [Unity B]
   |  1. POST /api/matchmaking/join                  |
   |------------------> realtime-p2p-server <--------|
   |                (Hono + D1 + partyserver,         |
   |                 ひとつの Cloudflare Worker)       |
   |   2. Lobby Durable Object を直接呼び出してpush     |
   |<--------------------------+                      |
   |  3. wss://.../parties/room/{roomId} に接続(signaling)
   |<===== SDP offer/answer, ICE candidates ==========>|
   |  4. WebRTC P2P DataChannel (STUNのみ, TURNなし)     |
   |<========= MessagePack encoded xyz ===============>|
```

- **マッチング**: Hono + Drizzle ORM + D1
- **シグナリング**: [partyserver](https://github.com/cloudflare/partykit/tree/main/packages/partyserver)
  (Cloudflareが公式に配布している「PartyKitのDurable Object実装」。`Server`クラスを
  Durable Objectとして同じWorkerにバインドするだけで、PartyKitと同じ`wss://.../parties/{party}/{room}`
  というルーティング規約のWebSocketサーバーになります)
- **P2P本体**: Unity (`com.unity.webrtc`) + MessagePack、STUNのみ・TURNなし直接P2P

**重要**: matching-api(REST)とsignaling(WebSocket)は **同じ`wrangler.jsonc`・同じ`src/index.ts`・
同じ`pnpm deploy`で1つのCloudflare Workerとしてデプロイされます**。以前のリビジョンでは
PartyKit CLIで別ホストとしてデプロイする構成でしたが、それだと事実上サーバーが2つに分かれてしまうため、
`partyserver`ライブラリを使って1Worker内のDurable Objectとして統合しました。

## ディレクトリ構成

```
server/                     単一のCloudflare Worker (Hono + Drizzle + D1 + partyserver)
  src/
    index.ts                fetchハンドラのエントリーポイント。Hono REST と
                             partyserverのWebSocketルーティングをここで1本化
    env.ts                  Bindings型 (DB, Lobby, Room)
    routes/matchmaking.ts   POST /api/matchmaking/join, /leave, GET /status/:id
    party/lobby.ts          Durable Object "Lobby" (1プレイヤーにつき1インスタンス)
    party/room.ts           Durable Object "Room"  (1対戦につき1インスタンス、SDP/ICE中継)
    db/schema.ts, db/client.ts
  wrangler.jsonc             D1バインディング + Durable Objectバインディングを1ファイルに
  migrations/                D1マイグレーション

unity-client/
  Packages/com.phantomcatworks.realtimep2p/   再利用可能なP2Pライブラリ (UPM embedded package)
                                               → 詳細なAPIリファレンスは同フォルダのREADME.md
  Assets/Scripts/Demo/                        ライブラリを使うサンプルコード(Scene/Prefabは未同梱、後述)
```

## 1. サーバーのセットアップ

```bash
cd server
pnpm install   # または npm install

# D1データベースを作成
npx wrangler d1 create realtime-p2p-db
# 出力された database_id を wrangler.jsonc の d1_databases[0].database_id に反映

pnpm db:migrate:remote     # 本番D1にマイグレーション適用
pnpm deploy                # 1コマンドで matching-api + signaling を同時デプロイ
# => https://realtime-p2p-server.<your-account>.workers.dev が発行される
```

ローカル開発:

```bash
pnpm db:migrate:local
pnpm dev        # wrangler dev、REST APIもWebSocketも同じ http://127.0.0.1:8787 で動く
```

`wrangler dev`実行中は、Unity側の`P2PConfig.ServerHost`を`127.0.0.1:8787`、
`UseSecureConnection`を`false`(http/ws)にすれば手元だけで疎通確認できます。

## 2. Unity のセットアップ

**この順番を必ず守ってください。** 順番を間違えると(特にMessagePackを後回しにすると)
ライブラリのコードがコンパイルエラーになり、`RealtimeP2PKit`メニュー自体が出てこなくなります。

### 2-1. Unityで`unity-client/`を開く

Unity 6000.3系(Unity 6 LTS相当)で作成しています。`Packages/manifest.json`に依存パッケージ
(`com.unity.webrtc`, `com.unity.nuget.newtonsoft-json`, NativeWebSocket, NuGetForUnity, 本ライブラリ)
は既に登録済みなので、Unity Hubで開けば自動的に解決されます(初回はダウンロードに数分かかります)。

> **⚠️ com.unity.webrtc の既知の注意点**: Unity公式discussionsで、Unity 6000.4以降では
> `com.unity.webrtc`が非推奨化されて動作しないと報告されています
> (https://discussions.unity.com/t/is-com-unity-webrtc-still-supported/1718939)。
> 現状Unity 6000.3系では動作しますが、Unityのバージョンを上げる際はこの点にご注意ください。

### 2-2. MessagePack と NativeWebSocket を NuGetForUnity でインストール(★ここが一番詰まりやすいポイント)

1. Unity起動後、メニュー `NuGet > Manage NuGet Packages` を開く
2. **MessagePack**(neuecc/MessagePack-CSharp)を検索してインストール
3. **Colyseus.NativeWebSocket**(endel/NativeWebSocket の現行2.x系、NuGet配布版)を検索してインストール
   (以前のリビジョンではgitパッケージ版`com.endel.nativewebsocket`(1.x系)を使っていましたが、
   `manifest.json`からは削除しました。NuGet版と共存すると型の重複が起きるため、NuGet版に一本化しています)
4. `Assets/Packages/` に `MessagePack.x.x.x` と `Colyseus.NativeWebSocket.x.x.x` が展開されたことを確認

**過去のバージョンで実際に起きていた不具合**: `PhantomCatWorks.RealtimeP2PKit.asmdef` に
`"overrideReferences": true` を設定していたため、Unityの「プロジェクト内のDLLを自動参照する」
デフォルト挙動が無効化され、明記した`MessagePack.dll`だけが参照されて`Newtonsoft.Json.dll`や
NativeWebSocketのDLLが参照されない状態になっていました(Package自体は入っているのに
`CS0246: The type or namespace name 'Newtonsoft' could not be found`のようなエラーになる不思議な現象は
これが原因でした)。現在は`overrideReferences: false`に修正済みで、DLLを個別に指定しなくても
自動参照されるようになっています。

### 2-3. コンパイルが通ることを確認

Unityの Console にエラーが出ていない状態が正常です。エラーが残っている場合は
`Assets > Reimport All` や Unity再起動で解消することがあります(NuGetForUnityの新規DLL認識のため)。

### 2-4. デモSceneを生成する

このリポジトリには **`.unity`シーンや`.prefab`は同梱していません**(バイナリ/YAMLの生成物を
手で編集するのは壊れやすいため、コードから再現できるようにしています)。
メニュー `RealtimeP2PKit > Build Demo Scene` を実行すると、以下が自動生成されます:

- `Assets/Scenes/P2PDemo.unity`
- `Assets/Prefabs/LocalPlayer.prefab`, `Assets/Prefabs/RemotePlayer.prefab`
- `Assets/Resources/P2PConfig.asset`(未作成の場合)

生成後、`Assets/Resources/P2PConfig.asset` を選択し、Inspectorで **`Server Host`** に
上でデプロイしたWorkerのホスト名(`https://`なしのホスト名のみ、例:
`realtime-p2p-server.<your-account>.workers.dev`)を設定してください。

自分でSceneを組む場合は、README末尾の「手動でSceneを組む場合」を参照してください
(`Build Demo Scene`が生成する内容と同じものを手作業で再現する手順です)。

### 2-5. 実行して動作確認

2台の実機、または `ParrelSync` 等で複製した2つのUnityエディタで `P2PDemo` シーンを再生します。
2人がキューに入ると自動的にマッチングし、WebRTC接続が確立してcubeが同期し始めます。
Consoleに`[RealtimeP2PKit]`プレフィックス付きのログが大量に出るので、`P2PConfig.LogLevel`を
`Info`にしておくと接続フローを追いやすいです(`Verbose`にすると送受信データの中身まで出ます)。

## ライブラリの使い方(クイックスタート・APIリファレンス)

`unity-client/Packages/com.phantomcatworks.realtimep2p` の使い方は、詳細を
**同フォルダの README.md** に集約しています。最短の使い方は次の5行です:

```csharp
P2PManager.Instance.Initialize(myConfig);
P2PManager.Instance.RegisterPacketHandler<MyPacket>(1, packet => { ... });
P2PManager.Instance.DataChannelReady += () => { /* 対戦開始 */ };
P2PManager.Instance.StartMatchmaking(myPlayerId);
P2PManager.Instance.Send(1, new MyPacket { ... });
```

## 手動でSceneを組む場合

`RealtimeP2PKit > Build Demo Scene` を使わず自分でSceneを構築する場合、必要なGameObjectは
以下の3つだけです(いずれも空のSceneに配置):

1. **`DemoBootstrap`** という名前のGameObjectを作成し、`DemoBootstrap`コンポーネントを追加。
   Inspectorで以下を割り当てる:
   - `Config` : `P2PConfig`アセット(`Assets > Create > RealtimeP2PKit > P2P Config`で作成し、
     `Server Host`にデプロイ済みWorkerのホスト名を設定)
   - `Local Player Prefab` : `DemoPlayerController`コンポーネントを付けたCubeのPrefab
   - `Remote Player Prefab` : 何もスクリプトを付けていないCubeのPrefab
     (`DemoRemotePlayerSync`は`DemoBootstrap`が実行時に自動でAddComponentします)
2. カメラとライトは通常のSceneと同様(`Main Camera` + `Directional Light`)。
3. 床は任意(Plane等、見た目のためだけ)。

Play再生すると`DemoBootstrap.Start()`がランダムなplayerIdでマッチングを開始し、
対戦相手が見つかり次第、自動で2体のCubeをInstantiateしてP2P同期を開始します
(`Assets/Scripts/Demo/DemoBootstrap.cs`の中身がそのままロジックです)。

自作ゲームに組み込む場合は`DemoBootstrap`をそのまま参考にしつつ、`P2PManager.Instance`を
直接呼び出すのが一番シンプルです(パッケージ側READMEのAPIリファレンス参照)。

## 既知の制約・注意点

- **TURNサーバー未使用**: 要件通りSTUNのみの直接P2Pです。両者が対称NAT(symmetric NAT)配下だと
  接続できません。実運用では coturn 等のTURNフォールバックを検討してください。
- **STUNサーバー**: `stun.l.google.com:19302` 等のGoogleの公開STUNは広く使われていますが、
  Googleが公式にドキュメント化・SLA保証しているサービスではないため、将来的に制限される
  可能性があります。`P2PConfig.StunServerUrls` は配列なので複数フォールバックを設定できます。
- **com.unity.webrtc の非推奨化**: 上記の通り、Unity 6000.4以降で動作しないという報告があります。
- **MessagePack + IL2CPP**: デフォルトの動的コード生成はIL2CPP/AOTビルドで動作しません。
  実機ビルドを行う場合は `mpc` (MessagePack Code Generator) で事前コード生成し、
  `MessagePackPayloadCodec` に生成された `GeneratedResolver` を渡してください。
- **D1のマッチング整合性**: デモ用の簡易実装のため、同時に大量の join が来た場合の
  完全な原子性は保証していません。本番運用ではDurable Object等でのロックを検討してください。

## ライブラリの再利用について

`unity-client/Packages/com.phantomcatworks.realtimep2p` はWebRTC接続〜データ交換の流れを
汎用化した独立パッケージです。エントリーポイントは `P2PManager` シングルトンのみで、
マッチング/シグナリング/WebRTC/シリアライズの各層はインターフェース越しに差し替え可能です。
詳細・全メソッドのリファレンスは同フォルダの README.md を参照してください。
