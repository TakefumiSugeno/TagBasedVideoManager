# Technical Design: WSL3 および DevContainer サポート

## Context

本システムは、バックエンドが .NET 10 + Giraffe + SQLite + FFmpeg で構成される単一 Web アプリケーションと、ホスト上で動作する Avalonia.FuncUI 製 Desktop Companion（Renamer）から成り立っています（詳細は `proposal.md` 参照）。
従来はコンテナ起動・管理に Docker Compose を使用していましたが、本アプリはマルチコンテナではなく単一コンテナ構成であるため、Compose のオーケストレーション機能は本質的に不要です。
Windows Subsystem for Linux 3.0（WSL 3.0）の GA により、Windows 標準のコンテナ CLI（`wslc.exe`）が利用可能となったことを受け、Docker Desktop 依存を排除し、Windows（WSL 3.0）および Linux（Docker/Podman）で共通して動作する単一コンテナ運用・開発基盤を設計します。

## Goals / Non-Goals

### Goals

- Windows 11 環境において Docker Desktop をインストールすることなく、WSL 3.0（`wslc`）単体で本番コンテナのビルド・起動・管理・再起動を行えるようにする。
- Linux ホスト環境でも `docker` または `podman` を通じて同一の運用・制御を可能にする。
- ソースコード変更後の再ビルド・コンテナ再作成・疎通確認を 1 コマンドで実行できるデプロイスクリプト（`scripts/deploy.ps1`, `scripts/deploy.sh`）を提供する。
- デプロイ時の旧コンテナ削除・新コンテナ作成において、SQLite DB（`metadata.db`）およびサムネイル（`thumbnails`）をホスト側の `./data` にバインドマウントしてデータ喪失を防止する。
- VS Code DevContainer 環境を提供し、開発環境専用ポート（5621）を割り当てることで、本番コンテナ（5620）を停止せずに並行開発できるようにする。
- Companion アプリの `DockerController.fs` を単一コンテナ OCI CLI に適合させ、`wslc` と `docker` の両方をシームレスに扱えるようにする。

### Non-Goals

- AMD GPU 用の独自カーネルパッチや特殊なデバイスマウント（`wslc` が `--device` をサポートするまでは AMD GPU は CPU フォールバックとする）。
- Avalonia.FuncUI デスクトップアプリの DevContainer 内 X11/Wayland 描画対応。
- 既存の `docker-compose.yml` の即時削除（移行期間中の代替手段として維持）。

## Architecture & Decisions

```
+-----------------------------------------------------------------------------+
|                         SYSTEM ARCHITECTURE OVERVIEW                        |
+-----------------------------------------------------------------------------+

  [Host OS: Windows 11 / Linux]
    |
    |-- scripts/deploy.ps1 / deploy.sh
    |     1. ポート 5620 事前衝突チェック
    |     2. CLI 検出 (wslc / docker / podman) & 未導入時ガイダンス
    |     3. build -t tag-based-video-manager:latest .
    |     4. stop & rm tag-based-video-manager
    |     5. run -d --name tag-based-video-manager -p 5620:5620 \
    |          -v <VideoDir>:/app/videos:ro -v ./data:/app/data ...
    |     6. HTTP GET http://localhost:5620/ 疎通リトライ確認 (失敗時は logs 出力)
    |
    |-- TagBasedVideoManager.Renamer (Desktop Companion)
    |     [ContainerController.fs]
    |       - ランタイム自動検出 (wslc -> docker)
    |       - プロセス実行タイムアウト (10秒) & エラーログ出力
    |       - CLI 呼び出し: ps / start / stop / restart
    |       - ポート 5620 監視 + リネーム連動再起動
    |
    +-- VS Code DevContainer (.devcontainer/)
          - ベースイメージ: mcr.microsoft.com/devcontainers/dotnet:10.0
          - パッケージ: ffmpeg, ffprobe, sqlite3, curl
          - ポートマッピング: 5621:5621 (PORT=5621)
          - 拡張機能: Ionide (F#), C# DevKit, Prettier
```

### Decision 1: 単一コンテナ OCI CLI 抽象化（`ContainerController.fs`）と耐障害性設計

- **選択**: 従来の `docker compose` コマンド呼び出しを廃止し、コンテナ CLI のサブコマンド（`ps`, `start`, `stop`, `restart`）を直接実行する。
- **ランタイム検出順序**:
  1. Windows 環境: `wslc.exe` の存在確認 → あれば `wslc` を使用。なければ `docker.exe` を使用。いずれもなければ `NotInstalled` 状態。
  2. Linux 環境: `docker` → なければ `podman` を使用。
- **プロセス実行の耐障害性（タイムアウト＆キャンセル機構）**:
  - 外部プロセス呼び出しには最大 **10 秒のタイムアウト** を設定する（`CancellationTokenSource` を利用）。
  - タイムアウト超過時は `proc.Kill(entireProcessTree = true)` を実行してプロセスハンドルを確実に解放し、UI フリーズやプロセスのゾンビ化を防止する。
- **可観測性の確保（Silent Discard 廃止）**:
  - CLI プロセスが非ゼロで終了した場合、または JSON パースに失敗した場合は、例外を握りつぶさずに stderr の内容および受信した生テキストをコンソール/デバッグログに出力する。
  - ステータス判定において、ランタイム未検出時は `NotInstalled`、パース失敗時は `Unhealthy`（警告ログ付き）を明示し、障害の切り分けを即座に行えるようにする。

### Decision 2: デプロイスクリプトの共通ライフサイクル設計と事前保護

- **配置**: `scripts/deploy.ps1`（Windows pwsh 用）および `scripts/deploy.sh`（Linux bash 用）。
- **シーケンス**:
  1. **ポート事前衝突チェック**: `Test-NetConnection` または `lsof`/`nc` を用いてポート 5620 が使用中か確認。使用中の場合はコンテナ停止・再作成を実行せず即時終了。
  2. **環境設定**: `.env` ファイルが存在すれば環境変数（`PORT`, `VIDEO_DIR`, `DATABASE_PATH`, `THUMBNAIL_DIR`）を読み込み。デフォルト値を設定。
  3. **ランタイム自動検出**: 未検出時は「Windows 11 の場合は `wsl --update` を実行してください」というガイダンスを表示して終了コード 1 で中断。
  4. **ビルド**: `build -t tag-based-video-manager:latest -f Dockerfile .`
  5. **コンテナ再作成**: 既存コンテナの停止（`stop tag-based-video-manager`）および削除（`rm -f tag-based-video-manager`）。
  6. **新コンテナ起動**: `run -d --name tag-based-video-manager -p 5620:5620 -v <wslVideoPath>:/app/videos:ro -v ./data:/app/data ...`（NVIDIA GPU 検出時は `--gpus all` を自動付与）。
  7. **ヘルスチェック**: 最大 30 秒間、3 秒間隔で `http://localhost:5620/` をポーリング。タイムアウト時は `logs --tail 50` をコンソールに出力して終了コード 1 で終了。

### Decision 3: Windows パスから WSL パスへの変換ユーティリティ

- **設計**: `scripts/deploy.ps1` において、Windows パス（例: `C:\Users\Public\Videos` やスペースを含む `D:\My Videos`）を WSL パス（例: `/mnt/c/Users/Public/Videos`, `/mnt/d/My Videos`）へと変換する専用関数を設ける。
- **規則**:
  - ドライブレター（大文字/小文字）を `/mnt/<小文字ドライブ文字>` に変換。
  - バックスラッシュ（`\`）をスラッシュ（`/`）に置換。
  - 末尾スラッシュを正規化して削除。

### Decision 4: DevContainer のポート分離（5621 vs 5620）

- **選択**: 本番コンテナの待ち受けポート `5620` に対し、DevContainer 内の環境変数に `PORT=5621` を注入し、ホスト側へのフォワードポートを `5621` とする。
- **理由**:
  - 常用コンテナをバックグラウンドで動かしたまま、開発者が VS Code で DevContainer を開き、`dotnet run` やデバッグ実行を衝突なく行えるようにするため。
  - Companion アプリが DevContainer の起動を本番コンテナと誤認する事故を構造的に防ぐため。

## Risks / Trade-offs

- **[Risk 1: WSL 3.0 未導入の Windows 11 環境での挙動]**
  - → **Mitigation**: スクリプトおよび Companion アプリは `wslc` が見つからない場合、自動的に `docker`（Docker Desktop 等）へフォールバックします。両方存在しない場合は `wsl --update` を案内するガイダンスを表示します。
- **[Risk 2: AMD GPU パススルーの制限]**
  - → **Mitigation**: `wslc` は `--device` フラグが未実装であるため、NVIDIA GPU のみ `--gpus all` を付与し、AMD GPU または GPU 未検出環境では CPU ソフトウェアデコードでフォールバック動作させます。
- **[Risk 3: Windows のファイルパス形式と WSL 内パス形式の変換]**
  - → **Mitigation**: スペースやドライブレター、末尾記号に対応したパス変換関数を実装し、単体テストで境界値を網羅します。

## Migration Plan

1. `scripts/` ディレクトリを作成し、`deploy.ps1` および `deploy.sh` を配置。
2. `.devcontainer/` ディレクトリを作成し、`devcontainer.json` および `Dockerfile` を配置。
3. `TagBasedVideoManager.Renamer` 内の `DockerController.fs` を `ContainerController.fs` に更新（既存インターフェース `checkStatus` / `executeAction` は後方互換性を維持し、タイムアウトとログ出力を付与）。
4. 既存の `docker-compose.yml` は削除せず、ドキュメントに「単一コンテナ運用（`scripts/deploy.ps1`）を推奨」と追記。
