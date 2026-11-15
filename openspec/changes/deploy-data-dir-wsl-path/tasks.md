# Tasks: deploy-data-dir-wsl-path

## Phase 1: テスト先行実装 (QA Tester — Red)

- [ ] 1.1 不具合再現テストの追加（`test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs`）
  - `UT-REPRO-01`: `TargetIsWindows=$true` で `VIDEO_DIRS="E:\e"` が `/mnt/e/e` ではなく `E:/e` に変換され、`DATA_DIR="D:\repo\data"` の `MountSource` が `D:/repo/data` になることを検証する失敗テストを作成
  - `UT-REPRO-02`: `Resolve-DataDir` で Linux 絶対パス `/home/spjao/var/apps/TagBasedVideoManager/data` が `D:\home\...` に化けず `HostPath="\\wsl.localhost\<Distro>\home\spjao\var\apps\TagBasedVideoManager\data"` へ解決されることを検証する失敗テストを作成
  - `UT-REPRO-03`: `scripts/deploy.sh` で `DATA_DIR="./data"` が相対パスのまま `-v ./data:/app/data` にならず `${REPO_ROOT}/data` へ正規化されることを検証する失敗テストを作成
- [ ] 1.2 `scripts/common.ps1` 新規ヘルパーの単体テスト追加（`DeployScriptTests.fs`）
  - `Get-DefaultWslDistro`（`UT-WSL-01`〜`03`: `docker-desktop` 除外・UTF-16LE BOM / `\0` / `\uFFFD` パース・未導入/タイムアウト時の回復案内エラー）および `Resolve-WslUncPath` の単体テストを追加
  - `Resolve-DataDir` の境界値・デシジョンテーブル D1〜D10（`UT-DATA-01`〜`13`: 相対、Windows 絶対、`\\wsl.localhost\...`、`\\wsl$\...`、Linux 絶対、空文字フォールバック vs `Resolve-WslUncPath` 空拒否、セグメント `..` トラバーサル vs 正当名 `v1..v2`、ルート直下/ドライブ直下/ドライブ相対、非 WSL UNC、既存通常ファイル誤指定、空白入りパス、戻り値 5 プロパティ契約）テストを追加
  - `ConvertTo-ContainerMountPath`（M1〜M4）および `ConvertTo-WslPath` UNC 対応（`UT-PATH-05`）のテストを追加
  - `Resolve-VideoMounts` における `-TargetIsWindows $true` スラッシュ形式・WSL UNC パス・Linux 絶対パス・空白入りパス・通常ファイル/ドライブ相対拒否（`UT-MOUNT-01`〜`06`, `UT-MOUNT-RO-01`, `/app/videos/<Name>` 正本検証）テストを追加
  - `Get-ExistingContainerDataMount` / `Test-SameMountSource`（`UT-WARN-01`〜`05`: 不一致、大文字小文字/スラッシュ差、旧 `/mnt/...` 検知、コンテナ未存在 `$null`、非空不正 JSON 警告、実機 `wslc inspect` 配列スキーマ抽出）のテストを追加
- [ ] 1.3 `scripts/deploy.ps1` / `scripts/deploy.sh` 結合テスト追加（`DeployScriptTests.fs`）
  - エフェメラルポートの一時 HTTP リスナー＋ダミー `wsl.exe` / ダミー `wslc.cmd` を用いた `IT-DATA-SIDE-01`, `IT-DATA-WSL-01`, `IT-DATA-WSL-02`, `IT-WARN-01`〜`03`, `IT-DEPLOY-OVERRIDE-01`, `IT-DEPLOY-FAILFAST-01` を追加
  - `bash` + モック `docker` による `IT-SH-DATA-01`〜`09`, `IT-SH-VIDEO-01`〜`02`, `IT-SH-WARN-01`, `IT-DATA-PERSIST-01` を追加
- [ ] 1.4 Red 状態の確認
  - `dotnet test test/TagBasedVideoManager.Renamer.Tests/TagBasedVideoManager.Renamer.Tests.fsproj --filter "FullyQualifiedName~DeployScript"` を実行し、新規追加テストが実装前のため期待通り Red（失敗）になることを確認する

## Phase 2: プロダクションコード実装・ドキュメント更新 (PG Implementer — Green & Refactor)

- [ ] 2.1 `scripts/common.ps1` のパス解決・マウント変換・比較ヘルパー実装
  - `ConvertTo-WslPath` に `\\wsl.localhost\<Distro>\...` および `\\wsl$\<Distro>\...` → `/...` 変換を追加し、既存の `/mnt/<drive>/...` 変換の後方互換を維持する
  - `Get-DefaultWslDistro` を実装し、`System.Diagnostics.Process` によるタイムアウト（5000ms）・`WSL_UTF8=1`・`\0` / `\uFFFD` 除去・終了コード検証・システムディストロ（`docker-desktop`, `docker-desktop-data`）除外を行い、0 件/失敗時は回復案内付きで `throw` する
  - `Resolve-WslUncPath` を実装し、正規化とセグメント境界 `..`・ルート直下・非 WSL UNC 拒否を行う
  - `ConvertTo-ContainerMountPath` を `-TargetIsWindows` パラメータ契約通りに実装する
  - `Resolve-DataDir` を設計書の I/F 契約（戻り値 `{ RawInput; ResolvedFrom; DistroName; HostPath; MountSource }`）通りに実装し、空文字フォールバック、通常ファイル拒否（`-PathType Leaf` / `-PathType Container`）、ドライブ直下/相対拒否、事前作成（`-CreateIfMissing`）を実装する
  - `Resolve-VideoMounts` を更新し、`-TargetIsWindows` による `ConvertTo-ContainerMountPath` 利用、WSL UNC/Linux パス解決、空白保持、通常ファイル・`..`・ドライブ相対拒否を組み込む
  - `Get-ExistingContainerDataMount`（`ProcessStartInfo` 5000ms タイムアウト、`@($parsed)[0].Mounts` 抽出、非空不正 JSON 時の診断警告、`ErrorActionPreference='Stop'` 耐性）と `Test-SameMountSource` を実装する
- [ ] 2.2 `scripts/deploy.ps1` の `DATA_DIR` 解決、可観測性ログおよびマウント変更警告対応
  - lines 35-48 付近の `DATA_DIR` 処理を `Resolve-DataDir -CreateIfMissing` 呼び出しに置き換え、`MountSource` を `New-RunArguments` の `-DataDir` 相当へ渡す
  - 起動前サマリに `DATA_DIR` の設定元・生値・WSL ディストロ名・`HostPath`・`MountSource` を出力する
  - **イメージビルド・既存コンテナ停止より前**に `Get-ExistingContainerDataMount` + `Test-SameMountSource` を実行し、不一致時に 4 要素（旧/新パス、旧データ非削除の明記、`appsettings.Production.json` 復帰例、PowerShell `Copy-Item` 手順例）を出力する
- [ ] 2.3 `scripts/deploy.sh` の `DATA_DIR` / `VIDEO_DIRS` パス解決・検証および警告対応
  - `DATA_DIR` の空/空白フォールバック（`${REPO_ROOT}/data`）、相対パスの POSIX/Bash 互換正規化（`python3` 利用＋純粋 Bash フォールバック）、`..` セグメント境界判定（`v1..v2` 許可）、通常ファイル拒否、ルート `/` 単体拒否を実装する
  - `DATA_DIR` / `VIDEO_DIRS` 双方で Windows ドライブパス（`^[a-zA-Z]:`）をビルド前に拒否（Linux パス設定例を案内）し、WSL UNC パスは `WSL_DISTRO_NAME` 非空かつ一致時のみ `/<subpath>` へ変換（それ以外は非 0 終了）する
  - 起動前サマリの可視化ログ、およびビルド/停止前の既存コンテナ `/app/data` マウント元変更警告（4 要素: 旧/新パス、旧データ非削除、設定復帰例、`cp -a` 手順例）を実装する
- [ ] 2.4 運用ドキュメントの更新
  - `README.md` の `DATA_DIR` / `VIDEO_DIRS` 設定例と `deploy.ps1` / `deploy.sh` のパス指定仕様（相対パス、Windows 絶対パス、WSL UNC パス、Linux 絶対パス、空白入りパス）を更新する
  - `PRODUCTION_ENVIRONMENT.md` に WSL3 内 `DATA_DIR` 永続化手順、既存 `/mnt/...` 等からの切り替え時のデータ移行手順・注意点を追記する

## Phase 3: 結合・E2E 検証・品質確認 (QA & Gate 2)

- [ ] 3.1 実機・結合検証とテスト実行（TRX / カバレッジ出力）
  - `scripts/deploy.ps1` / `scripts/deploy.sh` 構文チェック（PowerShell Parser / `bash -n`）およびテストスイート全件実行
  - `dotnet test TagBasedVideoManager.sln --logger "trx;LogFileName=TestRun_<timestamp>_net10.0.trx" --collect:"XPlat Code Coverage"` 相当を `test/TestResults/` 出力規則に従って実行し、全テスト Green・リグレッション 0 を確認する
  - **実機 `wslc` 非破壊 E2E 確認**（本番コンテナ・実データを改変せず、`reviews.md` へ実行コマンドと検証結果を記録）:
    1. 既存コンテナ `dd1e1e9654c2` に対する `Get-ExistingContainerDataMount` 読み取りで実機 `\\wsl.localhost\Ubuntu-24.04\home\spjao\var\apps\TagBasedVideoManager\data` を抽出できること
    2. 一時コンテナ `wslc run --rm` と一時フォルダ（Windows 一時動画フォルダ + `/tmp` 内 WSL 一時データフォルダ）で `:ro` 読取・`/app/data` 書込が可能であり、`D:\mnt\...` が作成されないことを確認し掃除すること
- [ ] 3.2 フォーマット・批判的レビュー・Gate 2 検証
  - `npx prettier --write`（`README.md`, `PRODUCTION_ENVIRONMENT.md`, `openspec/**/*.md`）を実行
  - `pg-reviewer` / `qa-reviewer` / `se-reviewer` による独立レビューを実施し、Adversarial QA 破壊テストログを `reviews.md` に追記して `LGTM: true` を確認
  - `pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply -ChangeName "deploy-data-dir-wsl-path"` を実行し通過させる
