# テスト計画 (Test Plan): deploy-data-dir-wsl-path

## 1. テスト方針と目標カバレッジ

- **変更規模レベル**: L2（通常変更・Issue #39 対応）
- **対象スコープ**:
  - Windows デプロイスクリプト（`scripts/common.ps1`, `scripts/deploy.ps1`）
  - Linux / WSL デプロイスクリプト（`scripts/deploy.sh`）
  - テストコード（`test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs`）
- **カバレッジ目標**:
  - C0（命令網羅 / Line）: 変更・新規スクリプト関数分岐の 90%+（プロジェクト全体目標 80%+）
  - C1（分岐網羅 / Branch）: 変更・新規スクリプト関数分岐の 85%+（プロジェクト全体目標 80%+）
  - C2（条件網羅 / Condition）: 下記「4. 複合条件デシジョンテーブル」記載の全複合条件を 100%
  - specs Scenario 網羅率: `openspec/changes/deploy-data-dir-wsl-path/specs/` 配下の全 25 シナリオ（変更・追加・維持）を 100% 検証
  - **Coverlet 計測に関する注記**: 本変更のプロダクションコードは PowerShell（`.ps1`）および Bash（`.sh`）スクリプトであり、.NET バイナリを計測する Coverlet のライン/ブランチカバレッジ数値には直接加算されない。そのため、本変更の分岐・条件網羅率は xUnit + FsUnit（`pwsh` / `bash` / `wslc` 呼び出しまたは出力検証）のテストケース一覧およびデシジョンテーブルとの 1 対 1 トレーサビリティによって保証する。

---

## 2. 仕様 Scenario トレーサビリティマトリクス

| #   | Spec ファイル / Requirement                                                                      | Scenario 名                                                                                       | テストレベル       | テストケース ID / 検証方法                                                          |
| :-- | :----------------------------------------------------------------------------------------------- | :------------------------------------------------------------------------------------------------ | :----------------- | :---------------------------------------------------------------------------------- |
| 1   | `container-deployment-scripts` / `VIDEO_DIRS のマウント変換と入力検証`                           | 複数フォルダの変換                                                                                | Unit / Integration | `UT-13`, `UT-MOUNT-01`（`TargetIsWindows` true/false）                              |
| 2   | `container-deployment-scripts` / `VIDEO_DIRS のマウント変換と入力検証`                           | 単一フォルダ指定                                                                                  | Unit               | `UT-14`, `UT-MOUNT-02`                                                              |
| 3   | `container-deployment-scripts` / `VIDEO_DIRS のマウント変換と入力検証`                           | WSL UNC パス・Linux パスの VIDEO_DIRS 指定（Windows）                                             | Unit               | `UT-MOUNT-03`, `UT-MOUNT-04`, `UT-MOUNT-05`                                         |
| 4   | `container-deployment-scripts` / `VIDEO_DIRS のマウント変換と入力検証`                           | Linux 環境での Windows パス・変換不能 UNC パスの拒否（deploy.sh）                                 | Integration        | `IT-SH-VIDEO-01`, `IT-SH-VIDEO-02`                                                  |
| 5   | `container-deployment-scripts` / `VIDEO_DIRS のマウント変換と入力検証`                           | 未設定時の早期エラー                                                                              | Unit / Integration | `UT-15`, `IT-09`                                                                    |
| 6   | `container-deployment-scripts` / `VIDEO_DIRS のマウント変換と入力検証`                           | 旧 VIDEO_DIR 残存時の移行案内エラー                                                               | Unit / Integration | `UT-16`, `IT-16`                                                                    |
| 7   | `container-deployment-scripts` / `VIDEO_DIRS のマウント変換と入力検証`                           | フォルダ名重複の拒否                                                                              | Unit / Integration | `UT-17`                                                                             |
| 8   | `container-deployment-scripts` / `VIDEO_DIRS のマウント変換と入力検証`                           | 存在しないパスの拒否                                                                              | Unit               | `UT-18`                                                                             |
| 9   | `container-deployment-scripts` / `VIDEO_DIRS のマウント変換と入力検証`                           | 通常ファイルなどディレクトリ以外の誤指定の拒否                                                    | Unit               | `UT-MOUNT-06`                                                                       |
| 10  | `container-deployment-scripts` / `VIDEO_DIRS のマウント変換と入力検証`                           | ドライブ直下の拒否                                                                                | Unit               | `UT-19`                                                                             |
| 11  | `container-deployment-scripts` / `VIDEO_DIRS のマウント変換と入力検証`                           | appsettings.json 系からの設定読み込みと .env 非依存の検証                                         | Integration        | `IT-09`, `IT-10`, `IT-11`                                                           |
| 12  | `container-deployment-scripts` / `VIDEO_DIRS のマウント変換と入力検証`                           | CLI引数・環境変数による appsettings 設定の上書き                                                  | Integration        | `IT-10`, `IT-DEPLOY-OVERRIDE-01`                                                    |
| 13  | `container-deployment-scripts` / `VIDEO_DIRS のマウント変換と入力検証`                           | 不正な JSON 構文またはツール不在時の安全 Fail-Fast 終了                                           | Integration        | `IT-17`, `IT-DEPLOY-FAILFAST-01`                                                    |
| 14  | `container-deployment-scripts` / `DATA_DIR のクロスプラットフォームパス解決と WSL3 内永続化対応` | Windows 相対パス・絶対パス・空白入りパスの DATA_DIR 解決と事前作成                                | Unit / Integration | `UT-DATA-01`, `UT-DATA-02`, `UT-DATA-07`, `UT-DATA-12`, `IT-14`, `IT-DATA-SIDE-01`  |
| 15  | `container-deployment-scripts` / `DATA_DIR のクロスプラットフォームパス解決と WSL3 内永続化対応` | Windows 環境での WSL UNC パス DATA_DIR の指定とディレクトリ作成                                   | Unit / Integration | `UT-DATA-03`, `UT-DATA-04`, `IT-DATA-WSL-01`                                        |
| 16  | `container-deployment-scripts` / `DATA_DIR のクロスプラットフォームパス解決と WSL3 内永続化対応` | Windows 環境での Linux 絶対パス DATA_DIR の既定 WSL ディストロ解決                                | Unit / Integration | `UT-WSL-01`〜`03`, `UT-DATA-05`, `UT-DATA-06`, `IT-DATA-WSL-02`                     |
| 17  | `container-deployment-scripts` / `DATA_DIR のクロスプラットフォームパス解決と WSL3 内永続化対応` | 不正な DATA_DIR（不正 UNC・ドライブ直下・ドライブ相対・通常ファイル・パストラバーサル）の早期拒否 | Unit               | `UT-DATA-08`〜`11`, `UT-DATA-13`                                                    |
| 18  | `container-deployment-scripts` / `DATA_DIR のクロスプラットフォームパス解決と WSL3 内永続化対応` | Linux (deploy.sh) における DATA_DIR 相対パスのリポジトリルート基準絶対パス正規化                  | Integration        | `IT-SH-DATA-01`, `IT-SH-DATA-02`, `IT-SH-DATA-07`, `IT-SH-DATA-09`                  |
| 19  | `container-deployment-scripts` / `DATA_DIR のクロスプラットフォームパス解決と WSL3 内永続化対応` | Linux (deploy.sh) における WSL UNC パス・Windows ドライブパスの扱い                               | Integration        | `IT-SH-DATA-03`, `IT-SH-DATA-04`, `IT-SH-DATA-05`, `IT-SH-DATA-06`, `IT-SH-DATA-08` |
| 20  | `container-deployment-scripts` / `既存コンテナの /app/data マウント元変更時の警告通知`           | 既存コンテナと異なる DATA_DIR 指定時の警告表示とデータ移行案内                                    | Unit / Integration | `UT-WARN-01`, `UT-WARN-03`, `UT-WARN-05`, `IT-WARN-01`                              |
| 21  | `container-deployment-scripts` / `既存コンテナの /app/data マウント元変更時の警告通知`           | 既存コンテナと同じ DATA_DIR 指定時は警告を出さない                                                | Unit / Integration | `UT-WARN-02`, `UT-WARN-04`, `IT-WARN-02`, `IT-WARN-03`                              |
| 22  | `docker-infrastructure` / `ホスト動画ディレクトリの読み取り専用マウント`                         | 読み取り専用ボリュームの保護                                                                      | Unit / Integration | `RunArgumentsScriptTests`, `UT-MOUNT-RO-01`                                         |
| 23  | `docker-infrastructure` / `ホスト動画ディレクトリの読み取り専用マウント`                         | 複数物理フォルダのマウント                                                                        | Unit / Integration | `UT-13`, `UT-MOUNT-01`                                                              |
| 24  | `docker-infrastructure` / `アプリケーションデータとサムネイルのボリューム永続化`                 | コンテナ再起動後のデータ永続性                                                                    | Integration / E2E  | `IT-DATA-PERSIST-01`, 実機確認                                                      |
| 25  | `docker-infrastructure` / `アプリケーションデータとサムネイルのボリューム永続化`                 | WSL3 ディストロ内パスへのデータ永続化                                                             | Integration / E2E  | `IT-DATA-WSL-01`, 実機 `wslc` 検証                                                  |

---

## 3. 単体テスト設計（境界値分析・同値分割）

### 3.1 不具合再現テスト（Bug Reproduction Tests: Test First）

| ID            | 再現対象の根本原因                                                                                                        | 入力条件                                                                                  | 期待結果（修正前 Red → 修正後 Green）                                                                                                           |
| :------------ | :------------------------------------------------------------------------------------------------------------------------ | :---------------------------------------------------------------------------------------- | :---------------------------------------------------------------------------------------------------------------------------------------------- |
| `UT-REPRO-01` | Windows (`TargetIsWindows=$true`) で `/mnt/e/e` を渡すと `wslc` が `D:\mnt\e\e` を参照するバグ                            | `TargetIsWindows=$true`, `VIDEO_DIRS="E:\e"`, `DATA_DIR="D:\repo\data"`                   | `VIDEO_DIRS` の `HostPath` / マウント元に `/mnt/e/e` ではなく `E:/e` が生成され、`DATA_DIR` の `MountSource` は `D:/repo/data` が生成されること |
| `UT-REPRO-02` | `deploy.ps1` で `DATA_DIR="/home/user/data"` を渡すと `[System.IO.Path]::GetFullPath` が `D:\home\user\data` に化けるバグ | `DATA_DIR="/home/spjao/var/apps/TagBasedVideoManager/data"`, DefaultDistro=`Ubuntu-24.04` | `D:\home\...` に化けず、`HostPath="\\wsl.localhost\Ubuntu-24.04\home\spjao\var\apps\TagBasedVideoManager\data"` として解決されること            |
| `UT-REPRO-03` | `deploy.sh` で `DATA_DIR="./data"` が相対パスのまま `docker run -v ./data:/app/data` に渡るバグ                           | `deploy.sh` に `DATA_DIR="./data"` と `VIDEO_DIRS` を設定                                 | `-v <REPO_ROOT>/data:/app/data`（絶対パス正規化済み）に変換され、相対パス `./data:` のまま渡らないこと                                          |

### 3.2 `Get-DefaultWslDistro` / `Resolve-DataDir` / `Resolve-WslUncPath`（`scripts/common.ps1`）

| ID           | テスト対象・分類                                                      | 入力パターン                                                                                                               | 期待結果                                                                                                                |
| :----------- | :-------------------------------------------------------------------- | :------------------------------------------------------------------------------------------------------------------------- | :---------------------------------------------------------------------------------------------------------------------- |
| `UT-WSL-01`  | システムディストロ除外・UTF-16LE パース                               | Mock `wsl -l -q` → `@("docker-desktop", "", "`0U`0b`0u`0n`0t`0u`0-`02`04`0.`00", "docker-desktop-data")`                   | `"Ubuntu-24.04"` を返すこと（`docker-desktop` や `\0` を除去）                                                          |
| `UT-WSL-02`  | UTF-16LE BOM 置換文字 `\uFFFD` 除去                                   | Mock 出力先頭に `"\uFFFD\uFFFDUbuntu-24.04"`                                                                               | `"Ubuntu-24.04"` に正規化されること                                                                                     |
| `UT-WSL-03`  | システムディストロのみ / 0 件 / タイムアウト                          | Mock が `"docker-desktop"` のみ返却、または異常終了コード / ハング                                                         | 回復案内（`\\wsl.localhost\<Distro名>\...` 明示指定・`wsl --status`）を含むエラーで `throw` すること                    |
| `UT-DATA-01` | 相対パス（明示 `./data`）                                             | `-Path "./data"`, `-RepoRoot "D:\repo"`                                                                                    | `HostPath="D:\repo\data"`, `MountSource="D:/repo/data"`                                                                 |
| `UT-DATA-02` | Windows 絶対パス（ドライブ文字）                                      | `-Path "D:\data\tvm"`                                                                                                      | `HostPath="D:\data\tvm"`, `MountSource="D:/data/tvm"`                                                                   |
| `UT-DATA-03` | WSL UNC パス（`\\wsl.localhost\...`）                                 | `-Path "\\wsl.localhost\Ubuntu-24.04\home\spjao\data"`                                                                     | `HostPath="\\wsl.localhost\Ubuntu-24.04\home\spjao\data"`, `MountSource="\\wsl.localhost\Ubuntu-24.04\home\spjao\data"` |
| `UT-DATA-04` | WSL UNC パス（旧形式 `\\wsl$\...`・スラッシュ混在）                   | `-Path "//wsl$/Ubuntu/var/data"`                                                                                           | `HostPath="\\wsl.localhost\Ubuntu\var\data"`, `MountSource="\\wsl.localhost\Ubuntu\var\data"` に正規化                  |
| `UT-DATA-05` | Linux 絶対パス（既定ディストロ補完）                                  | `-Path "/home/spjao/data"`, DefaultDistro=`Ubuntu-24.04`                                                                   | `HostPath="\\wsl.localhost\Ubuntu-24.04\home\spjao\data"`, `DistroName="Ubuntu-24.04"`                                  |
| `UT-DATA-06` | Linux 絶対パス + 既定ディストロ取得失敗                               | `-Path "/home/spjao/data"`, `GetDefaultDistro` が失敗                                                                      | `throw` し、`D:\home\...` へフォールバックせず回復手順を案内                                                            |
| `UT-DATA-07` | 境界値: 空文字・空白のみのフォールバック vs `Resolve-WslUncPath` 拒否 | (a) `Resolve-DataDir -Path "" -RepoRoot "D:\repo"` / `"   "`<br>(b) `Resolve-WslUncPath -Path ""`                          | (a) `D:\repo\data`（`D:/repo/data`）へ既定フォールバック<br>(b) ヘルパー単体への空引数はエラー `throw`                  |
| `UT-DATA-08` | 境界値: パストラバーサル vs 正当名 `v1..v2`                           | (a) `"\\wsl.localhost\Ubuntu\home\..\etc"`, `"/home/../etc"`, `"../data"`<br>(b) `"D:\v1..v2\data"`, `"/home/v1..v2/data"` | (a) セグメント `..` として早期 `throw` 拒否<br>(b) 正当パスとして許可                                                   |
| `UT-DATA-09` | 境界値: ルート直下・不完全 UNC・ドライブ相対                          | `"\\wsl.localhost\Ubuntu"`, `"\\wsl.localhost\"`, `"/"`, `"D:\"`, `"D:"`, `"D:data"`                                       | すべて早期 `throw` で拒否（設定例提示）                                                                                 |
| `UT-DATA-10` | 境界値: 非 WSL ネットワーク UNC                                       | `"\\fileserver\share\data"`                                                                                                | 未対応 UNC として早期 `throw` で拒否                                                                                    |
| `UT-DATA-11` | 境界値: 既存の通常ファイルを指定                                      | テスト一時フォルダに `metadata.db` ファイルを作成し `DATA_DIR` に同ファイルパスを指定（`-CreateIfMissing` 有効時）         | ディレクトリ以外の誤指定として早期 `throw` で拒否                                                                       |
| `UT-DATA-12` | 境界値: 半角スペースを含むパス                                        | `"D:\My Data\tbvm data"` および `"\\wsl.localhost\Ubuntu-24.04\home\u\my data"`                                            | トークン分割されず空白を保持したまま `HostPath` / `MountSource` が生成されること                                        |
| `UT-DATA-13` | 戻り値オブジェクト契約検証                                            | 任意有効入力で `Resolve-DataDir` 実行                                                                                      | `RawInput`, `ResolvedFrom`, `DistroName`, `HostPath`, `MountSource` の全 5 プロパティを持つこと                         |

### 3.3 `ConvertTo-ContainerMountPath` / `ConvertTo-WslPath` / `Resolve-VideoMounts`（`scripts/common.ps1`）

| ID               | テスト対象・分類                                          | 入力パターン                                                                   | 期待結果                                                                                            |
| :--------------- | :-------------------------------------------------------- | :----------------------------------------------------------------------------- | :-------------------------------------------------------------------------------------------------- |
| `UT-PATH-05`     | `ConvertTo-WslPath` 後方互換 + UNC                        | `\\wsl.localhost\Ubuntu-24.04\home\user\videos` / `\\wsl$\Ubuntu\data`         | `/home/user/videos` / `/data`（既存 `UT-PATH-01..04` の `/mnt/c/...` はそのまま維持）               |
| `UT-MOUNT-01`    | `TargetIsWindows` ($true vs $false) の変換分岐            | `Path="E:\e"`, `-TargetIsWindows $true` vs `$false`                            | `$true` → `"E:/e"`、`$false` → `"/mnt/e/e"`                                                         |
| `UT-MOUNT-02`    | 単一フォルダ指定（`-TargetIsWindows $true`）              | `VIDEO_DIRS="C:\Videos\Anime"`                                                 | `VolumeArg = "C:/Videos/Anime:/app/videos/Anime:ro"`                                                |
| `UT-MOUNT-03`    | WSL UNC パスの `VIDEO_DIRS`（`-TargetIsWindows $true`）   | 実在テストディレクトリまたはモック上で `\\wsl.localhost\Ubuntu-24.04\...\vids` | `VolumeArg = "\\wsl.localhost\Ubuntu-24.04\...\vids:/app/videos/vids:ro"`（ドライブ直下誤判定なし） |
| `UT-MOUNT-04`    | Linux 絶対パスの `VIDEO_DIRS`（`-TargetIsWindows $true`） | `VIDEO_DIRS="/home/u/vids"`, DefaultDistro=`Ubuntu-24.04`                      | `\\wsl.localhost\Ubuntu-24.04\home\u\vids` に解決して存在確認・`/app/videos/vids:ro` 生成           |
| `UT-MOUNT-05`    | 空白を含む `VIDEO_DIRS`（Windows）                        | `VIDEO_DIRS="D:\...\My Clips"`（一時フォルダ作成）                             | `.../My Clips:/app/videos/My Clips:ro` が分割されず生成されること                                   |
| `UT-MOUNT-06`    | `VIDEO_DIRS` への通常ファイル誤指定・`..`・ドライブ相対   | 一時通常ファイルパス、`"D:\a\..\b"`、`"D:videos"`                              | ビルド前に早期 `throw` で拒否されること                                                             |
| `UT-MOUNT-RO-01` | 読み取り専用マウント保護検証                              | `New-RunArguments` で生成される全マウント引数                                  | 動画 `-v` は末尾 `:ro`、`DATA_DIR` `-v`（`:/app/data`）は `:ro` なし                                |

### 3.4 既存コンテナマウント比較・警告判定（`Get-ExistingContainerDataMount`, `Test-SameMountSource`）

| ID           | テスト対象・分類                                          | 入力パターン                                                                                                 | 期待結果                                                                                     |
| :----------- | :-------------------------------------------------------- | :----------------------------------------------------------------------------------------------------------- | :------------------------------------------------------------------------------------------- |
| `UT-WARN-01` | 異なるマウント元（WSL vs Windows 空フォルダ）             | 既存 `/app/data` Source=`D:\repo\data`, 新 `DATA_DIR`=`\\wsl.localhost\Ubuntu-24.04\home\u\data`             | `Test-SameMountSource` が `$false`                                                           |
| `UT-WARN-02` | 同一パスでも大文字小文字・`/` と `\` 差異                 | `"D:\\Repo\\Data\\"` vs `"d:/repo/data"`                                                                     | `Test-SameMountSource` が `$true`（誤警告なし）                                              |
| `UT-WARN-03` | `wslc` 旧バグ `/mnt/...` と正規パスの不一致               | 既存 Source=`"D:\\mnt\\e\\data"` または `"/mnt/e/data"`, 新=`"\\wsl.localhost\\Ubuntu-24.04\\home\\u\\data"` | `Test-SameMountSource` が `$false`                                                           |
| `UT-WARN-04` | コンテナ未存在（空出力 / 非0終了）                        | `-InvokeInspect` が空出力・終了コード 1 を返す                                                               | 警告なし・例外なしで `$null` 返却                                                            |
| `UT-WARN-05` | 非空不正 JSON 出力 / `inspect` スキーマ配列・オブジェクト | (a) 不正 JSON 文字列 `"not json"`<br>(b) 実機 `wslc inspect` 配列形式 JSON（`/app/data` 有り）               | (a) 例外を投げず `$null` を返し、診断用 `Write-Warning` を出力<br>(b) 正しく `Source` を抽出 |

---

## 4. 複合条件デシジョンテーブル（C2 網羅 100% 対象）

### 4.1 Windows `Resolve-DataDir` パス種別・トラバーサル・ルート直下・種別判定

条件:

- `C1`: 入力が空または空白のみ
- `C2`: パスセグメントに `..` を含む / 非 WSL UNC / ドライブ相対 (`D:data`)
- `C3`: パス形式（`WinDrive` / `WslUnc` / `LinuxAbs` / `Relative`）
- `C4`: ルート直下（`D:\`, `D:`, `/`, `\\wsl.localhost\Distro` のみ）
- `C5`: 指定先に既存の通常ファイルが存在する

| Case |  C1   |  C2   |     C3     |  C4   |  C5   | 期待動作                                                                | 対応テスト                               |
| :--: | :---: | :---: | :--------: | :---: | :---: | :---------------------------------------------------------------------- | :--------------------------------------- |
|  D1  | True  |   -   |     -      |   -   | False | 既定 `./data`（`RepoRoot\data`）へフォールバック                        | `UT-DATA-07`                             |
|  D2  | False | True  |     -      |   -   |   -   | 早期エラー拒否（代替設定例を提示）                                      | `UT-DATA-08`, `UT-DATA-09`, `UT-DATA-10` |
|  D3  | False | False | `LinuxAbs` | True  |   -   | エラー拒否（`/` 直下不可）                                              | `UT-DATA-09`                             |
|  D4  | False | False | `LinuxAbs` | False | False | 既定ディストロ解決 → `\\wsl.localhost\<Distro>\...`（取得不可時エラー） | `UT-DATA-05`, `UT-DATA-06`               |
|  D5  | False | False |  `WslUnc`  | True  |   -   | エラー拒否（ディストロ名のみ/不完全 UNC 不可）                          | `UT-DATA-09`                             |
|  D6  | False | False |  `WslUnc`  | False | False | `\\wsl.localhost\<Distro>\...` へ正規化・作成                           | `UT-DATA-03`, `UT-DATA-04`               |
|  D7  | False | False | `WinDrive` | True  |   -   | エラー拒否（`D:\`, `D:` ドライブ直下不可）                              | `UT-DATA-09`                             |
|  D8  | False | False | `WinDrive` | False | False | Windows 絶対パス正規化（`MountSource` は `D:/...`）                     | `UT-DATA-02`, `UT-DATA-12`               |
|  D9  | False | False | `Relative` | False | False | `RepoRoot` 基準絶対パス正規化                                           | `UT-DATA-01`                             |
| D10  |   -   | False |    任意    | False | True  | エラー拒否（通常ファイル指定不可）                                      | `UT-DATA-11`                             |

### 4.2 `ConvertTo-ContainerMountPath`（`TargetIsWindows` × `Path` 種別）

条件:

- `C1`: `TargetIsWindows` が `$true` か（True: Windows コンテナ CLI `wslc` / Windows Docker / Windows Podman、False: Linux コンテナデーモン向け）
- `C2`: 入力パス種別（`WinDrive` / `WslPath`（UNC または `/` 始まり））

| Case | C1 (`TargetIsWindows`) |                                 C2 (`Path`)                                  | 期待出力                                   | 対応テスト                                 |
| :--: | :--------------------: | :--------------------------------------------------------------------------: | :----------------------------------------- | :----------------------------------------- |
|  M1  |          True          |                             `WinDrive` (`E:\e`)                              | `E:/e`                                     | `UT-REPRO-01`, `UT-MOUNT-01`               |
|  M2  |          True          | `WslPath` (`\\wsl.localhost\Ubuntu-24.04\home\u\data` または `/home/u/data`) | `\\wsl.localhost\Ubuntu-24.04\home\u\data` | `UT-DATA-03`, `UT-MOUNT-03`, `UT-MOUNT-04` |
|  M3  |         False          |                             `WinDrive` (`E:\e`)                              | `/mnt/e/e`                                 | `UT-MOUNT-01`                              |
|  M4  |         False          |            `WslPath` (`\\wsl.localhost\Ubuntu-24.04\home\u\data`)            | `/home/u/data`                             | `UT-PATH-05`                               |

### 4.3 `scripts/deploy.sh` の `DATA_DIR` / `VIDEO_DIRS` パス解決・拒否判定

条件:

- `C1`: `DATA_DIR` 未指定または空白のみ
- `C2`: `DATA_DIR` / `VIDEO_DIRS` が WSL UNC 形式（`//wsl.localhost/...` or `\\wsl.localhost\...`）
- `C3`: `DATA_DIR` / `VIDEO_DIRS` が Windows ドライブ形式（`C:\...`, `D:/...`, `C:foo`）
- `C4`: `DATA_DIR` が相対パス（`./data`, `var/data`）
- `C5`: セグメント `..` / ルート `/` 単体 / 既存通常ファイル

| Case |  C1   |  C2   |  C3   |  C4   |  C5   | 期待動作                                                                                             | 対応テスト                        |
| :--: | :---: | :---: | :---: | :---: | :---: | :--------------------------------------------------------------------------------------------------- | :-------------------------------- |
|  S1  | True  |   -   |   -   |   -   |   -   | `${REPO_ROOT}/data` に正規化                                                                         | `IT-SH-DATA-01`                   |
|  S2  | False |   -   |   -   |   -   | True  | 常に非 0 エラー終了（`v1..v2` は誤拒否しない）                                                       | `IT-SH-DATA-06`, `IT-SH-DATA-08`  |
|  S3  | False | True  |   -   |   -   | False | `$WSL_DISTRO_NAME` 非空かつ一致時のみ `/...` 変換 / 不一致・`$WSL_DISTRO_NAME` 空時は非 0 エラー終了 | `IT-SH-DATA-03`, `IT-SH-DATA-04`  |
|  S4  | False | False | True  |   -   | False | **常に非 0 エラー終了**（ビルド・停止前に Linux パス例を案内）                                       | `IT-SH-DATA-05`, `IT-SH-VIDEO-01` |
|  S5  | False | False | False | True  | False | `${REPO_ROOT}` 基準で正規化し絶対パス化                                                              | `IT-SH-DATA-02`                   |
|  S6  | False | False | False | False | False | Linux 絶対パスとして正規化・作成（空白保持）                                                         | `IT-SH-DATA-07`, `IT-SH-DATA-09`  |

### 4.4 既存コンテナマウント不一致警告判定（`deploy.ps1` / `deploy.sh`）

| Case | 既存コンテナ存在 | `/app/data` Source 取得成功 | 新旧パス一致 | 期待動作                                                                   | 対応テスト                                  |
| :--: | :--------------: | :-------------------------: | :----------: | :------------------------------------------------------------------------- | :------------------------------------------ |
|  W1  |      False       |              -              |      -       | 警告なし・新規作成                                                         | `IT-WARN-03`                                |
|  W2  |       True       |            False            |      -       | 例外停止せず `$null`（非空不正 JSON 時のみ診断警告）でデプロイ継続         | `UT-WARN-04`, `UT-WARN-05`                  |
|  W3  |       True       |            True             |     True     | 警告なし                                                                   | `UT-WARN-02`, `IT-WARN-02`                  |
|  W4  |       True       |            True             |    False     | 警告出力（旧/新パス + 非削除明記 + 復帰設定例 + コピー手順）＋デプロイ続行 | `UT-WARN-01`, `IT-WARN-01`, `IT-SH-WARN-01` |

---

## 5. 結合テスト・異常系注入・副作用テスト設計

### 5.1 Windows `deploy.ps1` 結合・モックコマンドテスト（ポート隔離方針含む）

- **ヘルスチェック干渉・30秒待機回避方針**: `scripts/deploy.ps1` を最後まで完走させる結合テスト（`IT-DATA-SIDE-01`, `IT-DATA-WSL-01/02`, `IT-WARN-01/02/03`, `IT-DEPLOY-OVERRIDE-01`）では、テストプロセス内で一時 `System.Net.HttpListener`（または軽量 `TcpListener` HTTP 200 応答スレッド）を空きエフェメラルポート（例: `GetUnusedPort()`）で起動し、`-Port <freePort>` を指定して `deploy.ps1` を実行する。これにより稼働中のポート `5620` へ一切干渉せず、ヘルスチェックが初回 1 秒以内で確実に成功する。

| ID                      | シナリオ概要                                                                   | モック・テスト構成                                                                                                                 | 検証項目                                                                                                                                                                            |
| :---------------------- | :----------------------------------------------------------------------------- | :--------------------------------------------------------------------------------------------------------------------------------- | :---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `IT-DATA-SIDE-01`       | `deploy.ps1` で相対 `DATA_DIR`（`./custom-data`）実行                          | 一時リポジトリ + ダミー `wslc.cmd` + エフェメラル HTTP ポート                                                                      | 1. `<temp>/custom-data` 自動作成<br>2. `-v <temp>/custom-data:/app/data`（スラッシュ変換）で渡ること                                                                                |
| `IT-DATA-WSL-01`        | `deploy.ps1` で `\\wsl.localhost\...` の `DATA_DIR` 実行                       | 実機ディストロ利用可能時は `/tmp/tvm-test-<guid>` を指定（テスト終了後削除）、または `common.ps1` + ダミー `wslc.cmd` 結合         | `-v \\wsl.localhost\<Distro>\...:/app/data` が渡り、`/mnt/...` に化けないこと                                                                                                       |
| `IT-DATA-WSL-02`        | `deploy.ps1` で Linux 絶対パス `DATA_DIR="/home/u/data"` + `VIDEO_DIRS="E:\e"` | PATH 先頭にダミー `wsl.exe`（UTF-16LE / `docker-desktop` 含む出力）+ ダミー `wslc.cmd` + 一時動画ディレクトリ + エフェメラルポート | 既定ユーザーディストロが補完され、`-v \\wsl.localhost\Ubuntu-24.04\home\u\data:/app/data` と `-v <動画Dir>:/app/videos/<DirName>:ro` が生成され、Windows `D:\home` が作られないこと |
| `IT-WARN-01`            | 既存コンテナ別 `DATA_DIR` 警告                                                 | ダミー `wslc.cmd` が `inspect` に対し実機準拠配列 JSON の旧 Source を返す                                                          | ビルド/停止ログより**前**に警告（旧パス・新パス・非削除・復帰設定例・コピー例）が出力され、コンテナ作成が続行すること                                                               |
| `IT-WARN-02`            | 既存コンテナ同一 `DATA_DIR`（非警告）                                          | ダミー `wslc.cmd` が `inspect` に対し新パスと同等 Source を返す                                                                    | 不一致警告メッセージが出力されないこと                                                                                                                                              |
| `IT-WARN-03`            | 既存コンテナ未存在（新規デプロイ）                                             | ダミー `wslc.cmd` が `inspect` に終了コード 1 を返す                                                                               | 不一致警告なしで新規起動まで完了すること                                                                                                                                            |
| `IT-DEPLOY-OVERRIDE-01` | CLI / 環境変数による `DATA_DIR` 上書き                                         | `appsettings.Production.json` の `DATA_DIR` を環境変数または CLI `-DataDir` で上書き                                               | 優先順位通り CLI ＞ 環境変数 ＞ `appsettings.Production.json` で解決されること                                                                                                      |
| `IT-DEPLOY-FAILFAST-01` | 不正 JSON / 不正 `DATA_DIR` の早期終了                                         | 構文不正 JSON または `DATA_DIR="D:\"` を指定し、`wslc.cmd` に呼び出し記録                                                          | `wslc build` / `stop` / `rm` / `run` を一切呼ばずに非 0 終了すること                                                                                                                |

### 5.2 Linux `deploy.sh` 結合・異常系注入テスト（`bash`）

| ID                   | シナリオ概要                                               | 実行環境・注入条件                                                                                                                    | 検証項目                                                                                            |
| :------------------- | :--------------------------------------------------------- | :------------------------------------------------------------------------------------------------------------------------------------ | :-------------------------------------------------------------------------------------------------- |
| `IT-SH-DATA-01`      | `DATA_DIR` 未設定・空文字時                                | `bash` 上の一時ディレクトリ + モック `docker`                                                                                         | `${REPO_ROOT}/data` が自動作成され、`-v <REPO_ROOT>/data:/app/data` で呼ばれること                  |
| `IT-SH-DATA-02`      | `DATA_DIR="./rel-data"` 相対パス（別 CWD 実行）            | `/tmp` 等、リポジトリ外 CWD から `deploy.sh` 実行                                                                                     | `${REPO_ROOT}/rel-data` に正規化され、`./rel-data:` にならないこと                                  |
| `IT-SH-DATA-03`      | `DATA_DIR="//wsl.localhost/Ubuntu-24.04/tmp/tvm-data"`     | `WSL_DISTRO_NAME="Ubuntu-24.04"` を明示設定                                                                                           | `/tmp/tvm-data` に解決されて `-v /tmp/tvm-data:/app/data` で呼ばれること                            |
| `IT-SH-DATA-04`      | 他ディストロまたは `WSL_DISTRO_NAME=""` での WSL UNC 指定  | `WSL_DISTRO_NAME="Ubuntu-24.04"` で `//wsl.localhost/Debian/data`、および `WSL_DISTRO_NAME=""` で `//wsl.localhost/Ubuntu-24.04/data` | ビルド・停止前に非 0 終了＋Linux パス設定例を案内すること                                           |
| `IT-SH-DATA-05`      | Windows ドライブパス指定（`DATA_DIR`）                     | `DATA_DIR="D:\data"`（`WSL_DISTRO_NAME` 有無双方）                                                                                    | 常にビルド前に非 0 終了＋Linux パス設定例を案内すること                                             |
| `IT-SH-DATA-06`      | 不正パス・トラバーサル vs `v1..v2`                         | `DATA_DIR="../etc"`, `DATA_DIR="/"` vs `DATA_DIR="/tmp/.../v1..v2/data"`                                                              | 前者は非 0 終了、後者は正当パスとして通過すること                                                   |
| `IT-SH-DATA-07`      | 通常の Linux 絶対パス指定                                  | `DATA_DIR="/tmp/.../abs-data"`                                                                                                        | そのまま絶対パスでディレクトリ作成・マウントされること                                              |
| `IT-SH-DATA-08`      | `DATA_DIR` に通常ファイルを誤指定                          | `touch` したファイルパスを `DATA_DIR` に設定                                                                                          | ビルド前にディレクトリ以外拒否で非 0 終了すること                                                   |
| `IT-SH-DATA-09`      | 空白を含む Linux パス（`DATA_DIR`）                        | `DATA_DIR="/tmp/.../my data dir"`                                                                                                     | 空白が分割されず `-v "/tmp/.../my data dir:/app/data"` 相当で渡ること                               |
| `IT-SH-VIDEO-01`     | `deploy.sh` で `VIDEO_DIRS="C:\videos"`                    | モック環境で `deploy.sh` 実行                                                                                                         | 非 0 終了＋Windows パス拒否エラー                                                                   |
| `IT-SH-VIDEO-02`     | `deploy.sh` で `VIDEO_DIRS="\\wsl.localhost\Other\videos"` | `WSL_DISTRO_NAME="Ubuntu-24.04"` または空                                                                                             | 解決不能 UNC としてビルド前に非 0 終了すること                                                      |
| `IT-SH-WARN-01`      | `deploy.sh` 既存コンテナ別 `DATA_DIR` 警告                 | モック `docker inspect` が旧 `/app/data` Source を返す                                                                                | ビルド/停止前に 4 要素を含む警告ログ出力＋デプロイ続行                                              |
| `IT-DATA-PERSIST-01` | 再起動後のデータ永続性（引数レベル契約）                   | `New-RunArguments` / `deploy.sh` 連続 2 回実行                                                                                        | 同一の `DATA_DIR` ホストパスが連続して `/app/data` に指定され `--rm` や匿名ボリュームに落ちないこと |

### 5.3 実機 `wslc` 非破壊 E2E 検証（タスク 3.1 にて実施）

- **制約**: 稼働中コンテナ `tag-based-video-manager` および既存実データ（`\\wsl.localhost\Ubuntu-24.04\home\spjao\var\apps\TagBasedVideoManager\data`）を停止・削除・改変しない。
- **検証項目と合格基準**:
  1. `wslc inspect dd1e1e9654c2`（読み取り専用）に対して新設 `Get-ExistingContainerDataMount` を直接呼び出し、実機 JSON から `\\wsl.localhost\Ubuntu-24.04\home\spjao\var\apps\TagBasedVideoManager\data` を正しく抽出できること。
  2. 一時テストディレクトリ（例: `%TEMP%\tvm-e2e-data` および `%TEMP%\tvm-e2e-vids`）＋ `/tmp/tvm-e2e-wsl-data` を用い、`wslc run --rm` の単発テストコンテナで `D:/.../tvm-e2e-vids:/app/videos/tvm-e2e-vids:ro` と `\\wsl.localhost\Ubuntu-24.04\tmp\tvm-e2e-wsl-data:/app/data` をマウントしてコンテナ内からの `/app/videos/...` 読み取りと `/app/data` 書き込みが可能であること、および実行後に `D:\mnt\...` が作成されないことを確認し、一時ファイル・ディレクトリを掃除する。

---

## 6. モック・テスト分離境界方針

- **モック化する外部境界**:
  - `wsl.exe`（`Get-DefaultWslDistro` の `-GetWslList` 注入およびダミー実行ファイル、UTF-16LE/NULL/タイムアウト/終了コード注入）
  - `wslc` / `docker` / `podman` コマンド実行（引数キャプチャ、`inspect` 実機準拠 JSON 出力、未導入/失敗ケース）
  - テスト用 HTTP ヘルスチェックリスナー（エフェメラルポートでの 200 OK 応答）
- **モック化禁止（実ロジック検証対象）**:
  - `scripts/common.ps1` のパス分類・セグメント `..` 検証・ルート直下/通常ファイル検証・マウント引数組み立て・ソース比較ロジック
  - `scripts/deploy.sh` の JSON 読み込み・相対パス正規化・WSL UNC/Windows パス判定・`docker`/`podman` 引数生成（実際の `bash` プロセスで実行して検証）
- **既存テストへの配慮**:
  - `DeployScriptTests.fs` 既存の `WslPathConversionTests` (`UT-PATH-01`..`04`)、`PortHelperTests`、`RuntimeNameScriptTests`、`Resolve-VideoMounts` (`UT-13`..`19`)、`DeployScriptAppSettingsTests`、`DeployScriptLinuxValidationTests` がすべて引き続き Green であることを検証する。
