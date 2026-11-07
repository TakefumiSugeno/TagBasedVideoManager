# docker-infrastructure Specification

## Purpose

TagBasedVideoManager を Docker コンテナ環境下で効率的かつ安全にビルド・運用するための、マルチステージビルド、ホスト動画フォルダの安全なマウント、データ永続化ボリューム、および CPU/AMD/NVIDIA GPU ハードウェアアクセラレーション構成仕様を規定する。

## Requirements

### Requirement: マルチステージビルドと軽量ランタイム配信

システムは、`Dockerfile` において .NET 10 SDK（`mcr.microsoft.com/dotnet/sdk:10.0`）によるビルドステージと、フットプリントの小さい ASP.NET ランタイム（`mcr.microsoft.com/dotnet/aspnet:10.0`）による実行ステージの2ステージ構成を採用し、実行環境内に `ffmpeg` および `ffprobe` パッケージを自動導入しなければならない（SHALL）。

#### Scenario: コンテナイメージのビルド

- **WHEN** デプロイスクリプト（`scripts/deploy.ps1` / `scripts/deploy.sh`）または単一コンテナ CLI（`wslc build` / `docker build` / `podman build`）が実行されたとき
- **THEN** SDKステージで F# Webアプリが Release ビルド・同封され、ランタイムステージに FFmpeg と共に配置された最小限のコンテナイメージが生成されること

### Requirement: ホスト動画ディレクトリの読み取り専用マウント

システムは、WSL 3.0 (`wslc`) または Docker による単一コンテナ実行時において、`VIDEO_DIRS` で指定されたホスト（Windows / WSL3 / Linux）上の各物理動画ディレクトリを、コンテナ内の `/app/videos/<フォルダ名>` に読み取り専用（`:ro`）でバインドマウントし、Webアプリ側からのファイル改変や誤削除を防止しなければならない（SHALL）。

#### Scenario: 読み取り専用ボリュームの保護

- **WHEN** コンテナが起動して `/app/videos/<フォルダ名>` に各ホスト動画フォルダがマウントされたとき
- **THEN** コンテナ内から動画ファイルの読み取りおよびメタデータ抽出が可能であり、かつコンテナからの書き込み・削除操作が拒否されること

#### Scenario: 複数物理フォルダのマウント

- **WHEN** `VIDEO_DIRS=D:\X;F:\Y` でコンテナが起動したとき
- **THEN** `D:\X` が `/app/videos/X`、`F:\Y` が `/app/videos/Y` にそれぞれ読み取り専用でマウントされ、両配下の動画がアプリから参照できること

### Requirement: アプリケーションデータとサムネイルのボリューム永続化

システムは、SQLite データベース（`metadata.db`）および生成されたサムネイル画像ディレクトリ（`thumbnails`）をホスト側の永続ディレクトリ（既定: `./data`）にバインドマウント（`./data:/app/data`）し、デプロイスクリプト等によるコンテナの破棄・再作成を行っても登録済みタグ情報やサムネイルが喪失しないよう永続化しなければならない（SHALL）。

#### Scenario: コンテナ再起動後のデータ永続性

- **WHEN** デプロイスクリプト等により既存コンテナが停止・削除された後に新コンテナが起動されたとき
- **THEN** 以前に登録された動画メタデータ、タグ情報、および生成済みサムネイル画像がそのまま維持・復元されること

### Requirement: CPUおよびマルチGPU（AMD/NVIDIA）アクセラレーションの切り替え

システムは、サムネイル生成時の FFmpeg デコード処理において、ホスト環境に応じた GPU アクセラレーションを適用できなければならない（SHALL）。WSL 3.0 (`wslc`) または Docker 環境下での NVIDIA GPU 利用時は `--gpus all` フラグにより CUDA アクセラレーションを有効化する。WSL 3.0 環境における AMD GPU 利用時は `--device` 非対応の制約により CPU フォールバックで安全に動作させ、Linux ホスト上の Docker 環境に限り `/dev/dri` デバイスパススルーを適用する。GPU が存在しない環境または非対応環境では自動的に CPU ソフトウェアデコードでフォールバック動作しなければならない（SHALL）。

#### Scenario: CPUのみの既定実行 (デフォルト)

- **WHEN** GPU フラグを指定せず、または対応 GPU がない環境（WSL 3.0 での AMD GPU 環境含む）でコンテナを起動したとき
- **THEN** エラーなく標準の CPU ソフトウェアデコードによりサムネイル生成が正常に完了すること

#### Scenario: AMD GPU (VAAPI/ROCm) によるハードウェア支援

- **WHEN** Linux ホスト環境上の Docker で `/dev/dri` デバイスパススルーが有効化されて起動されたとき
- **THEN** コンテナ内の FFmpeg がホストの AMD GPU レンダリングノードを利用して高速デコードを実行できること

#### Scenario: NVIDIA GPU (CUDA) によるハードウェア支援

- **WHEN** WSL 3.0 (`wslc`) または Docker で `--gpus all` が指定されてコンテナが起動されたとき
- **THEN** コンテナ内の FFmpeg がホストの NVIDIA CUDA コアを利用して高速デコードを実行できること

### Requirement: ポート公開と環境変数注入

システムは、Kestrel Web サーバーのポート（既定: 5620）をホスト側にマッピング（`5620:5620`）し、必須環境変数（`PORT=5620`, `VIDEO_DIRS=/app/videos/<名1>;/app/videos/<名2>;...`, `THUMBNAIL_DIR=/app/data/thumbnails`, `DATABASE_PATH=/app/data/metadata.db`）をコンテナ起動時に確実に注入しなければならない（SHALL）。`VIDEO_DIRS` はマウント先のコンテナ内パスを `;` 区切りで列挙しなければならない（SHALL）。

#### Scenario: 環境変数の注入と疎通確認

- **WHEN** デプロイスクリプト（`scripts/deploy.ps1` / `scripts/deploy.sh`）または単一コンテナ CLI の `run` コマンドによりコンテナが起動したとき
- **THEN** ポート 5620 で HTTP サーバーが待機し、指定されたコンテナ内パスで DB や動画ディレクトリが初期化されること

#### Scenario: コンテナ内 VIDEO_DIRS の整合

- **WHEN** ホスト `VIDEO_DIRS=D:\X;F:\Y` でコンテナが起動したとき
- **THEN** コンテナ内の環境変数 `VIDEO_DIRS` は `/app/videos/X;/app/videos/Y` となり、マウント先と一致すること

### Requirement: Docker Compose 構成ファイルの非依存と単一コンテナ運用の一本化

システムは、`docker-compose.yml` を保持・参照することなく、Windows（WSL 3.0 `wslc` または `docker`）および Linux ホスト（`docker` または `podman`）において、`Dockerfile` とデプロイスクリプト（`scripts/deploy.ps1`, `scripts/deploy.sh`）のみで単一コンテナのビルド・起動・GPU パススルー構成を完結させなければならない（SHALL）。

#### Scenario: リポジトリからの docker-compose.yml 廃止とドキュメント整合

- **WHEN** リポジトリのファイル構成および `Dockerfile`・`README.md` を検査したとき
- **THEN** リポジトリルートに `docker-compose.yml` が存在せず、`Dockerfile` のコメントおよび `README.md` の起動手順に `docker-compose.yml` や `docker compose` コマンドへの参照が含まれないこと

#### Scenario: Windows (WSL 3.0) および Linux ホスト双方でのデプロイ完結

- **WHEN** Windows 環境で `scripts/deploy.ps1`、または Linux ホスト環境で `scripts/deploy.sh` を参照・実行したとき
- **THEN** `docker-compose.yml` を一切必要とせず、Windows では `wslc`（または `docker`）、Linux では `docker`（または `podman`）を用いて `Dockerfile` からイメージをビルドし、単一コンテナを起動できること
