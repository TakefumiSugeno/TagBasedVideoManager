# Design: AIファイルリネーム・管理コンパニオン (Desktop Companion)

## Context

Docker Containerのバインドマウント（WSL2 / 9p / virtiofs）において、パス長制限（MAX_PATH / NAME_MAX）を超えるファイルが存在するとマウント自体が失敗し、Webアプリから動画が参照できなくなる（GitHub Issue #1）。
コンテナ内からは不可視となるため、ホストOS（Windows）上で稼働する独立したデスクトップ管理コンパニオンアプリを新設し、ホストファイルシステムを直接走査してAIリネームおよびDockerコンテナ制御を行う。

## Goals / Non-Goals

**Goals:**

- ホストOS上の動画フォルダを直接走査し、パス長閾値（デフォルト220文字以上）を超えるファイルを自動抽出
- OpenRouter API (Freeモデル) を活用した新ファイル名候補の自動生成
- WinUI 3 を用いたモダンな確認・インライン手動編集UIの提供
- 安全な物理リネーム（重複チェック、エラーハンドリング、ロールバック）
- デスクトップアプリからの Docker Compose 制御（Up / Down / Restart / 稼働ステータス取得）
- Webアプリ起動時のマウント異常検知とUI警告バナー表示
- F# .NET 10 によるコアロジックの完全分離（将来のLinux/macOS向けUI載せ替えに対応）

**Non-Goals:**

- 今回のフェーズでのLinux/macOS向けネイティブGUI実装（Avalonia等は将来フェーズ）
- コンテナ内部からの直接物理ファイルリネーム
- 有料AIモデルへの依存

## Decisions

### 1. プロジェクト構成と責務分離 (Clean Architecture)

- **決定**: `TagBasedVideoManager.Companion.Core` (F# .NET 10 クラスライブラリ) と `TagBasedVideoManager.Companion.App` (WinUI 3 / C# アプリ) の2プロジェクト構成とする。
- **理由**:
  - F# のイミュータブルなデータ構造、型安全性、ROP (Railway Oriented Programming: `Result` 型) を活かして堅牢なコアエンジンを構築できる。
  - コアエンジンを純粋な .NET 10 ライブラリとして切り出すことで、将来フロントエンドを Avalonia や Web、CLI に差し替えてクロスプラットフォーム展開可能。
  - WinUI 3 の XAML ツールチェーン（Windows App SDK）は C# ホストプロジェクトとの親和性が最も高く、安定している。
- **代替案**:
  - F# だけで WinUI 3 を直接記述するアプローチ: XAMLコンパイラやWinRTジェネレータとの摩擦が大きく、ビルドが不安定になるリスクがあるため不採用。
  - WPF: 古い技術スタックであり、モダンな Fluent Design や将来性が WinUI 3 に劣るため不採用。

### 2. AIプロバイダ: OpenRouter (Freeモデル)

- **決定**: OpenRouter API (`https://openrouter.ai/api/v1/chat/completions`) の Freeモデル（例: `meta-llama/llama-3.3-70b-instruct:free` や `google/gemini-2.0-flash-exp:free`）を採用する。
- **理由**: OpenAI互換の REST API であり、F# の `HttpClient` と `System.Text.Json` でシンプルに通信可能。無料枠で手軽に利用できる。
- **構造化出力**: プロンプトで JSON スキーマ（`[ { "original": "...", "suggested": "...", "reason": "..." } ]`）を厳格に指定し、レスポンスのパースエラーを防止する。

### 3. Docker Compose 制御

- **決定**: `System.Diagnostics.Process` をラップした `DockerController` モジュールを実装し、非同期に `docker compose` コマンドを実行・監視する。
- **ステータス取得**: `docker compose ps --format json` を定期実行し、コンテナの Running / Stopped / Exited 状態をパースしてUIに通知する。

## UI/UX モックアップ (WinUI 3)

```
+---------------------------------------------------------------------------------+
| TagBasedVideoManager - Desktop Companion                                 [-] [x]|
+---------------------------------------------------------------------------------+
| [Docker Controller]                                                             |
|  Status: [● RUNNING] (Port: 5620)                                               |
|  [ ▶ Up (Start) ]  [ ■ Down (Stop) ]  [ 🔄 Restart ]  [ 📋 View Logs ]          |
+---------------------------------------------------------------------------------+
| [AI File Rename]                                                                |
|  Target Directory: [ C:\Users\Videos\Touring                         ] [Browse] |
|  Path Length Filter: [x] 220 chars or more (Found: 4 files)                     |
|  Model: [ OpenRouter: meta-llama/llama-3.3-70b-instruct:free      v ]           |
|  Rule:  [ [Date]_[Location]_[ShortSummary(within 20 chars)]        ] [Get AI]   |
+---------------------------------------------------------------------------------+
| [x] | Length  | Current File Path                | Suggested Name (Editable)    |
+-----+---------+----------------------------------+------------------------------+
| [x] | 248 [!] | ...\20250812_very_long_title.mp4 | [20250812_Hokkaido_Cape.mp4] |
|     |         | (Path: C:\Videos\Touring\...)    | New Length: 42 chars [OK]    |
+-----+---------+----------------------------------+------------------------------+
| [x] | 231 [!] | ...\DSC_009988_bbq_party_long... | [20250813_BBQ_Camp.mp4     ] |
|     |         | (Path: C:\Videos\Touring\...)    | New Length: 38 chars [OK]    |
+-----+---------+----------------------------------+------------------------------+
| [ Actions ]                                                                     |
|  [ ⚡ Rename & Restart Docker ]      [ Rename Only ]       [ Cancel / Clear ]   |
+---------------------------------------------------------------------------------+
```

## アーキテクチャとデータフロー

```
 [WinUI 3 App (C#)]
      |
      | 1. スキャン要求 (フォルダパス, 閾値)
      v
 [Companion.Core (F#)] ---> FileScanner: ホストディレクトリ再帰走査 & パス長計算
      |
      | 2. 危険ファイル一覧 (VideoFileInfo list)
      v
 [WinUI 3 DataGrid]
      |
      | 3. 「AI提案取得」クリック
      v
 [Companion.Core (F#)] ---> OpenRouterClient: FreeモデルへPOST (JSON要求)
      |                                              |
      |<-- 提案JSONパース結果 -----------------------+
      v
 [WinUI 3 DataGrid] (提案ファイル名反映・ユーザー編集)
      |
      | 4. 「Rename & Restart Docker」クリック
      v
 [Companion.Core (F#)]
      |-- (a) FileRenamer: 重複検証 & File.Move で物理リネーム
      |-- (b) DockerController: `docker compose restart` 実行
      v
 [Docker Engine (WSL2)] ---> TagBasedVideoManager 再起動完了！
```

## Risks / Trade-offs

- **[Risk] OpenRouter Freeモデルのレートリミットやダウンタイム**
  → **Mitigation**: APIリクエスト失敗時はUIにエラー理由を表示し、手動リネーム入力欄を常に開放。またリトライ処理およびタイムアウト（15秒）を設定。
- **[Risk] リネーム先ファイル名の衝突（同名ファイルが既に存在）**
  → **Mitigation**: 物理リネーム前に `File.Exists` による重複チェックを必ず実施。重複時はサフィックス（`_1`, `_2`）を自動付加するかエラーとしてユーザーに確認を促す。
- **[Risk] ファイルロックによるリネーム失敗（動画再生中など）**
  → **Mitigation**: `IOException` を捕捉し、ロックしているプロセスがある場合はスキップし、他のファイルのリネームを継続できるように結果レポートを個別管理する。
