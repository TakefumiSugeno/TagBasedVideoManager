# Proposal: AIファイルリネームツール (TagBasedVideoManager - AI File Renamer)

## Why

Dockerバインドマウント（WSL2 / 9p / virtiofs）において、ファイルパスの桁数制限（MAX_PATH / NAME_MAX 260文字）を超えるファイルが存在すると、動画ディレクトリ全体がマウントされずWebアプリから動画が参照できなくなる問題が発生する（GitHub Issue #1）。
コンテナ内からはマウント失敗したファイルを認識・操作できないため、ホストOS側で直接ファイルを走査し、OpenRouter（Freeモデル）のAI支援を活用して長すぎるファイル名を安全に短縮・リネームできる独立デスクトップアプリ **TagBasedVideoManager - AI File Renamer** が必要とされている。

## What Changes

- **AI File Renamer アプリケーションの新規構築 (100% F#)**:
  - ホストOS（Windows / WSL）上で稼働する独立デスクトップアプリを **純粋な F# (.NET 10) ＋ Avalonia.FuncUI (Elmish MVU)** で構築
  - C# や XAML を一切使用せず、純粋な F# DSL による宣言的UIと Elmish による単方向データフローを採用し、最初から完全なクロスプラットフォーム（Windows / Linux / macOS）対応とする
- **AI支援ファイルリネーム機能**:
  - ホスト側の指定動画フォルダを走査し、絶対パス長 ≧ 設定基準（初期値: 240文字、Docker 260文字上限対策）のファイルを自動抽出
  - 抽出基準文字数は設定ファイル（`companion-settings.json`）で初期値を定義し、UI上で起動中に一時変更可能（設定ファイルは更新しない）
  - 命名規則・ルール設定をローカル設定ファイルで永続化し、専用の命名規則マネージャー画面でルールの新規作成・編集・削除・並び替え（先頭ルールが既定）を行えるようにする
  - OpenRouter API（Freeモデル）を呼び出し、命名規則に沿った新ファイル名候補を生成。風変わりな元ファイル名で代替補完等を行った問題発生時のみ「AIコメント」を表示
  - Before（変更前）と After（変更後）の2通りレイアウト表示（視線移動最小化の上下並び / 左右並び）で極低ハイト比較確認し、インライン手動微調整および一括リネームを実行
  - アプリ起動中に実行したリネームに対する **Undo（元に戻す）** 機能を実装
- **Docker Compose コンテナ制御機能**:
  - `docker compose ps --format json` および HTTP疎通（ポート5620）により、TagBasedVideoManager コンテナのステータス監視（Running/Stopped/Unhealthy）を行う
  - デスクトップアプリから Up / Down / Restart を実行可能にする
  - リネーム完了後にボタン1つで「リネーム＆コンテナ再起動」を実行し、即座にWebアプリ側へ反映
- **プロジェクト・フォルダ構成のクリーンな再編と回帰テスト**:
  - 既存のWebアプリには機能・UIの改修は一切加えない（案1採用）
  - `src/` 配下にWebアプリ（`TagBasedVideoManager`）とRenamerアプリ（`TagBasedVideoManager.Renamer`）を並列配置し、`test/` 配下も同様に1:1で整理
  - 既存ファイルの移動に伴い、`Dockerfile`, `docker-compose.yml`, `TagBasedVideoManager.slnx`, テストスクリプトのパスを同期更新し、**既存全テストによる回帰テストを確実に実施**する

## Non-goals（やらないこと）

- **既存Webアプリの機能・UI改修**（UI仕様未合意のためスコープ外とし、Webアプリのソース・UIは一切変更しない）
- コンテナ内部からの直接物理ファイルリネーム（マウント失敗時はそもそも不可視であるため対象外）
- 有料AIモデル専用の実装（OpenRouter の Freeモデルを主軸とし、高額なAPIコストを不要とする）
- C# や XAML の導入（本プロジェクトの F# ネイティブ方針を貫き、100% F# で完結させる）
- 起動中に変更した抽出基準文字数の設定ファイルへの自動保存（セッション限りの一時変更に留める）
- 正常なファイルに対する不要なAIコメント表示（問題発生時のみに限定）

## Capabilities

### New Capabilities

- `companion-file-rename`: ホストOS上でパス長制限を超過または接近した動画ファイルを検出し、OpenRouter FreeモデルによるAI支援で新ファイル名および問題時AIコメントを提案・編集・物理リネーム・Undoする機能（命名規則マネージャーを含む）
- `companion-docker-control`: デスクトップアプリからTagBasedVideoManagerのDockerコンテナ稼働状態（up/down/restart）を監視・制御する機能（`docker compose ps` + HTTP疎通）

### Modified Capabilities

なし（既存仕様書なし）

## Impact

- **プロジェクト・フォルダ構成の再編**:
  - `src/` 直下の既存Webアプリを `src/TagBasedVideoManager/` へ移動
  - `test/` 直下の既存テストを `test/TagBasedVideoManager.Tests/` へ移動
  - 新規Renamerデスクトップアプリを `src/TagBasedVideoManager.Renamer/` に配置
  - 新規Renamerテストを `test/TagBasedVideoManager.Renamer.Tests/` に配置
- **インフラ・構成ファイルの更新**:
  - `Dockerfile`, `docker-compose.yml`, `TagBasedVideoManager.slnx`, `test/run_tests_with_coverage.ps1` のパス更新
  - 移動後の既存機能に対する回帰テスト実行
- **既存Webアプリコードへの影響**:
  - なし（ソースコード・UIのロジック改修はゼロ行）
- **依存関係**:
  - Avalonia (11.x), Avalonia.FuncUI, Avalonia.Themes.Fluent
  - OpenRouter API (`https://openrouter.ai/api/v1/chat/completions`)
