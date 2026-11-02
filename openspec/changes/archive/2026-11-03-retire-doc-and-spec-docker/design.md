# Design: Docker仕様のOpenSpec化と./docフォルダの完全廃止 (retire-doc-and-spec-docker)

## Context

Webアプリケーション本体の機能仕様は `openspec/specs/` 配下の5モジュール（`video-library`, `video-scanner`, `tag-management`, `search-and-folders`, `viewing-analytics`）へ移行完了し、リネームアプリも `companion-*` として OpenSpec で管理されている。
しかし、Docker環境・GPU支援・マウント構成が依然として `doc/docker_design.md` に残っており、`doc/` フォルダ内にスクリーンショット画像や過去の設計書（`design_detail.md`）が混在していた。
これらを整理し、`doc/` ディレクトリを完全に廃止して仕様・設計の正本を OpenSpec に一本化する。

## Goals / Non-Goals

### Goals

- `doc/docker_design.md` の内容を、新規メイン仕様 `docker-infrastructure` として OpenSpec に完全移行する。
- `doc/images/` の画像を `assets/images/` へ移動し、`README.md` の画像リンクを更新する。
- `doc/design_detail.md` の物理データベーススキーマ等の重要リファレンスを `README.md` へ統合する。
- `doc/` ディレクトリ全体をリポジトリから完全に削除（廃止）する。

### Non-Goals

- `docker-compose.yml` や `Dockerfile` の破壊的改変（既存の正常稼働設定を維持）
- アプリケーションコード（F#）の改修

## Decisions

### 1. `docker-infrastructure` の独立仕様化

- **決定**: Docker環境、マルチステージビルド、マウント構成、GPUアクセラレーション支援を独立した Capability `docker-infrastructure` として策定した。
- **理由**: コンテナ実行環境はインフラストラクチャとしての独立した関心事であり、Webアプリの機能仕様（`video-library` 等）と分離して管理するのが適切であるため。

### 2. 画像アセットの `assets/images/` への移管

- **決定**: `README.md` から参照されているスクリーンショット画像を `assets/images/` へ移動した。
- **理由**: `doc/` フォルダを完全に消去するために、画像のみをGitHub標準的なアセット配置ディレクトリ（`assets/`）へ移設する。

### 3. `doc/` ディレクトリの完全削除

- **決定**: `doc/` 配下のすべてのファイル（`spec.md`, `docker_design.md`, `design_detail.md`, `work/` 等）を削除した。
- **理由**: 仕様の正本は `openspec/specs/`、変更履歴は `openspec/changes/archive/`、全体概要は `README.md` に集約され、`doc/` に残すべき資産が存在しなくなったため。

## Risks / Trade-offs

- **[過去の調査メモ（`doc/work/`）の喪失リスク]**
  - **緩和策**: `doc/work/` は初期開発時の一時的なスクラッチ・調査ログであり、Git 履歴（コミット履歴）に完全に記録・保存されているため、ワーキングツリーから削除しても過去履歴からいつでも参照可能。
