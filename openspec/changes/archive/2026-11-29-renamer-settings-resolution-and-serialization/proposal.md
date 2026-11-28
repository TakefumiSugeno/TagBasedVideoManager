# Proposal: AI File Renamer の設定読み込み・保存における API キー上書き・シリアライズ・保存先不一致の修正

## Metadata

- **Change Name**: `renamer-settings-resolution-and-serialization`
- **Scale Level**: `L2`（通常変更: 不具合修正＋仕様・UI 回復導線改善あり）
- **Type**: `fix` / `feat`（主たる目的は不具合修正のため `fix` プロトコル適用）
- **Related Issue**: [#41](https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/41)
- **Target Applications Version**:
  - デスクトップアプリ (`TagBasedVideoManager.Renamer`): `0.2.0` → **`0.3.0`**（L2 Minor インクリメント）
  - Web アプリ (`TagBasedVideoManager`): 変更なし（現状維持）
- **UI/UX 変更**: あり（`openspec/changes/renamer-settings-resolution-and-serialization/mockup.html` 作成：エラーバナーおよびメインコントロールパネルへの「🔄 設定を再読込」導線追加、モーダル表示時背面バナー抑止とルール管理モーダル内エラーバナー表示、起動時設定警告バナー表示）

---

## Why

AI File Renamer（`TagBasedVideoManager.Renamer`）において、有効な OpenRouter API キーを設定ファイルへ記入しているにもかかわらず「APIキーが未設定またはプレースホルダー ('xxx')」エラーになる不具合が報告された。調査の結果、設定読み込み・保存・UI 回復導線（`Settings.fs` / `State.fs` / `Views.fs`）に以下の根本原因（Root Cause）が存在する。

1. **上位レイヤー（`%APPDATA%`）のプレースホルダー `'xxx'` による有効キー上書きと出典不明エラー**:
   - `Settings.loadConfigurationWithConfig` / `mergePartialsChain` がプロジェクト側設定を読み込んだ後、`%APPDATA%\TagBasedVideoManager\` 配下を最優先オーバーライド層として重ねるため、残存 `"apiKey": "xxx"` がリポジトリ側の有効キーを上書きする。
   - また、過去のバグで `"apiKey": { "value": "..." }` に破損したファイルや空文字 `""` が `parsePartial` で明示的 `null`（`Some None`）と誤認され、下位の有効キーまで警告なしに消去する。
   - さらに `State.fs` のエラー案内が固定文字列で出典ファイルパスを明示せず、`RetryCandidateAiProposal` には検証ガード自体が欠落し、設定修正後にアプリを再起動せず回復する導線も存在しない。
2. **`Settings.save` における `ApiKey`（`string option`）の JSON シリアライズ不具合**:
   - `Settings.save` が `option` 用コンバータ未登録のままシリアライズするため、`Some "sk-..."` が `{ "value": "sk-..." }` オブジェクトとして出力され、次回起動時の `parsePartial` で `None` 扱いになり消失する。
3. **開発環境での「読込先」と「保存先」の不一致・全上書き破壊・UI 視認性欠陥**:
   - 読み込みは `src/TagBasedVideoManager.Renamer/` を優先するが、保存（`resolveSavePath`）は `bin/...` に保存するため次回起動へ反映されず、リビルドで消失する。また設定保存が全上書きのため既存キーを破壊し、ルール管理モーダル内で保存失敗してもメイン側の `errorBanner` がモーダル背後に隠れてユーザーに見えない。

---

## What Changes

- **読み込み・書き込み共通の 2 段階ディレクトリ解決統一（`%APPDATA%`・`CurrentDirectory` 単独探索の廃止）**:
  1. `src/TagBasedVideoManager.Renamer/`（カレントディレクトリまたは `AppDomain.CurrentDomain.BaseDirectory` から起点 `0`〜親 `8` 階層上＝計 `9` ステップ探索）
  2. `AppDomain.CurrentDomain.BaseDirectory`（リポジトリ外の publish / 単体配置時）
  - `%APPDATA%\TagBasedVideoManager\` 参照・フォールバック保存を完全廃止（旧ファイル残存時は読み込まず移行促進ログのみ出力）。書込不可時は保存先フルパスと失敗段階・試行回数・理由をエラー通知する。
  - 採用ディレクトリ内で `appsettings.json` → `appsettings.{Environment}.json` を JSON コメント（`//`）・末尾カンマ許容でマージ読み込みし、起動時警告（JSON 構文エラー・旧 `{ "value": ... }` 形式・`rules` パース失敗等）を画面バナーとログの両方へ出力する。
- **GUI 設定保存の部分更新（Patch Save）・排他制御・ロールバック**:
  - 保存対象は常に採用ディレクトリ内の `appsettings.{Environment}.json` とし、ベース `appsettings.json` は書き換えない。
  - パス単位のプロセス内排他ロックのもと `selectedModel` と `rules` のみ更新（UTF-8 日本語非エスケープ）し、既存他キー（`apiKey`, `targetDirectory`, `pathLengthThreshold`, 未知キー）を保持する（既存 JSON 破損・0 バイト空ファイル時は上書き中止）。
  - 保存失敗時はメモリ上の `Settings` 変更・`SelectedRuleId`・確認ダイアログを安全にロールバック／閉鎖し（編集フォーム入力は保持）、ルール管理モーダル開時はモーダル内エラーバナーのみを表示（背面バナー重複抑止）して確実に見せる。また `ComboBox` の同値選択ガードで不要な保存発火を防ぐ。
- **`Settings.save` / `Settings.load` の `ApiKey` シリアライズ修正と最小 JSON 補完**:
  - `StringOptionConverter` を導入して `ApiKey` を常に素の JSON 文字列または `null` で入出力し、`Settings.load` での一部キー欠損最小 JSON 読込時も既定値補完で安全に復元する。
- **出典パス明示・`PartialApiKey` 5 状態保護・統一検証ガード・設定再読込 UI**:
  - `apiKey` の出典ファイルパスを保持し、空文字 `""` や旧 `{ "value": ... }` 形式・上位プレースホルダーによる下位有効キーの消去を防止する。ベース `appsettings.json` 側の `"apiKey": null` によって未設定案内先が Git 管理対象 `appsettings.json` へ誤誘導されないよう、`MissingApiKey` 時は常に環境別保存先（`appsettings.{Environment}.json`）を案内する。
  - `ScanCompleted` と `RetryCandidateAiProposal` で同一の厳格検証ガード（`"sk-or-v1-xxxx..."` 等も検出）を適用し、エラーバナーおよびコントロールパネル常設の「🔄 設定を再読込」ボタン（`ReloadSettings`：同期読込・選択済み `TargetDirectory` 保持）で再起動なしに復帰可能にする。また `AiPipelineRunId` により割り込み再試行時の全非同期通知（`CandidateAiProcessing` / `CandidateAiProposed` / `AllAiProposalsCompleted`）の競合と CTS リークを防止し、`OpenRouterClient.fs` の 401 エラー案内から旧 `.env` 記述を整理する。
- **仕様・テスト・ドキュメント・バージョン整合**:
  - `openspec/specs/companion-file-rename/spec.md`、`SettingsTests.fs` / `StateTests.fs` / `RenameIntegrationE2ETests.fs`、`README.md` を更新し、`TagBasedVideoManager.Renamer.fsproj` の `<Version>` を `0.3.0` へ更新する。

---

## Non-goals（やらないこと）

- Web アプリ本体（`src/TagBasedVideoManager/`）の設定読み込み仕様の変更（引き続き `src/TagBasedVideoManager/appsettings*.json` 参照とする）。
- ユーザー環境に残存する旧 `%APPDATA%\TagBasedVideoManager\` ファイルの自動マイグレーションまたは自動削除。
- 新規設定画面・API キー編集テキストボックスの GUI 追加（ファイル編集＋「🔄 設定を再読込」導線で回復する）。
- `OpenRouterClient.fs` の通信プロトコル・HTTP リトライ仕様変更（401 案内文言の `.env` 記述整合のみ）。

---

## Capabilities

### New Capabilities

<!-- なし（既存機能の不具合修正および仕様・UI 回復導線更新） -->

### Modified Capabilities

- `companion-file-rename`: 2 段階共通ディレクトリ解決、`%APPDATA%` 廃止（残存移行ログ出力）、`ApiKey` JSON シリアライズ整合、Patch Save（排他制御・ロールバック・モーダル内エラー表示）、`PartialApiKey` 保護、統一 API キー検証ガードおよび「🔄 設定を再読込」回復導線を規定する。

---

## 受け入れ基準（Acceptance Criteria）

1. **Given** `src/TagBasedVideoManager.Renamer/appsettings.Development.json` に有効な `apiKey` があり、同時に `%APPDATA%\TagBasedVideoManager\appsettings.json` に `"apiKey": "xxx"` が残存しているとき / **When** 設定を読み込むと / **Then** `%APPDATA%` は参照されず（移行案内ログのみ出力）、有効な `apiKey` とその出典ファイルパスが取得されること。
2. **Given** `ApiKey = Some "sk-or-v1-test-key-12345"` を含む `RenamerSettings` を用意したとき / **When** `Settings.save` で保存し `Settings.load` / `Settings.loadConfiguration` で再読込すると / **Then** JSON 上に `"apiKey": "sk-or-v1-test-key-12345"` が出力され、往復復元されること。
3. **Given** `appsettings.Development.json` に `apiKey`, `targetDirectory`, `pathLengthThreshold` 等が設定されている状態で / **When** GUI でモデル選択変更または命名規則変更を実行すると / **Then** 採用ディレクトリ内の `appsettings.Development.json` のみが Patch Save され、他の既存キーおよびベース `appsettings.json` が保持されること。
4. **Given** 起動時の設定ファイルに JSON 構文エラー・旧 `{ "value": ... }` 形式・不正 `rules` が存在する、または上位に `"apiKey": "xxx"` / `""` があるとき / **When** 設定をマージ読み込みすると / **Then** 下位の有効 `apiKey` は維持され、警告が画面バナーとログの双方へ表示されること（環境別ファイルの明示的 `"apiKey": null` のみ無効化許可）。
5. **Given** `appsettings.json` に `"apiKey": null` のみ存在し、有効な `apiKey` が未設定またはプレースホルダーであるとき / **When** `ScanCompleted` または `RetryCandidateAiProposal` を実行すると / **Then** 同一検証ガードにより AI リクエストは開始されず、未設定時は環境別設定パス（`appsettings.{Environment}.json`）・プレースホルダー時は出典ファイルパスと最小 JSON 書式例を含むエラーバナーが表示され、ファイル修正後に「🔄 設定を再読込」で選択中 `TargetDirectory` を失わずに再起動せず復帰できること。
6. **Given** 保存先が読み取り専用または既存 JSON 破損・0 バイト空状態のとき / **When** メイン画面またはルール管理モーダル内で設定保存を実行すると / **Then** `%APPDATA%` へフォールバックせず既存ファイルを保護したままメモリ変更・`SelectedRuleId`・確認ダイアログをロールバック／閉鎖し、モーダル内エラーバナー（背面バナー重複なし）へ保存先パスと失敗段階・理由が通知されること。

---

## Impact

- `src/TagBasedVideoManager.Renamer/Settings.fs`
- `src/TagBasedVideoManager.Renamer/State.fs`
- `src/TagBasedVideoManager.Renamer/Views.fs`
- `src/TagBasedVideoManager.Renamer/OpenRouterClient.fs`（401 エラー案内文言の `.env` 記述整理）
- `src/TagBasedVideoManager.Renamer/TagBasedVideoManager.Renamer.fsproj`
- `test/TagBasedVideoManager.Renamer.Tests/{SettingsTests.fs,StateTests.fs,RenameIntegrationE2ETests.fs}`
- `openspec/specs/companion-file-rename/spec.md`
- `README.md`
