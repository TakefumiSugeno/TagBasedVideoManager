# Docker設計書 (Docker Design Document)

本ドキュメントは、TagBasedVideoManager を Docker コンテナ環境下で効率的かつ安全にビルド・運用するための、ファイル配置、永続化、および GPU 支援（ハードウェアアクセラレーション）に関する設計を整理したものです。

---

## 1. Docker化の基本設計思想

本システムは、ポータビリティとインストールの容易性を最優先とし、**「単一コンテナで完結するデプロイ」** を目指します。

- **マルチステージビルドの採用**:
  - ビルドステージには大容量の .NET 10 SDK イメージ (`mcr.microsoft.com/dotnet/sdk:10.0`) を使用します。
  - ランタイムステージには、フットプリントの小さい ASP.NET ランタイムイメージ (`mcr.microsoft.com/dotnet/aspnet:10.0`) を使用し、最終イメージサイズを最小限に抑えます。
- **静的ファイルのバンドル**:
  - `Microsoft.NET.Sdk.Web` のビルドシステムにより、`src/wwwroot` 配下のフロントエンドアセットは `dotnet publish` 実行時に自動的に `/app/wwwroot` へコピー・同封されます。
- **外部依存ツール（FFmpeg）の同封**:
  - メディア処理を行うため、ランタイムステージのパッケージマネージャ（`apt-get`）を介して `ffmpeg` および `ffprobe` をコンテナ内に自動調達します。

---

## 2. ディレクトリ配置とボリュームマウント（永続化）設計

コンテナの再起動や破棄に伴うデータ喪失を防ぐため、コンテナ内外のディレクトリマウント関係を厳密に分離します。とくに Windows 11 / WSL2 環境におけるマウント先との組み合わせを考慮します。

| 役割 | コンテナ内パス | ホスト側マウント例 (docker-compose) | 永続化 / マウント属性 | 説明 |
| :--- | :--- | :--- | :--- | :--- |
| **① 動画ファイル** | `/app/videos` | `/mnt/c/Users/Public/Videos` | 必須 / **Read-Only (`:ro`)** | Windows ホスト側の実際の動画フォルダを WSL 経由でマウントします。安全のため読み取り専用にします。 |
| **② データベース** | `/app/data/metadata.db` | `./data/metadata.db` | 必須 / **Read-Write** | アプリによる書き込み（スキャン結果、タグ等）が発生するため、ホスト側の `./data` フォルダにマウントして永続化します。 |
| **③ サムネイル画像** | `/app/data/thumbnails` | `./data/thumbnails` | 必須 / **Read-Write** | 非同期バックグラウンド処理で動的に生成される画像ファイル群を永続化します。 |

### 2.1 ローカル開発環境用環境変数定義ファイル (`.env`) サンプル

ローカル開発環境（非コンテナ環境）や開発・テスト実行時（`dotnet run` / `dotnet test`）に環境変数をロードするため、プロジェクトルートディレクトリに `.env` ファイルを配置します（※本番用資格情報などを含みうるため、このファイルは `.gitignore` にて Git 追跡から除外しています）。

以下は、ローカル環境でのパス設定に合わせた `.env` 記述のサンプルです。

```ini
# TagBasedVideoManager - ローカル開発用環境変数サンプル
PORT=5620

# ホストOS側の動画ディレクトリの絶対パス
# 例 (Windows): VIDEO_DIR=C:\Users\spjao\Videos
# 例 (WSL2): VIDEO_DIR=/mnt/c/Users/spjao/Videos
VIDEO_DIR=D:\programming\repos\TagBasedVideoManager\test\videos

# 生成されたサムネイル画像の保存先ディレクトリ
THUMBNAIL_DIR=D:\programming\repos\TagBasedVideoManager\test\thumbnails

# SQLiteデータベースファイルの保存先
DATABASE_PATH=D:\programming\repos\TagBasedVideoManager\test\video_manager.db
```

---

## 3. Dockerfile の構造詳細

ルートに配置された `Dockerfile` は以下の2ステージ構成です。

```dockerfile
# =============================================================================
# 1. ビルドステージ
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# プロジェクトファイルをコピーして復元 (依存関係レイヤーのキャッシュ化)
COPY ["src/TagBasedVideoManager.fsproj", "./src/"]
RUN dotnet restore "./src/TagBasedVideoManager.fsproj"

# ソースコード全体をコピーして Release ビルド
COPY src/ ./src/
WORKDIR /src/src
RUN dotnet publish "TagBasedVideoManager.fsproj" -c Release -o /app/publish /p:UseAppHost=false

# =============================================================================
# 2. ランタイムステージ
# =============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# メディア処理に必要な FFmpeg / ffprobe をインストール
RUN apt-get update && apt-get install -y \
    ffmpeg \
    && rm -rf /var/lib/apt/lists/*

# ビルド成果物をビルドステージからコピー
COPY --from=build /app/publish .

# 実行時エラーを防ぐため、必要なフォルダ構造をコンテナ内に事前作成
RUN mkdir -p /app/videos /app/data/thumbnails

# デフォルト環境変数の定義
ENV PORT=5620
ENV VIDEO_DIR=/app/videos
ENV THUMBNAIL_DIR=/app/data/thumbnails
ENV DATABASE_PATH=/app/data/metadata.db
EXPOSE 5620

ENTRYPOINT ["dotnet", "TagBasedVideoManager.dll"]
```

---

## 4. コンテナ起動構成 (`docker-compose.yml`)

ポートマッピングおよびボリューム、環境変数の注入をシームレスに行うため、以下の `docker-compose.yml` を定義します。

```yaml
version: '3.8'

services:
  video-manager:
    build:
      context: .
      dockerfile: Dockerfile
    container_name: tag-based-video-manager
    ports:
      - "5620:5620"
    environment:
      - PORT=5620
      - VIDEO_DIR=/app/videos
      - THUMBNAIL_DIR=/app/data/thumbnails
      - DATABASE_PATH=/app/data/metadata.db
    volumes:
      # 【データ永続化】DBとサムネイル画像をホスト側の `./data` フォルダに永続化
      - ./data:/app/data
      # 【動画ファイル】Windowsホスト側の動画フォルダをマウント (環境に合わせて書き換え)
      - /mnt/c/Users/Public/Videos:/app/videos:ro
    restart: unless-stopped
```

---

## 5. GPUアクセラレーションの構成と切り替え手順

サムネイル生成時の FFmpeg プロセスのデコード速度を向上させるため、ホストマシンの GPU リソースをコンテナから利用可能にする設定を提供します。`docker-compose.yml` で切り替えて使用します。

### パターン1: CPU のみで処理する場合 (デフォルト)
- **設定方法**: `docker-compose.yml` を編集せずそのまま起動します。
- **特徴**: 最も互換性が高く、GPU を搭載していないホストマシンや、仮想環境で動作させる場合に適しています。

### パターン2: AMD GPU を使用する場合 (VAAPI)
- **設定方法**: `docker-compose.yml` の `services.video-manager` 内に、以下の `devices` セクションを追加（コメントイン）します。
  ```yaml
      devices:
        - /dev/dri:/dev/dri
  ```
- **前提要件**:
  - ホストOSに AMD GPU ドライバが導入されており、`/dev/dri/renderD128` などのデバイスノードが存在すること。
  - WSL2 環境では、ホスト（Windows）側に最新の ROCm/AMD ドライバがインストールされている必要があります。

### パターン3: NVIDIA GPU を使用する場合 (CUDA)
- **設定方法**: `docker-compose.yml` の `services.video-manager` 内に、以下の `deploy` セクションを追加（コメントイン）します。
  ```yaml
      deploy:
        resources:
          reservations:
            devices:
              - driver: nvidia
                count: all
                capabilities: [gpu, video]
  ```
- **前提要件**:
  - ホストに NVIDIA グラフィックドライバが導入されていること。
  - ホスト上に **NVIDIA Container Toolkit** がインストールされ、Docker ランタイムに登録されていること。
  - WSL2 環境下では、WSL 内のシェルで `nvidia-smi` が正常に応答すること。

---

## 6. Windows 11 / WSL環境におけるマウント時の注意点

1. **パスの対応関係**:
   - Windows 11 の物理ドライブ（例: `C:\Users\Public\Videos`）は、WSL2 上ではマウントドライブ `/mnt/c/Users/Public/Videos` として表現されます。
   - `docker-compose.yml` でマウント元を指定する際は、この `/mnt/...` 形式の WSL2 パスを使用してください。
2. **パーミッションと文字コード**:
   - マウントする動画ファイルの名前に日本語文字（Unicode）が含まれている場合でも、F# バックエンドおよび SQLite は Unicode で正常に処理します。
   - マウント先ボリュームへの書き込みが必要なパス（`./data`）に関しては、Docker を実行する WSL ユーザーに書き込み権限（`chmod -R 777 ./data` など）が付与されていることを確認してください。
