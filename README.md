# realtime-p2p demo (Unity + WebRTC + partyserver/Cloudflare)

> Unity Hub で開くフォルダはリポジトリ直下ではなく **`unity-client/`** です。
> ライブラリ本体と接続設定 Window は `unity-client/Assets/RealtimeP2PKit/`、
> 動作例は `unity-client/Assets/Example/` にあります。
> `RealtimeP2PKit > Connection Settings` では Local / Remote をプルダウンで選び、
> シグナリング WebSocket URL と複数の STUN URL を設定します。Player ビルドは常に Remote を使います。
> マッチングと HTTP クライアントはライブラリには含まず、`unity-client/Assets/Example/Matchmaking/` にあります。
> `RealtimeP2PKit > Example Connection Settings` でルーム管理用 HTTP Base URL と完全な Lobby WebSocket URL を設定します。
> `Assets/RealtimeP2PKit/` と `Assets/Example/` の境界が Package に含めるかどうかの境界です。

DemoのUGUIはUnity Localizationで日本語・英語に対応しています。各画面の言語プルダウンで切り替えられ、選択は保存されます。初回はシステム言語を使い、対応しない言語の場合は英語になります。
翻訳テーブルと設定は同梱済みで、生成メニューの実行は不要です。翻訳は `Window > Asset Management > Localization Tables` の `Example UI` で編集します。
多言語UIの処理はDemoの `Assets/Example/Localization/` に置き、通信ライブラリ本体にはLocalizationの依存を追加していません。日本語表示用のNoto Sans JPとライセンスは `Assets/Example/Localization/Fonts/` にあります。
Demoの接続設定Windowは `Assets/Editor/Example/` にあります。Demo・テストのasmdefはテストからDemoを参照するためのもので、UPM公開対象ではありません。UPM公開対象は `Assets/RealtimeP2PKit/` のRuntime・Editorです。


複数参加者のリアルタイム通信の実証実験。座標(xyz)を各参加者へのWebRTC DataChannel経由でP2P直接送信し、
マッチング/シグナリングは **1つのCloudflare Worker** で行う構成です。

```
[Unity A]                                        [Unity B]
   |  1. POST /api/matchmaking/rooms / {id}/join     |
   |------------------> realtime-p2p-server <--------|
   |                (Hono + D1 + partyserver,         |
   |                 ひとつの Cloudflare Worker)       |
   |   2. HTTPで参加枠を確保してP2PExampleへ遷移       |
   |  3. wss://.../signaling に接続し、参加メッセージを送信
   |<===== SDP offer/answer, ICE candidates ==========>|
   |  4. WebRTC P2P DataChannel (STUNのみ, TURNなし)     |
   |<========= MessagePack encoded xyz ===============>|
```

- **ルーム管理**: Hono + D1（参加者・ルーム・参加枠。IDはAUTOINCREMENT）
- **シグナリング**: [partyserver](https://github.com/cloudflare/partykit/tree/main/packages/partyserver)
  の `Server` クラスでWebSocketを管理します。Honoが固定の `/signaling` からDurable Objectへ接続を渡し、Roomは参加メッセージで選びます。
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
    env.ts                  Bindings型 (DB, Lobby, Signaling)
    routes/matchmaking.ts   /players, /rooms, /rooms/:id/join, /rooms/:id/leave
    party/lobby.ts          /parties/lobby/rooms ― 新規ルームの差分通知
    party/signaling.ts      固定エンドポイントのDurable Object。参加済みRoom内で宛先付きSDP/ICEを中継
    db/schema.ts, db/client.ts
  wrangler.jsonc             D1バインディング + Durable Objectバインディングを1ファイルに
  migrations/                D1マイグレーション

unity-client/
  Assets/RealtimeP2PKit/                      配布するライブラリ本体（Runtime / Editor）
  Assets/Example/                             Packageに含めない動作例（Script / Scene / Prefab / Matchmaking）
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

`wrangler dev`実行中は、Unity側で `RealtimeP2PKit > Connection Settings` を開き、Environmentを
`Local`に切り替えてください(既定値がそのまま`http://localhost:8787` / `ws://localhost:8787`を
指しているので、通常は追加設定不要です)。

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

**過去のバージョンで実際に起きていた不具合**: `net.taptappun.RealtimeP2PKit.asmdef` に
`"overrideReferences": true` を設定していたため、Unityの「プロジェクト内のDLLを自動参照する」
デフォルト挙動が無効化され、明記した`MessagePack.dll`だけが参照されて`Newtonsoft.Json.dll`や
NativeWebSocketのDLLが参照されない状態になっていました(Package自体は入っているのに
`CS0246: The type or namespace name 'Newtonsoft' could not be found`のようなエラーになる不思議な現象は
これが原因でした)。現在は`overrideReferences: false`に修正済みで、DLLを個別に指定しなくても
自動参照されるようになっています。

### 2-3. コンパイルが通ることを確認

Unityの Console にエラーが出ていない状態が正常です。エラーが残っている場合は
`Assets > Reimport All` や Unity再起動で解消することがあります(NuGetForUnityの新規DLL認識のため)。

### 2-4. Example Sceneを使う

このリポジトリには動作例のSceneとPrefabを同梱しています。
`MatchingRoomExample.unity` を開いて実行してください。

- `Assets/Example/Scenes/P2PExample.unity`
- `Assets/Example/Scenes/MatchingRoomExample.unity`（Build Settingsの開始シーン）
- `Assets/Example/Prefabs/LocalPlayer.prefab`, `Assets/Example/Prefabs/RemotePlayer.prefab`
- `Assets/Example/Config/P2PConfig.asset`（任意の通信設定）

ゲーム画面はUGUIで作成しています。MatchingRoomExampleの定員入力・作成・Join・一覧更新は
InputField / Button / ScrollRect、P2PExampleの人数表示・退出操作はText / Buttonです。
Canvasと参照はScene・Prefabに保存済みで、Hierarchy / Inspectorから配置や見た目を編集できます。
一覧の各行は `Assets/Example/Prefabs/RoomListRow.prefab`、表示処理は `Assets/Example/UI/` にあります。

一覧は最初に `GET /api/matchmaking/rooms` で取得し、その最大RoomId（空なら `"0"`）を使って
`/parties/lobby/rooms` にWebSocket接続します。接続後に
`{"type":"subscribe","lastRoomId":"123"}` を送ると、そのIDより大きい現在有効なRoomだけが
`{"type":"rooms-created","rooms":[...]}` で届き、以降の新規作成も差分だけが通知されます。
HTTP取得から接続までの間、および切断中に作られたRoomも、再接続時のカーソルから補完します。
定期HTTPポーリングは行いません。新規追加の通知なので、既存Roomの人数変更・削除は
「一覧を更新」で再取得します。Join時の定員・Roomの存続はサーバーで再確認します。

接続先(サーバーのURL)は`P2PConfig`アセットではなく、次の「2-5. 接続先(Local/Remote)を設定する」で
説明するEditorツールで設定します。

自分でSceneを組む場合は、README末尾の「手動でSceneを組む場合」を参照してください。

### 2-5. 接続先(Local/Remote)を設定する

メニュー `RealtimeP2PKit > Connection Settings` を開きます。VRoid SDKの`SDKDebugger`と同様の
Editor拡張ウィンドウで、以下を設定できます:

- **Environment**: 現在の接続先が`Local`か`Remote`かをプルダウンで切り替え(PlayerPrefsに記録)。
  **切り替えると、その下に表示される入力欄が選択中の環境のものだけに差し替わります**
  (LocalとRemoteが同時に並んで表示されることはありません)。
- 選択中の環境について
  - Signaling WebSocket URL（パスを含む完全なURL。例: `wss://signaling.example.com/sessions/42`）
  - STUN Server URLs(**上から順に使用される複数エントリのリスト**。↑↓ボタンで並び替え、＋で追加、✕で削除)

  を入力し、**Save Local / Remote Settings** ボタンで `Assets/Resources/P2PConnectionSettings.asset` に保存します。
- **Network Logging**: HTTPのURL・ステータス、およびWebSocket/WebRTC DataChannelの送受信内容をログ出力する
  トグル(詳細は後述)。

接続先は Resources アセットに保存され、Player ビルドにも含まれます。
Editor の Local / Remote 選択は PlayerPrefs に記録されます。Player ビルドは常に Remote を使います。

Example のマッチング接続先は `RealtimeP2PKit > Example Connection Settings` で設定します。
Local / Remote ごとに **HTTP Base URL**（例: `http://localhost:8787`）と
**Lobby WebSocket URL**（例: `ws://localhost:8787/parties/lobby/rooms`）を入力し、
**Save Local / Remote Settings** で保存してください。Remote はデプロイ先の `https://...` / `wss://...` に変更してください。
HTTPのみDemoが `/api/matchmaking/...` を追加します。Lobbyもシグナリングも、WebSocketにはパスを含む完全なURLを指定します。
設定アセットは `Assets/Example/Resources/ExampleConnectionSettings.asset` にあり、パッケージ外に置かれます。
Editorの環境選択はパッケージと共通で、PlayerはRemote固定です。
`ExampleMatchmakingFlow` の参加者登録・ルーム作成・一覧・参加・退出はこの HTTP 設定を使います。
Example のシグナリングURLとSTUNは `Connection Settings` を使います。Demo同梱アセットの初期URLは `ws://localhost:8787/signaling` です。Remoteはデプロイ先の `wss://.../signaling` に変更してください。
ライブラリ本体には `ConnectAsync(localPeerId, signalingClient)` でシグナリング実装を渡します。汎用の `WebSocketSignalingTransport` は完全なURLをそのまま使用し、パス・認証情報・JSON形式を追加しません。WindowのURLは利用側のアダプターが `P2PEndpoints.GetSignalingWebSocketUrl()` で取得できます。DemoもこのURLを使い、Room IDや認証情報をURLに追加しません。
Demoの `Assets/Example/Signaling/PartyKitSignalingClient.cs` がRoom参加・認証メッセージ・JSON形式・heartbeatを扱います。Room管理のHTTP処理もDemoのみです。

DemoサーバーではHTTPで参加予約した後、固定URL `/signaling` に接続し、最初に
`{"type":"join","roomId":"42","playerId":"123","token":"..."}` を送ります。
D1の予約と認証が一致すると `room-joined` が返り、それ以降は所属Room内の相手だけに中継します。
未参加の通信や予約のない参加は拒否します。Room IDと認証情報はURLに含めません。
シグナリング接続は1つのDurable Objectで受け、接続状態のRoom IDで配信先を選びます。
これはDemoの構成であり、汎用ライブラリの制約ではありません。

旧 `/parties/room/{roomId}` との互換性はありません。サーバーとDemoを一緒に更新してください。
初期定義は `wrangler.jsonc` のv1とD1の `0000_smart_vulture.sql` にまとめています。

ライブラリの新規設定の初期値は次の通りです:

| | Local(既定値) | Remote(既定値) |
|---|---|---|
| Signaling WebSocket URL | 未設定（完全なURLを指定） | 未設定（完全なURLを指定） |
| STUN Server URLs | Google / Mozilla の公開STUN(下記) | 同左 |

```
stun:stun.l.google.com:19302
stun:stun1.l.google.com:19302
stun:stun.services.mozilla.com:3478
```

### 2-6. 実行して動作確認

複数の実機、または複製したUnityプロジェクトで `MatchingRoomExample` シーンを再生します。
1人が「新しいRoomを作成」を押すと、すぐに `P2PExample` へ移動してプレイヤーを操作できます。
ほかのクライアントには一覧にRoomが表示され、「Join」で同じシーンへ移動します。
参加者全員が相手ごとにWebRTC接続を持ち、全員に座標を送信します。受信側は送信元IDごとにCubeを表示します。
作成画面の定員は自分を含む人数で、既定は4、0は無制限です。Inspectorの `_defaultMaxPlayers` でも初期値を変更できます。
ルームの作成者が退出しても残りの参加者は通信を継続します。最後の参加者の退出時だけRoomを削除します。
P2PExampleを直接再生した場合はMatchingRoomExampleへ戻ります。

#### Multiplayer Play Modeで非フォーカス時の通信を確認

`Run In Background` を有効にし、Example起動時にも `Application.runInBackground = true` を設定しています。
Lobbyの更新、WebRTC接続、約20Hzの座標送信、受信したCubeの描画は、フォーカスを外しても継続します。
キー入力による移動はフォーカス中の画面でのみ行います。

1. `MatchingRoomExample` をMultiplayer Play Modeの4画面で再生し、全員で同じRoomへ参加します。
2. 各画面が `P2P接続数: 3` となり、自分と相手3人のCubeが表示されることを確認します。
3. 1画面にフォーカスを置いたままWASDで移動し、他の3画面のCubeも動くことを確認します。
4. Editor / Development Buildでは画面下部に通信確認用の表示が出ます。非フォーカスの画面で
   `Focus: False`、`Run in Background: True` のまま、相手ごとの `RX` (受信数) が増えることを確認します。
   移動を止めても座標は送信されるため、非フォーカスの相手からの `RX` も増え続け、最終受信からの秒数は小さい値を保ちます。

変更前からPlay中の場合は、一度停止して再生し直してください。

Consoleに`[RealtimeP2PKit]`プレフィックス付きのログが大量に出るので、`P2PConfig.LogLevel`を
`Info`にしておくと接続フローを追いやすいです。ログは共有ラッパーを介さず各呼び出し箇所で
直接`Debug.Log`/`LogWarning`/`LogError`を呼んでいるので、Consoleでログ行をダブルクリックすると
常にそのログを実際に出したコード行にジャンプします。
送受信データの内容(WebSocketメッセージ、WebRTC DataChannelの
バイト列)まで見たい場合は、上記の`RealtimeP2PKit > Connection Settings`の
**Network Logging** トグルをONにしてください(こちらもEditor上でのみON/OFFできます)。
HTTPはメソッド・URL・ステータスのみ記録し、認証トークンはログに出しません。

## ライブラリの使い方(クイックスタート・APIリファレンス)

`unity-client/Assets/RealtimeP2PKit` の使い方は、詳細を
**同フォルダの README.md** に集約しています。最短の使い方は次の5行です:

```csharp
P2PManager.Instance.Initialize();
P2PManager.Instance.RegisterPacketHandler<MyPacket>(1, (senderId, packet) => { ... });
P2PManager.Instance.PeerConnected += peerId => { /* 相手の表示を追加 */ };
await P2PManager.Instance.ConnectAsync(localPeerId, signalingClient); // 利用側のISignalingClient実装
P2PManager.Instance.Send(1, new MyPacket { ... });
```

`P2PConfig` は任意です。`Initialize()` または `Initialize(null)` は、チャンネル名 `gameplay`、
非Reliable（順序保証なし・再送なし）、ログレベル `Info` で初期化します。
変更したい場合だけ `Initialize(myConfig)` に設定アセットを渡してください。

## 手動でSceneを組む場合

自分でSceneを構築する場合、必要なGameObjectは
以下の3つだけです(いずれも空のSceneに配置):

1. **`ExampleBootstrap`** という名前のGameObjectを作成し、`ExampleBootstrap`コンポーネントを追加。
   必須の `ExampleMatchmakingFlow` も同じ GameObject に自動で追加されます（シーンに保存され、実行時は既存のコンポーネントを使います）。
   Inspectorで以下を割り当てる:
   - `Config` : 任意。未指定ならデフォルト値を使用。変更する場合は `P2PConfig`アセットを割り当てます
     (`Assets > Create > RealtimeP2PKit > P2P Config`で作成。
     Exampleの接続先URLは `RealtimeP2PKit > Example Connection Settings` で設定します)
   - `Local Player Prefab` : `ExamplePlayerController`コンポーネントを付けたCubeのPrefab
   - `Remote Player Prefab` : 何もスクリプトを付けていないCubeのPrefab
     (`ExampleRemotePlayerSync`は`ExampleBootstrap`が実行時に自動でAddComponentします)
2. カメラとライトは通常のSceneと同様(`Main Camera` + `Directional Light`)。
3. 床は任意(Plane等、見た目のためだけ)。

MatchingRoomExampleには `MatchingRoomExampleController` を配置します（必須の `ExampleMatchmakingFlow` が一緒に付きます）。
両シーンをBuild Settingsに追加してください。HTTPで参加枠を確保すると `ExampleRoomSession` に情報を保存し、
P2PExample側の `ExampleBootstrap` がWebSocketに接続します。IDはサーバーの連番で、クライアントは生成しません。
接続相手ごとに送信元ID付きの受信処理を行い、相手の退出ではその相手のCubeだけを削除します。

自作ゲームに組み込む場合は`ExampleBootstrap`をそのまま参考にしつつ、`P2PManager.Instance`を
直接呼び出すのが一番シンプルです(パッケージ側READMEのAPIリファレンス参照)。

## 既知の制約・注意点

- **TURNサーバー未使用**: 要件通りSTUNのみの直接P2Pです。両者が対称NAT(symmetric NAT)配下だと
  接続できません。実運用では coturn 等のTURNフォールバックを検討してください。
- **STUNサーバー**: `stun.l.google.com:19302` 等のGoogleの公開STUNは広く使われていますが、
  Googleが公式にドキュメント化・SLA保証しているサービスではないため、将来的に制限される
  可能性があります。`RealtimeP2PKit > Connection Settings` のSTUN Server URLsはリスト形式なので
  複数フォールバックを設定できます(既定でGoogleとMozillaの公開STUNを設定済み)。
- **com.unity.webrtc の非推奨化**: 上記の通り、Unity 6000.4以降で動作しないという報告があります。
- **MessagePack + IL2CPP**: デフォルトの動的コード生成はIL2CPP/AOTビルドで動作しません。
  実機ビルドを行う場合は `mpc` (MessagePack Code Generator) で事前コード生成し、
  `MessagePackPayloadCodec` に生成された `GeneratedResolver` を渡してください。
- **メッシュの規模**: ライブラリに固定の人数制限はありません。n人で各クライアントはn−1本、全体はn(n−1)/2本の接続を持つため、人数増加に伴って端末・帯域・サーバー基盤の実用上の限界があります。
- **退出検知**: 正常な退出・WebSocket切断はすぐ反映します。切断通知が届かない場合は45秒のリースと15秒間隔のアラームで、最後のハートビートから最大約60秒で削除します。

## マルチプレイヤー版への更新とサーバーテスト

サーバーとUnityを同時に更新し、`server` で `pnpm db:migrate:local` を実行してください。
本番環境を更新する場合はデプロイに合わせて `pnpm db:migrate:remote` が必要です。
初期migration `0000_smart_vulture.sql` で、
`players`・`game_rooms`・`room_members` を `INTEGER PRIMARY KEY AUTOINCREMENT` で作成します。
新しい通信プロトコルで旧ルームを継続することはできません。IDは識別にだけ使い、ホスト権限やOffer役割をIDから導きません。
セキュリティ用トークンはIDとは別に発行します。

Node.js 22.12以降で `pnpm typecheck` と `pnpm test` を実行できます。
`server/tests/rooms.integration.spec.ts` をVitestで実行し、既存の `tsconfig.json` でテストも型チェックします。
統合テストは一時ローカルD1/Workerを使い、
複数参加・同時Joinの定員・宛先付きシグナリング・退出・切断・空ルーム削除・期限切れを確認します。

## ライブラリの再利用について

`unity-client/Assets/RealtimeP2PKit` はWebRTC接続〜データ交換の流れを
汎用化したライブラリです。`Assets/Example` のスクリプト、Scene、Prefab、マッチング処理は
配布対象に含めません。エントリーポイントは `P2PManager` シングルトンです。
詳細・全メソッドのリファレンスは同フォルダの README.md を参照してください。
