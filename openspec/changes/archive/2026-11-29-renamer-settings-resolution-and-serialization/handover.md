# 申し送り事項 (Handover): renamer-settings-resolution-and-serialization

- **変更名**: `renamer-settings-resolution-and-serialization`
- **日付**: 2026-11-28
- **ブランチ**: `fix/renamer-settings-resolution-and-serialization`
- **関連 Issue / PR**: [Issue #41](https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/41)（PR は `develop` 向けに作成）

---

## 1. 変更サマリ

AI File Renamer（`TagBasedVideoManager.Renamer`）において、有効な OpenRouter API キーを設定済みでも「APIキーが未設定またはプレースホルダー ('xxx')」エラーとなる不具合（Issue #41）を根本修正し、設定の読込・保存・UI 回復導線・仕様・診断ログ・ドキュメントを整合させた（バージョン `0.2.0` → `0.3.0`）。

### 原因と対応（不具合修正）

- **原因**:
  1. **上位レイヤー（`%APPDATA%`）によるプレースホルダー上書きと誤認識**: `Settings` の階層マージが `%APPDATA%\TagBasedVideoManager\` を最優先層として重ねていたため、残存 `"apiKey": "xxx"` がリポジトリ側の有効キーを上書きし、さらに空文字 `""` や過去バグ由来の `{ "value": "..." }` が `parsePartial` で明示的 `null`（`Some None`）と誤認されていた。
  2. **`ApiKey`（`string option`）シリアライズ不整合**: `Settings.save` に `option` 用コンバータがなく、`Some "sk-..."` が `{ "value": "..." }` オブジェクトとして保存され次回起動時に消失していた。
  3. **読込先・保存先の不一致・全上書き破壊・UI 回復導線欠如**: リポジトリ実行時の読込は `src/TagBasedVideoManager.Renamer/` を選ぶ一方、`resolveSavePath` は `bin/...` に保存し、さらに全上書きで他キーを破壊していた。また `RetryCandidateAiProposal` に統一検証ガードがなく、ルール管理モーダル内のエラーバナーが背面隠れし、設定修正後の無再起動回復手段もなかった。
- **対応**:
  1. **読込・保存共通の 2 段階ディレクトリ解決統一（`Settings.fs`）**: ① `src/TagBasedVideoManager.Renamer/`（カレントディレクトリ／`AppBaseDirectory` 起点 0〜親 8 階層＝計 9 ステップ探索）→ ② `AppDomain.CurrentDomain.BaseDirectory` へ統一し、`%APPDATA%` 参照・フォールバック保存と `CurrentDirectory` 単独探索を廃止（旧ファイル残存時は読み込まず `[WARN]` 移行案内ログのみ出力）。
  2. **`StringOptionConverter`・JSONC・`PartialApiKey` 5 状態保護・`savePatch`（`Settings.fs`）**: `ApiKey` を常に素の JSON 文字列／`null` で入出力し、JSON コメント・末尾カンマ許容および `Settings.load` 最小 JSON 補完・0 バイト空ファイル保護を実装。パス単位排他ロック（`ConcurrentDictionary` + `.tmp` アトミック書込・重複キー後勝ち正規化）により環境別 `appsettings.{Environment}.json` の `selectedModel` / `rules` のみ Patch Save し、既存他キーとベース `appsettings.json` を保護。
  3. **統一 API キー検証ガード・レース防止・診断ログ・UI 回復導線（`State.fs` / `Views.fs` / `OpenRouterClient.fs`）**: `ScanCompleted` / `RetryCandidateAiProposal` で共通検証ガードを適用し、未設定時は環境別保存先・プレースホルダー時は出典パスを表示。`State.loadConfigWithContext`（`initWithContext` / `ReloadSettings` 経路）で秘密値除外済みの `[TRACE]`・`LoadedFiles`・`ApiKeySource` 診断ログを標準エラー出力へ出力。コントロールパネル＆エラーバナーの「🔄 設定を再読込」（`ReloadSettings`：選択中 `TargetDirectory` 維持）・モーダル内エラーバナー表示（背面バナー非重複）・保存失敗時ロールバック＆`ConfirmDialog = None` 閉鎖・`AiPipelineRunId` による割り込みレース防止・401 案内文言からの `.env` 記述整理を実装。

### 主要設計判断（Why / ボツ案 / トレードオフ）

- **`CurrentDirectory` 単独探索を廃止した理由**: 作業ディレクトリ直下の `appsettings.json` を無条件に採用する旧方式では、`bin/Debug/net10.0/` や別ディレクトリを起動元にした際にビルドコピー済み設定や無関係ファイルが読込・保存先に選ばれ、リポジトリ正本 `src/TagBasedVideoManager.Renamer/` と乖離する原因になっていた。そのため、起点 `0`〜親 `8` 階層の共通探索によりリポジトリ正本を優先し、見つからない単体配置時のみ `AppDomain.CurrentDomain.BaseDirectory` を採用する 2 段階解決へ統一した。
- **本番フロー未使用の `Settings.save` を残置・修正した理由**: GUI からの設定保存は他キー保護のため `Settings.savePatch` に一本化したが、`Settings.save` は `Settings` モジュールの公開全体書き出し API として互換維持および `Settings.save` ↔ `Settings.load` 往復契約検証（`StringOptionConverter` 整合を含む）のために残置した。
- **旧 `%APPDATA%` 設定の自動移行を行わない理由**: ユーザー環境に残る古いダミー値やテスト残骸をリポジトリ管理下へ自動コピーすると誤上書き・設定破損を招くため、自動移行・自動削除をボツ案とし、参照廃止＋起動時 `[WARN]` 移行促進ログ出力に留めた。
- **GUI に API キー入力欄を追加しない理由**: 画面メモリや設定 UI 状態に平文キーを保持・露出させるリスクと画面複雑化を避けるため、外部エディタでの設定ファイル編集＋アプリ内「🔄 設定を再読込」導線を採用した（将来必要になれば独立要件として検討可能）。
- **起動時設定ロードの同期化（`SettingsLoadedWithMeta` 廃止）**: `design.md` §2.6.1 の初期案では起動時非同期ロードメッセージ `SettingsLoadedWithMeta` を想定していたが、二重ロードによる一時的な未設定表示チラつきを防ぐため `State.initWithContext` / `State.updateWithContext` での同期ロードへ統一し、レガシー互換用 `SettingsLoaded` メッセージは互換維持のため残置した。
- **`JsonNode` Patch Save と UI スレッド同期 I/O のトレードオフ**: 他キー（`apiKey`, `targetDirectory`, 未知キー）と順序を非破壊で保つ一方、`System.Text.Json.Nodes.JsonNode` 再シリアライズ仕様上、GUI 経由で `appsettings.{Environment}.json` を Patch Save したファイルでは JSONC コメント（`//`）および末尾カンマは保持されない。また二重ロード・状態競合回避を優先し、設定読込・保存はローカル SSD を前提に Elmish `update` 上の同期実行（`parsePartial` 3 回×10ms / `savePatch` 5 回×10ms の同期 `Thread.Sleep` リトライ付き）としている。

---

## 2. 品質・テストエビデンス

- **ビルド / 静的チェック**:
  - `dotnet build test/TagBasedVideoManager.Renamer.Tests/TagBasedVideoManager.Renamer.Tests.fsproj --no-restore` → エラー 0 件、F# 警告 0 件（既存パッケージ `NU1903` 2 件のみ）
  - タブ文字・行末空白 0 件、`src/TagBasedVideoManager.Renamer/appsettings*.json` 無変更、`npx prettier --write`・`dotnet format whitespace --verify-no-changes --no-restore` 確認済み
- **単体・結合・E2E テスト（Renamer）**:
  - 実行コマンド: `dotnet exec test/TagBasedVideoManager.Renamer.Tests/bin/Debug/net10.0/TagBasedVideoManager.Renamer.Tests.dll --filter-not-class "*Deploy*" --filter-not-class "*BugReproductionTests*" --filter-not-class "*RunArgumentsScriptTests*" --filter-not-class "*ContainerMountPathTests*"`
  - 除外クラス（いずれも変更範囲外の `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs` 所属スクリプト検証群）の内訳・理由:
    - `*Deploy*` / `*BugReproductionTests*`: Windows 上で既存 `scripts/deploy.sh` が `i/lf w/crlf` となっていることに起因（未変更）
    - `*RunArgumentsScriptTests*`: ローカル作業ツリー上の変更範囲外未コミット差分 `M scripts/common.ps1`（`0.0.0.0:${Port}:5620`）に起因
    - `*ContainerMountPathTests*`: 2026-11-28 時点のホスト `wsl.exe` 15 秒タイムアウトに起因（直前ラン `TestRun_2026-11-28_09_00_00` では 463 件中 `UT-MOUNT-03` / `UT-MOUNT-04` の 2 件のみ `wsl.exe` タイムアウト失敗、他 461 件はすべて Pass）
  - 結果（`TestRun_2026-11-28_09_35_00`）: **合計 456 件 Pass（失敗 0 / スキップ 0）**
- **Web アプリ側非破壊確認**:
  - `dotnet exec test/TagBasedVideoManager.Tests/bin/Debug/net10.0/TagBasedVideoManager.Tests.dll --filter-not-class "*E2E*"` → **合計 157 件 Pass（失敗 0 / スキップ 0）**（`src/TagBasedVideoManager/` 無変更のため再ビルド前の確定値、変更後も `TagBasedVideoManager.fsproj` 無変更）
- **カバレッジ（`TestRun_2026-11-28_09_35_00`）**:
  - **変更差分合計（`f630b31..HEAD` ＋作業ツリー確定分）**: **DiffC0 = 96.00%** (`1153/1201` >= 90%)、**DiffC1 = 88.59%** (`357/403` >= 85%)
    - `Settings.fs`: DiffC0 **98.27%** (`455/463`) / DiffC1 **89.41%** (`228/255`)（`Summary.txt` クラス `Settings` Line `97.7%`・Cobertura ファイル集計 Branch `89.7%`）
    - `State.fs`: DiffC0 **95.21%** (`318/334`) / DiffC1 **87.93%** (`102/116`)（`Summary.txt` Line `78.3%`、`emitConfigDiagnostics` L126/L128 各 `100% (2/2)`）
    - `Views.fs`: DiffC0 **93.98%** (`375/399`) / DiffC1 **83.33%** (`25/30`)（`Summary.txt` Line `95.5%`）
    - `OpenRouterClient.fs`: DiffC0 **100%** (`5/5`) / DiffC1 **100%** (`2/2`)
  - **アセンブリ全体（`Summary.txt`）**: Line (C0) = **89.4%** (`4532/5064` >= 80%)、Branch (C1) = **72.1%** (`1117/1548`)（Coverlet コンソール表記: Line `89.7%` / Branch `69.38%`。全体 C1 が 80% 未満の要因は本変更対象外の未変更レガシー領域起因）
  - **C2 デシジョンテーブル・シナリオ・Adversarial QA**: `test_plan.md` §4 全 30 行（`DT-MERGE-01..11`, `DT-KEY-01..07`, `DT-DIR-01..06`, `DT-PATCH-01..06`）および §2 全 11 シナリオ 1:1 突合 Pass、Adversarial QA 破壊テスト **19/19 Pass**
- **エビデンス出力先**:
  - `test/TestResults/TestRun_2026-11-28_09_35_00/TestRun_2026-11-28_09_35_00_net10.0.trx`
  - `test/TestResults/TestRun_2026-11-28_09_35_00/CoverageReport/index.html`（サマリ: `Summary.txt`）
  - UI 実画面キャプチャ: `test/TestResults/E2E_Settings_*.png`（10 枚：`HappyPath`, `ZeroState_MissingKey`, `Placeholder_Error`, `Placeholder_Reloaded`, `Placeholder_Recovered`, `ModalSaveError`, `ModalSaveRecovered`, `LongPathBanner_800`, `LongPathBanner_1100`, `StartupWarningBanner`）

---

## 3. 保留指摘・技術的負債

| 指摘元（フェーズ/タスク/役割）                   | 内容                                                                                                                                                                                                                                                                                                                                                                                                                                                         | 保留理由 / 対応状況                                                                       | 推奨対応時期                |
| ------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------- | --------------------------- |
| Propose / Cycle 3 / `user-reviewer`              | `mockup.html` L351 モーダル内エラーバナー `✕` ボタンがモック上でモーダルごと閉じる遷移になっている                                                                                                                                                                                                                                                                                                                                                           | 実装（`State.fs` `DismissError` / `Views.fs` L2646）はバナーのみ閉鎖で仕様通り正常動作    | モック再利用時              |
| Propose Cycle 4 `se` / Apply Task 2.3 `pg`       | `design.md` §2.6(.1) の `SettingsLoadedWithMeta` 記述と実装（同期 `initWithContext`/`updateWithContext`＋レガシー `SettingsLoaded` 残置）の微差。なお `CancelAiProposal` の `AiPipelineRunId = None` 自体は実装・検証済み                                                                                                                                                                                                                                    | 挙動・安全性上の改善（同期化）によるもので仕様変更はなく、仕様書本文とも整合済みのため    | 次回ドキュメント整理時      |
| Apply / Task 3.1 / `pg-reviewer`                 | `Views.fs` Row 1 折り返し時の縦区切り線孤立リスク、`MinWidth = 450.0` 付近の右端余白、フィルタ `ComboBox` `ItemsSource` 更新時再同期配慮、閾値 `TextBox` 同値入力抑止ガード                                                                                                                                                                                                                                                                                  | 800px 以上で全要素収容・視覚検証 Pass 済みであり、`MinWidth = 450.0` 非クラッシュ確認済み | 次回 Renamer UI 改善時      |
| Apply Task 3.2 `pg` / Archive Cycle 1 `user`     | `README.md` L55 (`ENV: Development`)・L323 (JSONC コメント非保持明記)・L342 (`appsettings.json` 実ファイル一致) の整合、および `VIDEO_DIRS` 注意書き配置整理・401 文言のバッジ参照補足（Suggestion）                                                                                                                                                                                                                                                         | L55 / L323 / L342 の Major 3 点は **Archive Cycle 1 で修正済み**。Suggestion 2 点のみ保留 | 次回 README / UI 文言整理時 |
| Apply Task 4.1 / Archive Cycle 1-2 `pg-reviewer` | `Settings.fs` L640-715 `loadConfigurationWithMetadataWithContext` 3 分岐統合（約 37% 削減可）、`State.fs` 単一箇所ラッパー `saveSettingsPatchWithFeedback` / `emitConfigDiagnostics`（L125-143、`loadConfigWithContext` 統合＋`isNull` 防御で約 31% 削減可）および旧互換 `Msg`（`SettingsLoaded`, `AiProposeCompleted`）整理                                                                                                                                 | 全機能・テスト Pass 後に大規模リファクタを行わず安全性を維持するため                      | 次回リファクタタスク        |
| Apply Task 4.1 / Archive Cycle 1 `pg-reviewer`   | `Views.fs` L205 `ruleComboSyncAttr` の `comparer` `ValueSome (=)` 明示化（実装は `ValueNone`、`reviews.md` L1989 表記微差）、`Settings.fs` L546 `resolveConfigDirectoryWithOrigins` 不正パス文字（`\0` 等）ガード                                                                                                                                                                                                                                            | FuncUI 既定等値比較が動作し本番呼出元も有効パスであるため実害なし                         | 次回保守作業時              |
| Archive / Cycle 1 / `pg-reviewer`, `se-reviewer` | 後方互換・暫定コードの残存: `Settings.loadConfigurationWithMetadata` `envPathOpt` 二重用途（L717-739）、`defaultConfigurationName` 静的キャッシュ（L503）、`patchFileLocks` 無解放蓄積（L106）、UI 同期 `Thread.Sleep`・プロセス間非ロック（後勝ち）、未使用互換関数 `reorderRules`（L838-851）、`State.fs` コンテナ操作の `Directory.GetCurrentDirectory()` 残存（L404, L792, L828, L849, L860）、`SettingsTests.fs` L102-149 `transientBackups` 暫定コード | 既存テスト互換・二重ロード回避を優先し、挙動の安全性を維持したまま段階整理へ送るため      | 次回 Renamer 基盤整理時     |
| Apply / Task 4.1 / `pg-reviewer`, `qa-reviewer`  | アセンブリ全体 Branch (C1) カバレッジが `72.1% (1117/1548)`（本変更差分 C1 は `88.59%` 達成、未達成要因は本変更外のレガシー領域 `Program`/`TitleFormatter`/`FileScanner`/`WebSearchClient` 等）                                                                                                                                                                                                                                                              | 変更範囲外の既存コード群であり、本変更差分自体は C0 96.00% / C1 88.59% を達成しているため | レガシーテスト拡充タスク    |
| Apply / Task 4.1 / `qa-reviewer`                 | `E2E-09`（`ErrorMessage = Some null`）での可視バナー非描画（`📋 コピー` 非描画等）までの追加アサート余地                                                                                                                                                                                                                                                                                                                                                     | 実 `HostWindow` 描画で NRE 不発は確実検証済みであり必須修正ではないため                   | 次回 E2E テスト拡充時       |

---

## 4. 将来の改善・リファクタ候補

1. **将来の開発者が踏み抜きやすい設計上の前提・契約（要注意）**:
   - **テスト隔離契約**: GUI 設定保存を伴う `State.update` テストでは必ず `Model.SettingsSavePath` に一時ファイルパスを注入し、起動・再読込経路テストでは `State.initWithContext (Some testCtx)` / `State.updateWithContext (Some testCtx)` を使用すること。未注入のまま実行すると、`src/TagBasedVideoManager.Renamer/appsettings.{Environment}.json` 実ファイルが書き換わる。
   - **`RenamerSettings` フィールド追加時の 5 箇所非コンパイル保護結合**: 設定項目を増やす際、`PartialSettings`・`parsePartial`（なお `getProp` は `camelCase` と先頭大文字 `PascalCase` の 2 パターンのみ照合）・`mergePartialsChain`・`SettingsPatch`・`savePatch` を連動更新しないと、コンパイルエラーにならず読込／Patch Save がサイレント欠損する。また `loadConfigurationWithMetadataWithContext` は `jsonPathOpt` で存在しないパスを渡されてもエラーを返さず通常ディレクトリ探索へフォールバックする点に注意すること。
   - **`envPathOpt` 指定による 2 段階解決＆`%APPDATA%` 検知スキップ罠**: `Settings.loadConfigurationWithMetadata` の第 3 引数 `envPathOpt` にパス（例: `Some "C:\\work\\.env"`）を渡すと内部で `explicitDirOpt = Some "C:\\work"` へ変換され `isNormalMode = false` となるため、2 段階ディレクトリ解決と旧 `%APPDATA%` 移行検知が黙ってスキップされる。
   - **`Model.ErrorMessage` の情報通知相乗り＆先頭 `"💡"` 判定契約**: `State.fs` L530 の `ReloadSettings` 正常通知（`"💡 設定ファイルを再読込しました (...)"`）はエラー用フィールド `Model.ErrorMessage` を兼用しており、`Views.fs` L2592 の `msg.StartsWith("💡")` だけで青情報バナーと赤エラーバナーを切り替えている。先頭文字 `"💡"` を変更すると成功通知が赤エラー表示へ変わる。
   - **`AiPipelineRunId` レースガード契約と旧 `Msg` 破壊モード**: 新しい非同期 AI 関連メッセージを追加する場合は必ず `Guid` (`runId`) を持たせ、`updateWithContext` 先頭で `model.AiPipelineRunId <> Some runId` を弾くこと。互換残置中の `SettingsLoaded` を発火すると `ActiveEnvironment`/`SettingsSavePath`/`ApiKeySourcePath` が未更新になり、`AiProposeCompleted` を発火すると `AiPipelineRunId` ガードと `AiCancellationCts` 解除をバイパスしてスピナー競合が起きる。
   - **`ReloadSettings` における `TargetDirectory` と `CurrentThreshold` の非対称性**: 再読込時（`State.fs` L514-543）、`TargetDirectory` はファイル側が空なら画面選択値を維持するが、`CurrentThreshold`（画面一時変更値含む）はファイル側の `PathLengthThreshold`（未指定時は 240）で常に上書きリセットされる（`State.fs` L537）。
   - **`Views.fs` `ConditionalWeakTable` / 複数モデル拡張制約**: ルール選択 `ComboBox` とモデル選択 `ComboBox` は Avalonia の同期 `SelectedIndexChanged` 発火を抑止するため `IsSyncing` 排他と `shouldDispatchModelSelection` に依存している。将来 `availableModels` を複数選択肢へ拡張する場合は `ruleComboSyncAttr` 同様の `IsSyncing` 排他を導入しないと `ItemsSource` 更新時に意図しない `ModelSelected`（＝`savePatch`）が発火する。
   - **静的キャッシュ・ハードコード・10 倍規模時のボトルネック・プロセス間後勝ち**: `Settings.defaultConfigurationName`（L503）は初回ロード時固定（`resolveSavePath` 等と非対称）、Stage 1 探索は `"src/TagBasedVideoManager.Renamer"` ハードコード依存、`patchFileLocks` はプロセス内排他のため外部エディタ同時編集は後勝ち、長パス候補が数千件規模になると `createAsyncPipelineCmd` の 1 件ずつ直列実行（`ddgs`＋OpenRouter＋300ms）および `CandidateAiProposed` 毎全件 `FileScanner.sortCandidates`（`State.fs` L646）が遅延ボトルネックになる。
2. **リファクタ・テスト改善候補**:
   - `Settings.loadConfigurationWithMetadataWithContext`（L640-715）の 3 分岐出力構築一本化、`State.fs` `emitConfigDiagnostics` の `loadConfigWithContext` 統合＋`isNull` 防御、`SettingsLoaded` / `AiProposeCompleted` / コンテナ操作 `GetCurrentDirectory()` 整理、`SettingsTests.fs` `transientBackups` 除去。
   - 未変更レガシーモジュール群（`FileScanner`, `WebSearchClient`, `OpenRouterClient`, `TitleFormatter`, `State`/`Views` 既存部）の単体テスト追加によるアセンブリ全体 C1 ≥ 80% 達成。

---

## 5. 運用上の注意事項

### トラブルシューティング手順

1. **参照中環境と設定ファイルの特定**: アプリ上部の環境バッジ（`ENV: Development` 等）および保存先バッジ（`💾 appsettings.Development.json` 等）、エラーバナーに表示される出典ファイルフルパスを確認する。
2. **詳細診断ログの確認**: `State.initWithContext` / `ReloadSettings` および `Settings` 読込時に標準エラー出力へ出力される `[TRACE]` 行（採用ディレクトリ、探索ステップ、確定した `Env` / `SavePath` / `LoadedFiles` / `ApiKeySource` ※秘密値除外）および `[WARN]` 行（JSON 構文エラー行/列、旧 `%APPDATA%` 残存移行案内、旧 `{ "value": ... }` 形式・不正 `rules`・`.env` 検出警告）を確認する。
3. **API キー修復と無再起動反映**: 案内された `appsettings.{Environment}.json` へ `"apiKey": "sk-or-v1-..."`（文字列形式・最小 JSON 可）を記載・保存し、アプリ内の「🔄 設定を再読込」ボタンをクリックする（選択中フォルダを保持したまま再起動なしで復旧する）。

### 移行・作業ツリー・ロールバック手順

- **旧 `%APPDATA%\TagBasedVideoManager\` ファイル**: 本バージョンから参照されない（削除も行わない）。不要であれば運用者が手動で退避・削除してよい。
- **別件の未コミット変更注意（ローカル環境固有）**: 本ブランチの作業ツリー外ファイルとして `scripts/common.ps1` のローカル未ステージ差分が存在する（PR コミットには含めていない）。また Windows 環境では `scripts/deploy.sh` の改行コード（CRLF）や `wsl.exe` 応答状態により `DeployScriptTests.fs` の一部スクリプトテストが失敗するため、スクリプト検証は CI/Linux 環境等で別途確認すること。
- **ロールバック手順**: DB スキーマ変更・Web アプリ本体のコード変更・既存設定ファイルの自動削除を含まないため、Merge 後に致命的不具合が判明した場合は **GitHub PR の Merge commit に対する `git revert -m 1 <merge-commit-sha>`（または関連コミットの revert）のみ** で直前の安定状態へ安全に巻き戻せる（※前バージョンへ戻した場合、再び `%APPDATA%` 参照や旧 `ApiKey` シリアライズ不具合が生じる点には留意）。
