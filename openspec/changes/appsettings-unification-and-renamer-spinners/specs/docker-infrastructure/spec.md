# docker-infrastructure Delta Specification

## MODIFIED Requirements

### Requirement: ポート公開と環境変数注入

システムは、Kestrel Web サーバーのポート（既定: 5620）をホスト側にマッピング（`5620:5620`）し、必須設定キー（`PORT=5620`, `VIDEO_DIRS=/app/videos/<名1>;/app/videos/<名2>;...`, `THUMBNAIL_DIR=/app/data/thumbnails`, `DATABASE_PATH=/app/data/metadata.db`）をコンテナ起動時の環境変数注入または `.NET` 標準構成（`appsettings.json` / `appsettings.{Environment}.json`）を通じて `IConfiguration` へ供給しなければならない（SHALL）。コンテナ環境実行時はホスト側デプロイスクリプト等から注入されるコンテナ環境変数が `appsettings*.json` の既定値をオーバーライドし、`VIDEO_DIRS` はマウント先のコンテナ内パスを `;` 区切りで列挙しなければならない（SHALL）。

#### Scenario: 環境変数注入および IConfiguration による疎通確認

- **WHEN** デプロイスクリプト（`scripts/deploy.ps1` / `scripts/deploy.sh`）または単一コンテナ CLI の `run` コマンドによりコンテナが起動したとき
- **THEN** ポート 5620 で HTTP サーバーが待機し、コンテナ環境変数で上書きされた `IConfiguration` のパス設定（`VIDEO_DIRS`, `THUMBNAIL_DIR`, `DATABASE_PATH`）で DB や動画ディレクトリが初期化されること

#### Scenario: コンテナ内 VIDEO_DIRS の整合

- **WHEN** ホスト `VIDEO_DIRS=D:\X;F:\Y` でコンテナが起動したとき
- **THEN** コンテナ内の環境変数 `VIDEO_DIRS` は `/app/videos/X;/app/videos/Y` となり、Web アプリの `IConfiguration["VIDEO_DIRS"]` から同じパス群が参照されること

---

## ADDED Requirements

### Requirement: Webアプリケーションにおける .env 廃止と appsettings 階層構成への一本化

Web アプリケーション（`TagBasedVideoManager`）は、起動時に独自の `.env` パーサ（`loadDotEnv`）を実行してはならず（SHALL NOT）、リポジトリルートに `.env.sample` を保持してはならない（SHALL NOT）。設定値の解決は ASP.NET Core 標準の `IConfiguration`（`appsettings.json` → `appsettings.{Environment}.json` → OS/コンテナ環境変数 → コマンドライン引数）に一本化し、`PORT`, `VIDEO_DIRS`, `THUMBNAIL_DIR`, `DATABASE_PATH`（およびデプロイ支援用 `DATA_DIR`）を統合参照しなければならない（SHALL）。`src/TagBasedVideoManager/appsettings.json` は秘密情報や環境固有パスを含まないクリーンな既定テンプレートのみを Git 管理対象とし、実環境別の `appsettings.Development.json` および `appsettings.Production.json` は `.gitignore` により Git 管理から除外しなければならない（SHALL）。

#### Scenario: Web アプリ起動時の .env 非参照と IConfiguration 優先順位の維持

- **WHEN** `.env` ファイルが存在する状態で `TagBasedVideoManager` を起動したとき
- **THEN** `.env` は一切読み込まれず、`appsettings.json`、`appsettings.{Environment}.json`、およびコンテナ/OS環境変数によって `PORT`, `VIDEO_DIRS`, `THUMBNAIL_DIR`, `DATABASE_PATH` が決定されること

#### Scenario: クリーンな Web 用 appsettings.json テンプレートの維持と環境別ファイルの Git 除外

- **WHEN** `git ls-files` で `src/TagBasedVideoManager/` 配下を検査したとき
- **THEN** `appsettings.json` のみが Git 管理されており、`appsettings.Development.json` および `appsettings.Production.json` は追跡対象外（`.gitignore` 除外）であること
