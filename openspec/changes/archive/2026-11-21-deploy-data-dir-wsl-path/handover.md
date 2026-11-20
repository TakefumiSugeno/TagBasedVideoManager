# 申し送り事項 (Handover): deploy-data-dir-wsl-path

- **変更名**: `deploy-data-dir-wsl-path`
- **日付**: 2026-11-21
- **ブランチ**: `feature/deploy-data-dir-wsl-path`
- **関連 Issue / PR**: https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/39

---

## 1. 変更サマリ

Windows ホストの WSL 3.0 Containers（`wslc`）運用および Linux 環境（`scripts/deploy.sh`）において、コンテナマウント用パス解決・WSL3 内での `DATA_DIR` 永続化・既存データ保護警告を整備した。また `README.md`・`PRODUCTION_ENVIRONMENT.md` にパス仕様・データ移行手順・トラブルシューティング手順を反映した（Web/Desktop 本体の `.fs` コードおよび `.fsproj` バージョンは変更なし）。

### 原因と対応

- **原因**:
  1. `scripts/common.ps1` の `ConvertTo-WslPath` が Windows 絶対パス（`E:\e` 等）を `/mnt/e/e` へ変換して Windows ネイティブ CLI である `wslc.exe` に渡していたため、`wslc` がカレントドライブ上の `D:\mnt\e\e` を誤参照し空フォルダを新規マウントしていた。また `\\wsl.localhost\...` 指定時は `//wsl.localhost/...` へ破壊していた。
  2. `scripts/deploy.ps1` が Linux 絶対パス（`/home/...`）の `DATA_DIR` に対して `[System.IO.Path]::GetFullPath` / `New-Item` を直接実行していたため、Windows カレントドライブ直下（`D:\home\...`）を参照・作成していた。
  3. `scripts/deploy.sh` は `./data` 等の相対パスを `${REPO_ROOT}` 基準の絶対パスへ正規化しておらず、実行時のカレントディレクトリ依存・Windows/UNC パス誤指定ガード・既存コンテナ `/app/data` マウント元変更時の移行警告が不足していた。
- **対応**:
  1. `scripts/common.ps1`: 既存の `ConvertTo-WslPath`（`/mnt/...` 出力互換維持＋WSL UNC→Linux 変換追加）を残しつつ、ホスト側マウント専用ヘルパー（`Get-DefaultWslDistro`, `Resolve-WslUncPath`, `ConvertTo-ContainerMountPath`, `Resolve-DataDir`, `Get-ExistingContainerDataMount`, `Test-SameMountSource`）を実装し、`Resolve-VideoMounts` をこれに合わせて更新した（5 秒タイムアウト、`docker-desktop` 除外、通常ファイル/ドライブ直下/ドライブ相対/偽装 UNC/セグメント `..` 拒否、角括弧 `[` `]` パス対応、WSL サブパス大小文字区別）。
  2. `scripts/deploy.ps1`: ビルド・既存コンテナ停止より前に `Resolve-VideoMounts`・`Resolve-DataDir -CreateIfMissing`・設定元/ディストロ/`HostPath`/`MountSource` の起動前サマリ出力・既存コンテナ `/app/data` マウント元変更警告（必須 4 要素）を実行するよう更新した。
  3. `scripts/deploy.sh`: 共通関数 `resolve_host_dir_for_linux` で `DATA_DIR` / `VIDEO_DIRS` の検証・相対パスの `${REPO_ROOT}` 基準絶対パス正規化・`WSL_DISTRO_NAME` 一致時のみの UNC 変換・Windows ドライブパス拒否を一元化し、5 秒タイムアウト付き `inspect` 警告と `utf-8-sig` / 厳格 `jq` JSON 検証を追加した（LF 改行維持）。
  4. **ボツ案・トレードオフ**: 既存コンテナの `/app/data` が新 `DATA_DIR` と異なる場合でも、大容量サムネイルの誤上書きや長時間停止を避けるため自動コピー／対話プロンプトは行わず、警告 4 要素（旧/新パス・非削除明記・設定復帰例・コピーコマンド例）を表示して非対話で続行する設計とした。また `ConvertTo-WslPath` の戻り値契約破壊を避けるため `ConvertTo-ContainerMountPath` / `Resolve-DataDir` へ責務を分離した。

---

## 2. 品質・テストエビデンス

- **新規単体・結合テスト（PowerShell + WSL2 Bash）**: `dotnet exec ...TagBasedVideoManager.Renamer.Tests.dll --filter-class ...`（新規 7 テストクラス） → **70 / 70 件 Pass（失敗 0 / スキップ 0）**
- **Adversarial QA 破壊テスト（インライン PowerShell / WSL Bash）**: 偽装 UNC（`//wsl$evil/data`, `\\wsl.localhost.evil\...`）、角括弧・特殊文字（`tbvm-qa[1]\sub$dir;test`）、3 本スラッシュ（`///home/user/data`, `///`）、通常ファイル指定、WSL サブパス大小文字差、破損 `inspect` JSON → **全ケース堅牢性確認（Pass）**
- **実機 `wslc` 非破壊 E2E 確認**: 既存コンテナ `dd1e1e9654c2` の `/app/data`（`\\wsl.localhost\Ubuntu-24.04\home\spjao\var\apps\TagBasedVideoManager\data`）抽出、および一時 `wslc run --rm` での `:ro` 読取保護・WSL3 `/app/data` 書込永続化・`D:\mnt\c\...` 非生成を確認済み
- **`TagBasedVideoManager.Renamer.Tests` 全体実行（TRX / カバレッジ収集）**: **439 / 447 件 Pass（失敗 8 / スキップ 0）**
  - 失敗 8 件の内訳（すべて本変更前の既存環境・テスト基盤依存であり本変更によるリグレッションなし）:
    - `DeployScriptAppSettingsTests.IT-10` (1件), `DeployScriptLinuxValidationTests.IT-16` (2件), `IT-17` (4件): 既存テストコード（`DeployScriptTests.fs` L972, L1037, L1078）がレガシー `run "bash" [ "-c"; ... ]` + CRLF 文字列を使用しており、`System32\bash.exe` 経由実行時に `/bin/docker: Read-only file system` / `mkdir ''` 等で失敗する既存現象
    - `SettingsTests.プロジェクト直下の appsettings.Development.json と appsettings.Production.json は正しくデシリアライズできる` (1件): ローカル環境に実運用の Git 管理外 `appsettings.Production.json`（`VIDEO_DIRS="E\e"` 等）が存在するための既存環境依存失敗
- **カバレッジ**:
  - `TagBasedVideoManager.Renamer`: 行網羅 **87.57%** / 分岐網羅 **64.66%**（`coverage.cobertura.191126173533147.xml` 実測値。なお実行時コンソールサマリ上は Method **70.23%** も表示されたが Cobertura XML ルート属性・ReportGenerator HTML 無料版には出力されない）
  - ※ 本変更のプロダクションコードは PowerShell（`.ps1`）および Bash（`.sh`）スクリプトであり .NET Coverlet の直接計測対象外のため、`openspec/changes/deploy-data-dir-wsl-path/test_plan.md` の全 25 シナリオ・C2 デシジョンテーブル（Table 4.1〜4.4）と新規 70 テストの 1 対 1 対応で網羅性を担保している。
- **エビデンス出力先**: `test/TestResults/TestRun_2026-11-20_02_33_18/`（`TestRun_2026-11-20_02_33_18_net10.0.trx`、`coverage.cobertura.191126173533147.xml`、`CoverageReport/index.html`）

---

## 3. 保留指摘・技術的負債

| 指摘元（フェーズ/タスク/役割）                                  | 内容                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    | 保留理由                                                                                                                                                  | 推奨対応時期                                                                   |
| :-------------------------------------------------------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | :-------------------------------------------------------------------------------------------------------------------------------------------------------- | :----------------------------------------------------------------------------- |
| Archive / PG Reviewer & SE Reviewer（軽微な二重構造・負債記録） | `scripts/common.ps1` の以下 3 点: (1) `Resolve-WebDeploySettings`（L885-905）で `$baseFile` / `$envFile` JSON 読み込み `try/catch` が同構造で 2 回記述されている、(2) `Resolve-HostDirectoryInfo` が実働パス解決を担う一方 `ConvertTo-ContainerMountPath`（L359-410）が単体契約維持用として独立実装で並存しており、`ConvertTo-WslPath`（L186: サブパスなし UNC を `/` 許可）と `Resolve-WslUncPath`（L301: サブパスなし UNC を拒否）も別正規表現を持つ、(3) `$script:SystemWslDistros`（L6）が `@("docker-desktop", "docker-desktop-data")` 固定である（Rancher Desktop / Podman Desktop 等の別ディストロは未除外）                                                                                                                                     | 現状は機能要件・単体テスト・Adversarial QA をすべて満たしており、過剰リファクタや既存契約破壊を避けるため保留・記録とした                                 | 次回の `common.ps1` 改修時                                                     |
| Archive / PG Reviewer & QA Reviewer（`deploy.sh` 負債記録）     | `scripts/deploy.sh` の以下 3 点: (1) `read_appsettings_with_source`（L48-90）と `get_existing_container_data_mount`（L291-317）に `python3` 優先・`jq` フォールバックの二重 JSON パース実装が存在し、実機/テスト環境には `python3` が存在するため `PARSER="jq"` 経路と純粋 Bash `normalize_linux_path`（L121-142）が動的テスト未踏である（`jq` 側は UTF-8 BOM 付き JSON に非対応）、(2) `resolve_unc_path_for_linux`（L187-188）と `normalize_mount_source_for_compare`（L328）の双方で Bash 4+ 固有 `${var,,}` を使用している、(3) `read_appsettings_with_source` / `resolve_unc_path_for_linux` / `resolve_host_dir_for_linux` はサブシェル抑止のためグローバル変数（`APPSETTING_*`, `RESOLVED_UNC_*`, `RESOLVED_DIR_*`）経由で戻り値を受け渡している | Linux/WSL2 Bash 4+・`python3` 環境の受け入れ基準 25 シナリオをすべて満たしており、本番阻害要因ではないため                                                | 次回の `deploy.sh` 改修・`DeployScriptTests.fs` 拡張時                         |
| 本変更外（既存テスト・環境負債の記録）                          | `DeployScriptTests.fs` L972 (`IT-10`), L1037 (`IT-16`), L1078 (`IT-17`) がレガシー `run "bash" ["-c"; ...]`＋CRLF 文字列を使用しており Windows `System32\bash.exe` で 7 件失敗する。また `SettingsTests`（1件）がローカル `appsettings.Production.json` の実設定値で失敗し、`TagBasedVideoManager.Tests.fsproj` はローカル環境に `coverlet.MTP 10.0.1` 未復元状態                                                                                                                                                                                                                                                                                                                                                                                       | TDD ガードレール（既存テスト無断改変禁止）および本変更スコープ（`L1122` 以降・スクリプト改修のみ）を厳守し、既存テスト・他 `.fsproj` には手を加えなかった | 別タスクで既存 Bash テストを `runWslBash` へ統一し、Web 側テスト依存復元を確認 |

---

## 4. 将来の改善・リファクタ候補

- 既存 `DeployScriptTests.fs`（L972, L1037, L1078）の `run "bash"` を本変更で整備した `DeployDataDirTestHarness.runWslBash`（`wsl.exe -e bash -s` + LF 変換）へ移行し、Windows 環境上の既存失敗 7 件を恒久解消する。
- `scripts/deploy.sh` の `PARSER="jq"` のみ環境（`python3` 不在・`jq` のみ）および純粋 Bash 正規化フォールバックを検証する結合テストを追加する。
- `scripts/common.ps1` の `ConvertTo-ContainerMountPath` 内部実装と `Resolve-HostDirectoryInfo` の位置付け整理、および `Resolve-WebDeploySettings` のベース/環境別 JSON 読み込みブロックのローカルヘルパー抽出を行う。
- 将来 `scripts/deploy.sh` を macOS（Bash 3.2）にも対応させる場合は `resolve_unc_path_for_linux`（L187-188）**および `normalize_mount_source_for_compare`（L328）** の `${var,,}` 小文字化を `tr '[:upper:]' '[:lower:]'` に置換する（現時点は Linux 向けとして動作要件充足）。

---

## 5. 運用上の注意事項（トラブルシューティング・ロールバック手順）

- **WSL3 内 `DATA_DIR` 永続化の設定例（`src/TagBasedVideoManager/appsettings.Production.json`）**:
  - Windows (`scripts/deploy.ps1`) から WSL3 内へ永続化する場合は `"/home/<user>/var/apps/TagBasedVideoManager/data"`（システムディストロ `docker-desktop` / `docker-desktop-data` を除く既定ユーザー WSL ディストロへ自動解決）または `"\\\\wsl.localhost\\Ubuntu-24.04\\home\\<user>\\var\\apps\\TagBasedVideoManager\\data"` を指定する。
  - Linux / WSL 内 (`scripts/deploy.sh`) では `"/home/<user>/var/apps/TagBasedVideoManager/data"` または `"./data"`（`${REPO_ROOT}/data` へ自動正規化）を使用する。
- **将来改修時のコーディング上の注意（踏み抜き防止）**:
  - **PowerShell 7+ 必須**: `scripts/deploy.ps1` の `??` 演算子および `scripts/common.ps1` の `Process.Kill($true)` は PowerShell 7+（.NET 5+）専用であり、Windows 標準 PowerShell 5.1 では動作しない。
  - **`deploy.ps1` `-DataDir ""` 明示指定**: `$env:DATA_DIR` が設定された状態で CLI から空文字 `-DataDir ""` を渡しても `$env:DATA_DIR` 側が参照されるため、既定 `./data` へ戻したい場合は環境変数を空にするか `-DataDir "./data"` を明示する。`Resolve-DataDir` 自体は `-TargetIsWindows $true` 固定で動作する。
  - **`deploy.sh` のグローバル変数契約**: `read_appsettings_with_source` / `resolve_unc_path_for_linux` / `resolve_host_dir_for_linux` を `val="$(...)"` のようにコマンド置換（サブシェル）で呼ぶと戻り値変数（`APPSETTING_*`, `RESOLVED_UNC_*`, `RESOLVED_DIR_*`）が親シェルに返らず空文字になるため厳禁。
  - **`deploy.ps1` 移行警告の `Copy-Item` 例と旧 `/mnt/...` パス読み替え・`thumbnails` 二重ネスト防止**: `scripts/deploy.ps1` L82 が出力する `Copy-Item -Path "<旧パス>\thumbnails" -Destination "<新パス>\"` は旧パスが Windows/UNC 形式で新フォルダ側にまだ `thumbnails` がない前提の例であり、新コンテナ起動後に既に `<新パス>\thumbnails` が作られている場合は `Copy-Item -Path "<旧パス>\thumbnails\*" -Destination "<新パス>\thumbnails\"` を使うこと（`PRODUCTION_ENVIRONMENT.md` に反映済み）。また旧バグ由来で既存コンテナの `Source` が `/mnt/<drive>/...` 等だった場合は、実ディレクトリの `D:\mnt\<drive>\...` 等へ読み替えて参照する。
- **トラブルシューティング（障害調査手順、詳細は `PRODUCTION_ENVIRONMENT.md` セクション 5 参照）**:
  1. デプロイ起動前サマリの「設定元（`CLI` / `環境変数` / `appsettings.*.json` / `既定値`）」「生入力値」「解決済み `HostPath` / `MountSource`」「（WSL 時）ディストロ名」を確認する。
  2. コンテナ稼働ログの確認には `wslc logs --tail 50 tag-based-video-manager` / `docker logs --tail 50 tag-based-video-manager` を実行する。
  3. 既存コンテナのマウント元確認には `wslc inspect tag-based-video-manager` / `docker inspect tag-based-video-manager` の `Mounts` 配列を参照する。
  4. WSL 解決エラー時は `wsl --status`・`wsl -l -v`（および `deploy.sh` 側では `echo "$WSL_DISTRO_NAME"`）を確認する。
- **ロールバック手順**:
  - 本変更はデータベーススキーマ変更や既存データファイルの自動移動・削除を伴わないため、問題発生時は `git revert` によるコード復元、または `src/TagBasedVideoManager/appsettings.Production.json` の `"DATA_DIR"` を旧マウント元へ戻して `deploy.ps1` / `deploy.sh` を再実行するだけで即座に直前状態へ復旧できる。
