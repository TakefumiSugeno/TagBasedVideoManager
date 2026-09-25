# Proposal: AIファイルリネームツール (TagBasedVideoManager - AI File Renamer)

## Why

Dockerバインドマウント（WSL2 / 9p / virtiofs）において、ファイルパスの桁数制限（MAX_PATH / NAME_MAX 260文字）を超えるファイルが存在すると、動画ディレクトリ全体がマウントされずWebアプリから動画が参照できなくなる問題が発生する（GitHub Issue #1）。
コンテナ内からはマウント失敗したファイルを認識・操作できないため、ホストOS側で直接ファイルを走査し、OpenRouter（Freeモデル）のAI支援を活用して長すぎるファイル名を安全に短縮・リネームできるデスクトップアプリ **TagBasedVideoManager - AI File Renamer** が必要とされている。

## What Changes

- **AI File Renamer アプリケーションの新規構築 (100% F#)**:
  - ホストOS（Windows / WSL）上で稼働する独立デスクトップアプリを **純粋な F# (.NET 10) ＋ Avalonia.FuncUI (Elmish MVU)** で構築
  - C# や XAML を一切使用せず、純粋な F# DSL による宣言的UIと Elmish による単方向データフローを採用し、最初から完全なクロスプラットフォーム（Windows / Linux / macOS）対応とする
- **AI支援ファイルリネーム機能**:
  - ホスト側の指定動画フォルダを走査し、絶対パス長 ≧ 240文字（Docker 260文字上限対策）のファイルを自動抽出
  - 命名規則・ルール設定をローカル設定ファイル（`companion-settings.json`）で永続化し、専用の命名規則マネージャー画面でルールの新規作成・編集・削除・並び替え（先頭ルールが既定）を行えるようにする
  - OpenRouter API（Freeモデル）を呼び出し、命名規則に沿った新ファイル名候補とともに、風変わりな元ファイル名に対する命名根拠や補完理由を説明する「AIコメント」を生成
  - Before（変更前）と After（変更後）の2通りレイアウト表示（視線移動最小化の上下並び / 左右並び）で比較確認し、インライン手動微調整および一括リネームを実行
  - アプリ起動中に実行したリネームに対する **Undo（元に戻す）** 機能を実装
- **Docker Compose コンテナ制御機能**:
  - `docker compose ps --format json` および HTTP疎通（ポート5620）により、TagBasedVideoManager コンテナのステータス監視（Running/Stopped/Unhealthy）を行う
  - デスクトップアプリから Up / Down / Restart を実行可能にする
  - リネーム完了後にボタン1つで「リネーム＆コンテナ再起動」を実行し、即座にWebアプリ側へ反映
- **Webアプリ側のマウント異常検知**:
  - Dockerコンテナ側のTagBasedVideoManager起動時/スキャン時に、動画ディレクトリの存在・ファイル件数をチェックし、マウント失敗が疑われる場合にWeb UI上で警告アラートを表示
- **自動テスト・E2Eテストの拡充**:
  - 単体テスト（TDD）に加え、実際のファイルシステム・モックAPI・物理リネーム・Undoを検証する結合E2Eテストを実装
  - Webアプリ側にも、マウント異常検知と警告バナー表示を検証する Playwright E2Eテストを追加

## Non-goals（やらないこと）

- コンテナ内部からの直接物理ファイルリネーム（マウント失敗時はそもそも不可視であるため対象外）
- 有料AIモデル専用の実装（OpenRouter の Freeモデルを主軸とし、高額なAPIコストを不要とする）
- C# や XAML の導入（本プロジェクトの F# ネイティブ方針を貫き、100% F# で完結させる）
- 曖昧なパス長閾値スライダーの提供（240文字の絶対基準で自動抽出）

## Capabilities

### New Capabilities

- `companion-file-rename`: ホストOS上でパス長制限を超過または接近した動画ファイルを検出し、OpenRouter FreeモデルによるAI支援で新ファイル名およびAIコメントを提案・編集・物理リネーム・Undoする機能（命名規則マネージャーを含む）
- `companion-docker-control`: デスクトップアプリからTagBasedVideoManagerのDockerコンテナ稼働状態（up/down/restart）を監視・制御する機能（`docker compose ps` + HTTP疎通）
- `mount-failure-detection`: Webアプリ起動時に動画ディレクトリの存在・ファイル件数をチェックし、マウント失敗疑い時にUI上で警告通知する機能

### Modified Capabilities

なし（既存仕様書なし）

## Impact

- **新規プロジェクト追加**:
  - `src/TagBasedVideoManager.Companion/` (F# .NET 10 + Avalonia.FuncUI アプリケーション)
  - `test/TagBasedVideoManager.Companion.Tests/` (F# xUnit + FsUnit 単体/結合E2Eテスト)
- **既存Webアプリへの影響**:
  - `src/Program.fs`, `src/Scanner.fs`: 起動時マウントチェックロジックの追加
  - `src/wwwroot/index.html`, `src/wwwroot/app.js`: マウントエラー検知時のアラート表示
  - `test/TagBasedVideoManager.Tests/`: マウント異常検知の Playwright E2Eテスト追加
- **依存関係**:
  - Avalonia (11.x), Avalonia.FuncUI, Avalonia.Themes.Fluent
  - OpenRouter API (`https://openrouter.ai/api/v1/chat/completions`)
