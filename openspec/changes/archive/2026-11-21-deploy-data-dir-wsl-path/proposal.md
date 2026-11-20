# Proposal: デプロイスクリプト（deploy.ps1 / deploy.sh）の DATA_DIR / VIDEO_DIRS パス解決改善（WSL3 内パス・相対パス対応と既存データ保護）

## Metadata

- **Change Name**: `deploy-data-dir-wsl-path`
- **Scale Level**: `L2`（通常変更: 仕様変更・バグ修正・クロスプラットフォームデプロイスクリプト改善）
- **Type**: `fix` / `feat`（既存の `wslc` バインドマウントパス誤変換バグ修正、および WSL3 内 `DATA_DIR` 永続化・`deploy.sh` 相対パス解決・既存マウント不一致警告の追加）
- **Related Issue**: [#39](https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/39)
- **Target Applications Version**: 変更なし（Web アプリ `TagBasedVideoManager` およびデスクトップアプリ `TagBasedVideoManager.Renamer` のプロダクションコード・`.fsproj` は変更しないため、各 `<Version>` は現状維持）

---

## Why

1. **実機 `wslc`（WSL 3.0 Containers）でのマウントパス誤解釈による空フォルダマウント（Root Cause）**:
   - 現在の `scripts/common.ps1`（`ConvertTo-WslPath`）は、`D:\Anime` や `./data` のような Windows パスを `/mnt/d/Anime` 形式の WSL2 Linux パスへ一律変換して `wslc run -v` に渡している。
   - しかし、Windows ホスト上で動作する `wslc.exe`（実機確認済み: `wslc 3.0.5.0`）はホストパスとして **Windows 絶対パス（スラッシュ区切り `D:/...`）および WSL UNC パス（`\\wsl.localhost\<Distro>\...`）** を直接解釈する仕様であり、`/mnt/e/e` のような Linux 形式パスを渡すと「Windows カレントドライブ上の相対ルートパス（例: `D:\mnt\e\e`）」として解釈し、空ディレクトリを新規作成してマウントしてしまう不具合を引き起こしている。
2. **WSL3 内部（ext4 側）での `DATA_DIR` 永続化未対応**:
   - SQLite DB（`metadata.db`）や大量のサムネイル（`thumbnails/`）を I/O 性能の高い WSL3 内部（`\\wsl.localhost\<Distro>\home\...` または `/home/...`）に永続化したいケース（既に WSL3 内に存在する本番データの引き継ぎ含む）において、`ConvertTo-WslPath` が `\\wsl.localhost\...` を `//wsl.localhost/...` へ破壊し、さらに `deploy.ps1` の `Test-Path` / `New-Item` が `/home/...` を Windows カレントドライブ直下（`D:\home\...`）として作成しようとしてしまう。
3. **`scripts/deploy.sh` の相対パス未解決・不正パスガード不足と既存データ取り違えリスク**:
   - `deploy.sh` は `"DATA_DIR": "./data"` 等の相対パスを `${REPO_ROOT}` 基準の絶対パスに解決せずそのまま `mkdir -p` と `-v` に渡すため実行ディレクトリ次第で意図しない場所を参照する。また、Windows/UNC パス誤混入時のガードや、既存コンテナの `/app/data` マウント元と異なる `DATA_DIR` で上書き起動しようとした際の警告表示が存在しない。

---

## What Changes

- **Windows (`scripts/common.ps1` / `scripts/deploy.ps1`) のバインドマウントパス解決刷新**:
  - `wslc`（および Windows 版 `docker.exe`）向けにホストマウント用パスを正しく生成する解決関数を導入し、Windows パスはスラッシュ区切りの絶対パス（`D:/...`）、WSL UNC パス（`\\wsl.localhost\<Distro>\...`, `\\wsl$\<Distro>\...`）は正規化した `\\wsl.localhost\<Distro>\...`（または指定 UNC 形式）として `-v` へ渡すように修正する（`VIDEO_DIRS` と `DATA_DIR` の双方を修正）。
  - `DATA_DIR` に Linux 絶対パス（例: `/home/user/data`）が指定された場合は、既定の WSL ディストロ名（`wsl -l -q` 等で解決、テスト時は注入可能）を用いて `\\wsl.localhost\<Distro>\<subpath>` へ自動解決し、WSL 側でディレクトリの存在確認・作成を行う（Windows ドライブ直下にフォルダを作らない）。
  - 既存の `ConvertTo-WslPath` 関数は後方互換性を維持しつつ、`\\wsl.localhost\<Distro>\<subpath>` / `\\wsl$\<Distro>\<subpath>` → `/<subpath>` 変換にも対応させる。
- **Linux / WSL Bash (`scripts/deploy.sh`) の `DATA_DIR` 解決・検証強化**:
  - `DATA_DIR` の相対パス（例: `./data`）を `${REPO_ROOT}` 基準の絶対パスへ正規化する。
  - `\\wsl.localhost\...` / `\\wsl$\...` や Windows ドライブパスが指定された場合、WSL 上の bash 実行かつ変換可能なケース（同一環境）の扱いおよび Linux ネイティブ環境での明確な Fail-Fast エラー検証を整備する。
- **既存コンテナ `/app/data` マウント元変更時の警告表示（`deploy.ps1` / `deploy.sh` 共通）**:
  - デプロイ対象の既存コンテナ（`tag-based-video-manager`）が存在する場合、現在の `/app/data` バインドマウント元を取得し、今回指定された `DATA_DIR` と異なる場合に警告メッセージと旧パス・新パス・データ移行案内を出力する（非対話実行を阻害しないよう警告の上で続行）。
- **テスト・ドキュメント更新**:
  - `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs` に単体テスト・境界値・デシジョンテーブル・結合テストを追加する。
  - `README.md` および `PRODUCTION_ENVIRONMENT.md` のパス変換仕様・WSL3 永続化手順を更新する。

---

## Non-goals（やらないこと）

- 既存コンテナの `/app/data` 内の実データ（`metadata.db`, `thumbnails/`）をスクリプトが自動的に別ディレクトリへコピー／削除する自動ファイルマイグレーション（意図しない大容量サムネイルコピーや上書き事故を防ぐため警告・案内のみとする）。
- `wslc` 名前付きボリューム（`wslc volume`）による `/app/data` 永続化への移行（ホストパス / WSL3 ディストロ内パスでのバインドマウント構成を維持）。
- Web アプリ本体（`src/TagBasedVideoManager/*.fs`）およびデスクトップアプリ（`src/TagBasedVideoManager.Renamer/*.fs`）のプロダクションコード変更。

---

## Capabilities

### New Capabilities

<!-- なし（既存デプロイスクリプトおよびインフラ仕様の拡張・修正） -->

### Modified Capabilities

- `container-deployment-scripts`: `deploy.ps1` における `wslc` / Windows 向けホストマウントパス解決（`VIDEO_DIRS`・`DATA_DIR` の `D:/...` / `\\wsl.localhost\...` 対応、Linux パスの既定 WSL ディストロ自動解決）、`deploy.sh` における `DATA_DIR` 相対パスの `${REPO_ROOT}` 基準絶対パス正規化と Windows/UNC パス誤指定ガード、および既存コンテナの `/app/data` マウント元変更時警告を追加する。
- `docker-infrastructure`: WSL 3.0 (`wslc`) 実行時のホストパス解決仕様（Windows スラッシュパスおよび `\\wsl.localhost\...` による WSL3 永続化）を反映する。

---

## 受け入れ基準（Acceptance Criteria）

1. **Given** Windows ホスト上で `VIDEO_DIRS="E:\e;D:\e_d"` および `DATA_DIR="./data"` が指定されているとき / **When** `scripts/deploy.ps1` で `wslc` 向けにマウント引数を構築すると / **Then** `-v E:/e:/app/videos/e:ro`、`-v D:/e_d:/app/videos/e_d:ro`、`-v D:/.../data:/app/data` の形式で生成され、`/mnt/...` 誤変換による空ディレクトリが生成されないこと。
2. **Given** `DATA_DIR` に `\\wsl.localhost\Ubuntu-24.04\home\user\tbvm\data` または `/home/user/tbvm/data` が指定されているとき / **When** `scripts/deploy.ps1` を実行すると / **Then** Windows ドライブ直下（`D:\home\...` 等）にディレクトリを作らず、既定ディストロの WSL UNC パスとして存在確認・作成され、`-v \\wsl.localhost\Ubuntu-24.04\home\user\tbvm\data:/app/data` が構築されること。
3. **Given** Linux 環境の `scripts/deploy.sh` で `DATA_DIR="./data"` が設定されているとき / **When** 任意のカレントディレクトリから `deploy.sh` を実行すると / **Then** `${REPO_ROOT}/data` の絶対パスへ正規化されてマウントされること。また、Linux 環境で Windows パスや変換不能な UNC パスが指定された場合はイメージビルド・コンテナ停止前に非 0 でエラー終了すること。
4. **Given** 既存の `tag-based-video-manager` コンテナが `/app/data` に別パスをマウントしている状態で / **When** 異なる `DATA_DIR` を指定して `deploy.ps1` / `deploy.sh` を実行すると / **Then** 旧マウント元と新マウント先を含むデータ移行警告メッセージが標準出力/警告に出力されること。

---

## Impact

- `scripts/common.ps1`
- `scripts/deploy.ps1`
- `scripts/deploy.sh`
- `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs`
- `README.md`, `PRODUCTION_ENVIRONMENT.md`
