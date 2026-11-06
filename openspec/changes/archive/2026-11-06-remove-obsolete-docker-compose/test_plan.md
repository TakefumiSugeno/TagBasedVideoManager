# テスト計画・テストケース定義書 (Test Plan): remove-obsolete-docker-compose

- **変更名**: `remove-obsolete-docker-compose`
- **適用規模レベル**: `L2`
- **変更種別**: `refactor`（リファクタリング・不要資産削除）

---

## 1. カバレッジ目標と完了基準

| 指標                          | 定義                                                                  | 目標値（全体 / 新規・変更差分） | 適用対象・備考                                                   |
| :---------------------------- | :-------------------------------------------------------------------- | :------------------------------ | :--------------------------------------------------------------- |
| **C0 (命令網羅 / Line)**      | 実行可能な全ステートメントの網羅率                                    | **80%以上** / **90%以上**       | `TagBasedVideoManager.Renamer` 全体および変更箇所（`State.fs`）  |
| **C1 (分岐網羅 / Branch)**    | `if/else`, `switch/match`, ガード節, 例外経路の真偽両方の網羅率       | **80%以上** / **85%以上**       | `ContainerController.fs`, `State.fs` の `DockerCommandCompleted` |
| **C2 (条件網羅 / Condition)** | 複合条件式（`A && B \|\| C`）の各基本条件の真偽組み合わせ網羅         | **100%**（対象ロジック）        | 下記「4. デシジョンテーブル」記載のランタイム・GPU判定ロジック   |
| **Scenario 網羅率**           | `specs/**/spec.md` の全 `#### Scenario:` に対する結合/E2Eテスト対応率 | **100%**                        | 下記「2. トレーサビリティマトリクス」で対応付け                  |

- **カバレッジ除外対象**: `Program.fs`（Avalonia デスクトップアプリのエントリポイント定型ボイラープレート）
- **モック境界方針**:
  - **モック化する外部境界**: コンテナ CLI 外部プロセス実行（`processRunner`）、ポート 5620 への HTTP ヘルスチェック通信（`httpHealthChecker`）、コマンド存在確認プローブ（`commandExists`）
  - **実物を使用する範囲**: `State.update` の状態遷移・エラーメッセージ生成、`ContainerController` の JSON パース・ランタイム判定、`scripts/common.ps1` / `scripts/deploy.ps1` / `scripts/deploy.sh` のスクリプト構文・AST・引数構築ロジック、リポジトリ内の実ファイル（`Dockerfile`, `README.md`, `.fsproj`）検証

---

## 2. シナリオ・トレーサビリティマトリクス

| Spec Capability / Requirement                                                             | Scenario 名                                                | Unit | Integration | E2E | 対応テストケースID                              |
| :---------------------------------------------------------------------------------------- | :--------------------------------------------------------- | :--: | :---------: | :-: | :---------------------------------------------- |
| `docker-infrastructure` / `Docker Compose 構成ファイルの非依存と単一コンテナ運用の一本化` | リポジトリからの docker-compose.yml 廃止とドキュメント整合 |  -   |     ✅      |  -  | `IT-CLEAN-01`, `IT-CLEAN-02`, `IT-CLEAN-03`     |
| `docker-infrastructure` / `Docker Compose 構成ファイルの非依存と単一コンテナ運用の一本化` | Windows (WSL 3.0) および Linux ホスト双方でのデプロイ完結  |  ✅  |     ✅      |  -  | `IT-SCRIPT-01`, `IT-SCRIPT-02`, `UT-RUN-C2`     |
| `docker-infrastructure` / `マルチステージビルドと軽量ランタイム配信`                      | コンテナイメージのビルド                                   |  -   |     ✅      |  -  | `IT-CLEAN-02`, `IT-SCRIPT-01`, `IT-SCRIPT-02`   |
| `docker-infrastructure` / `ポート公開と環境変数注入`                                      | 環境変数の注入と疎通確認                                   |  ✅  |     ✅      |  -  | `UT-RUN-ARGS`, `UT-PORT-01`, `UT-PORT-02`       |
| `companion-docker-control` / `Docker Compose コマンドのGUI実行`                           | コンテナの再起動実行                                       |  ✅  |     ✅      | ✅  | `UT-CC-03`, `ST-01〜04`, `E2E-01`               |
| `companion-docker-control` / `Docker Compose コマンドのGUI実行`                           | コマンド実行失敗時のエラー通知                             |  ✅  |     ✅      | ✅  | `UT-STATE-ERR-01`〜`03`, `IT-CC-01`, `IT-CC-03` |
| `companion-docker-control` / `Docker Compose コマンドのGUI実行`                           | 旧 DockerController および DockerError の完全廃止          |  ✅  |     ✅      |  -  | `IT-CLEAN-04`, `UT-STATE-ERR-01`〜`03`          |

---

## 3. 単体テスト：同値分割・境界値テストケース定義

### 3.1 同値クラス・境界値一覧

| ケースID          | 対象関数 / メソッド                       | 検証観点（区分）                                        | 入力値・前提状態                                                                            | 期待される出力・状態変化                                                                                        |
| :---------------- | :---------------------------------------- | :------------------------------------------------------ | :------------------------------------------------------------------------------------------ | :-------------------------------------------------------------------------------------------------------------- |
| `UT-STATE-ERR-01` | `State.update` (`DockerCommandCompleted`) | 正常系（コンテナ操作成功）                              | `DockerCommandCompleted ("restart", Ok "tag-based-video-manager")`                          | `ErrorMessage = None` を維持し、`CheckDockerStatus` コマンドを発行すること                                      |
| `UT-STATE-ERR-02` | `State.update` (`DockerCommandCompleted`) | 異常系（`ContainerError` 発生）                         | `DockerCommandCompleted ("restart", Error (ContainerError ("restart", 1, "daemon error")))` | `IsDockerBusy = false`、`ErrorMessage` に `"コンテナ コマンド 'restart' エラー: daemon error"` が設定されること |
| `UT-STATE-ERR-03` | `State.update` (`DockerCommandCompleted`) | 異常系（`TimeoutError` / `ValidationError`）            | `DockerCommandCompleted ("start", Error (TimeoutError "start"))` および `ValidationError`   | `IsDockerBusy = false`、タイムアウトまたはランタイム未検出エラーが `ErrorMessage` に設定されること              |
| `UT-CC-03`        | `ContainerController.executeAction`       | 正常系（Windows `Wslc` / Linux `Docker`, `Podman`）     | 各 `ContainerRuntime` と `"start"` / `"stop"` / `"restart"`                                 | `<runtime> <action> tag-based-video-manager` が発行され `Ok` が返ること                                         |
| `UT-CC-08`        | `ContainerController.executeAction`       | 境界値（終了コード `1`, `2`, `125`, `-1`, `255`）       | 非ゼロ終了コードとエラーメッセージ（空文字・1文字・複数行日本語）                           | `Error (ContainerError (action, exitCode, stderr))` が返ること（`DockerError` 型が存在しないこと）              |
| `UT-PORT-01/02`   | `Resolve-WebPort` (`scripts/common.ps1`)  | 境界値（ポート番号 `1`, `5620`, `65535`, `""`, `null`） | 最小ポート `1`、標準 `5620`、最大 `65535`、空文字・空白・null                               | 有効範囲は数値変換値、空/不正値は既定 `5620` を返すこと                                                         |

---

## 4. 単体テスト：複合条件デシジョンテーブル（C2 / MC/DC 網羅）

対象ロジック 1: `ContainerController.detectContainerRuntime` および `Get-ContainerRuntimeName`（Windows WSL3 と Linux ホスト両対応のランタイム決定）

| ケースID       | 条件1: `isWindows` | 条件2: `wslc` 存在 | 条件3: `docker` 存在 | 条件4: `podman` 存在 | 期待される判定結果・アクション |
| :------------- | :----------------: | :----------------: | :------------------: | :------------------: | :----------------------------- |
| `UT-RUN-C2-01` |         T          |         T          |          T           |          -           | `Ok Wslc` (`"wslc"`)           |
| `UT-RUN-C2-02` |         T          |         T          |          F           |          -           | `Ok Wslc` (`"wslc"`)           |
| `UT-RUN-C2-03` |         T          |         F          |          T           |          -           | `Ok Docker` (`"docker"`)       |
| `UT-RUN-C2-04` |         T          |         F          |          F           |         T/F          | `Error` (`$null`)              |
| `UT-RUN-C2-05` |         F          |         -          |          T           |         T/F          | `Ok Docker` (`"docker"`)       |
| `UT-RUN-C2-06` |         F          |         -          |          F           |          T           | `Ok Podman` (`"podman"`)       |
| `UT-RUN-C2-07` |         F          |        T/F         |          F           |          F           | `Error` (`$null`)              |

対象ロジック 2: `New-RunArguments`（`scripts/common.ps1` における NVIDIA / Linux AMD DRI / CPU の複合条件）

| ケースID       | 条件1: `HasNvidiaGpu` | 条件2: `TargetIsLinux` | 条件3: `HasDri` | 期待される判定結果・アクション                           |
| :------------- | :-------------------: | :--------------------: | :-------------: | :------------------------------------------------------- |
| `UT-GPU-C2-01` |           T           |          T/F           |       T/F       | `--gpus all` を付与し、`--device` は付与しない           |
| `UT-GPU-C2-02` |           F           |           T            |        T        | `--device /dev/dri:/dev/dri` を付与する（Linux AMD）     |
| `UT-GPU-C2-03` |           F           |           T            |        F        | GPU 引数なし（CPU フォールバック）                       |
| `UT-GPU-C2-04` |           F           |           F            |       T/F       | GPU 引数なし（WSL3 AMD 非対応のため CPU フォールバック） |

---

## 5. 結合テスト（Integration Test）ケース定義

### 5.1 インターフェース契約・廃止資産不在検証・異常系注入

| ケースID       | 連携対象（File / Script / AST）                         | シナリオ・検証内容                                                                          | 期待される振る舞い                                                                                                      |
| :------------- | :------------------------------------------------------ | :------------------------------------------------------------------------------------------ | :---------------------------------------------------------------------------------------------------------------------- |
| `IT-CLEAN-01`  | リポジトリルート (`docker-compose.yml`)                 | 廃止された `docker-compose.yml` の物理存在チェック                                          | `File.Exists(RepoLayout.pathOf "docker-compose.yml")` が `false` であること                                             |
| `IT-CLEAN-02`  | `Dockerfile`                                            | マルチステージ構成の維持と廃止済み `docker-compose.yml` コメント参照の不在検証              | `Dockerfile` が存在し、`docker-compose.yml` 文字列を含まず、`deploy.ps1` / `deploy.sh` への案内を含むこと               |
| `IT-CLEAN-03`  | `README.md`                                             | 起動手順における `docker-compose.yml` / `docker compose` 廃止とスクリプト案内検証           | `README.md` に `docker-compose.yml` や `docker compose up` が存在せず、`deploy.ps1` と `deploy.sh` が記載されていること |
| `IT-CLEAN-04`  | `DockerController.fs` / `DockerControllerTests.fs` / 型 | 旧モジュール・旧テストファイルの物理不在および `RenamerError` からの `DockerError` 廃止検証 | 両ファイルが存在せず、`FSharpType.GetUnionCases typeof<RenamerError>` に `DockerError` が存在しないこと                 |
| `IT-SCRIPT-01` | `scripts/deploy.ps1` / `scripts/common.ps1`             | PowerShell Parser AST 解析（Windows WSL3 デプロイスクリプト検証）                           | 構文エラー 0 件、`Invoke-Expression` 不使用、`ConvertTo-WslPath` / `Get-ContainerRuntimeName` 呼び出し確認              |
| `IT-SCRIPT-02` | `scripts/deploy.sh`                                     | `bash -n` 構文チェック・LF 改行・Linux `docker` / `podman` 対応検証                         | 構文エラー 0 件、CRLF なし、`docker` / `podman` 判別と GPU 自動判定が含まれること                                       |

### 5.2 状態遷移網羅（0-switch: 有効遷移 / 1-switch: 無効遷移の拒否）

| ケースID | 現在の状態 | イベント / 操作                             | 遷移後の状態（または拒否エラー）                  | 区分                 |
| :------- | :--------- | :------------------------------------------ | :------------------------------------------------ | :------------------- |
| `ST-01`  | `Stopped`  | `ExecuteDockerAction "start"`               | `Running`（`CheckDockerStatus` 経由で更新）       | 正常遷移（0-switch） |
| `ST-02`  | `Running`  | `ExecuteDockerAction "restart"`             | `Running`                                         | 正常遷移（0-switch） |
| `ST-03`  | `Running`  | `ExecuteDockerAction "stop"`                | `Stopped`                                         | 正常遷移（0-switch） |
| `ST-04`  | `Stopped`  | `ExecuteDockerAction "stop"`（二重停止）    | `Ok`（冪等成功扱い）→ `Stopped` 維持              | 異常遷移拒否・冪等性 |
| `ST-05`  | 任意       | `DockerCommandCompleted` (`ContainerError`) | `IsDockerBusy = false`, `ErrorMessage = Some ...` | 異常系復帰           |

---

## 6. E2Eテスト・画面検証ケース定義

| ケースID | ジャーニー種別               | 操作手順（Given → When）                                                               | 期待される画面状態・出力エビデンス（Then）                                                      |
| :------- | :--------------------------- | :------------------------------------------------------------------------------------- | :---------------------------------------------------------------------------------------------- |
| `E2E-01` | **Happy Path**               | 長パス動画走査 → AI 短縮提案 → 物理リネーム → Undo 復元（`RenameIntegrationE2ETests`） | リネームと Undo が正常完了し、各画面状態の PNG キャプチャ（`E2E_01`〜`E2E_10`）が生成されること |
| `E2E-02` | **Zero-State / 初回起動**    | API キー未設定状態で起動・スキャン実行                                                 | クラッシュせず未提案状態と設定案内メッセージが表示され、手動編集・リネーム・Undo が可能なこと   |
| `E2E-03` | **Negative / Recovery Path** | コンテナ操作失敗（`ContainerError` / `TimeoutError`）またはファイル名衝突発生          | エラー通知表示後もアプリがフリーズせず、再操作・自動連番付与により正常完了できること            |
