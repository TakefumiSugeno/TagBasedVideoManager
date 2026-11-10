# Design: 稼働アプリケーションバージョンの画面表示とセマンティックバージョニング規則の制定

## Context

See `proposal.md - Why`。
現在のシステムは以下の技術スタックで構成されています：

- **Webバックエンド**: F# (.NET 10) + Giraffe
- **Webフロントエンド**: HTML5 + Alpine.js (CDN) + Tailwind CSS (CDN)（Node.js / ビルドツールなし）
- **デスクトップアプリ**: F# (.NET 10) + Avalonia.FuncUI (Elmish MVU)
- **仕様駆動/テスト駆動**: OpenSpec (spec-driven, L1/L2/L3 規模レベル)

これまで両プロジェクトの `.fsproj` に `<Version>` タグが明示されておらず、画面上でバージョンを確認する術がありませんでした。

---

## Goals / Non-Goals

### Goals

- .NET の `<Version>` 要素を Single Source of Truth としたセマンティックバージョニング（SemVer 2.0.0）の導入。
- バックエンド API（`GET /api/version`）による軽量なバージョン情報提供とブラウザキャッシュ防止。
- Web 画面ヘッダー（デスクトップおよびモバイル）への視認性の高いバージョンバッジ表示と通信ハング耐性。
- デスクトップアプリのネイティブウィンドウタイトルバーへのバージョン表示。
- バックエンドおよびデスクトップアプリの起動時バージョンログ出力（可観測性向上）。
- OpenSpec 変更規模（L1/L2/L3）およびユーザー指示に連動した採番運用の確立。

### Non-Goals

- ビルドメタデータや Git コミットハッシュの画面常時表示。
- バージョンバッジのクリックによるダイアログ表示や外部通信（静的表示に徹する）。
- 自動アップデート機能や最新バージョンの通知機能。

---

## Architecture & Data Flow

```text
+---------------------------------------------------------------------------------+
| 【Web アプリケーション】                                                        |
|                                                                                 |
|  TagBasedVideoManager.fsproj                                                    |
|    <Version>0.1.0</Version>                                                     |
|                 │                                                               |
|                 ▼                                                               |
|  HttpHandlers.fs (純粋関数 formatAssemblyVersion による安全取得)                |
|    route "/api/version" >=> setHttpHeader "Cache-Control" "no-cache, ..."       |
|                         >=> json {| version = "0.1.0" |}                        |
|                 │                                                               |
|                 ▼ HTTP GET /api/version (2秒タイムアウト・独立非同期実行)       |
|  app.js (Alpine.js init - Bulkheadパターン)                                     |
|    fetch('/api/version', { signal: AbortSignal.timeout(2000) })                 |
|      .then(...) ──> appVersion = 'v0.1.0'                                       |
|    ※ コアデータ（動画・タグ）の取得処理とは完全分離・ブロックなし               |
|                 │                                                               |
|                 ▼                                                               |
|  index.html (Header & <title>)                                                  |
|    <div class="flex items-center gap-1.5 flex-wrap">                            |
|      <h1 class="... bg-clip-text text-transparent">Tag-Based Video Manager</h1> |
|      <span x-show="appVersion" x-cloak class="... text-indigo-300">v0.1.0</span>|
|    </div>                                                                       |
+---------------------------------------------------------------------------------+

+---------------------------------------------------------------------------------+
| 【デスクトップアプリケーション (AI File Renamer)】                              |
|                                                                                 |
|  TagBasedVideoManager.Renamer.fsproj                                            |
|    <Version>0.1.0</Version>                                                     |
|                 │                                                               |
|                 ▼                                                               |
|  Program.fs (MainWindow 初期化 & 純粋関数 formatTitle)                          |
|    let title = formatTitle (Some assemblyVersion)                               |
|    base.Title <- title                                                          |
|    printfn "%s started" title                                                   |
+---------------------------------------------------------------------------------+
```

---

## Decisions

### 1. バージョンの Single Source of Truth とサニタイズ

- **決定**: 各プロジェクトの `.fsproj` 内 `<Version>` 要素で管理し、アセンブリ取得時に `+` 以降のコミットハッシュを除去する純粋関数を用意する。
  ```fsharp
  let formatAssemblyVersion (versionOpt: Version option) : string =
      match versionOpt with
      | Some v -> sprintf "%d.%d.%d" v.Major v.Minor v.Build
      | None -> "0.0.0"
  ```
- **理由**: .NET SDK が自動付与する InformationalVersion（Git ハッシュ）を確実にトリミングし、純粋な `X.Y.Z` 形式を保証する。また、テスト時にモック不要で境界値（None）を検証できる。

### 2. Web API のキャッシュ抑止（Cache-Control）

- **決定**: `GET /api/version` のレスポンスに `Cache-Control: no-cache, no-store, must-revalidate` ヘッダーを付与する。
- **理由**: コンテナ更新後、ブラウザがディスクキャッシュや 304 応答により古いバージョン文字列を表示し続ける事故（「更新したはずなのにバージョンが変わらない」）を防止する。

### 3. フロントエンド通信の耐障害性と非同期隔離（Bulkhead パターン）

- **決定**:
  - `fetch('/api/version')` に短時間タイムアウト（`AbortSignal.timeout(2000)`）を設定する。
  - `app.js` の `init()` において、バージョン取得処理を動画やタグの取得フローから完全に独立した非同期タスクとして起動（Fire-and-Forget）する。
  ```javascript
  // 独立非同期タスクとして実行（コア初期化を一切ブロックしない）
  this.fetchAppVersion();
  // コア初期化
  await Promise.all([this.fetchTags(), this.fetchRules(), this.fetchVideos()]);
  ```
- **理由**: バージョンバッジという副次的な装飾の取得遅延・ハングによって、動画一覧等のコア業務画面が真っ白のままフリーズする連鎖崩壊を構造的に遮断する。

### 4. モバイル折り返しとグラデーション CSS 干渉の防止

- **決定**:
  - タイトル `<h1>` の内部にバッジを配置せず、`flex items-center flex-wrap gap-1.5` の親コンテナ内で `h1` とバッジ `span` を兄弟要素として配置する。
  - バッジには固定の高さ（`h-5`）とインライン幅を確保し、取得完了時のレイアウトシフト（CLS）を最小化する。
- **理由**: 親 `h1` のグラデーションテキスト（`bg-clip-text text-transparent`）が子要素バッジにスタイル継承され文字が透明化するブラウザ互換性不具合を完全に回避する。

### 5. デスクトップアプリの表示箇所とテスト容易性

- **決定**:
  - ウィンドウタイトルバー（`base.Title`）に `TagBasedVideoManager - AI File Renamer v0.1.0` と設定。
  - タイトル文字列生成を純粋関数 `formatTitle: Version option -> string` として切り出し、単体テスト可能とする。
- **理由**: GUI 描画領域を圧迫せず、テストハーネスが不要な純粋関数として単体テストで C0/C1 100% を達成できる。

### 6. 可観測性（起動ログ出力）

- **決定**:
  - Web バックエンド（`Program.fs`）: 起動時に `Log.Information("TagBasedVideoManager v{Version} started", version)` を出力。
  - デスクトップアプリ（`Program.fs`）: 起動時に `printfn "%s started" title` を出力。
- **理由**: 夜間障害時やコンテナデプロイ時に、`docker logs` を確認するだけで現在稼働バージョンを一目で特定可能にする。

### 7. SemVer 採番規則と OpenSpec レベルの連携

- **決定**:
  - **`X` (メジャー)**: L3（破壊的変更・大規模刷新）、または **ユーザーからの明示的指示**。カウントアップ時に Y, Z は 0 リセット。
  - **`Y` (マイナー)**: L2（新機能追加・仕様変更・UI追加）。カウントアップ時に Z は 0 リセット。
  - **`Z` (パッチ)**: L1（不具合修正・typo・軽微な調整）。
- **理由**: OpenSpec のフェーズゲートと完全に整合し、開発者がバージョンの上げ方に迷う余地をなくす。

---

## Error Handling & Resilience

1. **Web API 取得失敗・タイムアウト時のフォールバック**:
   - `fetch('/api/version')` が 2 秒タイムアウト、ネットワーク切断、または 500 エラー等で失敗した場合、コンソールに警告ログ（`console.warn('Failed to fetch app version:', error)`）を出力しつつ、`appVersion` は空文字（`""`）のまま保持する。
   - `index.html` 側では `x-show="appVersion"` により、取得完了するまでバッジ領域を非表示とするため、UI 破綻（`vundefined` や空白枠の露出）を完全に防止する。

2. **アセンブリ例外時の安全起動**:
   - リフレクションによるアセンブリ取得で予期せぬ例外が発生した場合は、`"0.0.0"` を返却してプロセス起動や HTTP 応答を継続する。
