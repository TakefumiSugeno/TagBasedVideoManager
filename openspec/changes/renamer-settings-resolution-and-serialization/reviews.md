# レビュー記録一覧: renamer-settings-resolution-and-serialization

---

# レビュー記録: renamer-settings-resolution-and-serialization / propose / Propose-User (Cycle 1)

- **日時**: 2026-11-22
- **フェーズ**: propose
- **タスク**: 対象外（Propose フェーズレビュー）
- **レビュアー**: user
- **使用プロトコル**: openspec/templates/checklist_propose.md
- **対象成果物**: openspec/changes/renamer-settings-resolution-and-serialization/proposal.md, openspec/changes/renamer-settings-resolution-and-serialization/specs/companion-file-rename/spec.md, openspec/changes/renamer-settings-resolution-and-serialization/design.md, openspec/changes/renamer-settings-resolution-and-serialization/test_plan.md, openspec/changes/renamer-settings-resolution-and-serialization/tasks.md, openspec/specs/companion-file-rename/spec.md

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: 仕様書（`specs/**/spec.md`）のブラインド比較レビュー（先出し予測と突合）

- **確認内容・回答**:
  `specs/` を開く前に `proposal.md` の背景・目的（Why / What Changes / Non-goals）のみから、初心者が遭遇する異常系・境界条件を以下の 5 点としてゼロベースで予測した。

  1. **予測1（`%APPDATA%` 廃止による既存ユーザーの設定消失・混乱）**: 旧 `%APPDATA%\TagBasedVideoManager\` に設定を残していたユーザーが更新後に突然設定が消えた・API キー未設定になったとパニックになり、移行案内がないと原因が分からないのではないか。
  2. **予測2（プレースホルダー境界値・空白・サンプルコピペ・旧壊れ形式）**: 空文字 `""` や大文字、`"sk-or-v1-..."` 等のサンプル文字列コピペ、さらに過去のバグで `"apiKey": { "value": "..." }` と保存されてしまったファイルの扱いで混乱しないか。
  3. **予測3（設定ファイル破損時の起動時表示およびファイル修正後の回復導線）**: 設定ファイルに JSON 構文エラーがある状態で起動した時、画面上にエラーが出るのか、それとも黙って起動して後から混乱するか。また、API キーエラー後に設定ファイルを直した時、アプリを再起動せずに反映・再試行できるのか。
  4. **予測4（Patch Save 時の JSONC コメント・未作成ファイル・UI 上の保存エラー視認性）**: ユーザーが設定ファイルに `//` コメントを書いていたり、環境別ファイルが未作成の初回状態だったりした時、適切な保護や案内があるか。また、GUI で保存エラーが起きた時に確実に画面上で気づけるか。
  5. **予測5（環境名不一致や探索階層・単体配置時の書き込み不可への案内）**: 環境変数未指定時や単体配置時、存在しない環境別ファイル（例: `appsettings.Production.json`）への記入を促されても初心者がゼロからファイルを作れないのではないか。

  その後 `specs/companion-file-rename/spec.md`、メイン仕様 `openspec/specs/companion-file-rename/spec.md`、`design.md`、`test_plan.md`、および現行実装（`Views.fs` / `State.fs` / `Settings.fs`）と突合した結果、以下の重要な仕様抜け・退行を検出した。

  - **起動時 JSON 構文エラー警告の画面表示が仕様から脱落（退行）**: 現行メイン仕様（L222）では「構文エラーが発生したファイルパスとエラー内容を**画面バナー・ログへ警告表示すること**」と規定されていたが、本変更のデルタ仕様（L47）では「警告ログに記録されること」へ後退し、`design.md`（L139）でも `eprintfn` 出力のみで `Model.ErrorMessage`（画面バナー）に表示されない。GUI ユーザーはコンソールを見ないため、誤記で設定が読めていないことに気づけない。
  - **設定ファイル修正後の回復導線（再読込手段）が欠落**: エラー案内に従って外部エディタで `appsettings.{Environment}.json` の `apiKey` を修正しても、GUI 画面上に設定を再読込するボタンも、再実行・再試行時に再読込する仕組みも定義されていない。`test_plan.md`（`E2E-03` L282）ではテストコードが内部メッセージ `SettingsLoadedWithMeta` を直接送信して復帰を検証しているが、実画面のユーザーはその手段を持たない。
  - **GUI 保存失敗時のエラーバナーがルール管理モーダルの背後に隠れる**: `Views.fs`（L3309-3323, L2641-2655）のレイアウト上、実運用で Patch Save を最も多く実行する「命名規則マネージャーモーダル（960x680・全画面オーバーレイ）」は、メイン領域最上部にある `errorBanner` の前面に重なって完全に覆い隠す。モーダル内で ReadOnly や破損 JSON による保存失敗が起きても、モーダルを閉じるまでユーザーはエラーに気づけない。
  - **プレースホルダー境界と空文字 `""` の扱い、旧 `{ "value": ... }` 形式の診断が未整備**: `design.md`（L50-55）の `isPlaceholderApiKey` 規則では `README.md`（L244, L326）に記載のサンプル値 `"sk-or-v1-xxxxxxxxxxxxxxxx"`（24文字）等が素通りして `ValidApiKey` 扱いになり 401 を連発する。また、現行 `parsePartial`（`Settings.fs` L158）では `"apiKey": ""` が `Some None` となり、明示的 `null` と同様に下位の有効キーを無警告で上書き無効化してしまう点について仕様上の定義が欠けている。旧バグ形式 `{ "value": "..." }` が残っていた場合も、画面上に形式エラーの案内が出ない。
  - **旧 `%APPDATA%` 残存時の案内および JSON コメント許容範囲・新規ファイル作成手順の欠落**: `.env` 廃止時には検出時の移行案内が仕様化されていたが（デルタ仕様 L35）、今回廃止される `%APPDATA%` 残存ファイルに対しては移行案内がない。また .NET 設定ファイルでよく書かれる `//` コメントや末尾カンマを `JsonDocument.Parse` / `JsonNode.Parse` で許容するかが未定義であり、ファイル未存在の初期状態でエラー案内にファイルパスだけ出されても JSON 作成形式が分からない。

- **確認根拠**:
  - `openspec/changes/renamer-settings-resolution-and-serialization/proposal.md` L12, L18-47, L50-56, L71-79
  - `openspec/specs/companion-file-rename/spec.md` L207-243
  - `openspec/changes/renamer-settings-resolution-and-serialization/specs/companion-file-rename/spec.md` L16-99
  - `openspec/changes/renamer-settings-resolution-and-serialization/design.md` L32-36, L42-56, L97-105, L121-158
  - `openspec/changes/renamer-settings-resolution-and-serialization/test_plan.md` L280-284

### 設問2: 都合の良い前提（暗黙の仮定）の告発

- **確認内容・回答**:
  以下の 4 つの「開発者に都合の良い暗黙の仮定」に依存している。
  1. **「GUI のエラーはユーザーに必ず見えている」という仮定**: 既存 `errorBanner` を使うため UI 変更不要としているが（`proposal.md` L12）、ルール管理モーダル（`Views.fs` L2641, L3321）を開いたまま保存操作を行えば、`errorBanner` はモーダルと半透明幕の背面に完全に隠れ、コピーボタンも閉じるボタンもクリック不能になる。
  2. **「設定ファイルを書き直せばユーザーは自力でアプリを再起動するはず」という仮定**: エラーメッセージ（`design.md` L154-156）に再起動が必要とも書かれておらず、UI に再読込ボタンもないため、せっかちなユーザーはスキャン後にファイルへ API キーを書いてそのまま「↻ 再試行」ボタンを押し続け、「直したのに同じエラーが出る」と諦める。
  3. **「ユーザーは GUI アプリ起動時にコンソール（`eprintfn`）を読むはず」という仮定**: JSON 構文エラーや旧 `{ "value": ... }` オブジェクト検出、プレースホルダー上書き保護発動時の警告が `Warnings`（`eprintfn`）止まりであり（`design.md` L35, L101, L139）、デスクトップ GUI ユーザーが原因を把握できない。
  4. **「テストが通れば実ユーザーも回復できる」という錯覚**: `test_plan.md` の `E2E-03`（L282）はテストハーネスから内部 Msg `SettingsLoadedWithMeta` を直接ディスパッチして回復を再現しているが、実画面にはそのトリガーとなる操作が存在しない。

- **確認根拠**:
  - `src/TagBasedVideoManager.Renamer/Views.fs` L682-700, L2491-2549, L2641-2655, L3301-3325
  - `src/TagBasedVideoManager.Renamer/Program.fs` L44-45
  - `src/TagBasedVideoManager.Renamer/State.fs` L252-285, L312-315, L473-509, L822-906
  - `openspec/changes/renamer-settings-resolution-and-serialization/test_plan.md` L282

### 設問3: UIモック（`mockup.html`）の初心者ペルソナ検証（UI変更時のみ）

- **確認内容・回答**:
  `proposal.md`（L12）は本変更を「UI/UX 変更なし（`mockup.html` は対象外）」としているが、**この「対象外」という前提自体に欠陥がある**と判定する。
  - **Zero-State（初期状態）の課題**: `TargetDirectory` 未設定時のガイドは既存 UI に存在する一方、API キー未設定または設定ファイル未存在の単体配置（`E2E-02`）では、スキャンボタンを押して重いディレクトリ走査が終わった後に初めて「設定ファイル (`.../appsettings.Production.json`) の apiKey に...」と警告が出る。しかも対象ファイルがまだ存在しない場合、新規作成する JSON 形式の案内がない。
  - **Error-State（エラー時）の回復導線の破綻**:
    1. 設定ファイル修正後に再起動なしで復帰できる UI 導線（設定再読込ボタン等）がないため、表示された案内通りにファイルを直しても「↻ 再試行」で回復できない。
    2. `ruleManagerModal` 内での保存失敗（ReadOnly / JSON 破損）時、エラー表示先であるメイン側の `errorBanner` がモーダルの裏側に完全に隠れて見えなくなる（`Views.fs` L3309-3323）。メモリ上のルール一覧だけ変更された状態でモーダルを閉じずにいると、ユーザーは保存成功と誤認する。
  - **結論**: Error-State の視認性と回復導線を正しく成立させるためには、モーダル内のエラーバナー表示および設定再読込導線を含む UI/UX の修正が必要であり、プロジェクト規約に基づき設計変更・`mockup.html` の作成（またはエラー時状態追加）を検討すべきである。

- **確認根拠**:
  - `openspec/changes/renamer-settings-resolution-and-serialization/proposal.md` L12, L54
  - `src/TagBasedVideoManager.Renamer/Views.fs` L2210-2289, L2491-2549, L2621-2655, L3301-3325
  - `openspec/changes/renamer-settings-resolution-and-serialization/test_plan.md` L281-284

---

## 2. Adversarial QA（破壊テスト実行結果）※Apply実装レビュー時のみ

対象外（Propose フェーズの仕様・設計・計画レビューのため、実装コードおよび破壊テスト実行は対象外）。

---

## 3. 指摘事項

### 指摘1

- **内容**: 起動時の設定ファイル JSON 構文エラー警告の「画面バナー表示」がデルタ仕様で脱落している（既存仕様からの退行）。現行メイン仕様（`openspec/specs/companion-file-rename/spec.md` L222）には「画面バナー・ログへ警告表示すること」と明記されていたが、本変更のデルタ仕様（L47）では「警告ログに記録されること」のみに後退し、`design.md`（L139）でも `eprintfn` 出力に留まっている。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: 既存仕様の明確な UX 退行であり、エラー原因を即座に可視化して 1 分以内に特定・修復可能にする本変更の目的にも反するため。
- **対応コミット**: Cycle 2 修正で対応予定

### 指摘2

- **内容**: ルール管理モーダル（`Views.fs` L2641, L3321）を開いている間に `savePatch` が失敗した場合、`errorBanner` がモーダル背後に隠れて視認不能になり、メモリ上のルール一覧も更新されて誤認を招く。モーダル内へのエラーバナー表示・ロールバックおよび `mockup.html` 作成が必要。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: 実画面上、最も頻繁に `savePatch` が発火する操作経路において保存失敗通知がユーザーに見えないため。
- **対応コミット**: Cycle 2 修正で対応予定

### 指摘3

- **内容**: API キーエラー後に設定ファイルを修正した際、再起動せずに再読込・回復できる UI 導線（設定再読込ボタン等）が欠如しており、`test_plan.md` `E2E-03` と実操作が乖離している。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: 案内通りに設定ファイルを修正したユーザーが画面上の操作で回復できるようにするため。
- **対応コミット**: Cycle 2 修正で対応予定

### 指摘4

- **内容**: `validateApiKey` / `mergePartialsChain` において、(1) `"sk-or-v1-xxxxxxxxxxxxxxxx"` 等のサンプル文字列漏れ、(2) `"apiKey": ""` による下位有効キー無警告消去、(3) 旧 `{ "value": ... }` 形式の診断案内欠如がある。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: プレースホルダー判定漏れによる API 401 やサイレント無効化・原因不明エラーを防ぐため。
- **対応コミット**: Cycle 2 修正で対応予定

### 指摘5

- **内容**: 旧 `%APPDATA%\TagBasedVideoManager\` に設定ファイルが残存していた場合の移行促進診断ログが未規定。
- **重要度**: Minor
- **対応方針**: 採用
- **理由**: アップグレード直後の設定消失誤認を防ぎ、`.env` 廃止時との運用一貫性を保つため。
- **対応コミット**: Cycle 2 修正で対応予定

### 指摘6

- **内容**: JSON コメント（`//`）・末尾カンマの許容範囲と、環境別ファイル未存在時の最小 JSON 書式案内（`{ "apiKey": "sk-or-v1-..." }`）が未定義。
- **重要度**: Minor
- **対応方針**: 採用
- **理由**: 初心者ユーザーの誤編集や初回ファイル作成時の戸惑いを減らすため。
- **対応コミット**: Cycle 2 修正で対応予定

---

## 4. 判定

- **LGTM**: false
- **条件付きLGTM**: なし（Major 指摘 1〜4 の仕様・設計・UI モック反映後に再レビューを実施）
- **保留指摘（申し送り）**: なし
- **次のアクション**: 対応後再レビュー

---

# レビュー記録: renamer-settings-resolution-and-serialization / propose / Propose-SE (Cycle 1)

- **日時**: 2026-11-22
- **フェーズ**: propose
- **タスク**: 対象外（Propose フェーズレビュー）
- **レビュアー**: se
- **使用プロトコル**: openspec/templates/checklist_propose.md
- **対象成果物**: openspec/changes/renamer-settings-resolution-and-serialization/{proposal.md, specs/companion-file-rename/spec.md, design.md, test_plan.md, tasks.md}

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: 設計書の故障シミュレーション — 外部依存ハング・切断時の連鎖崩壊検証（checklist §2.1）

- **確認内容・回答**:
  1. **起動時二重ロードの遅延競合（ロストアップデート）**: `State.init ()` での同期ロードに加え `loadSettingsCmd` が非同期再読込を行い、`SettingsLoadedWithMeta (Ok meta)` で `Settings` 等を上書きするため、遅延到着時にユーザー変更が消失するリスクがある。また `loadConfigurationWithMetadata` と `Result<LoadedConfiguration, RenamerError>` の型不一致および `Error _` 処理も欠落している。
  2. **`savePatch` における並行 Read-Modify-Write のロストアップデートと UI スレッド停止**: プロセス内ファイル単位の排他ロックおよび `File.ReadAllText` リトライがないため、同時保存で更新消失が起きる。また最大 250ms の同期 `Thread.Sleep` が UI スレッドを停止させるリスクがある。
  3. **壊れた旧形式 `apiKey`（`{ "value": ... }`）に対する `parsePartial` の誤判定**: 非 `String` トークンを `Some None` 扱いすると、明示的 `null` と誤認され下位 `appsettings.json` の有効キーまで消去される。
  4. **`RetryCandidateAiProposal` 割り込み時の先行パイプライン終了通知によるフラグ誤解除**: `createAsyncPipelineCmd` の `finally` が無条件に `AllAiProposalsCompleted` を発行するため、旧パイプライン終了が新しい再試行の `IsRequestingAi` を `false` に上書きする。
- **確認根拠**: `design.md` §2.1 L32-36, §2.4 L98-107, §2.5 L123-133, §2.6 L139-147, L157; `State.fs` L141-218, L220-261, L312-315, L434-447, L473-508; `Settings.fs` L79-89, L153-161, L198-206

### 設問2: 設計書の故障シミュレーション — 可観測性（調査可能性）の検証（checklist §2.2）

- **確認内容・回答**:
  1. 起動時 `Warnings`（JSON 構文エラー・保護発動・旧形式検出）が `eprintfn` 止まりで画面バナーに出ない。
  2. `resolveConfigDirectory` で `BaseDirectory` へフォールバックした際の探索トレース出力がない。
  3. `parsePartial` の不正トークン警告および `savePatch` 失敗段階（読込 / パース行位置 / `.tmp` 書込 / リトライ後 `File.Move`）・試行回数の診断粒度が不足している。
- **確認根拠**: `design.md` §2.3 L63-74, §2.4 L85-90, §2.5 L124-133, §2.6 L139, §3.2 L166-168; `openspec/specs/companion-file-rename/spec.md` L219-222

### 設問3: テスト計画書の死角告発 — 100%パスしても抜けるバグ（最大の死角トップ2）（checklist §3.1）

- **確認内容・回答**:
  1. **トップ1**: `Views.fs` のモデル選択 `ComboBox.onSelectedIndexChanged` に同値ガードがなく、UI 再描画等で意図しない `ModelSelected` → `savePatch` が走る恐れがあるが、静的 `Cmd.none` 描画の E2E テストでは検出できない。
  2. **トップ2**: 旧 `{ "value": ... }` 形式の階層マージ検証不足、および `UT-PATCH-04` で新規作成した最小 JSON を `Settings.load` で読んだ際の欠損フィールド `null` リスク、JSON コメント／末尾カンマ対応のテスト欠落。
- **確認根拠**: `test_plan.md` L67, L120, L146-165, L274-284; `Views.fs` L688-700; `RenameIntegrationE2ETests.fs` L1256

### 設問4: テスト計画書の死角告発 — モックの嘘（偽の安心感）の検証（checklist §3.2）

- **確認内容・回答**:
  1. `resolveConfigDirectory (baseDirOpt)` が本番の `BaseDirectory` や `CurrentDirectory` を注入できないため、リポジトリ内実行時に `UT-DIR-05` / `06` / `09` が本物の `src/TagBasedVideoManager.Renamer` を拾って成立せず、テストが本番経路を通らなくなる。
  2. `IT-FAULT-03`（並行 `savePatch`）に更新後の全対象キーがロストアップデートせず残っていることのアサーションが欠落している。
- **確認根拠**: `design.md` §2.3 L67-74; `test_plan.md` L101-111, L204-211, L246

### 設問5: タスクリストの手戻りリスク検証 — 最も詰まるリスクが高いタスクの特定（checklist §5.1）

- **確認内容・回答**:
  - **タスク 2.1 / 2.3** と **タスク 3.1（既存テスト整理・実ファイル隔離）** の順序が逆転しているため、Phase 2.1/2.3 で既存テストが落ちるだけでなく、Phase 2.3 実行時に開発者の実設定ファイル `src/TagBasedVideoManager.Renamer/appsettings.Development.json` が破壊的に上書きされる。
  - タスク 1.1 でも `design.md`（depth 8..1 = 7階層上）と `test_plan.md` `UT-DIR-04`（8階層上）で 1 階層のズレがある。
- **確認根拠**: `tasks.md` L19, L34, L48, L54-58; `SettingsTests.fs` L249-258, L279-327, L396-432, L822-880; `StateTests.fs` L233-243, L369-400, L613-670, L1140-1321

### 設問6: タスクリストの手戻りリスク検証 — 完了基準の検証可能性（checklist §5.2）

- **確認内容・回答**:
  1. タスク 1.1 / 2.2 での未定義シンボル参照時の先行 Red テスト構築方法（最小スタブ＋実行時失敗 vs コンパイル失敗）が曖昧。
  2. タスク 3.1 の `E2E-05` 視覚検証基準および `StateTests.fs` L1140 の旧文言テスト整理の明記漏れ。
  3. タスク 4.1 の「全ソリューションテスト」範囲と C2 100% 証跡化手法が未定義。
- **確認根拠**: `tasks.md` L11-22, L36-42, L54-58, L68-72; `test_plan.md` L15, L284

---

## 2. Adversarial QA（破壊テスト実行結果）※Apply実装レビュー時のみ

対象外（Propose フェーズのため実装コード編集・破壊テスト実行前段階）。

---

## 3. 指摘事項

### 指摘1

- **内容**: `tasks.md` で旧テスト整理・実ファイル隔離（Phase 3.1）が Phase 2.1/2.3 より後ろにあり、Phase 2 完了不能および開発者実設定 `appsettings.Development.json` 破壊を引き起こす。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: 実環境データ保護と TDD 進行上の確実な手戻り防止のため、既存テスト隔離・整理を Phase 1 先頭へ前倒しする必要があるため。
- **対応コミット**: Cycle 2 修正で対応予定

### 指摘2

- **内容**: `resolveConfigDirectory` が `CurrentDirectory` / `BaseDirectory` を注入できず `UT-DIR-05` / `06` / `09` がテスト不能、かつ探索深度（7 階層 vs 8 階層）に 1 ズレがある。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: 純粋解決関数 `resolveConfigDirectoryWithOrigins` へ分離してテスタビリティを確保し、探索深度定義を統一するため。
- **対応コミット**: Cycle 2 修正で対応予定

### 指摘3

- **内容**: `parsePartial` で壊れた旧形式 `{ "value": ... }` や非文字列トークンが `Some None`（明示 `null`）と誤認され、下位の有効キーまで警告なしに無効化してしまう。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: 既存被害環境でのサイレント無効化を防ぎ、`PartialApiKey` 5 状態で明確に分離・警告するため。
- **対応コミット**: Cycle 2 修正で対応予定

### 指摘4

- **内容**: `savePatch` にファイルパス別排他ロック・読込リトライがなく並行保存でロストアップデートが起きる（`IT-FAULT-03` アサーションも不足）。また最小 JSON に対する `Settings.load` 安全性と JSONC コメント方針が未定義。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: 並行保存の整合性担保、最小 JSON 読込での null 安全確保、コメント許容方針明確化のため。
- **対応コミット**: Cycle 2 修正で対応予定

### 指摘5

- **内容**: (1) `State.init ()` の二重ロード競合と `Result` 型不整合、(2) `Views.fs` `ComboBox` 同値ガード欠如、(3) UI スレッド同期 I/O リトライ待機時間、(4) `RetryCandidateAiProposal` と `AllAiProposalsCompleted` のレースコンディションが未対策。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: レースコンディションと意図しない保存発火を構造的に防止するため。
- **対応コミット**: Cycle 2 修正で対応予定

### 指摘6

- **内容**: 起動時警告の GUI バナー表示退行と、`resolveConfigDirectory` 探索トレース・`savePatch` 失敗段階診断情報の不足。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: デスクトップ GUI アプリでの可観測性（MTTR 1 分以内）とメイン仕様整合のため。
- **対応コミット**: Cycle 2 修正で対応予定

### 指摘7

- **内容**: `validateApiKey` が `"sk-or-v1-xxxxxxxxxxxxxxxx"` 等のサンプルを素通りするリスク、および `OpenRouterClient.fs` L124/L350 に残存する `.env` 案内文言の整理。
- **重要度**: Minor
- **対応方針**: 採用
- **理由**: ドキュメント例示コピーによる 401 誤送信および案内文言の矛盾を解消するため。
- **対応コミット**: Cycle 2 修正で対応予定

### 指摘8

- **内容**: `tasks.md` の先行 Red スタブ方針、`E2E-05` 視覚検証基準、`StateTests.fs` L1140 整理、Phase 4.1 のテスト・C2 証跡範囲の明確化。
- **重要度**: Minor
- **対応方針**: 採用
- **理由**: サブエージェント間の完了基準の曖昧さを排除するため。
- **対応コミット**: Cycle 2 修正で対応予定

---

## 4. 判定

- **LGTM**: false
- **条件付きLGTM**: なし（指摘 1〜6 の Major 指摘解消後に再レビューを実施）
- **保留指摘（申し送り）**: なし
- **次のアクション**: 対応後再レビュー

---

# レビュー記録: renamer-settings-resolution-and-serialization / propose / Propose-User (Cycle 2)

- **日時**: 2026-11-22
- **フェーズ**: propose
- **タスク**: 対象外（Propose フェーズ Cycle 2 再レビュー）
- **レビュアー**: user
- **使用プロトコル**: openspec/templates/checklist_propose.md
- **対象成果物**: openspec/changes/renamer-settings-resolution-and-serialization/{proposal.md, specs/companion-file-rename/spec.md, design.md, test_plan.md, tasks.md, mockup.html, reviews.md}, openspec/specs/companion-file-rename/spec.md

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: 仕様書（`specs/**/spec.md`）のブラインド比較レビュー（先出し予測と Cycle 1 指摘反映検証）

- **確認内容・回答**:
  Cycle 1 指摘 1〜6 は反映されたが、改訂後 `proposal.md` からのブラインド再予測と実ファイル突合により、以下の 3 つの穴を検出した。
  1. 実ファイル `src/TagBasedVideoManager.Renamer/appsettings.json`（L5）の `"apiKey": null` により、`ApiKeyExplicitNull` 規則が `ApiKeySourcePath` にベースファイルパスを記録し、未設定エラー時に Git 管理対象 `appsettings.json` への記入を誤案内する。
  2. `ReloadSettings` 実行時、最小 JSON `{ "apiKey": "sk-or-v1-..." }` 作成後の再読込で直前に UI で選択した `TargetDirectory` が `""` に消去され、スキャンが無言・無反応になる。
  3. `mockup.html`（モーダル内バナーの `🔓 権限回復して再保存` と `✕`/`🔄` 欠落、およびコントロールパネル常設 `🔄 設定を再読込` ボタン）と `design.md` / `tasks.md` / `specs/` のボタン仕様が乖離している。
- **確認根拠**: `src/TagBasedVideoManager.Renamer/appsettings.json` L5; `src/TagBasedVideoManager.Renamer/State.fs` L305-307, L327-328; `design.md` L117-118, L152-162; `mockup.html` L144-164, L229-234, L310-332

### 設問2: 都合の良い前提（暗黙の仮定）の告発

- **確認内容・回答**: 「ベース `appsettings.json` が `ApiKeySourcePath = None` になるはず」「`ReloadSettings` で画面選択フォルダを保持しなくてよい」「モックと設計書の細かなボタン差は実装者が吸収する」という暗黙の仮定が残存。
- **確認根拠**: `appsettings.json` L1-6; `design.md` L118, L153-162; `mockup.html` L229-234, L318-330

### 設問3: UIモック（`mockup.html`）の初心者ペルソナ検証（UI変更時のみ）

- **確認内容・回答**: 5 状態切替・Zero-State 案内文・モーダル内ロールバック構成は良好だが、モーダル内エラーバナー右側のボタン不一致（`🔓 権限回復して再保存` 混入・`🔄`/`✕` 欠落）とコントロールパネル常設ボタンの仕様未定義を解消する必要がある。
- **確認根拠**: `mockup.html` L36-67, L81-129, L229-234, L284-393

---

## 2. Adversarial QA（破壊テスト実行結果）※Apply実装レビュー時のみ

対象外（Propose フェーズの再レビューのため、実装コードおよび破壊テスト実行は対象外）。

---

## 3. 指摘事項

### 指摘1

- **内容**: `appsettings.json`（L5）の `"apiKey": null` により、未設定エラー時に Git 追跡対象のベース `appsettings.json` へ API キー記入を誤誘導する。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: ベース `IsBaseLayer = true` の `ApiKeyExplicitNull` は `ApiKeySourcePath = None` とし、`MissingApiKey` 時は常に環境別 `SettingsSavePath` を案内するように修正した。
- **対応コミット**: Cycle 3 修正で対応済み

### 指摘2

- **内容**: `ReloadSettings` 実行時に直前に画面で選択していた `TargetDirectory` がファイル側の空文字 `""` で消去される。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: ファイル側の `TargetDirectory` が空白なら画面上の非空 `TargetDirectory` を保持し、`IsScanning || IsRenaming` 排他と `SelectedRuleId` フォールバックも明記した。
- **対応コミット**: Cycle 3 修正で対応済み

### 指摘3

- **内容**: `mockup.html` と `design.md` / `tasks.md` / `specs/` 間でコントロールパネル常設 `🔄 設定を再読込` ボタンとモーダル内バナーボタン（`🔓 権限回復して再保存` vs `🔄 設定を再読込`・`📋 コピー`・`✕`）が乖離している。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: コントロールパネル常設ボタンを正式採用して全成果物に統一し、モック用シミュレーションボタンはアプリ枠外コントロールバーへ分離、モーダル内バナーは 3 ボタンに完全一致させた。
- **対応コミット**: Cycle 3 修正で対応済み

### 指摘4

- **内容**: 保存失敗ロールバック時の `SaveEditingRule` 失敗時 `SelectedRuleId` 不変維持・`DeleteRule` 失敗時 `ConfirmDialog = None`＋`EditingRule` 保持の明記不足。
- **重要度**: Minor
- **対応方針**: 採用
- **理由**: `design.md` §2.6.5・`specs/`・`test_plan.md` `ST-RULE-05` へ明記した。
- **対応コミット**: Cycle 3 修正で対応済み

### 指摘5

- **内容**: `E2E-02` にベース `appsettings.json`（`"apiKey": null`）存在下での実リポジトリ構成ケース追加。
- **重要度**: Minor
- **対応方針**: 採用
- **理由**: `test_plan.md` `E2E-02`・`ST-SCAN-03` へ追加した。
- **対応コミット**: Cycle 3 修正で対応済み

---

## 4. 判定

- **LGTM**: false
- **条件付きLGTM**: なし（指摘 1〜3 解消後に Cycle 3 再レビューを実施）
- **保留指摘（申し送り）**: なし
- **次のアクション**: 対応後再レビュー

---

# レビュー記録: renamer-settings-resolution-and-serialization / propose / Propose-SE (Cycle 2)

- **日時**: 2026-11-22
- **フェーズ**: propose
- **タスク**: 対象外（Propose フェーズ Cycle 2 再レビュー）
- **レビュアー**: se
- **使用プロトコル**: openspec/templates/checklist_propose.md
- **対象成果物**: openspec/changes/renamer-settings-resolution-and-serialization/{proposal.md, specs/companion-file-rename/spec.md, design.md, test_plan.md, tasks.md, mockup.html, reviews.md}

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: 設計書の故障シミュレーション — 外部依存ハング・切断時の連鎖崩壊検証（checklist §2.1）

- **確認内容・回答**:
  1. OpenRouter / `ddgs` 5 秒ハング中の `RetryCandidateAiProposal` 割り込み時に、先行 `AiCancellationCts` の未キャンセル・新 `cts` 未格納、および `CandidateAiProcessing` / `CandidateAiProposed` への `runId` ガード欠落によりスピナーゾンビ化と CTS リークが起きる。
  2. `ReloadSettings` の同期（`design.md`）と非同期（`test_plan.md`）矛盾、および `LoadedConfiguration` の `EnvironmentName` / `SavePath` 欠落による TOCTOU。
  3. `DeleteRule` 失敗時の `ConfirmDialog` 未閉鎖と `SaveEditingRule` 失敗時の `SelectedRuleId` ズレ。
  4. `savePatch` における日本語エンコーダ明記漏れおよび `Settings.load` 置換に伴う `StringOptionConverter`・`rules` パース握りつぶし。
- **確認根拠**: `design.md` L31-36, L91-97, L139-162; `test_plan.md` L122, L247; `State.fs` L141-218, L413-508, L789-890; `Settings.fs` L163-172; `Views.fs` L3321-3323

### 設問2: 設計書の故障シミュレーション — 可観測性（調査可能性）の検証（checklist §2.2）

- **確認内容・回答**: モーダル開時の `errorBanner` 二重描画、`mockup.html` とのボタン不一致、`parsePartial` の `rules` 不正配列無警告スキップが残存。
- **確認根拠**: `design.md` L19, L153-158; `mockup.html` L136, L229-234, L310-332; `Settings.fs` L163-172

### 設問3: テスト計画書の死角告発 — 100%パスしても抜けるバグ（最大の死角トップ2）（checklist §3.1）

- **確認内容・回答**: (1) `RetryCandidateAiProposal` 割り込み時の先行 CTS 破棄・全通知 `oldRunId` 無視テストの欠落、(2) `DeleteRule` 失敗時 `ConfirmDialog = None` 検証および 0 バイト空ファイル境界テストの欠落。
- **確認根拠**: `test_plan.md` L125-126, L242-255; `State.fs` L473-508, L853-890

### 設問4: テスト計画書の死角告発 — モックの嘘（偽の安心感）の検証（checklist §3.2）

- **確認内容・回答**: (1) `loadConfigurationWithMetadata` にコンテキスト注入経路がなく `UT-RED-01` / `IT-CONF-01` が実環境依存、(2) `E2E-07` の単一要素 `ComboBox` 同値再描画はガードなしでも Pass する偽陽性。
- **確認根拠**: `test_plan.md` L53, L224, L271; `Settings.fs` L347-355; `Views.fs` L682-700

### 設問5: タスクリストの手戻りリスク検証 — 最も詰まるリスクが高いタスクの特定（checklist §5.1）

- **確認内容・回答**: Task 1.1 で `StateTests.fs` へ `SettingsSavePath` を注入しても、現行 `State.fs` L265 が `Settings.resolveSavePath None` を直呼びするため、Task 2.1〜2.3 の間で `StateTests` を実行すると開発者の実 `appsettings.Development.json` が破壊される。
- **確認根拠**: `tasks.md` L11-14, L33-54; `State.fs` L264-270

### 設問6: タスクリストの手戻りリスク検証 — 完了基準の検証可能性（checklist §5.2）

- **確認内容・回答**: Task 2.2 での `AllAiProposalsCompleted of Guid` 変更時の既存 `StateTests.fs` コンパイル整合手順と、Task 2.3 の `mockup.html` ボタン差異解消が必要。
- **確認根拠**: `tasks.md` L12, L41-52; `StateTests.fs` L497-504

---

## 2. Adversarial QA（破壊テスト実行結果）※Apply実装レビュー時のみ

対象外（Propose フェーズの再レビューのため、実装コード編集・破壊テスト実行前段階）。

---

## 3. 指摘事項

### 指摘1

- **内容**: Task 1.1〜2.3 の移行期間において `State.fs` L265 の `resolveSavePath None` 直呼びにより開発者実 `appsettings.Development.json` が破壊されるリスク。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: Task 1.1 で実ファイル保護ガードを追加し、Task 2.1 の `Settings.fs` 実装と同時に `State.fs` L265 へ `model.SettingsSavePath` 優先参照を前倒し適用するよう修正した。
- **対応コミット**: Cycle 3 修正で対応済み

### 指摘2

- **内容**: `RetryCandidateAiProposal` 割り込み時の先行 CTS キャンセル/破棄・新 CTS 保持漏れ、および `CandidateAiProcessing` / `CandidateAiProposed` への `runId` ガード欠落（`ST-RETRY-05` 含む）。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: 全非同期通知へ `runId: Guid` を付与して `model.AiPipelineRunId` と照合し、共通 CTS/runId 生成ヘルパーと `ST-RETRY-01` / `05` 検証を追加した。
- **対応コミット**: Cycle 3 修正で対応済み

### 指摘3

- **内容**: `ReloadSettings` 同期/非同期矛盾・`LoadedConfiguration` 項目不足、および `DeleteRule` 失敗時 `ConfirmDialog` 未閉鎖・`SaveEditingRule` 失敗時 `SelectedRuleId` ズレ。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: `ReloadSettings` を同期ロードに統一し、`LoadedConfiguration` へ `EnvironmentName`/`SavePath` を追加、失敗時ロールバック・`ConfirmDialog = None` 閉鎖を規定した。
- **対応コミット**: Cycle 3 修正で対応済み

### 指摘4

- **内容**: `loadConfigurationWithMetadata` のコンテキスト注入経路欠如（`UT-RED-01`/`IT-CONF-01` 実環境依存）と `E2E-07` 単一要素 `ComboBox` 偽陽性。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: `ConfigLoadContext` / `loadConfigurationWithMetadataWithContext` を追加し、`E2E-07` を `Views.shouldDispatchModelSelection` 判定＋実モデル切替再描画検証へ具体化した。
- **対応コミット**: Cycle 3 修正で対応済み

### 指摘5

- **内容**: `savePatch` 日本語エンコーダ明記、`Settings.load` での `parsePartial`＋`StringOptionConverter` 責務整理、`rules` 配列パース警告、0 バイト空ファイル境界追加。
- **重要度**: Minor
- **対応方針**: 採用
- **理由**: `design.md` §2.1・§2.5 および `test_plan.md`（`UT-CONV-06〜10`, `UT-PATCH-10`, `UT-ERR-01〜02`, `IT-FAULT-02`）へ反映した。
- **対応コミット**: Cycle 3 修正で対応済み

### 指摘6

- **内容**: `mockup.html` と `design.md`/`tasks.md` 間のボタン配置・モーダル開時背面バナー二重描画の不一致。
- **重要度**: Minor
- **対応方針**: 採用
- **理由**: 背面バナー非表示条件・3 ボタン統一・コントロールパネル常設ボタン・シミュレーション分離を全成果物で一致させた。
- **対応コミット**: Cycle 3 修正で対応済み

---

## 4. 判定

- **LGTM**: false
- **条件付きLGTM**: なし（指摘 1〜4 の Major 指摘解消後に Cycle 3 再レビューを実施）
- **保留指摘（申し送り）**: なし
- **次のアクション**: 対応後再レビュー

---

# レビュー記録: renamer-settings-resolution-and-serialization / propose / Propose-User (Cycle 3)

- **日時**: 2026-11-22
- **フェーズ**: propose
- **タスク**: 対象外（Propose フェーズ Cycle 3 再レビュー）
- **レビュアー**: user
- **使用プロトコル**: openspec/templates/checklist_propose.md
- **対象成果物**: openspec/changes/renamer-settings-resolution-and-serialization/{proposal.md, specs/companion-file-rename/spec.md, design.md, test_plan.md, tasks.md, mockup.html, reviews.md}, src/TagBasedVideoManager.Renamer/appsettings.json, openspec/specs/companion-file-rename/spec.md

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: 仕様書（`specs/**/spec.md`）のブラインド比較レビュー（Cycle 2 指摘 1〜5 反映検証と突合）

- **確認内容・回答**:
  Cycle 2 指摘 1〜5（①ベース `appsettings.json` `"apiKey": null` 時の誤案内防止、②`ReloadSettings` での選択中 `TargetDirectory` 維持、③`mockup.html` と設計・仕様・タスクのボタン完全一致化、④保存失敗ロールバック時の付随状態維持・ダイアログ閉鎖、⑤`E2E-02` 実リポジトリ構成ケース追加）が全成果物へ漏れなく反映されていることを確認した。
- **確認根拠**: `proposal.md` L12, L33-48, L75-80; `specs/companion-file-rename/spec.md` L7, L30-103; `design.md` L32-40, L51-60, L64-85, L110-188; `test_plan.md` L28-42, L141, L168, L242-259, L269-275; `tasks.md` L26, L37, L47-54; `mockup.html` L36-89, L91-184, L247-252, L328-358; `src/TagBasedVideoManager.Renamer/appsettings.json` L1-24

### 設問2: 都合の良い前提（暗黙の仮定）の告発

- **確認内容・回答**: Cycle 1〜2 で挙げた暗黙の仮定はいずれも仕様・設計・テスト・モックで明示的に解消済み。
- **確認根拠**: `specs/companion-file-rename/spec.md` L102-103; `design.md` L156-183; `test_plan.md` L270-272; `Views.fs` L754-757, L1165, L1662

### 設問3: UIモック（`mockup.html`）の初心者ペルソナ検証（UI変更時のみ）

- **確認内容・回答**: Zero-State / Error-State 回復導線およびモーダル内バナー・枠外シミュレーション分離が良好。ただし `mockup.html` L351 のモーダル内バナー `✕` ボタンのみモック上でモーダルごと閉じる遷移になっている点を Minor として記録。
- **確認根拠**: `mockup.html` L36-89, L91-184, L223-259, L301-419; `design.md` L58, L156-183, L188

---

## 2. Adversarial QA（破壊テスト実行結果）※Apply実装レビュー時のみ

対象外（Propose フェーズの再レビューのため、実装コードおよび破壊テスト実行は対象外）。

---

## 3. 指摘事項

### 指摘1

- **内容**: `mockup.html` L351 のモーダル内 `errorBanner` にある `✕` ボタンが `@click="state = 'normal'"` となっており、モックプレビュー上でバナーだけ閉じる代わりにモーダル全体が閉じてしまう。
- **重要度**: Minor
- **対応方針**: 保留
- **理由**: 実アプリの設計（`design.md` L176-177）および実装構造（`Views.fs` L2491 の共通 `errorBanner` が `DismissError` を発行し `IsRuleManagerOpen` を変更しない）ではモーダルを閉じずにバナーのみ閉じる動作が明確に定義されており、実装・仕様上の阻害要因にはならないため。
- **対応コミット**: 保留（Archive 時に `handover.md` へ転記または Apply Task 2.3 で併せて確認）

---

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし（`Critical`: 0 件、`Major`: 0 件、`Minor`: 1 件、`Suggestion`: 0 件）
- **保留指摘（申し送り）**: 指摘 1（`mockup.html` L351 モーダル内バナー `✕` のモック遷移）
- **次のアクション**: 次フェーズへ（SE Reviewer の `LGTM: true` および Gate 1 通過後、Apply フェーズへ進行）

---

# レビュー記録: renamer-settings-resolution-and-serialization / propose / Propose-SE (Cycle 3)

- **日時**: 2026-11-22
- **フェーズ**: propose
- **タスク**: 対象外（Propose フェーズ Cycle 3 再レビュー）
- **レビュアー**: se
- **使用プロトコル**: openspec/templates/checklist_propose.md
- **対象成果物**: openspec/changes/renamer-settings-resolution-and-serialization/{proposal.md, specs/companion-file-rename/spec.md, design.md, test_plan.md, tasks.md, mockup.html, reviews.md}

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: 設計書の故障シミュレーション — 外部依存ハング・切断時の連鎖崩壊検証（checklist §2.1）

- **確認内容・回答**:
  OpenRouter API / `ddgs` 5 秒ハング中の `ExecuteScanAndPropose` 割り込み再実行と `runId` ガードの相互作用を検証した結果、`ScanCompleted` が対象 0 件 / エラー / API キー無効分岐に入った場合に現行 `State.fs`（L328, L335, L341, L364, L395, L406）が `IsRequestingAi = false` へ戻しておらず、旧パイプラインの `AllAiProposalsCompleted oldRunId` も無視されるため **`IsRequestingAi` が `true` のまま永久ロック（スピナーゾンビ化・リネームボタン永久非活性）** になる穴を特定した。ファイルロック／0 バイト／破損 JSON への耐性は妥当。
- **確認根拠**: `design.md` L142-147, L154, L164-173; `State.fs` L318-411, L434-447; `Views.fs` L753-768

### 設問2: 設計書の故障シミュレーション — 可観測性（調査可能性）の検証（checklist §2.2）

- **確認内容・回答**: `TraceLogs`, `Warnings` バナー、`savePatch` 段階別エラーにより可観測性は十分。ただし `Views.shouldDispatchModelSelection` の FuncUI 同一クロージャ参照方針を明記すべき。
- **確認根拠**: `design.md` L40, L82-84, L128-130, L143, L147, L157, L163, L178, L194; `Views.fs` L682-700

### 設問3: テスト計画書の死角告発 — 100%パスしても抜けるバグ（最大の死角トップ2）（checklist §3.1）

- **確認内容・回答**:
  1. `IsRequestingAi = true` 中の `ExecuteScanAndPropose` 割り込み → 0 件 / エラー / キー不備での `IsRequestingAi = false` リセット検証が `ST-SCAN-01`〜`04` に欠落している。
  2. `parsePartial` の引用符付き空文字（`"\"\""` 等）正規化共通化、および `DT-MERGE-08`（`test_plan.md` L170）と `design.md` L126 の `IsBaseLayer = T` 時 `ApiKeySourcePath` 表記微差の整合が必要。
- **確認根拠**: `test_plan.md` L80, L170, L240-243; `design.md` L126; `Settings.fs` L156-160

### 設問4: テスト計画書の死角告発 — モックの嘘（偽の安心感）の検証（checklist §3.2）

- **確認内容・回答**: `Settings.fs` には `ConfigLoadContext` 注入経路があるが、`State.fs` の `init ()` / `ReloadSettings` に `ConfigLoadContext` 注入境界がなく、`ST-RELOAD-*`・`E2E-03` が本番経路を通る無汚染テストとして成立しない。
- **確認根拠**: `design.md` L132-133, L156, L160; `test_plan.md` L249-251, L269, L271

### 設問5: タスクリストの手戻りリスク検証 — 最も詰まるリスクが高いタスクの特定（checklist §5.1）

- **確認内容・回答**: Task 2.2 / Task 2.3 の `ST-RELOAD-*`・`E2E-03` 実装が、`State.fs` 側への `ConfigLoadContext` 注入境界未定義により途中で設計変更の手戻りを起こすリスクが高い。
- **確認根拠**: `tasks.md` L43, L47, L49, L52; `design.md` L156, L160

### 設問6: タスクリストの手戻りリスク検証 — 完了基準の検証可能性（checklist §5.2）

- **確認内容・回答**: 概ね明確だが、Task 2.2 L43 の最小スタブ一覧へ `State.initWithContext` / `State.updateWithContext` 等を追加する必要がある。
- **確認根拠**: `tasks.md` L11-27, L33-55, L61-77

---

## 2. Adversarial QA（破壊テスト実行結果）※Apply実装レビュー時のみ

対象外（Propose フェーズの再レビューのため、実装コードおよび破壊テスト実行前段階）。

---

## 3. 指摘事項

### 指摘1

- **内容**: `ExecuteScanAndPropose` による割り込み再スキャンと `runId` ガード併用時、`IsRequestingAi = true` の状態から再スキャンして「対象 0 件」「スキャンエラー」「API キー無効（未設定・プレースホルダー）」へ入った場合に `IsRequestingAi` が `true` のまま永久ロック（スピナーゾンビ化）する。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: `IsRequestingAi = false`・`AiPipelineRunId = None` の明示リセットおよび `ST-SCAN-*` へのテスト追加が必要。
- **対応コミット**: ユーザーエスカレーション中（Cycle 3 上限到達のため判断待ち）

### 指摘2

- **内容**: `State.fs` の `init ()` および `ReloadSettings` に `ConfigLoadContext` 注入境界（`State.initWithContext` / `State.updateWithContext` 等）が設計されておらず、`ST-RELOAD-01`〜`03`・`E2E-01`・`E2E-03` が本番経路を通る無汚染テストとして成立しない。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: `design.md` §2.6 と `tasks.md` Task 2.2/2.3 へ薄いコンテキスト注入境界を明記する必要がある。
- **対応コミット**: ユーザーエスカレーション中（Cycle 3 上限到達のため判断待ち）

### 指摘3

- **内容**: `Views.shouldDispatchModelSelection` の FuncUI 同一クロージャ参照方針（`ComboBox.onSelectedIndexChanged` 内で `availableModels |> List.tryItem idx` を渡す）の明記。
- **重要度**: Minor
- **対応方針**: 採用
- **理由**: FuncUI 差分更新中の古いクロージャ値比較による誤発火を防ぐため。
- **対応コミット**: ユーザーエスカレーション中（Cycle 3 上限到達のため判断待ち）

### 指摘4

- **内容**: `parsePartial` / `StringOptionConverter` の `normalizeApiKeyString` 共通化と `DT-MERGE-08`（`IsBaseLayer = T` 時 `ApiKeySourcePath`）表記微差の整合。
- **重要度**: Minor
- **対応方針**: 採用
- **理由**: 設計書とテスト計画書の期待値・正規化契約を完全一致させるため。
- **対応コミット**: ユーザーエスカレーション中（Cycle 3 上限到達のため判断待ち）

---

## 4. 判定

- **LGTM**: false
- **条件付きLGTM**: なし（最大 3 サイクル到達のためユーザーへエスカレーション。Major 指摘 1〜2 解消後に再レビュー・Gate 1 検証を実施）
- **保留指摘（申し送り）**: なし
- **次のアクション**: ユーザーへエスカレーション（判断確認後に設計反映・再レビュー）

---

# レビュー記録: renamer-settings-resolution-and-serialization / propose / Propose-SE (Cycle 4)

- **日時**: 2026-11-22
- **フェーズ**: propose
- **タスク**: 対象外（Propose フェーズ Cycle 4 再レビュー）
- **レビュアー**: se
- **使用プロトコル**: openspec/templates/checklist_propose.md
- **対象成果物**: openspec/changes/renamer-settings-resolution-and-serialization/{proposal.md, specs/companion-file-rename/spec.md, design.md, test_plan.md, tasks.md, mockup.html, reviews.md}

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: Cycle 3 指摘 1〜4 の解消状況検証および設計書の故障シミュレーション — 外部依存ハング・切断時の連鎖崩壊検証（checklist §2.1）

- **確認内容・回答**:
  Cycle 3 の指摘 1〜4（① `ExecuteScanAndPropose` 割り込み再スキャン＋`ScanCompleted` 全非 AI 分岐での `IsRequestingAi = false` / `AiPipelineRunId = None` リセットおよび `ST-SCAN-05`、② `State.initWithContext` / `State.updateWithContext` コンテキスト注入境界、③ `Views.shouldDispatchModelSelection` の FuncUI 同一クロージャ参照方針、④ `normalizeApiKeyString` 共通化と `IsBaseLayer = true` 時 `ApiKeySourcePath = None` 表記整合）がすべて解消されていることを確認した。外部通信 5 秒ハング・I/O ロック・0 バイト破損 JSON に対する耐性も妥当である。
- **確認根拠**: `design.md` L35-41, L120, L131, L143-152, L156-190; `test_plan.md` L41, L139, L170, L234, L238, L244, L250-252, L270, L272, L276; `tasks.md` L43-53; `State.fs` L449-471; `Views.fs` L682-700

### 設問2: 設計書の故障シミュレーション — 可観測性（調査可能性）の検証（checklist §2.2）

- **確認内容・回答**: `TraceLogs`（探索ステップ採否・`%APPDATA%` / `.env` 移行案内）、`Warnings` バナー（JSON 構文エラー・0 バイト・旧 `{ "value": ... }`・`rules` 異常・保護警告）、環境別 `SettingsSavePath` / 出典パス案内、`savePatch` 段階別・試行回数・行/列位置通知（モーダル内バナー含む）により、1 分以内での原因特定が可能であることを確認した。
- **確認根拠**: `design.md` L41, L83-85, L126, L131-135, L147-152, L163, L169, L182-189, L201; `mockup.html` L35-66, L153-184, L327-358

### 設問3: テスト計画書の死角告発 — 100%パスしても抜けるバグ（最大の死角トップ2）（checklist §3.1）

- **確認内容・回答**: (1) `CancelAiProposal` 後の遅延 `oldRunId` 通知および複数候補処理中における別候補個別再試行時の先行他候補 `IsAiProcessing` クリア、(2) JSONC コメント除去のユーザー告知・最大パス近傍 `.tmp` 生成境界が未定義だが、永久ロックやデータ破壊には至らない。
- **確認根拠**: `test_plan.md` L129, L244-249; `design.md` L33, L151, L171-173; `State.fs` L449-508

### 設問4: テスト計画書の死角告発 — モックの嘘（偽の安心感）の検証（checklist §3.2）

- **確認内容・回答**: `ConfigLoadContext` / `initWithContext` / `updateWithContext` 注入境界と `Views.shouldDispatchModelSelection` 実検証の追加により、設定解決・保存・状態管理・GUI 描画の本番経路に対する偽の安心感は解消された。
- **確認根拠**: `test_plan.md` L19-22, L54-56, L105-116, L226, L229, L234, L270-276; `proposal.md` L57

### 設問5: タスクリストの手戻りリスク検証 — 最も詰まるリスクが高いタスクの特定（checklist §5.1）

- **確認内容・回答**: 実設定ファイル破壊および注入境界のボトルネックは解消済み。Task 1.2 の先行スタブ列挙補完と Task 2.2 の `RenameIntegrationE2ETests.fs` `runId` 追従のみ作業時留意事項として記録する。
- **確認根拠**: `tasks.md` L17, L23, L25, L43; `test_plan.md` L68-70, L105, L195

### 設問6: タスクリストの手戻りリスク検証 — 完了基準の検証可能性（checklist §5.2）

- **確認内容・回答**: 全フェーズの完了基準・コマンド・エビデンス・C0/C1/C2 基準・SemVer 更新値が具体的に検証可能であることを確認した。
- **確認根拠**: `tasks.md` L14, L17, L40, L43, L55, L62-67, L73-77

---

## 2. Adversarial QA（破壊テスト実行結果）※Apply実装レビュー時のみ

対象外（Propose フェーズの再レビューのため、実装コードおよび破壊テスト実行前段階）。

---

## 3. 指摘事項

### 指摘1

- **内容**: `design.md` L158 の `Msg.SettingsLoadedWithMeta`（同期ロード統一後の用途要否）および `CancelAiProposal` 実行時の `AiPipelineRunId = None` 明示リセットが `design.md` §2.6 内で明記されていない。
- **重要度**: Minor
- **対応方針**: 保留
- **理由**: `CancelAiProposal` は即座に `IsRequestingAi = false` を設定するため永久ロックには至らず、`SettingsLoadedWithMeta` も Apply Task 2.3 の `State.fs` 実装時に `updateWithContext` での処理定義または不要削除として安全に整理可能なため。
- **対応コミット**: 保留（Apply Task 2.3 で整理・反映、または Archive 時に `handover.md` へ転記）

### 指摘2

- **内容**: `tasks.md` Task 1.2（L17）の先行スタブ一覧に `resolveConfigDirectory` / `normalizeApiKeyString` / `StringOptionConverter` が含まれておらず、Task 2.2（L43）の `runId` 付き `Msg` シグネチャ変更追従対象として `RenameIntegrationE2ETests.fs` の記載が省略されている。
- **重要度**: Minor
- **対応方針**: 保留
- **理由**: いずれも `test_plan.md`・`design.md` 上でシグネチャおよび利用箇所が確定しており、Apply Task 1.2 / 2.2 実施時にコンパイル整合として容易に補完できる軽微な記述省略のため。
- **対応コミット**: 保留（Apply Task 1.2 / 2.2 で対応、または Archive 時に `handover.md` へ転記）

---

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし（`Critical`: 0 件、`Major`: 0 件、`Minor`: 2 件、`Suggestion`: 0 件。Cycle 3 指摘 1〜4 全解消確認済み）
- **保留指摘（申し送り）**: 指摘 1（`SettingsLoadedWithMeta` 整理・`CancelAiProposal` 時 `AiPipelineRunId = None`）、指摘 2（Task 1.2 スタブ一覧補完・Task 2.2 `RenameIntegrationE2ETests.fs` `Msg` 追従）
- **次のアクション**: 次フェーズへ（Gate 1 検証 `openspec validate` / `verify-gate.ps1 -Gate propose` 通過後、Apply フェーズへ進行）
