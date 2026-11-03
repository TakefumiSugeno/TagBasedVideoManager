# Proposal

## 変更メタデータ

- 適用規模レベル: `L2`
- 変更種別: `chore` / `docs`

## Why

Webアプリケーション本体の機能仕様は OpenSpec（`video-library` 等の5モジュール）へ移行完了しましたが、Docker環境・GPUアクセラレーション・マウント設計は依然として `doc/docker_design.md` に残存しており、旧資産 `doc/` フォルダが残存する原因となっています。
未移行の Docker インフラ仕様を OpenSpec メイン仕様（`docker-infrastructure`）として正式化し、`doc/` 内のスクリーンショット画像を `assets/` へ移管した上で、`doc/` ディレクトリ全体を完全廃止（削除）し、リポジトリの仕様・設計管理を OpenSpec および README のみに完全一本化します。

## What Changes

- Docker環境の仕様を定義する新規 Capability `docker-infrastructure`（マルチステージビルド、ボリュームマウント、CPU/AMD/NVIDIA GPU支援切り替え、ポート・環境変数構成）を策定
- `doc/images/` 配下のスクリーンショット画像を `assets/images/` へ移動し、`README.md` 内の画像リンクを更新
- `doc/design_detail.md` に記載されていた物理テーブル定義（SQLite DDL）等の主要リファレンスを `README.md` に統合
- 不要となった `doc/` ディレクトリ（`spec.md`, `docker_design.md`, `design_detail.md`, `work/` 等）を完全に削除・廃止

## Non-goals (やらないこと)

- `Dockerfile` や `docker-compose.yml` の破壊的改変（現行の稼働設定をそのまま仕様化する）
- F# バックエンドおよびフロントエンドコードの改修

## Capabilities

### New Capabilities

- `docker-infrastructure`: 単一コンテナによるマルチステージビルド（.NET 10 + FFmpeg）、Windows/WSL2ホスト動画の読み取り専用マウント（`:ro`）、DBおよびサムネイルのボリューム永続化（`./data`）、およびマルチGPU（CPU/AMD VAAPI/NVIDIA CUDA）アクセラレーション構成仕様

### Modified Capabilities

（なし）

## Acceptance Criteria (受け入れ基準)

- **GIVEN** `docker-compose.yml` および `Dockerfile` による環境が定義されている状態において
- **WHEN** `docker-infrastructure` 仕様書が作成され、画像移管および `doc/` ディレクトリ削除が完了したとき
- **THEN** OpenSpec の `docker-infrastructure` 仕様がコンテナ環境の実態と 100% 整合していること
- **THEN** `README.md` の全画像リンクが壊れずに正常表示されること
- **THEN** リポジトリルートから `doc/` ディレクトリが完全に削除され、`openspec validate` および全フェーズゲート検証が PASS すること

## Impact

- **仕様ドキュメント**: `doc/` フォルダが消滅し、すべての仕様が `openspec/specs/` に統合される
- **README**: スクリーンショット画像の参照先が `assets/images/` に変更され、物理スキーマ等のリファレンスが拡充される
- **コード/インフラ**: 既存の `Dockerfile`, `docker-compose.yml` への破壊的影響なし
