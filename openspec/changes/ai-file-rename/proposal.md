# Proposal: AIファイルリネーム・管理コンパニオン (Desktop Companion)

## Why

Dockerバインドマウント（WSL2 / 9p / virtiofs）において、ファイルパスの桁数制限（MAX_PATH / NAME_MAX）を超えるファイルが存在すると、動画ディレクトリ全体がマウントされずWebアプリから動画が参照できなくなる問題が発生する（GitHub Issue #1）。
コンテナ内からはマウント失敗したファイルを認識・操作できないため、ホストOS（Windows）側で直接ファイルを走査し、OpenRouter（Freeモデル）のAI支援を活用して長すぎるファイル名を安全に短縮・リネームできるデスクトップ管理コンパニオンアプリが必要とされている。

## What Changes

- **Desktop Companion アプリケーションの新規構築**:
  - ホストOS（Windows）上で稼働する独立デスクトップアプリ（WinUI 3 ＋ F# .NET 10 Core）を新設
  - 将来のLinux/macOS等のクロスプラットフォーム展開を見据え、コアロジック（`Companion.Core`）を純粋な .NET 10 F# ライブラリとしてUIと完全分離
- **AI支援ファイルリネーム機能**:
  - ホスト側の指定動画フォルダを再帰スキャンし、パス長が閾値（例: 220文字以上）を超える危険なファイルを自動抽出
  - OpenRouter API（Freeモデル）を呼び出し、命名規則や文字数制限に沿った新ファイル名候補を生成
  - WinUI 3 のグリッドUIで変更前後の比較プレビュー、インライン編集、手動微調整、一括リネーム実行
- **Docker Compose コンテナ制御機能**:
  - デスクトップアプリから `docker compose` コマンドを発行し、TagBasedVideoManager コンテナのステータス監視（Running/Stopped）および Up / Down / Restart を実行可能にする
  - リネーム完了後にボタン1つで「リネーム＆コンテナ再起動」を実行し、即座にWebアプリ側へ反映
- **Webアプリ側のマウント異常検知**:
  - Dockerコンテナ側のTagBasedVideoManager起動時/スキャン時に、動画ディレクトリの存在・ファイル件数をチェックし、マウント失敗が疑われる場合にWeb UI上で警告アラートを表示

## Non-goals（やらないこと）

- コンテナ内部からの直接物理ファイルリネーム（マウント失敗時はそもそも不可視であるため対象外）
- 有料AIモデル専用の実装（OpenRouter の Freeモデルを主軸とし、高額なAPIコストを不要とする）
- Windows以外のUI実装（今回のフェーズではWinUI 3に集中し、クロスプラットフォームUIは将来の別変更とする）

## Capabilities

### New Capabilities

- `companion-file-rename`: ホストOS上でパス長制限を超過または接近した動画ファイルを検出し、OpenRouter FreeモデルによるAI支援で新ファイル名を提案・編集・物理リネームする機能
- `companion-docker-control`: WinUI 3デスクトップアプリからTagBasedVideoManagerのDockerコンテナ稼働状態（up/down/restart）を監視・制御する機能
- `mount-failure-detection`: Webアプリ起動時に動画ディレクトリの存在・ファイル件数をチェックし、マウント失敗疑い時にUI上で警告通知する機能

### Modified Capabilities

なし（既存仕様書なし）

## Impact

- **新規プロジェクト追加**:
  - `src/TagBasedVideoManager.Companion.Core/` (F# .NET 10 クラスライブラリ)
  - `src/TagBasedVideoManager.Companion.App/` (WinUI 3 / C# アプリケーション)
  - `test/TagBasedVideoManager.Companion.Tests/` (F# xUnit + FsUnit テスト)
- **既存Webアプリへの影響**:
  - `src/Program.fs`, `src/Scanner.fs`: 起動時マウントチェックロジックの追加
  - `src/wwwroot/index.html`, `src/wwwroot/app.js`: マウントエラー検知時のアラート表示
- **依存関係**:
  - OpenRouter API (`https://openrouter.ai/api/v1/chat/completions`)
  - Windows App SDK (WinUI 3)
