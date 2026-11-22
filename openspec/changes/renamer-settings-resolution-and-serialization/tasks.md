# Tasks: renamer-settings-resolution-and-serialization

- **Change Name**: `renamer-settings-resolution-and-serialization`
- **Scale Level**: `L2`（通常変更・`fix` 不具合修正プロトコル適用）
- **Related Issue**: [#41](https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/41)

---

## Phase 1: 既存テスト実ファイル隔離と Root Cause 先行再現テスト (QA Tester — Red)

- [x] 1.1 既存テストの実設定ファイル隔離・旧 `%APPDATA%` テスト整理（実ファイル破壊防止のため最優先実施）
  - `test/TagBasedVideoManager.Renamer.Tests/StateTests.fs` の `ModelSelected`・ルール追加/編集/削除/並び替えテスト（L233, L369, L376, L613, L631, L652, L1203 等）に一時 `SettingsSavePath` 注入および**テスト実行前後に実設定ファイル `src/TagBasedVideoManager.Renamer/appsettings.{env}.json` の内容を保護・検証する安全ラッパー**を適用する。また `StateTests.fs` L1140 の `RetryCandidateAiProposal` 未設定エラー検証を統一ガード導入（Phase 2.2〜2.3）に備えて一時隔離・整理する。
  - `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs` の `%APPDATA%` 前提テスト（`UT-04`, `IT-02`, `%APPDATA%` 最優先上書き回帰テスト）および実ファイル `appsettings.Development.json` 固定値依存テスト（L279, L396）を、一時ディレクトリフィクスチャによるテストへ切り替える。
  - 隔離後、`dotnet test test/TagBasedVideoManager.Renamer.Tests/TagBasedVideoManager.Renamer.Tests.fsproj` が全件 Pass し、かつリポジトリ内の実 `appsettings.Development.json` に一切変更が走らないことを確認する。

- [x] 1.2 不具合再現テスト（Root Cause Reproduction）と `Settings.fs` 先行単体・結合テストの作成・Red 確認（`qa-tester`）
  - **コンパイル通過＋実行時 Red 手順**: 先行テストが参照する公開型・関数シグネチャ（`ApiKeyValidationResult`, `ConfigLoadContext`, `LoadedConfiguration`, `SettingsPatch`, `validateApiKey`, `resolveConfigDirectoryWithOrigins`, `loadConfigurationWithMetadataWithContext`, `loadConfigurationWithMetadata`, `savePatch`）は、`src/TagBasedVideoManager.Renamer/Settings.fs` へ最小限の `raise (NotImplementedException())` スタブのみを配置してビルドを通した上で、`dotnet test --filter "FullyQualifiedName~SettingsTests"` で意図通り実行時 Red（失敗）となることを確認・記録する（本実装ロジックは書かず、この段階では `StateTests` は未変更のまま）。
  - **Root Cause 再現（`UT-RED-01`〜`03`）**:
    - `UT-RED-01`: `loadConfigurationWithMetadataWithContext` に一時 `AppDataDirectory`（`"apiKey": "xxx"`）と一時リポジトリ（有効 `apiKey`）を注入し、`%APPDATA%` が無視され（移行案内ログのみ）有効キーと出典パスが返る失敗テスト。
    - `UT-RED-02`: `Settings.save` で `ApiKey = Some "sk-or-v1-..."` を保存した際、素の文字列 `"apiKey": "sk-or-v1-..."` が出力され `Settings.load` / `Settings.loadConfiguration` で往復できる失敗テスト。
    - `UT-RED-03`: `resolveConfigDirectoryWithOrigins` / `resolveSavePath` で `bin/...` にコピーが存在しても `src/TagBasedVideoManager.Renamer/` が共通ディレクトリとして選ばれる失敗テスト。
  - **単体・結合先行テスト**:
    - `StringOptionConverter`（直接シリアライズ/デシリアライズ含む）& `Settings.load` 最小 JSON 補完 & 0 バイト空ファイル（`UT-CONV-01`〜`10`）。
    - `validateApiKey` 境界値・デシジョンテーブル（`UT-KEY-01`〜`22`, `DT-KEY-01`〜`07`: 0/1/9/10/11 文字、`"sk-or-v1-xxxxxxxxxxxxxxxx"`, `"sk-or-v1-..."` 含む）。
    - `resolveConfigDirectoryWithOrigins` 2 段階解決・step 0..8（親 8 階層上まで）・step 9 超過・探索トレース（`UT-DIR-01`〜`11`, `DT-DIR-01`〜`06`）。
    - `mergePartialsChain` `PartialApiKey` 5 状態・`IsBaseLayer`（`appsettings.json` `"apiKey": null` 誤案内防止）・空文字抑止・旧 `{ "value": ... }` 修正案内・`rules` パース警告・JSONC 読込（`UT-MERGE-01`〜`06`, `DT-MERGE-01`〜`11`, `UT-ERR-01`〜`03`）。
    - `savePatch`・並行競合（双方の最新キー＋既存キー保持アサーション含む）・ReadOnly/破損 JSON/0 バイト保護（`UT-PATCH-01`〜`10`, `DT-PATCH-01`〜`06`, `IT-IO-01`, `IT-CONF-01`, `IT-FAULT-01`〜`04`）。

---

## Phase 2: Settings.fs 実装と State.fs / Views.fs TDD サイクル (QA Tester → PG Implementer)

- [x] 2.1 `Settings.fs` の実装および `State.fs` 保存先隔離の前倒し適用（`pg-implementer` — Green & Refactor）
  - `StringOptionConverter`、JSONC コメント/末尾カンマ許容、`Settings.load` の `parsePartial` + 既定値補完（0 バイト空ファイル保護含む）を実装する。
  - `ApiKeyValidationResult`、`isPlaceholderApiKey`、`validateApiKey` を実装する。
  - `resolveConfigDirectoryWithOrigins`（起点 `0`〜親 `8` 階層＝計 9 ステップ探索＋トレース出力）、`resolveConfigDirectory`、`resolveSavePath` を実装し、`%APPDATA%` 参照・フォールバック保存および `CurrentDirectory` 単独探索を完全削除する。
  - `PartialApiKey` 5 状態・`IsBaseLayer` 判定・`LoadedConfiguration`（`EnvironmentName`, `SavePath` 含む）・`loadConfigurationWithMetadataWithContext` / `loadConfigurationWithMetadata`・旧 `%APPDATA%` 残存移行通知・`rules` パースエラー警告伝播を実装する。
  - パス単位 `ConcurrentDictionary<string, obj>` ロック付き `savePatch`（読込・移動各最大 5 回 × 10ms リトライ、UTF-8 日本語非エスケープ、破損/0 バイト JSON 中止、失敗段階・行/列位置・試行回数付きエラー通知）を実装する。
  - **実ファイル保護のための安全前倒し**: `State.fs` の `saveSettingsWithFeedback` が空でない `model.SettingsSavePath` を優先使用する 2 行の保護修正を同時に適用し、以降の全テスト実行で開発者の実 `appsettings.Development.json` が絶対に書き換わらない状態にする。
  - `dotnet test --filter "FullyQualifiedName~SettingsTests"` が全件 Green になることを確認する。

- [ ] 2.2 `State.fs` / `Views.fs` 向け先行失敗テストの作成・Red 確認（`qa-tester` — `StateTests.fs`, `RenameIntegrationE2ETests.fs`）
  - **コンパイル整合手順**: `State.fs` / `Views.fs` へ最小シグネチャスタブ（`ApiKeySourcePath`, `AiPipelineRunId`, `ReloadSettings`, `SettingsLoadedWithMeta`, `CandidateAiProcessing of Guid * string`, `CandidateAiProposed of Guid * RenameProposal`, `AllAiProposalsCompleted of Guid`, `State.initWithContext`, `State.updateWithContext`, `Views.shouldDispatchModelSelection`）を配置し、既存の `StateTests.fs` における `CandidateAiProcessing` / `CandidateAiProposed` / `AllAiProposalsCompleted` 呼出箇所を `runId` 対応に更新してビルドを通した上で Red を確認する。
  - `UT-RED-04`（`RetryCandidateAiProposal` のプレースホルダー検証欠落再現）。
  - `ST-INIT-01`〜`02`（`initWithContext` 経由での起動時二重ロード廃止、`Warnings` の `ErrorMessage` 画面バナー反映）。
  - `ST-SCAN-01`〜`05` / `ST-RETRY-01`〜`05`（統一ガード・環境別保存先案内・最小 JSON 例・再読込案内、**`IsRequestingAi = true` 中の `ExecuteScanAndPropose` 割り込み後に全非 AI 分岐で `IsRequestingAi = false` / `AiPipelineRunId = None` へリセットされること**、**`RetryCandidateAiProposal` 割り込み時の先行 CTS キャンセル/破棄＋新 CTS 保持＋`CandidateAiProcessing`/`CandidateAiProposed`/`AllAiProposalsCompleted` の `oldRunId` 無視検証**）。
  - `ST-RELOAD-01`〜`03`（`updateWithContext` 経由の `ReloadSettings` 同期再読込による再起動なし回復、**選択済み `TargetDirectory` の保持**、ビジー排他、削除された `SelectedRuleId` の安全フォールバック）。
  - `ST-MODEL-01`〜`03` / `ST-RULE-01`〜`05`（Patch Save 呼び出し、保存失敗時の**`Settings.SelectedModel` / `Settings.Rules` / `SelectedRuleId` ロールバック、`DeleteRule` 時の `ConfirmDialog = None` 閉鎖、`EditingRule` 保持**・段階別エラー通知）。
  - `E2E-01`〜`E2E-07`（`initWithContext` / `updateWithContext` を用いた無汚染テスト、実リポジトリ構成 `appsettings.json` `"apiKey": null` での `E2E-02`、モーダル開時背面バナー非表示＆モーダル内バナー検証の `E2E-04`、`shouldDispatchModelSelection` 同値ガード検証の `E2E-07` 含む）を追加し、実行時 Red を確認する。

- [ ] 2.3 `State.fs`・`Views.fs`・`OpenRouterClient.fs` の実装（`pg-implementer` — Green & Refactor）
  - `State.fs`: `Model.ApiKeySourcePath` / `AiPipelineRunId` 追加、`initWithContext` / `updateWithContext`（`init ()` / `update` は `None` ラッパー）による同期メタデータ読込＆起動時 `Warnings` の `ErrorMessage` 反映（冗長な起動時 `loadSettingsCmd` 廃止）、`ReloadSettings`（同期再読込・`TargetDirectory` 維持・ビジー排他）、`saveSettingsPatchWithFeedback`（失敗時ロールバック・`SelectedRuleId` 保護・`ConfirmDialog = None` 閉鎖）、共通 CTS/runId 発行＆`ExecuteScanAndPropose` / `ScanCompleted` 全非 AI 分岐での `IsRequestingAi = false`・`AiPipelineRunId = None` リセット＆全通知 `runId` ガード、`validateApiKeyForAi`（`MissingApiKey` 時は常に環境別 `SettingsSavePath` 案内）を実装。
  - `Views.fs`（`mockup.html` 忠実再現）: メイン側 `if not model.IsRuleManagerOpen then errorBanner model dispatch`（背面バナー重複抑止）、`ruleManagerModal` 上部への `errorBanner` 配置、`errorBanner` の 3 ボタン統一（`🔄 設定を再読込`, `📋 コピー`, `✕`）、コントロールパネル常設の「🔄 設定を再読込」ボタン（ビジー時非活性）、`ComboBox.onSelectedIndexChanged` 内で同一クロージャ内の `availableModels |> List.tryItem idx` を渡す同値チェックガード `shouldDispatchModelSelection` を実装。
  - `OpenRouterClient.fs`: L124, L350 の 401 認証エラー案内から廃止済み `.env` 記述を整理し `appsettings.{Environment}.json` 案内に統一。
  - `StateTests` および `RenameIntegrationE2ETests` が全件 Green になることを確認する。

---

## Phase 3: E2E 視覚エビデンス検証・ドキュメント・バージョン更新

- [ ] 3.1 GUI E2E 実画面キャプチャ検証と `mockup.html` 突合
  - `E2E-01`〜`E2E-07` を実行し、`test/TestResults/` へ PNG スクリーンショット（`E2E_Settings_HappyPath.png`, `E2E_Settings_ZeroState_MissingKey.png`, `E2E_Settings_Placeholder_Error.png`, `E2E_Settings_Placeholder_Recovered.png`, `E2E_Settings_ModalSaveError.png`, `E2E_Settings_ModalSaveRecovered.png`, `E2E_Settings_LongPathBanner_800.png`, `E2E_Settings_LongPathBanner_1100.png`, `E2E_Settings_StartupWarningBanner.png`）を出力する。
  - `E2E-05` について、ビジュアルツリー上の `errorBanner`・各ボタン・環境バッジの `Bounds` がウィンドウ幅内に収まり相互重なり・はみ出しがないことをアサートで機械検証し、出力 PNG を目視確認して `mockup.html` と整合していることを確認する。

- [ ] 3.2 ドキュメント（`README.md`）およびバージョン（`.fsproj`）の整合更新
  - `README.md` から `%APPDATA%` に関する記述を削除し、2 段階ディレクトリ解決、Patch Save 仕様、最小 JSON 書式例（`{ "apiKey": "sk-or-v1-..." }`）、正しい camelCase 設定キー名（`targetDirectory`, `pathLengthThreshold`, `selectedModel`, `apiKey`, `rules`）へ更新する。
  - `src/TagBasedVideoManager.Renamer/TagBasedVideoManager.Renamer.fsproj` の `<Version>` を `0.2.0` → `0.3.0`（L2 Minor インクリメント）へ更新する。

---

## Phase 4: 全テスト・カバレッジ・フォーマット・レビュー・Gate 2 最終検証

- [ ] 4.1 全体品質検証・カバレッジ計測・レビュー・Gate 2 通過
  - 自動フォーマット実行（`dotnet format whitespace`, `npx prettier --write "README.md" "openspec/**/*.md"`）。
  - `pwsh ./test/TagBasedVideoManager.Renamer.Tests/run_tests_with_coverage.ps1` を実行して `TagBasedVideoManager.Renamer.Tests` 全件 Pass・Skip 0 件、および C0（全体80%+/差分90%+）・C1（全体80%+/差分85%+）達成を確認し、`test_plan.md` §4 の C2 デシジョンテーブル全行が対応単体テストで実行・Pass していることを突合証跡化する。また Web アプリ側の非破壊確認として `dotnet test test/TagBasedVideoManager.Tests/TagBasedVideoManager.Tests.fsproj --filter "Category!=E2E"`（Docker/Playwright 非依存テスト、環境利用可能時は全件）を実行する。
  - `pg-reviewer` / `qa-reviewer`（Adversarial QA 破壊テスト実行含む）による独立批判的レビューを実施し、`reviews.md` に記録して `LGTM: true` を取得する。
  - `pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply -ChangeName "renamer-settings-resolution-and-serialization"` を実行し、`PASSED` を確認する。
