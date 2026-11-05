# テスト計画・テストケース定義書 (Test Plan): wsl3-and-devcontainer-support

- **変更名**: `wsl3-and-devcontainer-support`
- **適用規模レベル**: `L2`
- **変更種別**: `feat`（機能追加/変更）

---

## 1. カバレッジ目標と完了基準

| 指標                          | 定義                                                          | 目標値（全体 / 新規・変更差分） | 適用対象・備考                                |
| :---------------------------- | :------------------------------------------------------------ | :------------------------------ | :-------------------------------------------- |
| **C0 (命令網羅 / Line)**      | 実行可能な全ステートメントの網羅率                            | **80%以上** / **90%以上**       | `ContainerController.fs` および関連モジュール |
| **C1 (分岐網羅 / Branch)**    | `if/else`, `match`, ガード節等の網羅率                        | **80%以上** / **85%以上**       | `ContainerController.fs`                      |
| **C2 (条件網羅 / Condition)** | 複合条件式の各基本条件の真偽組み合わせ網羅                    | **100%**（対象ロジック）        | ランタイム自動検出およびステータス判定        |
| **Scenario 網羅率**           | `specs/**/spec.md` の全 `#### Scenario:` に対するテスト対応率 | **100%**                        | 下記「2. トレーサビリティマトリクス」         |

- **カバレッジ除外対象**: `scripts/`（PowerShell / Bash スクリプト本体はスクリプト実行テストで検証）、`.devcontainer/`（JSON/Dockerfile 設定ファイル）
- **モック境界方針**:
  - **モック化する外部境界**: 外部プロセス実行（`ProcessStartInfo` による `wslc`/`docker` 呼び出し）、HTTP 疎通（`HttpClient`）
  - **実物を使用する範囲**: JSON パース処理、ステータス判定計算、コマンド引数構築、ドメイン型変換、パス正規化変換

---

## 2. シナリオ・トレーサビリティマトリクス

| Spec Capability / Requirement                                                          | Scenario 名                                 | Unit | Integration | E2E | 対応テストケースID                   |
| :------------------------------------------------------------------------------------- | :------------------------------------------ | :--: | :---------: | :-: | :----------------------------------- |
| `devcontainer-environment` / DevContainer 開発環境の提供                               | DevContainer 環境の起動とツール確認         |  -   |     ✅      | ✅  | `IT-DEV-01`, `E2E-DEV-01`            |
| `devcontainer-environment` / 開発環境専用ポートによる競合防止                          | 本番稼働中の DevContainer 並行起動          |  ✅  |     ✅      |  -  | `UT-PORT-01`, `IT-PORT-01`           |
| `container-deployment-scripts` / クロスプラットフォームデプロイスクリプトの提供        | Windows PowerShell によるデプロイ実行       |  -   |     ✅      | ✅  | `IT-SCRIPT-01`                       |
| `container-deployment-scripts` / クロスプラットフォームデプロイスクリプトの提供        | Linux Bash によるデプロイ実行               |  -   |     ✅      |  -  | `IT-SCRIPT-02`                       |
| `container-deployment-scripts` / コンテナランタイムの自動検出とガイダンス              | WSL 3.0 環境での wslc 自動選択              |  ✅  |     ✅      |  -  | `UT-RUN-01`, `IT-RUN-01`             |
| `container-deployment-scripts` / コンテナランタイムの自動検出とガイダンス              | ランタイム未導入時の具体的ガイダンス提示    |  ✅  |     ✅      |  -  | `UT-RUN-02`, `IT-RUN-02`             |
| `container-deployment-scripts` / ポート事前衝突チェック                                | ポート使用中による事前検知と中断            |  ✅  |     ✅      |  -  | `UT-PORTCHECK-01`, `IT-PORTCHECK-01` |
| `container-deployment-scripts` / デプロイ後の自動ヘルスチェックと障害ログ出力          | 正常起動完了の検証                          |  ✅  |     ✅      |  -  | `UT-HEALTH-01`, `IT-HEALTH-01`       |
| `container-deployment-scripts` / デプロイ後の自動ヘルスチェックと障害ログ出力          | 起動失敗時のタイムアウト検出とログ出力      |  ✅  |     ✅      |  -  | `UT-HEALTH-02`, `IT-HEALTH-02`       |
| `docker-infrastructure` / ホスト動画ディレクトリの読み取り専用マウント                 | 読み取り専用ボリュームの保護                |  -   |     ✅      |  -  | `IT-DOCKER-01`                       |
| `docker-infrastructure` / アプリケーションデータとサムネイルのボリューム永続化         | コンテナ再起動後のデータ永続性              |  -   |     ✅      |  -  | `IT-DOCKER-02`                       |
| `docker-infrastructure` / CPUおよびマルチGPU（AMD/NVIDIA）アクセラレーションの切り替え | CPUのみの既定実行 (デフォルト)              |  ✅  |     ✅      |  -  | `UT-GPU-02`, `IT-GPU-02`             |
| `docker-infrastructure` / CPUおよびマルチGPU（AMD/NVIDIA）アクセラレーションの切り替え | AMD GPU (VAAPI/ROCm) によるハードウェア支援 |  ✅  |     ✅      |  -  | `UT-GPU-03`, `IT-GPU-03`             |
| `docker-infrastructure` / CPUおよびマルチGPU（AMD/NVIDIA）アクセラレーションの切り替え | NVIDIA GPU (CUDA) によるハードウェア支援    |  ✅  |     ✅      |  -  | `UT-GPU-01`, `IT-GPU-01`             |
| `companion-docker-control` / Dockerコンテナ稼働状態の特定と監視                        | コンテナ正常稼働の特定と表示                |  ✅  |     ✅      | ✅  | `UT-CC-01`, `E2E-CC-01`              |
| `companion-docker-control` / Dockerコンテナ稼働状態の特定と監視                        | コンテナ停止または異常の特定                |  ✅  |     ✅      | ✅  | `UT-CC-02`, `E2E-CC-02`              |
| `companion-docker-control` / Dockerコンテナ稼働状態の特定と監視                        | ランタイム未導入時の状態表示                |  ✅  |     ✅      | ✅  | `UT-CC-07`, `E2E-CC-03`              |
| `companion-docker-control` / Docker Compose コマンドのGUI実行                          | コンテナの再起動実行                        |  ✅  |     ✅      |  -  | `UT-CC-03`, `IT-CC-01`               |
| `companion-docker-control` / Docker Compose コマンドのGUI実行                          | コマンド実行失敗時のエラー通知              |  ✅  |     ✅      |  -  | `UT-CC-08`, `IT-CC-03`               |
| `companion-docker-control` / リネーム連動コンテナ再起動                                | リネーム後のワンクリック復旧                |  ✅  |     ✅      |  -  | `UT-CC-04`, `IT-CC-02`               |
| `companion-docker-control` / リネーム連動コンテナ再起動                                | 連動再起動失敗時の通知                      |  ✅  |     ✅      |  -  | `UT-CC-09`, `IT-CC-04`               |

---

## 3. 単体テスト：同値分割・境界値テストケース定義

### 3.1 同値クラス・境界値一覧

| ケースID          | 対象関数 / メソッド      | 検証観点（区分）                         | 入力値・前提状態                                                           | 期待される出力・状態変化                                 |
| :---------------- | :----------------------- | :--------------------------------------- | :------------------------------------------------------------------------- | :------------------------------------------------------- |
| `UT-CC-01`        | `parseContainerPsOutput` | 正常系（単一コンテナ running）           | `[{"Names":"tag-based-video-manager","State":"running","ID":"abc"}]`       | `(Running, Some "abc")`                                  |
| `UT-CC-02`        | `parseContainerPsOutput` | 正常系（コンテナ exited / stopped）      | `[{"Names":"tag-based-video-manager","State":"exited","ID":"abc"}]`        | `(Stopped, Some "abc")`                                  |
| `UT-CC-03`        | `parseContainerPsOutput` | 境界値（空文字列・空白）                 | `""` / `"   \r\n"`                                                         | `(NotFound, None)`                                       |
| `UT-CC-04`        | `parseContainerPsOutput` | 境界値（対象外コンテナのみ存在）         | `[{"Names":"other-service","State":"running","ID":"xyz"}]`                 | `(NotFound, None)`                                       |
| `UT-CC-05`        | `parseContainerPsOutput` | 異常系（破損 JSON）                      | `{"invalid-json...`                                                        | `(Unhealthy, None)`（警告ログ出力＆安全ハンドリング）    |
| `UT-CC-06`        | `parseContainerPsOutput` | 境界値（NDJSON改行区切り形式）           | `{"Names":"tag-based-video-manager","State":"running"}\n{"Names":"other"}` | `(Running, _)`                                           |
| `UT-CC-07`        | `checkStatus`            | 異常系（ランタイム未検出）               | ランタイム = None                                                          | `{ State = NotInstalled; IsPortAccessible = false }`     |
| `UT-CC-08`        | `executeAction`          | 異常系（コマンド実行非ゼロ終了）         | exitCode = 1, stderr = "access denied"                                     | `Error (ContainerError ("restart", 1, "access denied"))` |
| `UT-CC-09`        | `executeAction`          | 異常系（10秒タイムアウト発生）           | プロセス応答なし                                                           | `Error (TimeoutError "restart")`（プロセス強制終了）     |
| `UT-RUN-01`       | `detectContainerRuntime` | 正常系（WSL3 環境: wslc 検出）           | `wslc.exe` 存在 = true                                                     | `Ok Runtime.Wslc`                                        |
| `UT-RUN-02`       | `detectContainerRuntime` | 境界値（wslc なし・docker あり）         | `wslc` = false, `docker` = true                                            | `Ok Runtime.Docker`                                      |
| `UT-RUN-03`       | `detectContainerRuntime` | 異常系（ランタイム全滅）                 | 全ランタイム = false                                                       | `Error "No container runtime found"`                     |
| `UT-PORT-01`      | `resolveWebPort`         | 正常系（PORT 環境変数あり）              | `PORT="5621"`                                                              | `5621`                                                   |
| `UT-PORT-02`      | `resolveWebPort`         | 境界値（未設定・空白時既定値）           | `PORT=""` または null                                                      | `5620`                                                   |
| `UT-GPU-01`       | `buildRunArguments`      | 正常系（NVIDIA GPU 有効）                | `hasNvidiaGpu = true`                                                      | 引数に `--gpus all` が含まれる                           |
| `UT-GPU-02`       | `buildRunArguments`      | 正常系（GPU なし）                       | `hasNvidiaGpu = false`                                                     | 引数に `--gpus` が含まれない                             |
| `UT-GPU-03`       | `buildRunArguments`      | 正常系（Linux Docker + AMD）             | `isLinux = true, isAmd = true`                                             | 引数に `--device /dev/dri:/dev/dri` が含まれる           |
| `UT-PATH-01`      | `toWslPath`              | 正常系（標準Windowsパス）                | `C:\Users\Public\Videos`                                                   | `/mnt/c/Users/Public/Videos`                             |
| `UT-PATH-02`      | `toWslPath`              | 境界値（小文字ドライブ・末尾スラッシュ） | `d:\videos\`                                                               | `/mnt/d/videos`                                          |
| `UT-PATH-03`      | `toWslPath`              | 境界値（スペースを含むパス）             | `D:\My Videos\Season 1`                                                    | `/mnt/d/My Videos/Season 1`                              |
| `UT-PATH-04`      | `toWslPath`              | 境界値（すでにLinux/WSLパス形式）        | `/mnt/c/data`                                                              | `/mnt/c/data`（変更なし）                                |
| `UT-PORTCHECK-01` | `isPortInUse`            | 正常系（空きポート）                     | ポート 5620 リッスンなし                                                   | `false`                                                  |
| `UT-PORTCHECK-02` | `isPortInUse`            | 異常系（ポート使用中）                   | ポート 5620 リッスン中                                                     | `true`                                                   |

---

## 4. 単体テスト：複合条件デシジョンテーブル（C2 網羅）

対象ロジック: `ContainerController.checkStatus` の状態判定

| ケースID | 条件1: ランタイム検出 | 条件2: コンテナ State == Running | 条件3: HTTP :5620 疎通成功 | 期待される判定結果                                         |
| :------- | :-------------------: | :------------------------------: | :------------------------: | :--------------------------------------------------------- |
| `DT-01`  |           T           |                T                 |             T              | `{ State = Running; IsPortAccessible = true }`             |
| `DT-02`  |           T           |                T                 |             F              | `{ State = Unhealthy; IsPortAccessible = false }`          |
| `DT-03`  |           T           |                F                 |             -              | `{ State = Stopped / NotFound; IsPortAccessible = false }` |
| `DT-04`  |           F           |                -                 |             -              | `{ State = NotInstalled; IsPortAccessible = false }`       |

---

## 5. 結合テスト（Integration Test）ケース定義

### 5.1 インターフェース契約・異常系注入（Fault Injection）

| ケースID       | 連携対象             | シナリオ・注入する異常                                                  | 期待される振る舞い                                                                   |
| :------------- | :------------------- | :---------------------------------------------------------------------- | :----------------------------------------------------------------------------------- |
| `IT-CC-01`     | CLI プロセス         | `restart` コマンド実行時に終了コード 1（エラー）                        | `Error (ContainerError ("restart", 1, "failed"))` が返りアプリがクラッシュしない     |
| `IT-CC-02`     | HTTP ヘルスチェック  | ポート 5620 接続タイムアウト（2秒経過）                                 | 即座に false が返り UI がブロックされない                                            |
| `IT-DEV-01`    | DevContainer 設定    | `.devcontainer/devcontainer.json` の構文妥当性検証                      | JSON スキーマ検証および必須キー（`forwardPorts: [5621]`, `PORT=5621`）の存在確認     |
| `IT-SCRIPT-01` | `scripts/deploy.ps1` | PowerShell 構文解析（`[System.Management.Automation.Language.Parser]`） | スクリプト構文エラーが 0 件であり、AST に危険な構文が存在しないこと                  |
| `IT-SCRIPT-02` | `scripts/deploy.sh`  | ShellCheck または Bash 静的解析                                         | 構文エラーが 0 件であり、未定義変数展開防止（`set -euo pipefail`）がなされていること |
| `IT-DOCKER-02` | ボリュームマウント   | `./data:/app/data` バインドマウント                                     | コンテナ再起動後も SQLite DB ファイルの更新が保持されること                          |

### 5.2 状態遷移網羅

| ケースID | 現在の状態 | イベント / 操作         | 遷移後の状態                        | 区分                 |
| :------- | :--------- | :---------------------- | :---------------------------------- | :------------------- |
| `ST-01`  | `Stopped`  | `Start` 実行            | `Running`                           | 正常遷移（0-switch） |
| `ST-02`  | `Running`  | `Restart` 実行          | `Running`                           | 正常遷移（0-switch） |
| `ST-03`  | `Running`  | `Stop` 実行             | `Stopped`                           | 正常遷移（0-switch） |
| `ST-04`  | `Stopped`  | `Stop` 実行（二重停止） | `Stopped`（エラーとならず安全処理） | 冪等性（1-switch）   |

---

## 6. E2Eテスト・画面検証ケース定義

| ケースID     | ジャーニー種別                      | 操作手順（Given → When）                    | 期待される画面状態・出力エビデンス                         |
| :----------- | :---------------------------------- | :------------------------------------------ | :--------------------------------------------------------- |
| `E2E-CC-01`  | **Happy Path**（稼働中表示）        | コンテナ稼働中に Companion アプリを起動     | ステータスバーに「● RUNNING (Port 5620)」が表示される      |
| `E2E-CC-02`  | **Zero-State**（停止中表示）        | コンテナ未起動状態で Companion アプリを起動 | ステータスバーに「○ STOPPED」が表示される                  |
| `E2E-CC-03`  | **Error-State**（ランタイム未検出） | ランタイム未導入環境でアプリを起動          | ステータスバーに「✕ NO RUNTIME」が表示され案内が出ること   |
| `E2E-DEV-01` | **DevContainer 検証**               | DevContainer 内で Web アプリを起動          | ホストのブラウザで `http://localhost:5621/` にアクセス可能 |
