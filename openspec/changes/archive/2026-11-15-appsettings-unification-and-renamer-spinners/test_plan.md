# テスト計画・テストケース定義書 (Test Plan): appsettings-unification-and-renamer-spinners

- **変更名**: `appsettings-unification-and-renamer-spinners`
- **適用規模レベル**: `L2`
- **変更種別**: `feat`（設定ファイル統合・環境別保存対応・ローディングスピナーUI導入）

---

## 1. カバレッジ目標と完了基準

| 指標                          | 定義                                                                  | 目標値（全体 / 新規・変更差分） | 適用対象・備考                                       |
| :---------------------------- | :-------------------------------------------------------------------- | :------------------------------ | :--------------------------------------------------- |
| **C0 (命令網羅 / Line)**      | 実行可能な全ステートメントの網羅率                                    | **80%以上** / **90%以上**       | Web `Program.fs` / `HttpHandlers.fs`・Renamer 全対象 |
| **C1 (分岐網羅 / Branch)**    | `if/else`, `switch/match`, ガード節, 例外経路の真偽両方の網羅率       | **80%以上** / **85%以上**       | 同上                                                 |
| **C2 (条件網羅 / Condition)** | 複合条件式（`A && B \|\| C`）の各基本条件の真偽組み合わせ網羅         | **100%**（対象ロジック）        | 下記「4. デシジョンテーブル」記載の判定ロジック      |
| **Scenario 網羅率**           | `specs/**/spec.md` の全 `#### Scenario:` に対する結合/E2Eテスト対応率 | **100%**                        | 下記「2. トレーサビリティマトリクス」で対応付け      |

- **カバレッジ除外対象**: Avalonia FuncUI `Views.fs` の宣言的 UI レイアウト構築コード（実ウィンドウ / Visual 描画チェックおよび State 分岐テストで検証）、エントリポイント定型ホスト起動文
- **モック境界方針**:
  - **モック化する外部境界**: OpenRouter HTTP 通信 (`OpenRouterClient` モック関数), DuckDuckGo 外部プロセス (`WebSearchClient`), Docker CLI (`ContainerController` コマンド実行モック), 読み取り専用ディレクトリ権限テスト（ACL または抽象化境界）
  - **実物を使用する範囲**: 実際の JSON シリアライズ/デシリアライズ (`appsettings.json` / `appsettings.{env}.json` 一時ディレクトリ), `IConfigurationBuilder`, `pwsh` / `bash` デプロイスクリプト実プロセス検証, Elmish `State.update` 状態機械, 実 Avalonia ウィンドウまたは実画面キャプチャ検証

---

## 2. シナリオ・トレーサビリティマトリクス

| Spec Capability / Requirement                                             | Scenario 名                                                   | Unit | Integration | E2E | 対応テストケースID                  |
| :------------------------------------------------------------------------ | :------------------------------------------------------------ | :--: | :---------: | :-: | :---------------------------------- |
| `companion-file-rename` / `.NET標準外部設定ファイル...環境別ファイル保存` | .env および VIDEO_DIR OS環境変数を参照しない純粋マージ        |  ✅  |     ✅      |  -  | `UT-01`, `IT-01`                    |
| `companion-file-rename` / `.NET標準外部設定ファイル...環境別ファイル保存` | アクティブ環境の appsettings.{Environment}.json への保存      |  ✅  |     ✅      |  -  | `UT-02`, `UT-03`, `DT-01`〜`DT-04`  |
| `companion-file-rename` / `.NET標準外部設定ファイル...環境別ファイル保存` | 書込不可環境での %APPDATA% 環境別保存と再読込整合             |  ✅  |     ✅      |  -  | `UT-04`, `IT-02`                    |
| `companion-file-rename` / `.NET標準外部設定ファイル...環境別ファイル保存` | Development / Production オーバーライドマージとフォールバック |  ✅  |      -      |  -  | `UT-05`                             |
| `companion-file-rename` / `.NET標準外部設定ファイル...環境別ファイル保存` | 不正な環境名指定時の安全なフォールバック                      |  ✅  |      -      |  -  | `UT-06`                             |
| `companion-file-rename` / `クリーンなベース設定ファイル配備とビルド起動`  | 環境別ファイル不在時のビルドおよび起動成功                    |  -   |     ✅      |  -  | `IT-03`                             |
| `companion-file-rename` / `クリーンなベース設定ファイル配備とビルド起動`  | ベース設定ファイルの秘密情報・個人パス非混入                  |  ✅  |      -      |  -  | `UT-07`                             |
| `companion-file-rename` / `環境別設定ファイルのGit管理完全除外`           | appsettings.Development/Production.json の Git 非追跡         |  -   |     ✅      |  -  | `IT-04`                             |
| `companion-file-rename` / `環境別設定ファイルのGit管理完全除外`           | 既存追跡環境ファイルおよび .env.sample のリポジトリ除外       |  -   |     ✅      |  -  | `IT-05`                             |
| `companion-file-rename` / `アクティブ構成環境および保存先ファイル名表示`  | 画面上のアクティブ環境バッジ表示                              |  ✅  |      -      | ✅  | `UT-08`, `E2E-01`                   |
| `companion-file-rename` / `スキャン・AI提案・リネーム/UndoスピナーUI`     | ディレクトリスキャン中のスピナー表示と Empty State 抑止       |  ✅  |      -      | ✅  | `ST-01`, `DT-05`, `E2E-02`          |
| `companion-file-rename` / `スキャン・AI提案・リネーム/UndoスピナーUI`     | AI 提案処理中の行・フッタースピナー表示                       |  ✅  |      -      | ✅  | `ST-02`, `DT-06`, `E2E-02`          |
| `companion-file-rename` / `スキャン・AI提案・リネーム/UndoスピナーUI`     | リネームおよび Undo 実行中のオーバーレイスピナー表示          |  ✅  |      -      | ✅  | `ST-03`, `E2E-02`                   |
| `companion-file-rename` / `スキャン・AI提案・リネーム/UndoスピナーUI`     | 非同期処理エラー・キャンセル時のスピナー確実解除              |  ✅  |     ✅      | ✅  | `ST-04`, `ST-05`, `IT-06`, `E2E-03` |
| `companion-docker-control` / `コンテナ操作中のステータスバー小型スピナー` | コンテナ再起動実行中の小型スピナー表示と完了後の解除          |  ✅  |      -      | ✅  | `ST-06`, `ST-07`, `E2E-02`          |
| `docker-infrastructure` / `ポート公開と環境変数注入`                      | 環境変数注入および IConfiguration による疎通確認              |  ✅  |     ✅      |  -  | `UT-09`, `IT-07`                    |
| `docker-infrastructure` / `Webにおける .env 廃止と appsettings 一本化`    | Web アプリ起動時の .env 非参照と IConfiguration 優先順位      |  ✅  |     ✅      |  -  | `UT-10`, `IT-08`                    |
| `docker-infrastructure` / `Webにおける .env 廃止と appsettings 一本化`    | クリーンな Web 用 appsettings.json 維持と Git 除外            |  ✅  |     ✅      |  -  | `UT-07`, `IT-05`                    |
| `container-deployment-scripts` / `VIDEO_DIRS のマウント変換と入力検証`    | appsettings*.json 読み込み・CLI上書き・.env 非依存検証        |  -   |     ✅      |  -  | `IT-09`, `IT-10`, `IT-11`           |
| `application-versioning` / `セマンティックバージョニング規則と独立管理`   | Web `0.3.0` および Renamer `0.2.0` の独立バージョン反映       |  ✅  |     ✅      | ✅  | `UT-11`, `IT-12`, `E2E-04`          |

---

## 3. 単体テスト：同値分割・境界値テストケース定義

### 3.1 同値クラス・境界値一覧

| ケースID | 対象関数 / モジュール                             | 検証観点（区分）               | 入力値・前提状態                                                                        | 期待される出力・状態変化                                                               |
| :------- | :------------------------------------------------ | :----------------------------- | :-------------------------------------------------------------------------------------- | :------------------------------------------------------------------------------------- |
| `UT-01`  | `Settings.loadConfigurationWithConfig`            | `.env` / OS環境変数無視        | `.env` と OS環境変数 `VIDEO_DIR`/`OPENROUTER_API_KEY` が存在、`appsettings.json` に別値 | `.env`・OS環境変数の値は使われず `appsettings.json` 値が採用される                     |
| `UT-02`  | `Settings.resolveSavePathForEnv`                  | 正常系（Development 環境）     | `envName = "Development"`、書込可能な基底ディレクトリ                                   | `<baseDir>/appsettings.Development.json` を返す（ベース `appsettings.json` 非変更）    |
| `UT-03`  | `Settings.resolveSavePathForEnv`                  | 正常系（Production 環境）      | `envName = "Production"`、書込可能な基底ディレクトリ                                    | `<baseDir>/appsettings.Production.json` を返す                                         |
| `UT-04`  | `Settings.saveWithFallback` / `loadConfiguration` | 権限制約・AppData 整合         | 基底が ReadOnly（または未配置）、保存後に同一設定で `loadConfiguration` 実行            | `%APPDATA%/TagBasedVideoManager/appsettings.{env}.json` へ書込され再読込でも最優先取得 |
| `UT-05`  | `Settings.loadConfigurationWithConfig`            | 環境別不在・単一欠損・空ルール | `appsettings.json` のみ、両方なし、`rules: []` 指定など                                 | 環境別なし→ベース値、両方なし→既定値、明示空ルール時はゾンビ復元なし                   |
| `UT-06`  | `Settings.resolveEnvironmentName`                 | 境界値・不正値・構文エラー     | `DOTNET_ENVIRONMENT` が `""`, `"../evil"`、または壊れた JSON                            | パストラバーサル拒否フォールバック＋構文エラー時はクラッシュせず警告診断付き既定値返却 |
| `UT-07`  | 静的設定検証テスト（Web & Renamer）               | クリーンテンプレート検査       | `src/**/appsettings.json` ファイル内容                                                  | 実個人パス（`E:\e` 等）や有効 OpenRouter API キーが含まれていないこと                  |
| `UT-08`  | `State.init` / 環境表示ヘルパー                   | アクティブ環境名・保存先保持   | `ActiveEnvironment` と `SettingsSavePath` の初期モデル値                                | 環境名（`Development`/`Production`）と `appsettings.{env}.json` 名を保持               |
| `UT-09`  | `Web Config` 解決関数 (`Program` / `VideoDirs`)   | IConfiguration 階層解決        | JSON (`PORT: 5620`, `VIDEO_DIRS: "/a"`) + 環境変数 (`VIDEO_DIRS: "/b;/c"`)              | 環境変数（コンテナ注入値）が `appsettings*.json` をオーバーライドすること              |
| `UT-10`  | `Web Config` (`ProgramTests`)                     | `.env` 読み込み非存在          | 一時ディレクトリに `.env` ファイルを配置して `IConfiguration` 構築                      | `.env` の設定キーが Web 構成値に反映されないこと                                       |
| `UT-11`  | `Version` 取得関数（Web / Renamer）               | SemVer インクリメント確認      | Web アセンブリ・Renamer アセンブリ                                                      | Web = `"0.3.0"`, Renamer = `"0.2.0"`                                                   |

---

## 4. 単体テスト：複合条件デシジョンテーブル（C2 / MC/DC 網羅）

### 4.1 対象ロジック 1: `Settings.resolveSavePathForEnv` 保存先ディレクトリ決定

| ケースID | 条件1: `envName` が有効か | 条件2: ベース設定Dir存在＆書込可能 | 条件3: `explicitPathOpt` 指定あり | 期待される判定結果・保存先                                                             |
| :------- | :-----------------------: | :--------------------------------: | :-------------------------------: | :------------------------------------------------------------------------------------- |
| `DT-01`  |             T             |                 -                  |                 T                 | 明示指定パス（単体テスト分離用）へ保存                                                 |
| `DT-02`  |             T             |                 T                  |                 F                 | `<BaseOrProjectConfigDir>/appsettings.{envName}.json`                                  |
| `DT-03`  |             T             |                 F                  |                 F                 | `%APPDATA%/TagBasedVideoManager/appsettings.{envName}.json`                            |
| `DT-04`  |             F             |               T / F                |                 F                 | サニタイズ済み既定環境名（`Development`/`Production`）にフォールバックし DT-02/03 適用 |

### 4.2 対象ロジック 2: Renamer 候補エリア表示・スピナー/Empty-State/リスト排他制御

| ケースID | 条件1: `IsScanning` | 条件2: `Candidates.IsEmpty` | 条件3: `filteredCandidates.IsEmpty` | 期待される描画状態・ボタン有効性                                                            |
| :------- | :-----------------: | :-------------------------: | :---------------------------------: | :------------------------------------------------------------------------------------------ |
| `DT-05`  |          T          |              T              |                  T                  | スキャン中スピナー（「動画ファイルをスキャン中...」）。Empty/未実行案内なし。実行系Disabled |
| `DT-06`  |          T          |              F              |                  F                  | スキャン中スピナー（再スキャン中の古い候補誤表示抑止）                                      |
| `DT-07`  |          F          |              T              |                  -                  | `HasScanned` 偽→未実行プレースホルダー / `HasScanned` 真→0件 Empty State                    |
| `DT-08`  |          F          |              F              |                  T                  | フィルタ合致 0 件 Empty State                                                               |
| `DT-09`  |          F          |              F              |                  F                  | 候補カード一覧描画（個別 `IsAiProcessing` 行にインラインスピナー表示）                      |

---

## 5. 結合テスト（Integration Test）ケース定義

### 5.1 インターフェース契約・異常系注入（Fault Injection）

| ケースID | 連携対象（API / File / Script）            | シナリオ・注入する異常                                              | 期待される振る舞い（リトライ・回復・エラー伝播）                                                                |
| :------- | :----------------------------------------- | :------------------------------------------------------------------ | :-------------------------------------------------------------------------------------------------------------- |
| `IT-01`  | Renamer `Settings` ファイルシステム        | 一意一時ファイル連続保存競合検証                                    | 連続・並行呼び出し時でも GUID `.tmp` 経由の置換で `IOException` なく破損なし、エラー時は `Error (IoError)` 返却 |
| `IT-02`  | Renamer `Settings` 権限異常                | Read-Only ディレクトリ指定下で設定変更を保存                        | クラッシュせず `%APPDATA%/.../appsettings.{env}.json` にフォールバックし再読込で復元                            |
| `IT-03`  | `dotnet build`（Debug / Release）          | `appsettings.Development.json` / `Production.json` 不在状態         | MSBuild 条件付き Content によりエラー 0 でビルド・テスト起動成功                                                |
| `IT-04`  | `.gitignore` + `git check-ignore`          | `appsettings.Development.json` / `Production.json` 生成             | Web / Renamer 配下いずれも `git check-ignore` が 0（除外対象）を返す                                            |
| `IT-05`  | Git インデックス検証 (`git ls-files`)      | `.env.sample`, `appsettings.Development/Production.json`            | いずれもリポジトリ追跡対象に含まれないこと                                                                      |
| `IT-06`  | Renamer AI提案パイプライン                 | HTTP 500 / タイムアウト / `CancellationToken` 中断注入              | 全候補 `IsAiProcessing = false`, `IsRequestingAi = false` でスピナー完全解除                                    |
| `IT-07`  | Web `TestServer` / `HttpHandlers`          | `IConfiguration` + 環境変数上書きでの `POST /api/scan`              | `VIDEO_DIRS` と `THUMBNAIL_DIR` が環境変数優先で解決されること                                                  |
| `IT-08`  | Web `VersionApiTests` (`GET /api/version`) | `.env` 無効化確認＋バージョンエンドポイント疎通                     | `200 OK` かつ `"version": "0.3.0"` を返却                                                                       |
| `IT-09`  | `scripts/deploy.ps1` / `common.ps1`        | `.env` 不在＋`appsettings.Production.json` でセミコロン複数パス検証 | `.env` 非依存で `VIDEO_DIRS`, `PORT`, `DATA_DIR` が解決され、マウント変換成功                                   |
| `IT-10`  | `scripts/deploy.sh` (`bash`)               | `.env` 不在＋`appsettings.Production.json` で `;` 含むパス検証      | シェル展開・コード実行脆弱性なしで全パス文字列を安全抽出                                                        |
| `IT-11`  | `scripts/deploy.ps1` / `deploy.sh` 異常系  | 不正 JSON 構文 / `VIDEO_DIRS` 未設定時の早期失敗                    | 旧コンテナ停止やビルドを行わず、エラー原因と `appsettings.Production.json` 設定例を表示し非 0 終了              |
| `IT-12`  | Renamer `Program.fs` / アセンブリ情報      | ウィンドウタイトル・起動ログ文字列解決検証                          | `TagBasedVideoManager - AI File Renamer v0.2.0`                                                                 |

### 5.2 状態遷移網羅（0-switch: 有効遷移 / 1-switch: 無効遷移の拒否）

| ケースID | 現在の状態                                | イベント / 操作                                                 | 遷移後の状態（または拒否動作）                                              | 区分                   |
| :------- | :---------------------------------------- | :-------------------------------------------------------------- | :-------------------------------------------------------------------------- | :--------------------- |
| `ST-01`  | `Idle (IsScanning=false)`                 | `ExecuteScanAndPropose`                                         | `IsScanning = true`, `Candidates = []`, `HasScanned = true`                 | 正常遷移（0-switch）   |
| `ST-02`  | `IsScanning = true`                       | `ScanCompleted (Ok nonEmptyCandidates)`                         | `IsScanning = false`, `IsRequestingAi = true`, 先頭候補処理開始             | 正常遷移（0-switch）   |
| `ST-03`  | `Candidates ready`                        | `ExecuteRenameOnly` / `ConfirmUndo`                             | `IsRenaming = true` → 完了後 `IsRenaming = false`                           | 正常遷移（0-switch）   |
| `ST-04`  | `IsScanning = true`                       | `ScanCompleted (Error _)` / 0件完了                             | `IsScanning = false`, スピナー解除 + エラー/Empty表示                       | 異常・境界遷移         |
| `ST-05`  | `IsRequestingAi = true`                   | `CancelAiProposal` / 個別カード失敗＆`RetryCandidateAiProposal` | キャンセルで全残行解除 / 個別失敗時該当カードのみ `AiError`＋単独再試行可能 | キャンセル・個別再試行 |
| `ST-06`  | `Docker Idle (IsDockerBusy=false)`        | `ExecuteDockerAction "restart"`                                 | `IsDockerBusy = true`（ボタン非活性・スピナーON）                           | 正常遷移（0-switch）   |
| `ST-07`  | `IsDockerBusy = true`                     | `DockerActionCompleted (Error TimeoutError)`                    | `IsDockerBusy = false`, エラー表示＋再チェック実行                          | 異常遷移回復           |
| `ST-08`  | `IsScanning = true` / `IsRenaming = true` | `TargetDirectoryChanged` / `RuleSelected` / `ModelSelected`     | 状態変更を拒否（無視）し直前状態を保持、`IsScanning`/`IsRenaming` 整合維持  | 割り込み入力排他ガード |

---

## 6. E2Eテスト・画面検証ケース定義

| ケースID | ジャーニー種別                   | 操作手順（Given → When）                                                                                               | 期待される画面状態・出力エビデンス（Then）                                                          |
| :------- | :------------------------------- | :--------------------------------------------------------------------------------------------------------------------- | :-------------------------------------------------------------------------------------------------- |
| `E2E-01` | **Happy Path**（起動・設定保存） | Development 環境で Renamer ウィンドウを起動・レンダリングし、設定を変更・保存                                          | タイトル `v0.2.0`、環境バッジ `DEV`、`appsettings.Development.json` へ書込、画面 PNG キャプチャ保存 |
| `E2E-02` | **Interactive Spinners**         | Avalonia 画面（実描画 / テスト用画面生成）で `IsScanning`, `IsRequestingAi`, `IsRenaming`, `IsDockerBusy` 各状態を描画 | 各スピナーとステータスが崩れなく描画され PNG エビデンス取得                                         |
| `E2E-03` | **Negative / Recovery Path**     | 不在ディレクトリ指定でスキャン失敗させ、その後有効ディレクトリを指定して再スキャン                                     | スキャンエラー時にスピナーが消えエラーメッセージ表示→再試行で正常表示（NG時はキャプチャ出力）       |
| `E2E-04` | **Web E2E (Playwright)**         | Web アプリを起動し `/api/version`・画面ロード・`v0.3.0` 表示を確認（`.env` なし・`appsettings.json` + env構成）        | ブラウザで `v0.3.0` ヘッダーバッジ表示、動画一覧が正常動作し E2E PNG 出力                           |
