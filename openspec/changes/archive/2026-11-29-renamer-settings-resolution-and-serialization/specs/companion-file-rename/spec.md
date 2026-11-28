# companion-file-rename Delta Specification

## MODIFIED Requirements

### Requirement: 命名規則マネージャー（作成・編集・削除・並び替え・永続化）

システムは、命名規則の作成・編集・削除・優先順位並び替えが可能な管理モーダルを提供し、環境別設定ファイル（`appsettings.{Environment}.json`、読込時は `appsettings.json` とのマージ結果）のリスト先頭にあるルールを起動時の既定値として適用しなければならない（SHALL）。保存失敗時はメモリ上のルール順序・`SelectedRuleId`・確認ダイアログを安全にロールバック／閉鎖し、モーダル内エラーバナーで通知しつつ編集フォーム入力を保持しなければならない（SHALL）。

#### Scenario: 命名規則の並び替えと既定ルール適用

- **WHEN** ユーザーが管理画面でルールを一番上に移動して保存したとき
- **THEN** アクティブ環境に対応する `appsettings.{Environment}.json` の `rules` のみが部分更新（Patch Save）され、次回起動時にそのルールが自動選択されて AI プロンプト生成の既定として適用されること

---

## REMOVED Requirements

### Requirement: .NET標準外部設定ファイル（appsettings.json / appsettings.{Environment}.json）からの設定取得と環境別ファイルへの保存

**Reason**: `%APPDATA%\TagBasedVideoManager\` 参照・フォールバック保存および `CurrentDirectory` 単独探索による設定不整合・プレースホルダー上書き不具合（Issue #41）を根本解消し、2 段階共通ディレクトリ解決・Patch Save・`ApiKey` シリアライズ整合・出典パス明示・再読込導線へ要件を再編するため。

**Migration**: 本要件の `.env` / OS 環境変数非参照、環境名サニタイズ、および階層マージは、下記の `ADDED Requirements`（5 要件）へ移行・統合される。

---

## ADDED Requirements

### Requirement: .NET標準設定の2段階共通ディレクトリ解決とマージ読み込み

システムは `.env` および汎用 OS 環境変数を参照せず（SHALL NOT）、解決環境名に基づき ① `src/TagBasedVideoManager.Renamer/`（起点 0〜親 8 階層＝計 9 ステップ探索）→ ② `AppDomain.CurrentDomain.BaseDirectory` の 2 段階共通解決で `appsettings.json` と `appsettings.{Environment}.json` を JSON コメント・末尾カンマ許容でマージしなければならない（SHALL）。`%APPDATA%` 参照と `CurrentDirectory` 単独探索は廃止する（SHALL NOT）。

#### Scenario: .env および VIDEO_DIR OS環境変数を参照しない純粋な appsettings マージ読み込み（移行通知付き）

- **WHEN** `.env` ファイルまたは OS 環境変数 `VIDEO_DIR` / `OPENROUTER_API_KEY` が存在する状態で AI File Renamer を起動したとき
- **THEN** `.env` および OS 環境変数の設定値は無視され、採用ディレクトリ内の `appsettings.json` と `appsettings.{Environment}.json` のマージ結果のみが適用され、`.env` が検出された場合は廃止・移行を促す診断ログが出力されること

#### Scenario: 読込・保存共通の 2 段階ディレクトリ解決と %APPDATA% / CurrentDirectory 単独探索の廃止

- **WHEN** リポジトリ環境で実行ディレクトリ（`bin/...`）にも設定ファイルがコピーされており、かつ `%APPDATA%\TagBasedVideoManager\appsettings.json` に `"apiKey": "xxx"` が残存している状態で起動および GUI 設定保存を実行したとき
- **THEN** `%APPDATA%` および `CurrentDirectory` 単独探索は参照されず（旧 `%APPDATA%` 残存時は移行案内ログのみ出力し）、読み込み・保存ともに `src/TagBasedVideoManager.Renamer/` が採用されて有効な `apiKey` が保持されること
- **WHEN** リポジトリ外（起点 0〜親 8 階層内に `src/TagBasedVideoManager.Renamer/` が存在しない単体配置）で起動および保存を実行したとき
- **THEN** `AppDomain.CurrentDomain.BaseDirectory` が共通ディレクトリとして採用され、同ディレクトリ内の `appsettings.{Environment}.json` が読み書きされること

#### Scenario: 設定ファイル JSON 構文エラー時の安全な起動と警告通知

- **WHEN** `appsettings.json` または `appsettings.{Environment}.json` に不正な JSON 構文エラー、0 バイト空内容、または不正な `rules` 配列が含まれる状態で起動したとき
- **THEN** アプリはクラッシュせず有効な下位設定層または組み込み既定値で安全に起動し、構文エラーが発生したファイルパスとエラー内容が画面バナーおよび診断ログの双方へ警告表示されること

#### Scenario: 不正な環境名指定時の安全なフォールバック

- **WHEN** `DOTNET_ENVIRONMENT` に空文字やパストラバーサル文字列（例: `../evil`）が指定されたとき
- **THEN** 不正なパス結合を行わず、安全なデフォルト環境名（Debug 時は `Development`、Release 時は `Production`）へフォールバックすること

### Requirement: ApiKey (string option) の JSON シリアライズ整合

システムは、`Settings.save` および設定ファイル読み書きにおいて `ApiKey`（`string option`）を常に素の JSON 文字列または `null` としてシリアライズ／デシリアライズしなければならず（SHALL）、`{ "value": "..." }` のようなラッパーオブジェクト形式で出力してはならない（SHALL NOT）。また、一部項目が欠損した最小 JSON を `Settings.load` で読み込む際も既定値補完により安全に復元しなければならない（SHALL）。

#### Scenario: ApiKey (string option) の JSON シリアライズ往復整合

- **WHEN** `ApiKey = Some "sk-or-v1-roundtrip-key"` および `ApiKey = None` を含む `RenamerSettings` を `Settings.save` で保存し、`Settings.load` および `Settings.loadConfiguration` で再読込したとき
- **THEN** JSON ファイルには `"apiKey": "sk-or-v1-roundtrip-key"` または `"apiKey": null` が出力され（`{ "value": ... }` にならず）、再読込後に元の `ApiKey` 値が完全に復元されること
- **WHEN** `selectedModel` のみを含む最小 JSON を `Settings.load` で読み込んだとき
- **THEN** 欠損フィールド（`TargetDirectory`, `Rules` 等）に既定値が補完され、`null` 参照例外なく `RenamerSettings` が復元されること

### Requirement: GUI 設定保存の部分更新（Patch Save）とエラー時保護・モーダル内通知

GUI 操作で設定を保存する際、システムはベース `appsettings.json` を書き換えず（SHALL NOT）、採用ディレクトリ内の `appsettings.{Environment}.json` へ `selectedModel` または `rules` のみを排他制御付きで Patch Save しなければならない（SHALL）。モデル値が変化しない場合は保存を発火してはならず（SHALL NOT）、失敗時はメモリ変更・確認ダイアログをロールバック／閉鎖し、メインまたはモーダル内の一方のみのバナーへ通知しなければならない（SHALL）。

#### Scenario: GUI 設定保存の部分更新（Patch Save）による他キー非破壊と最小作成

- **WHEN** `appsettings.Development.json` に `apiKey`, `targetDirectory`, `pathLengthThreshold` 等が定義された状態で、GUI から `ModelSelected` または命名規則変更を保存したとき
- **THEN** ベース `appsettings.json` は変更されず、`appsettings.Development.json` 内の `selectedModel` または `rules` のみが UTF-8 非エスケープで更新され、他の既存キー（`apiKey`, `targetDirectory`, `pathLengthThreshold`, 未知キー等）の値が一切変更・消失しないこと
- **WHEN** `appsettings.{Environment}.json` が未存在の状態で GUI 設定保存を実行したとき
- **THEN** 変更対象のキー（`selectedModel` または `rules`）のみを含む最小の `appsettings.{Environment}.json` が新規作成され、未変更の `apiKey` 等を `null` で書き出して下位設定を覆い隠さないこと

#### Scenario: 書込不可または既存設定JSON破損時のフォールバックなしエラー通知と保護

- **WHEN** 保存先の `appsettings.{Environment}.json` が読み取り専用属性やアクセス権限不足で書き込みできない状態でメイン画面またはルール管理モーダルから設定保存を実行したとき
- **THEN** `%APPDATA%` へフォールバックせず、メモリ上のモデル／ルール変更・`SelectedRuleId` をロールバックし、削除確認ダイアログが開いていれば閉じた上で、保存先フルパス・失敗段階・エラー理由がモーダル内エラーバナー（背面バナー重複なし）へ通知されること
- **WHEN** 既存の `appsettings.{Environment}.json` に構文エラーまたは 0 バイト空状態がある状態で GUI 設定保存を実行したとき
- **THEN** 既存ファイルの上書き破壊を行わず保存を中止し、保存先パスと JSON パース失敗理由（行・列位置含む）をエラー通知すること

### Requirement: プレースホルダー apiKey の上書き保護と出典ファイルパス保持

システムは設定マージ時に `apiKey` の出典ファイルパスを保持し（SHALL）、上位の `apiKey` がプレースホルダー・空文字 `""`・旧 `{ "value": ... }` 等の不正形式である場合は下位の有効 `apiKey` を上書きしてはならない（SHALL NOT）。環境別ファイルの明示 `null` 指定のみ意図的無効化として許可し、不正形式検出時は修正案内を画面バナーとログへ表示しなければならない（SHALL）。

#### Scenario: Development / Production 各環境でのオーバーライドマージとプレースホルダー上書き保護

- **WHEN** `appsettings.json` に有効な `apiKey`（`'sk-or-v1-...'`）が設定されており、`appsettings.Development.json` に `"apiKey": "xxx"`、`"apiKey": ""`、または旧破損形式 `"apiKey": { "value": "..." }` が定義されているとき
- **THEN** 下位の有効 `apiKey` と出典パスが維持され、警告バナー（特に旧破損形式では文字列形式への修正案内）および診断ログが出力されること
- **WHEN** 上位 `appsettings.{Environment}.json` に明示的に `"apiKey": null` が定義されているとき
- **THEN** 意図的な無効化として `ApiKey = None` へ上書きされること

### Requirement: AI 提案・個別再試行における統一 API キー検証ガードと設定再読込導線

システムは `ScanCompleted` と `RetryCandidateAiProposal` で同一の API キー検証ガードを適用し（SHALL）、未設定（環境別保存先を案内）やプレースホルダー検出時は AI リクエストを実行せずエラーバナーへ案内し、エラーバナーおよびコントロールパネルの「🔄 設定を再読込」で選択中フォルダを失わずに再起動せず回復できなければならない（SHALL）。割り込み再試行時も全非同期通知で競合を起こしてはならない（SHALL NOT）。

#### Scenario: APIキー未設定・プレースホルダー検出時の出典パス明示と再読込回復

- **WHEN** ベース `appsettings.json` に `"apiKey": null` のみがある未設定状態、または環境別ファイルに `"xxx"` / `"sk-or-v1-xxxxxxxxxxxxxxxx"` 等のプレースホルダーが設定された状態で `ScanCompleted` または `RetryCandidateAiProposal` を実行したとき
- **THEN** 双方のハンドラで同一の検証ガードが動作して AI リクエストを実行せず、未設定時は環境別設定ファイルパス（ベース `appsettings.json` ではない）と最小 JSON 例、プレースホルダー時は出典ファイルパスを含むエラーバナーが表示されること
- **WHEN** 画面上で対象フォルダを選択済みかつエラー表示後に設定ファイルの `apiKey` を有効キーへ修正し、エラーバナーまたはコントロールパネルの「🔄 設定を再読込」をクリックしたとき
- **THEN** アプリを再起動することなく最新の設定が再読込され、選択中の対象フォルダがファイル側の空文字で消去されずに維持され、エラーバナーが解消して AI 提案または「↻ 再試行」を実行できること
