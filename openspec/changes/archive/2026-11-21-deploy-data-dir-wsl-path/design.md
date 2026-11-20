# Technical Design: デプロイスクリプト（deploy.ps1 / deploy.sh）の DATA_DIR / VIDEO_DIRS パス解決改善

## Context

- Windows 11 環境の `scripts/deploy.ps1` は現在 `scripts/common.ps1` の `ConvertTo-WslPath` を呼び出して `D:\Anime` や `./data` を `/mnt/d/Anime` 形式へ変換し、`wslc run -v` へ渡している。
- 実機 `wslc 3.0.5.0`（および既存コンテナ `dd1e1e9654c2` の `wslc inspect` 結果）の調査により、Windows ネイティブ CLI である `wslc.exe` は `-v` 引数として **Windows スラッシュ区切り絶対パス（`D:/...`）** および **WSL UNC パス（`\\wsl.localhost\<Distro>\<Path>`）** を受け付ける一方、`/mnt/e/e` 形式のパスを渡すと「Windows カレントドライブ上の `D:\mnt\e\e`」として解釈して空ディレクトリを自動作成しマウントしてしまうことが判明した（実測 `Mounts[].Source` に `D:\mnt\e\e` 等が記録されていた）。
- さらに `DATA_DIR` に `\\wsl.localhost\...` を指定すると `ConvertTo-WslPath` が `//wsl.localhost/...` に破壊し、`/home/...` を指定すると `deploy.ps1` の `Test-Path` / `New-Item` が Windows のカレントドライブ直下（`D:\home\...`）を参照・作成してしまう。
- また実機 `wsl.exe -l -q` の確認では、UTF-16LE（NULL 文字混在）で出力されるほか、`Ubuntu-24.04`・`Ubuntu-26.04` と並んで `docker-desktop` 等のシステム専用ディストロが出力されることが判明した。
- 一方、Linux 向けの `scripts/deploy.sh` は `DATA_DIR="./data"` を `${REPO_ROOT}` 基準の絶対パスへ解決せずそのまま `-v` に渡しており、実行時のカレントディレクトリ依存および Windows/UNC パス誤混入に対する防御が欠如している。

### 実機 `wslc inspect` JSON スキーマ根拠（`wslc 3.0.5.0` 実測値）

```json
[
  {
    "Name": "/tag-based-video-manager",
    "Mounts": [
      {
        "Destination": "/app/videos/e",
        "Name": "",
        "ReadWrite": false,
        "Source": "D:\\mnt\\e\\e",
        "Type": "bind"
      },
      {
        "Destination": "/app/data",
        "Name": "",
        "ReadWrite": true,
        "Source": "\\\\wsl.localhost\\Ubuntu-24.04\\home\\spjao\\var\\apps\\TagBasedVideoManager\\data",
        "Type": "bind"
      }
    ]
  }
]
```

- Docker / Podman と同様の **トップレベル JSON 配列**（単一オブジェクトの場合も `@($parsed)[0]` で統一吸収可）であり、`Mounts[].Destination == "/app/data"` の要素が `Source` を保持する。

---

## Goals / Non-Goals

### Goals

- `scripts/deploy.ps1`（`scripts/common.ps1`）で、`wslc` / Windows コンテナ CLI（`TargetIsWindows = $true`）が正しく解釈できるホストマウントパス（Windows 絶対パスは `D:/...`、WSL3 内パスは `\\wsl.localhost\<Distro>\<subpath>`）を `VIDEO_DIRS` と `DATA_DIR` の双方で一貫して解決できるようにする。
- `DATA_DIR`（および `VIDEO_DIRS`）に `/home/user/data` のような Linux 絶対パスが指定された場合、システム用ディストロ（`docker-desktop`, `docker-desktop-data`）を除外して既定ユーザー WSL ディストロ名を取得し、`\\wsl.localhost\<既定Distro>\home\user\data` へ自動解決する。
- 既存テスト（`ConvertTo-WslPath` の `/mnt/...` 変換を検証する `UT-PATH-01`〜`04`）との後方互換性を壊さず、`ConvertTo-WslPath` 自体にも `\\wsl.localhost\...` / `\\wsl$\...` → `/<subpath>` 変換を追加し、コンテナマウント用パス変換は責務分離した専用関数として設計する。
- `scripts/deploy.sh` で `DATA_DIR` の相対パスを `${REPO_ROOT}` 基準の絶対パスへ正規化し、Windows/UNC パス誤混入や不正パスをビルド前に Fail-Fast 拒否する。
- 両スクリプトで、イメージビルド・既存コンテナ停止より**前**に既存コンテナ `tag-based-video-manager` の `/app/data` マウント元と新 `DATA_DIR` の不一致を検出し、安全な移行案内警告を出力する。
- 外部プロセス（`wsl.exe`, `wslc inspect` / `docker inspect`）にタイムアウト（5 秒）を設け、ハングによる連鎖停止・無期限待機を防ぐ。

### Non-Goals

- 既存コンテナ側の `/app/data` 実ファイルを別フォルダへ自動コピー・削除する処理（意図しない大容量転送・上書きを防ぐため警告・案内のみ）。
- `wslc` 名前付きボリューム管理への変更。
- Web アプリ本体およびデスクトップアプリの `.fs` コード変更。

---

## 公開関数・I/F 契約（`scripts/common.ps1`）

| 関数名                           | 主なパラメータ（既定値）                                                                                                                                                                       | 戻り値型・プロパティ                                                                                                        | 異常時契約                                                                            |
| :------------------------------- | :--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | :-------------------------------------------------------------------------------------------------------------------------- | :------------------------------------------------------------------------------------ |
| `ConvertTo-WslPath`              | `-Path <string>` (必須)                                                                                                                                                                        | `[string]` (`/mnt/c/...` または `/home/...`)                                                                                | 空文字は空文字または既存通り例外                                                      |
| `Get-DefaultWslDistro`           | `-GetWslList <scriptblock>` (任意) / `-TimeoutMs <int>` (=5000)                                                                                                                                | `[string]`（例: `Ubuntu-24.04`）                                                                                            | タイムアウト・非0終了・ユーザーディストロ0件時は回復手順付きで `throw`                |
| `Resolve-WslUncPath`             | `-Path <string>` (必須) / `-GetDefaultDistro <scriptblock>` (任意)                                                                                                                             | `[PSCustomObject]@{ DistroName; SubPath; UncPath; LinuxPath }`                                                              | 空/空白・`..` セグメント・ディストロ名欠落・ルート直下・非 WSL UNC は `throw`         |
| `ConvertTo-ContainerMountPath`   | `-Path <string>` (必須) / `-TargetIsWindows <bool>` (=$true) / `-GetDefaultDistro <scriptblock>` (任意)                                                                                        | `[string]` (`D:/X` または `\\wsl.localhost\...`、`$false` 時は Linux パス)                                                  | 不正 UNC・解決不能 Linux パスは `throw`                                               |
| `Resolve-DataDir`                | `[AllowEmptyString()][string]$Path = ""` / `-RepoRoot <string>` (任意, 既定 `$PWD`) / `-CreateIfMissing <switch>` / `-GetDefaultDistro <scriptblock>` (任意) / `-ResolvedFrom <string>` (任意) | `[PSCustomObject]@{ RawInput; ResolvedFrom; DistroName; HostPath; MountSource }`                                            | `..` セグメント・ドライブ直下/相対・不正 UNC・既存通常ファイル・作成失敗は `throw`    |
| `Resolve-VideoMounts`            | `-VideoDirs <string>` / `-OldVideoDir <string>` / `-TargetIsWindows <bool>` (=$IsWindows) / `-RepoRoot <string>` (任意) / `-GetDefaultDistro <scriptblock>` (任意)                             | `[PSCustomObject]@{ ContainerVideoDirs; VolumeArgs; Mounts[] }`（`Mounts` 要素は `{ HostPath; ContainerPath; VolumeArg }`） | 未設定・旧 `VIDEO_DIR` 残存・不存在・通常ファイル・ルート直下・`..`・重複名は `throw` |
| `Get-ExistingContainerDataMount` | `-Runtime <string>` (必須) / `-ContainerName <string>` (必須) / `-TimeoutMs <int>` (=5000) / `-InvokeInspect <scriptblock>` (任意)                                                             | `[string]` または `$null`（未存在・取得不可時）                                                                             | 例外は投げず `$null` 返却（ただし非空出力で JSON 抽出失敗時は `Write-Warning`）       |
| `Test-SameMountSource`           | `-ExistingSource <string>` / `-NewSource <string>`                                                                                                                                             | `[bool]`                                                                                                                    | 引数空時は `$false`                                                                   |

---

## Decisions

### Decision 1: 責務分離 — `ConvertTo-WslPath` の維持・拡張と `ConvertTo-ContainerMountPath` の新設（`scripts/common.ps1`）

- **理由**: 既存の単体テスト（`UT-PATH-01`〜`04`）は `ConvertTo-WslPath` が `C:\Users\Public\Videos` → `/mnt/c/Users/Public/Videos` を返す契約を検証している。これを破壊的に変更すると既存契約が崩れるため、関数役割を明確に分離する。
  - `ConvertTo-WslPath -Path <string>`:
    - 純粋に「任意の Windows / WSL UNC / Linux パスを **WSL Linux 内部表現**（`/mnt/c/...`, `/home/...`）へ正規化」するユーティリティとして維持。
    - 新たに `\\wsl.localhost\<Distro>\<subpath>` および `\\wsl$\<Distro>\<subpath>`（`/` 区切り表現含む）を `/<subpath>`（サブパス空なら `/`）へ変換する分岐を追加する。
  - `Get-DefaultWslDistro`:
    - テスト時に `[scriptblock]$GetWslList` を注入できる構造とし、ホスト環境非依存でテスト可能にする。
    - 実機呼び出し時は `System.Diagnostics.Process` によりタイムアウト付き（既定 `5000ms`）かつ環境変数 `WSL_UTF8=1` で `wsl.exe -l -q` を実行する。標準出力からは `\0`、UTF-16/UTF-8 BOM・置換文字 `\uFFFD`・前後空白を除去して行単位に分解する。
    - 終了コードが非 0、またはタイムアウトした場合は、`throw`（回復案内: `wsl --status` / `wsl -l -v` 確認および `appsettings.Production.json` で `\\wsl.localhost\<Distro名>\...` を直接指定する例）する。
    - `docker-desktop`・`docker-desktop-data` 等のコンテナランタイム内部ディストロを除外した残余リストの先頭を既定ユーザーディストロとして返す。0 件の場合も上記回復案内付きで `throw` する。
  - `Resolve-WslUncPath`:
    - `..` パスセグメント検出は部分文字列ではなくセグメント境界正規表現 `'(^|[\\/])\.\.($|[\\/])'` で行い、`v1..v2` のような正当ディレクトリ名を誤拒否しない。
    - `\\wsl.localhost\<Distro>\<subpath>` / `\\wsl$\<Distro>\<subpath>`（`//` 始まり含む）はディストロ名欠落・サブパス欠落（ルート直下）を拒否し、`UncPath = "\\wsl.localhost\<Distro>\<subpath>"`（`\` 区切り・末尾 `\` 除去）と `LinuxPath = "/<subpath>"` を返す。
    - 非 WSL UNC（`\\fileserver\share\...` 等）は `throw` で拒否する。
    - `/` 始まり Linux 絶対パスは `/` 単体および `..` セグメントを拒否した上で、`Get-DefaultWslDistro` で取得した `<Distro>` を使い `UncPath` を組み立てる（Windows ドライブ側には一切アクセスしない）。
  - `ConvertTo-ContainerMountPath`:
    - `TargetIsWindows = $true`（`deploy.ps1` が `wslc` / Windows コンテナ CLI を呼ぶ場合）:
      - WSL UNC パスまたは `/` 始まり Linux パス → `Resolve-WslUncPath` 経由の正規化 `\\wsl.localhost\<Distro>\<subpath>` を返す。
      - Windows パス → バックスラッシュを `/` に統一し末尾 `/` 除去（ただしルート表現除き）した Windows スラッシュパス（例: `D:/Anime`, `D:/My Data/tbvm`）を返す。
    - `TargetIsWindows = $false`（Linux コンテナデーモン向けモード）:
      - `ConvertTo-WslPath` 相当の Linux パスを返す。

### Decision 2: `Resolve-DataDir` 関数の切り出し（`scripts/common.ps1` / `scripts/deploy.ps1`）

- これまで `scripts/deploy.ps1` L35-48 に直書きされていた `DATA_DIR` 解決・検証・作成処理を、`scripts/common.ps1` の関数 `Resolve-DataDir` として抽出する。
- 処理フロー:
  1. **優先順位解決と空文字フォールバック**: 設定解決側（CLI `-DataDir` ＞ `$env:DATA_DIR` ＞ `appsettings.{env}.json` ＞ `appsettings.json` ＞ 既定 `./data`）で未設定・空文字・空白のみとなった場合（および `Resolve-DataDir` に空文字/空白のみが渡された場合）は、`./data`（`$RepoRoot\data`）へフォールバックする。
  2. **事前構文ガード**: 空でない入力に対し、セグメント境界の `..`、非 WSL ネットワーク UNC（`\\server\share`）、Windows ドライブ相対パス（`^[a-zA-Z]:(?![\\/])`、例: `D:data`）、Windows ドライブ直下（`^[a-zA-Z]:[\\/]*$`、例: `D:\`, `D:`）、Linux ルート `/` 単体を即座に `throw` で拒否する。
  3. **種別分類・解決**:
     - **A. WSL UNC パス**（`^(\\\\|//)(wsl\.localhost|wsl\$)`）または **B. Linux 絶対パス**（`^/`）: `Resolve-WslUncPath` を呼び出し、`DistroName` と `HostPath = $unc.UncPath`、`MountSource = $unc.UncPath` を設定する（**Windows ドライブ `D:\home\...` には一切アクセス・作成しない**）。
     - **C. Windows 絶対パスまたは相対パス**: 相対パスは `Join-Path $RepoRoot $raw` → `[System.IO.Path]::GetFullPath(...)` で絶対パス化。解決結果がドライブ直下でないことを再確認し、`HostPath`（バックスラッシュ正規化）、`MountSource`（スラッシュ変換 `D:/.../data`）を設定する。
  4. **ディレクトリ種別チェック＆事前作成**（`-CreateIfMissing` 指定時、ビルド・既存コンテナ停止より前）:
     - `Test-Path -LiteralPath $HostPath -PathType Leaf` が `$true`（既存の通常ファイル誤指定、例: `./data/metadata.db`）の場合は即座に `throw` する。
     - `Test-Path -LiteralPath $HostPath -PathType Container` が `$false` の場合は `New-Item -ItemType Directory -Path $HostPath -Force` で作成する（失敗時はディストロ未存在・WSL 応答不良・権限エラーの切り分け案内 `wsl --status` 等を添えて `throw`）。
  5. **戻り値**: `[PSCustomObject]@{ RawInput; ResolvedFrom; DistroName; HostPath; MountSource }`（全成果物で `HostPath` に名称統一）。

### Decision 3: `Resolve-VideoMounts` の更新（`scripts/common.ps1`）

- 既存の `Resolve-VideoMounts` でも `Resolve-DataDir` と共通の安全性基準を適用する:
  - 各要素（`;` 分割、前後空白トリム、空要素無視）について、`..` パスセグメント・Windows ドライブ相対（`D:foo`）・ルート直下（`D:\`, `/`, `\\wsl.localhost\Distro`）を早期拒否する。
  - WSL UNC パスまたは `/` 始まり Linux パスが渡された場合は `Resolve-WslUncPath` 経由で `\\wsl.localhost\<Distro>\<subpath>` へ解決する。相対パスが渡された場合は `[System.IO.Path]::GetFullPath((Join-Path ($RepoRoot ?? $PWD.Path) $entry))` で解決する。
  - 実在確認は `Test-Path -LiteralPath $hostPath -PathType Leaf`（通常ファイル誤指定なら明示エラー）および `Test-Path -LiteralPath $hostPath -PathType Container`（不存在なら設定例付きエラー）で行う。
  - 変換後のマウント元文字列は `ConvertTo-ContainerMountPath -Path $hostPath -TargetIsWindows:$TargetIsWindows` で生成する（Windows 実行時 `-TargetIsWindows $true` で `D:/X` または `\\wsl.localhost\...` となり `/mnt/...` を生成しない）。コンテナ側パス `/app/videos/<フォルダ名>` および `:ro` 付与、大文字小文字無視のフォルダ名重複拒否は既存通り維持する。

### Decision 4: `scripts/deploy.sh` における `DATA_DIR` と `VIDEO_DIRS` の整合

- `scripts/deploy.sh` 内のビルド・停止前検証ブロックに以下を配置する:
  1. **設定元と未設定/空白フォールバック**: `DATA_DIR` が未設定・空文字・空白のみの場合は `./data`（すなわち `${REPO_ROOT}/data`）へフォールバックし、記録する。
  2. **Windows パスおよび通常ファイル・ルート・`..` 拒否**:
     - `DATA_DIR` / `VIDEO_DIRS` 双方で `^[a-zA-Z]:`（Windows ドライブ絶対/相対パス）は即座に非 0 終了し、正しい Linux パス設定例（例: `"/home/<user>/tbvm/data"` または `"./data"`）を出力する。
     - パスセグメント境界の `..`（`(^\.\./|/\.\./|/\.\.$|^\.\.$)`）はトラバーサルとして拒否する（`v1..v2` は許可）。
     - 既存パスが通常ファイル（`[ -f "$path" ] && [ ! -d "$path" ]`）またはシンボリックリンク先がディレクトリでない場合はビルド前にエラー終了する。
  3. **WSL UNC パスの厳格判定**:
     - `^(\\\\|//)(wsl\.localhost|wsl\$)[\\/]([^\\/]+)(?:[\\/](.*))?$`（大文字小文字無視）に一致した場合:
       - ディストロ名とサブパスを抽出（サブパス空・`..` セグメント含む場合は拒否）。
       - **環境変数 `WSL_DISTRO_NAME` が非空であり、かつ大文字小文字無視で抽出したディストロ名と一致する場合のみ** `/<subpath>`（`\` → `/` 変換、末尾 `/` 除去）へ変換する。
       - `$WSL_DISTRO_NAME` が空（ネイティブ Linux、または `cron` / `ssh` 等で WSL ディストロ情報欠落）または別ディストロ名である場合は、解決不能 UNC として Linux パス明示設定例を出力して即座に非 0 終了する。
  4. **相対パスの POSIX/Bash 互換絶対パス正規化**:
     - GNU `realpath -m` には依存せず、まず `${DATA_DIR#/}` チェックで相対パスなら `${REPO_ROOT}/${DATA_DIR}` へ結合し、`python3 -c 'import os,sys; print(os.path.normpath(sys.argv[1]))'`（`python3` 不在時は純粋 Bash の `/` 配列正規化関数）により冗長な `./` や `//` を正規化して絶対パスを得る。
     - 正規化後に `/` 単体であれば拒否し、`mkdir -p "${DATA_DIR}"` も検証ブロック内で実行（失敗時は非 0 終了）。

### Decision 5: 既存コンテナの `/app/data` マウント元不一致警告と可観測性（`deploy.ps1` / `deploy.sh` 共通）

- **起動前サマリログ（可観測性向上）**:
  - 検証完了・警告チェック後、スクリプトは `DATA_DIR` 解決結果として以下を一貫して出力する:
    - 設定元（`CLI` / `環境変数` / `appsettings.<Env>.json` / `appsettings.json` / `既定値`）と生入力値
    - WSL ディストロ名（WSL パス時）
    - ホスト参照パス（`HostPath`）およびマウント引数元（`MountSource`）
- **不一致チェックのタイミング**:
  - イメージ `build` や既存コンテナ `stop`/`rm` より**前**に `Get-ExistingContainerDataMount` / `check_existing_data_mount_mismatch` を実行する。
- **耐障害性の高い `inspect` 実行契約（PowerShell）**:
  - `$ErrorActionPreference = 'Stop'` 下でネイティブ stderr に影響されないよう、`System.Diagnostics.Process` を使いタイムアウト `5000ms` で `& $runtime inspect $containerName`（テスト時は `-InvokeInspect` scriptblock 注入）を実行する。
  - 終了コード非 0 または空出力（コンテナ未存在等）の場合は `$null` を返しサイレントスキップする。
  - **非空出力があるにもかかわらず** JSON パース失敗、または想定構造（`@($json)[0].Mounts`）から読み取り不能な場合は、`Write-Warning` で診断メッセージ（先頭出力抜粋付き）を出した上で `$null` として安全側に継続する（スキーマ変更によるサイレント故障の検知）。
- **比較・警告内容**:
  - `Test-SameMountSource` / `normalize_mount_source` で `\` → `/`、末尾 `/` 除去、Windows ドライブ・UNC を含む場合は大文字小文字無視で比較（`/mnt/d/...` と `D:/...` のような旧バグ由来パスと正規パスは別物として検出し警告を出す）。
  - 不一致時に必須出力する 4 項目:
    1. 旧マウント元（`$existingSource`）と新マウント先（`$newSource`）
    2. **旧データは削除されておらず旧マウント元ディレクトリに残存している**旨の明言
    3. 旧データをそのまま使うための設定復帰例（`appsettings.Production.json` の `"DATA_DIR"` を旧パスに設定）
    4. 新フォルダへ移行する際の具体的なコピーコマンド例（Windows PowerShell `Copy-Item` / Linux `cp -a` による `metadata.db`・`thumbnails` コピー手順）
  - 警告出力後、非対話要件に従いデプロイを続行する。

---

## Risks / Trade-offs

- **[Risk 1: `wsl.exe -l -q` の文字コード・システムディストロ・ハングリスク]**
  - 実機検証により `wsl.exe -l -q` が UTF-16LE（NULL バイト混在）で出力され、`Ubuntu-24.04`, `Ubuntu-26.04` に加え `docker-desktop` が列挙されることを確認した。また未導入環境では案内メッセージが stdout に出る場合がある。
  - **Mitigation**: `WSL_UTF8=1` 指定＋タイムアウト `5000ms`＋終了コード非 0 検証＋`\0` / `\uFFFD` 除去＋システムディストロ（`docker-desktop`, `docker-desktop-data`）除外フィルタを実装し、取得不能時は回復手順付きで安全に Fail-Fast する。
- **[Risk 2: 既存単体テストへの影響範囲]**
  - `DeployScriptTests.fs` の `WslPathConversionTests`（`UT-PATH-01`〜`04`）は `ConvertTo-WslPath` の `/mnt/c/...` 出力を検証し、`VideoMountResolutionTests`（`UT-13`〜`19`）は `-TargetIsWindows` で `Resolve-VideoMounts` を呼び出している。
  - **Mitigation**: `ConvertTo-WslPath` 自体の Windows→`/mnt/...` 挙動は変更せず残し、`Resolve-VideoMounts` も既存シグネチャ（`-TargetIsWindows`）を維持したまま `$true` 時のパス形式を `D:/...` に適正化するため、既存テストを改ざんせずに新旧両契約を担保できる。
- **[Risk 3: WSL2 上での bash テスト環境（Windows 上の Git Bash vs WSL Bash）]**
  - Windows 上の `DeployScriptTests.fs` が `bash` を呼ぶと Git Bash と WSL bash で挙動（`/proc/version` 等）が異なり得る。
  - **Mitigation**: `deploy.sh` の WSL UNC 許可判定を `/proc/version` 推測ではなく「`WSL_DISTRO_NAME` 環境変数の有無と一致」に一本化したため、`WSL_DISTRO_NAME=""` によってテスト上から純粋 Linux 挙動を厳密・再現可能に検証できる。
- **[Risk 4: 既存コンテナ `tag-based-video-manager` の起動履歴と `D:\mnt\...`]**
  - 本番ユーザーが `DATA_DIR` に `/home/spjao/var/apps/TagBasedVideoManager/data`（または `\\wsl.localhost\Ubuntu-24.04\home\spjao\var\apps\TagBasedVideoManager\data`）を設定すれば、既存の WSL3 データへそのまま接続できる。一方で `VIDEO_DIRS` を実動画フォルダ（`E:\e;D:\e_d;F:\e_f;C:\Users\spjao\Videos\e_c` 等）へ設定してデプロイすることで、以前の空 `D:\mnt\...` マウント状態も解消される。
