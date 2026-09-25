# Design: AIファイルリネーム・管理コンパニオン (Desktop Companion)

## Context

Docker Containerのバインドマウント（WSL2 / 9p / virtiofs）において、パス長制限（MAX_PATH / NAME_MAX 260文字）を超えるファイルが存在するとマウント自体が失敗し、Webアプリから動画が参照できなくなる（GitHub Issue #1）。
コンテナ内からは不可視となるため、ホストOS側で稼働する独立したデスクトップ管理コンパニオンアプリを新設し、ホストファイルシステムを直接走査してAIリネームおよびDockerコンテナ制御を行う。

## Goals / Non-Goals

**Goals:**

- ホストOS上の動画フォルダを走査し、マウント破綻原因となる**絶対パス長 ≧ 240文字（MAX_PATH 260文字へのマージン考慮）**のファイルを自動抽出
- OpenRouter API (Freeモデル) を活用した新ファイル名候補の自動生成
- 命名規則設定・APIキーのローカル永続化（`companion-settings.json`）とUIプリセット選択
- **100% F# (.NET 10) ＋ Avalonia.FuncUI (Elmish MVU)** による完全な関数型クロスプラットフォームデスクトップアプリの構築
- **Before（変更前）と After（変更後）の完全対比UI**（元フルパス・文字数 vs 新フルパス・短縮文字数・手動微調整入力欄）
- 安全な物理リネーム（重複チェック、エラーハンドリング、ロールバック）
- `docker compose ps --format json` および HTTP疎通確認による確実なコンテナ状態特定と Up / Down / Restart 制御
- Webアプリ起動時のマウント異常検知とUI警告バナー表示
- 自動テストの徹底（TDD単体テスト ＋ 結合E2Eテスト ＋ Playwright E2Eテスト）

**Non-Goals:**

- ユーザーにパス長閾値（スライダー）を選ばせる曖昧なUI（240文字の明確な絶対基準で自動抽出）
- C# や XAML の導入（本プロジェクトの F# ネイティブ方針を堅持）
- コンテナ内部からの直接物理ファイルリネーム
- 有料AIモデルへの依存

## Decisions

### 1. リネーム対象の抽出条件（明確な絶対基準）

- **決定**: **絶対パス長 ≧ 240文字** のファイルを自動抽出する。
- **理由**:
  - Docker Desktop WSL2バインドマウントの制限は 260文字（MAX_PATH）。
  - コンテナ内のマウントパス（`/app/videos/...`）への置換マージンを考慮し、240文字以上を「マウント破綻の危険ファイル」として自動抽出する。
  - スライダー等でユーザーに閾値を選ばせる曖昧さを排除し、システムが安全基準で自動判定する。

### 2. 100% F# によるデスクトップGUI: Avalonia.FuncUI (Elmish MVU)

- **決定**: C# / XAML を一切使用せず、**純粋な F# (.NET 10) ＋ Avalonia.FuncUI** を採用する。
- **アーキテクチャ**: Elmish (Model-View-Update: MVU) パターンによる単方向データフロー。
- **UIレイアウト**: Before（変更前: 赤トーン）と After（変更後: 緑トーン）を横並びで対比確認できるカード/グリッドビュー。

### 3. コンテナ状態の特定アーキテクチャ

- **決定**: `docker compose ps --format json` を基点とし、ポート5620への HTTP Ping を併用する。
- **理由**:
  - `docker-compose.yml` に定義されたサービス `video-manager`（コンテナ名 `tag-based-video-manager`）を正確に特定でき、他プロジェクトのコンテナと混同しない。
  - プロセス状態とWebサーバー応答の二重確認により、ハングアップも確実に検知。

### 4. 命名規則・設定の永続化設計

- **決定**: `%APPDATA%/TagBasedVideoManager/companion-settings.json`（またはアプリ直下）に設定をJSON形式で永続化する。
- **保持する情報**:
  - `openRouterApiKey`: APIキー
  - `model`: 選択モデル（デフォルト: `meta-llama/llama-3.3-70b-instruct:free`）
  - `activeRuleIndex`: 選択中の命名ルールプリセット
  - `customRules`: ユーザーが追加したプリセットルール配列

### 5. 自動テスト・E2Eテスト戦略

- **単体テスト (Unit)**: xUnit + FsUnit
  - `FileScanner`, `FileRenamer`, `OpenRouterClient`, `DockerController`, `Settings` の純粋関数・ROP logic
- **結合E2Eテスト (Companion E2E)**:
  - 一時ディレクトリに実際の長パスファイル（240文字以上）を動的生成し、「走査 → AIモック提案 → 物理リネーム → 整合性確認」を一気通貫で検証
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
| [Scan & AI Rule]                                                                |
|  対象フォルダ: [ C:\Users\Videos\Touring_2025                       ] [参照...] |
|  抽出条件: [⚠️ 絶対パス長 ≧ 240文字 (Docker上限 260字対策)] (検出: 3 件)         |
|  AIモデル: [ OpenRouter: Llama-3.3 70B (Free) v ]  命名: [ [日付]_[要約] v ]    |
+---------------------------------------------------------------------------------+
| 【リネーム対象ファイル (Before / After 比較確認)】                               |
| +-----------------------------------------------------------------------------+ |
| | [x] #1  短縮効果: -209文字 (83%削減) [✓ マウント安全]                         | |
| |  [BEFORE (変更前: 251文字 [危険])]                                          | |
| |    パス: C:\Videos\Touring_2025\Hokkaido_Very_Long_Directory\...            | |
| |    名前: VID_20250812_143022_Recorded_At_Wakkanai_Hokkaido_Very_Long...mp4  | |
| |  [AFTER  (変更後: 42文字 [安全])]                                           | |
| |    パス: C:\Videos\Touring_2025\Hokkaido_Very_Long_Directory\...            | |
| |    名前: [ 20250812_Hokkaido_Soya_Cape.mp4                  ] (手動編集可)  | |
| +-----------------------------------------------------------------------------+ |
| [ Actions ]                                                                     |
|  リネーム予定: 3 件   [ リネームのみ実行 ]   [ ⚡ リネームしてコンテナ再起動 ]  |
+---------------------------------------------------------------------------------+
```

## アーキテクチャとデータフロー

```
 [Avalonia.FuncUI (Elmish: Model-View-Update)]
      |
      | 1. Msg: StartScan (フォルダパス) -> パス長 ≧ 240文字で抽出
      v
 [FileScanner.fs] ---> ホストディレクトリ再帰走査 & パス長計算
      |
      | 2. Msg: ScanCompleted (VideoFileInfo list)
      v
 [Elmish Model 更新 -> View 再描画] (Before / After 対比カードを表示)
      |
      | 3. Msg: RequestAiSuggestions (選択ファイル, ルール)
      v
 [OpenRouterClient.fs] ---> OpenRouter API (Free) へ POST (JSON Schema要求)
      |                                |
      |<-- 提案JSONパース結果 ---------+
      v
 [Elmish Model 更新 -> View 再描画] (After の提案ファイル名反映・リアルタイム新パス長計算)
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
