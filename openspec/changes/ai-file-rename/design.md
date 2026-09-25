# Design: AIファイルリネーム・管理コンパニオン (Desktop Companion)

## Context

Docker Containerのバインドマウント（WSL2 / 9p / virtiofs）において、パス長制限（MAX_PATH / NAME_MAX）を超えるファイルが存在するとマウント自体が失敗し、Webアプリから動画が参照できなくなる（GitHub Issue #1）。
コンテナ内からは不可視となるため、ホストOS側で稼働する独立したデスクトップ管理コンパニオンアプリを新設し、ホストファイルシステムを直接走査してAIリネームおよびDockerコンテナ制御を行う。

## Goals / Non-Goals

**Goals:**

- ホストOS上の動画フォルダを直接走査し、パス長閾値（デフォルト220文字以上）を超えるファイルを自動抽出
- OpenRouter API (Freeモデル) を活用した新ファイル名候補の自動生成
- 命名規則設定・APIキーのローカル永続化（`companion-settings.json`）とUIプリセット選択
- **100% F# (.NET 10) ＋ Avalonia.FuncUI (Elmish MVU)** による完全な関数型クロスプラットフォームデスクトップアプリの構築
- 安全な物理リネーム（重複チェック、エラーハンドリング、ロールバック）
- `docker compose ps --format json` および HTTP疎通確認による確実なコンテナ状態特定と Up / Down / Restart 制御
- Webアプリ起動時のマウント異常検知とUI警告バナー表示
- 自動テストの徹底（TDD単体テスト ＋ 結合E2Eテスト ＋ Playwright E2Eテスト）

**Non-Goals:**

- C# や XAML の導入（本プロジェクトの F# ネイティブ方針を堅持）
- コンテナ内部からの直接物理ファイルリネーム
- 有料AIモデルへの依存

## Decisions

### 1. 100% F# によるデスクトップGUI: Avalonia.FuncUI (Elmish MVU)

- **決定**: C# / XAML を一切使用せず、**純粋な F# (.NET 10) ＋ Avalonia.FuncUI** を採用する。
- **アーキテクチャ**: Elmish (Model-View-Update: MVU) パターンによる単方向データフロー。
- **理由**:
  - 本プロジェクトの「F# ファースト」原則に完全に合致し、C# を1行も書かずに完結できる。
  - XAML を排除し、F# の型安全な関数型 DSL でUIコンポーネントを宣言的に構築できる。
  - Windows 11 の Fluent テーマ（Mica / アクリル効果）に標準対応しており、WinUI 3 と同等以上の洗練された外観を実現できる。
  - 最初からクロスプラットフォーム（Windows / Linux / macOS）で動作するため、将来他OSでコンパニオンを動かす際にもコード変更が不要。
- **代替案**:
  - WinUI 3 + C#: XAMLツールチェーンが C# 前提であり、F# との混在による保守負荷・ビルド摩擦が高いため不採用。
  - Photino.NET: Web技術との共通化は可能だが、ネイティブUIコンポーネントの操作感（DataGrid等）において Avalonia.FuncUI の方が関数型GUIとして優れる。

### 2. コンテナ状態の特定アーキテクチャ

- **決定**: `docker compose ps --format json` を基点とし、ポート5620への HTTP Ping を併用する。
- **理由**:
  - `docker-compose.yml` に定義されたサービス `video-manager`（コンテナ名 `tag-based-video-manager`）を正確に特定でき、他プロジェクトのコンテナと混同しない。
  - `docker compose` プロセスで `State == "running"` を確認しつつ、Kestrel Webサーバーが実際にHTTP 200を返すかを確認することで、プロセスハングアップも検知可能。

### 3. 命名規則・設定の永続化設計

- **決定**: `%APPDATA%/TagBasedVideoManager/companion-settings.json`（またはアプリ直下）に設定をJSON形式で永続化する。
- **保持する情報**:
  - `openRouterApiKey`: APIキー（暗号化またはローカルセキュア保存）
  - `model`: 選択モデル（デフォルト: `meta-llama/llama-3.3-70b-instruct:free`）
  - `pathLengthThreshold`: パス長警告閾値（デフォルト: 220）
  - `activeRuleIndex`: 選択中の命名ルール
  - `customRules`: ユーザーが追加したプリセットルール配列
- **UI連携**: ドロップダウンでプリセット（「日付＋要約」「日付＋親フォルダ＋通番」等）を選択でき、直接カスタム編集したプロンプトも即座に反映・保存される。

### 4. 自動テスト・E2Eテスト戦略

- **単体テスト (Unit)**: xUnit + FsUnit
  - `FileScanner`, `FileRenamer`, `OpenRouterClient`, `DockerController`, `Settings` の純粋関数・ROP logic
- **結合E2Eテスト (Companion E2E)**:
  - 一時ディレクトリに実際の長パスファイルを動的生成し、「走査 → AIモック提案 → 物理リネーム → 整合性確認」を一気通貫で検証
- **Web側E2Eテスト (Playwright)**:
  - マウント先が空の状態でWebサーバーを立ち上げ、ブラウザ画面に警告バナーが表示されることを検証

## UI/UX モックアップ (Avalonia.FuncUI - Fluent テーマ)

> ブラウザで操作可能なHTMLモックアップ（UI仕様正本）: [mockup.html](./mockup.html)

```
+---------------------------------------------------------------------------------+
| TagBasedVideoManager - Desktop Companion                                 [-] [x]|
+---------------------------------------------------------------------------------+
| [Docker Controller]                                                             |
|  Container: tag-based-video-manager   Status: [● RUNNING] (Port: 5620)          |
|  [ ▶ Up (Start) ]  [ ■ Down (Stop) ]  [ 🔄 Restart ]  [ 📋 View Logs ]          |
+---------------------------------------------------------------------------------+
| [AI File Rename]                                                                |
|  Target Directory: [ C:\Users\Videos\Touring                         ] [Browse] |
|  Path Length Filter: [x] 220 chars or more (Found: 4 files)                     |
|  Model: [ OpenRouter: meta-llama/llama-3.3-70b-instruct:free      v ]           |
|  Rule:  [ [日付]_[要約(20文字以内)]                                v ] [Get AI] |
|  Custom Rule Prompt: [ [Date]_[Location]_[ShortSummary(20chars)]             ]  |
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
 [Avalonia.FuncUI (Elmish: Model-View-Update)]
      |
      | 1. Msg: StartScan (フォルダパス, 閾値)
      v
 [FileScanner.fs] ---> ホストディレクトリ再帰走査 & パス長計算
      |
      | 2. Msg: ScanCompleted (VideoFileInfo list)
      v
 [Elmish Model 更新 -> View 再描画] (DataGrid に危険ファイル一覧表示)
      |
      | 3. Msg: RequestAiSuggestions (選択ファイル, ルール)
      v
 [OpenRouterClient.fs] ---> OpenRouter API (Free) へ POST (JSON Schema要求)
      |                                |
      |<-- 提案JSONパース結果 ---------+
      v
 [Elmish Model 更新 -> View 再描画] (提案ファイル名反映・インライン編集可能)
      |
      | 4. Msg: ExecuteRenameAndRestart
      v
 [FileRenamer.fs]      ---> 重複検証 & File.Move で物理リネーム
      |
 [DockerController.fs] ---> `docker compose restart` 実行 & HTTP疎通確認
      v
 [Elmish Model 更新]   ---> 完了通知ダイアログ表示！
```

## Risks / Trade-offs

- **[Risk] OpenRouter Freeモデルのレートリミットやダウンタイム**
  → **Mitigation**: APIリクエスト失敗時はUIにエラー理由を表示し、手動リネーム入力欄を常に開放。またリトライ処理およびタイムアウト（15秒）を設定。
- **[Risk] リネーム先ファイル名の衝突（同名ファイルが既に存在）**
  → **Mitigation**: 物理リネーム前に `File.Exists` による重複チェックを必ず実施。重複時はサフィックス（`_1`, `_2`）を自動付加するかエラーとしてユーザーに確認を促す。
- **[Risk] ファイルロックによるリネーム失敗（動画再生中など）**
  → **Mitigation**: `IOException` を捕捉し、ロックしているプロセスがある場合はスキップし、他のファイルのリネームを継続できるように結果レポートを個別管理する。
