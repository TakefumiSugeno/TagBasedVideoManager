# devcontainer-environment Specification Delta

## Purpose

開発者がホストマシンに個別のランタイムや外部依存を手動導入することなく、VS Code Dev Containers を通じて .NET 10 SDK、FFmpeg、SQLite3 等の開発・テスト環境を即座に利用できるコンテナ構成を提供する。

## ADDED Requirements

### Requirement: DevContainer 開発環境の提供

システムは、VS Code Dev Containers 設定（`.devcontainer/devcontainer.json`）および専用のコンテナ定義を提供し、.NET 10 SDK、FFmpeg、SQLite3、および F# 言語開発に必要な拡張機能を備えた開発環境を起動できなければならない（SHALL）。

#### Scenario: DevContainer 環境の起動とツール確認

- **WHEN** 開発者が VS Code で「Reopen in Container」を実行したとき
- **THEN** コンテナが `root` ユーザー（Windows バインドマウント上の権限不一致防止）で正常にビルド・起動し、コンテナ内ターミナルで `dotnet --version`（.NET 10 系）、`dotnet build`、および `ffmpeg -version` が権限エラーなく利用可能であること

### Requirement: 開発環境専用ポートによる競合防止

システムは、DevContainer 内で起動される Web アプリケーションのポートを開発専用ポート（既定: 5621）にバインドし、ホスト側へフォワードしなければならない（SHALL）。本番常用コンテナ（ポート 5620）とのポート競合を防止しなければならない。

#### Scenario: 本番稼働中の DevContainer 並行起動

- **WHEN** ホスト上で本番コンテナがポート 5620 で稼働している状態で、DevContainer 内で Web アプリが起動されたとき
- **THEN** ポート衝突エラーを起こさず、ポート 5621 で HTTP サーバーがリクエストを受け付けること
