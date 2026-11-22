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

---

# レビュー記録: renamer-settings-resolution-and-serialization / apply / Task 1.1 (Cycle 1)

- **日時**: 2026-11-22
- **フェーズ**: apply
- **タスク**: 1.1 既存テストの実設定ファイル隔離・旧 `%APPDATA%` テスト整理
- **レビュアー**: qa
- **使用プロトコル**: openspec/templates/checklist_apply_test.md
- **対象成果物**: test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs, test/TagBasedVideoManager.Renamer.Tests/StateTests.fs, openspec/changes/renamer-settings-resolution-and-serialization/tasks.md

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: トートロジーテスト（実装式のコピペ）と形式的アサーションの告発（checklist §1）

- **確認内容・回答**:
  - `SettingsTests.fs` L254-263（`UT-04`）および L854-861（`IT-02`）では、`path |> should equal expectedEnvPath`（`tempBaseDir` / `tempDir` 配下）で完全一致検証した直後に `Path.GetDirectoryName(path)` と `StartsWith(appDataRoaming)` の重複・形式アサーションが付いている。
  - `StateTests.fs` で `withIsolatedStateTest (fun tempSavePath -> ...)` が `tempSavePath` を注入している保存を伴う全 7 テストにおいて `ErrorMessage |> should equal None` 等を確認しておらず、実保存失敗を見逃す形式的隔離となっている。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs` L250-265, L847-875
  - `test/TagBasedVideoManager.Renamer.Tests/StateTests.fs` L85-142, L386-397, L528-562, L795-858, L1495-1529

### 設問2: 甘いアサーション・無意味なモックの告発（checklist §2）

- **確認内容・回答**:
  - `SettingsTests.fs` L389-456 は固定 JSON を定義しながら `SelectedModel |> should not' (be EmptyString)`、`Rules.Length |> should be (greaterThan 0)` の曖昧アサーションのまま。
  - `SettingsTests.fs` L287-319 はテスト名で `loadConfiguration` としているが実装は `loadConfigurationWithConfig` を呼んでおり L459 と重複、`Settings.loadConfiguration` 経由のオーバーライドロード検証が欠落している。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs` L287-319, L389-456, L459-496

### 設問3: ミューテーション思考（バグのすり抜け検証）（checklist §3）

- **確認内容・回答**:
  1. `SettingsTests.fs` L1011-1036 は `tempDir` に `appsettings.Development.json` 等を置かず既定値 `240` と比較しているため、環境名サニタイズ破壊でも Pass する。
  2. `SettingsTests.fs` L447-454 の `prodJson` は既定値と同値であり、`Settings.load` が `defaultSettings ()` を返す破壊でも Pass する。
  3. `StateTests.fs` の保存系 7 テストは `ErrorMessage |> should equal None` を確認しておらず、保存失敗でも Pass する。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs` L414-455, L1011-1036
  - `src/TagBasedVideoManager.Renamer/Settings.fs` L317-327
  - `src/TagBasedVideoManager.Renamer/State.fs` L264-270, L312-315, L821-905
  - `test/TagBasedVideoManager.Renamer.Tests/StateTests.fs` L386-397, L528-562, L795-858, L1495-1529

### 設問4: フレーキー要因・環境依存・隔離漏れ・クリーンアップ安全性の摘出（checklist §4）

- **確認内容・回答**:
  - `SettingsTests` での `DOTNET_ENVIRONMENT` 等プロセスグローバル環境変数書換と `StateTests` / `RenameIntegrationE2ETests` 間の `[<Collection>]` 直列化がなく並列実行競合リスクがある。
  - `withIsolatedStateTest` で `tempDir` 作成後のバックアップ取得が `try ... finally` 外にあり、また `with _ -> ()` で復元失敗を握りつぶしている。
  - `StateTests.fs` L145 の `State.init ()` が実設定依存のまま、かつ `RenameIntegrationE2ETests.fs` の 9 箇所の `State.init ()` が未隔離である。また親環境で `DeployScript*` 以外の非 Deploy テスト全件結果の確認も必要。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/StateTests.fs` L12-142, L145-156
  - `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs` L137-163, L268-283, L645-672, L732-750
  - `test/TagBasedVideoManager.Renamer.Tests/RenameIntegrationE2ETests.fs` L544, L628, L691, L753, L844, L1032, L1104, L1163, L1299

---

## 2. Adversarial QA（破壊テスト実行結果）※Apply実装レビュー時のみ

対象外（Apply テストレビューフェーズのため `checklist_apply_test.md` 観点のみ実施）。

---

## 3. 指摘事項

### 指摘1

- **内容**: `SettingsTests.fs` の一時フィクスチャ化箇所に甘いアサーション・ミューテーションすり抜け・`loadConfiguration` 未検証経路が残存（L389-456 曖昧アサーション＆既定値一致 JSON、L1011-1036 `240` 比較のみ、L287-319 `loadConfigurationWithConfig` 重複）。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: 実装破壊時にすり抜ける甘いテストであるため。
- **対応コミット**: Cycle 2 修正で対応予定

### 指摘2

- **内容**: `StateTests.fs` の保存を伴う全 7 テスト（L386, L528, L537, L795, L815, L838, L1495-1529）で `ErrorMessage |> should equal None` 等の保存成否検証が欠落。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: 保存失敗を見逃す形式的テストであるため。
- **対応コミット**: Cycle 2 修正で対応予定

### 指摘3

- **内容**: テストクラス間（`SettingsTests`, `StateTests`, `RenameIntegrationE2ETests`）の `[<Collection>]` 直列化不在による環境変数/`bin/` 競合リスク、`withIsolatedStateTest` の `try ... finally` 外準備＆例外握りつぶし、`StateTests.fs` L145 および `RenameIntegrationE2ETests.fs` 9 箇所の `State.init ()` 隔離漏れ。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: フレーキー要因・クリーンアップ安全性・Task 1.1 の隔離要件に直結するため。
- **対応コミット**: Cycle 2 修正で対応予定

### 指摘4

- **内容**: `SettingsTests.fs` L258-263 / L855-861 の冗長アサーションと両テストファイル末尾の余分な空行。
- **重要度**: Minor
- **対応方針**: 採用
- **理由**: 可読性・フォーマット統一のため。
- **対応コミット**: Cycle 2 修正で対応予定

---

## 4. 判定

- **LGTM**: false
- **条件付きLGTM**: なし（Major 指摘 1〜3 解消および全非 Deploy テスト Pass・実設定不変確認後に Cycle 2 再レビュー）
- **保留指摘（申し送り）**: なし
- **次のアクション**: 対応後再レビュー（`qa-tester` で指摘 1〜4 を修正）
- **テスト品質評価**: 要改善
- **承認者**: QA Reviewer Agent

---

# レビュー記録: renamer-settings-resolution-and-serialization / apply / Task 1.1 (Cycle 2)

- **日時**: 2026-11-22
- **フェーズ**: apply
- **タスク**: 1.1 既存テストの実設定ファイル隔離・旧 `%APPDATA%` テスト整理（Cycle 2 再レビュー）
- **レビュアー**: qa
- **使用プロトコル**: openspec/templates/checklist_apply_test.md
- **対象成果物**: test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs, test/TagBasedVideoManager.Renamer.Tests/StateTests.fs, test/TagBasedVideoManager.Renamer.Tests/RenameIntegrationE2ETests.fs, openspec/changes/renamer-settings-resolution-and-serialization/tasks.md

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: トートロジーテスト（実装式のコピペ）と重複・形式アサーションの検証（checklist §1）

- **確認内容・回答**:
  - Cycle 1 指摘 4 で挙げた `SettingsTests.fs` `UT-04`（L440-452）および `IT-02`（L1050-1075）の冗長アサーション（`Path.GetDirectoryName`・`StartsWith(appDataRoaming)`）が整理され、`path |> should equal expectedEnvPath` / `saveTarget |> should equal expectedPath` による簡潔かつ明確な期待値比較になった。
  - `SettingsTests.fs` L578-659 / L1211-1279 および `StateTests.fs` の期待値はすべて具体的な固定リテラル値で定義されており、実装ロジックのコピペによるトートロジーは存在しない。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs` L440-452, L578-659, L1050-1075, L1211-1279

### 設問2: 甘いアサーション・無意味なモックの告発と Cycle 1 指摘 1〜2 解消確認（checklist §2）

- **確認内容・回答**:
  - **指摘 1-(1) 解消**: `SettingsTests.fs` L578-659（`一時ディレクトリの appsettings.Development.json と appsettings.Production.json は正しくデシリアライズできる`）で、両 JSON に既定値（`240`, `""`, `"meta-llama/..."`）と異なる非既定値（`200` / `180`, `"D:\\ProdVideos"`, `"fixture/dev-model:free"` / `"fixture/prod-model:v1"`）を設定し、`TargetDirectory`, `PathLengthThreshold`, `SelectedModel`, `ApiKey`, `Rules.Length = 1` および先頭ルール全項目（`Id`, `Name`, `Pattern`, `PromptInstruction`, `Order`, `EnableWebSearch`）を具体リテラルで厳密検証している。
  - **指摘 1-(3) 解消**: `SettingsTests.fs` L474-508（`loadConfiguration は一時ディレクトリ内の構成別設定ファイルをベース設定にオーバーライドロードする`）が `Settings.defaultConfigurationName` に対応したファイルを作成し、実際に `Settings.loadConfiguration None (Some dummyEnvPath)` を呼び出してオーバーライドおよびベース非既定値（`PathLengthThreshold = 210`）の継承を検証している。
  - **指摘 2 解消**: `StateTests.fs` の保存系全 7 テスト（`MoveRuleOrder` L269, `ModelSelected` L408, `OpenRuleManager と SaveEditingRule` L431, `SaveEditingRule` 既存更新 L681・新規追加 L708, `RequestDeleteRule`→`DeleteRule` L733, `State.update の各種メッセージ遷移` 内の `DeleteRule`/`SaveRule`/`MoveRuleOrder` L1376, L1384, L1396, L1403, L1408, L1413）で `ErrorMessage |> should equal None` が追加され、保存失敗の黙殺が解消された。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs` L474-508, L578-659
  - `test/TagBasedVideoManager.Renamer.Tests/StateTests.fs` L260-272, L403-439, L672-738, L1374-1415

### 設問3: ミューテーション思考（バグのすり抜け検証）（checklist §3）

- **確認内容・回答**:
  - **指摘 1-(2) 解消**: `SettingsTests.fs` L1211-1279（`loadConfigurationWithConfig は空白環境名・パストラバーサル文字列・不在ディレクトリ指定時に安全フォールバックする`）で、環境変数を一時クリアした上で隔離ディレクトリにフォールバック先用の非既定値（`199`, `"fallback-env-model"`）を配置し、かつトラバーサル・不正名参照先に別の罠値（`222`, `"evil-model"`）を配置した。これにより、サニタイズやトラバーサル防止の条件を反転・削除すれば確実に `240` または `222` となって Red（失敗）になる。
  - **Task 1.1 範囲における死角・スコープ境界の明確化**: 現行 `State.fs`（L265）は未修正のまま Task 2.1 へ進む段階であるため、`StateTests.fs` 内の保存系テストは「実設定ファイル不変 + 暫定退避復元 + `ErrorMessage = None`」までを検証しており、`tempSavePath` 実ファイル自体への書き込み・部分更新確認は計画通り Phase 2（Task 2.1 の `State.fs` 前倒し保護および Task 2.2 の `ST-MODEL-01〜02`, `ST-RULE-01〜05`）の責務となる。また未変更の既存テスト `SettingsTests.fs` L1178（`env*with:bad?chars`）は既定値 `240` 比較のままだが、同じ `sanitize` ロジックの検証は上記 L1211 で厳格にカバーされている。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs` L1178-1191, L1211-1279
  - `src/TagBasedVideoManager.Renamer/Settings.fs` L317-327
  - `openspec/changes/renamer-settings-resolution-and-serialization/tasks.md` L11-14, L39

### 設問4: フレーキー要因・環境依存・隔離漏れ・クリーンアップ安全性の確認（checklist §4）

- **確認内容・回答**:
  - **指摘 3-(1) 並列実行直列化**: `SettingsTests.fs` L11-12 で `[<CollectionDefinition("IsolatedSettingsAndState", DisableParallelization = true)>]` が定義され、`SettingsTests`（L198）、`StateTests`（L9）、`RenameIntegrationE2ETests`（L10）の 3 モジュールすべてに `[<Collection("IsolatedSettingsAndState")>]` が付与された（他ファイルからの `Settings.` / `SetEnvironmentVariable` 呼び出しがないことも全テスト検索で確認済み）。
  - **指摘 3-(2) クリーンアップ安全性と無変更書戻し回避**: `SettingsIsolationHarness.withIsolatedSettingsEnvironment`（`SettingsTests.fs` L93-151）で、`realSnapshots` と `transientBackups` の取得が `try ... finally` の内側に入り、一時ディレクトリ削除と復元の失敗が `cleanupErrors` へ集約され、テスト本体成功時にクリーンアップ失敗があれば `failwith` で通知されるようになった。また L135 の `System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(path), bytes)` により内容変更時のみ書き戻すため、不要なタイムスタンプ更新が発生しない。
  - **指摘 3-(3)(4) 隔離漏れ解消と実ファイル不変の実測**:
    - `StateTests.fs` L19-30, L741-745, L878-902 の `State.init ()` 呼び出しが `SettingsIsolationHarness.withCleanInitEnvironment`（一意のクリーン環境名 `IsolatedCleanEnv_<guid>` を使用し開発者の `appsettings.Development.json` を読まない）へ移行された。
    - `RenameIntegrationE2ETests.fs` の `State.init ()` 呼び出し全 9 箇所（L531, L630, L695, L759, L852, L1042, L1116, L1177, L1254）が `SettingsIsolationHarness.withIsolatedSettingsEnvironment` ＋ `initIsolatedModel` へ隔離された。
    - ワークスペース実測で、`src/TagBasedVideoManager.Renamer/` 配下の設定ファイルは未変更であり、`src/TagBasedVideoManager.Renamer/appsettings.Development.json` と `test/TagBasedVideoManager.Renamer.Tests/bin/Debug/net10.0/appsettings.Development.json` の SHA256 ハッシュも完全一致（`3CC161541975F7DC6F984C3A8CE430F14DE290C6D953699FFDC1D59B09BF1FEB`）している。
    - 親エージェント側で非 Deploy 全テスト 389 件（`SettingsTests`, `StateTests`, `RenameIntegrationE2ETests` 他 7 テストクラス）が **合計: 389, 失敗: 0, 成功: 389, スキップ済み: 0** で通過している（なお除外された `DeployScript*` / `BugReproductionTests` は `git ls-files --eol scripts/deploy.sh` が `i/lf w/crlf` となる Windows チェックアウト既存のスコープ外要因によるものであることを実測確認済み）。
  - **規約準拠**: 行末空白・タブ 0 件、`Skip` / `Ignore` 属性 0 件、日本語 XML ドキュメントコメント準拠。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs` L10-198
  - `test/TagBasedVideoManager.Renamer.Tests/StateTests.fs` L9-30, L741-745, L878-902
  - `test/TagBasedVideoManager.Renamer.Tests/RenameIntegrationE2ETests.fs` L10, L531, L630, L695, L759, L852, L1042, L1116, L1177, L1254
  - 実測: `git status -s`、`Get-FileHash`（`src/` と `bin/` の `appsettings*.json` 全 3 ファイル SHA256 一致）、`git ls-files --eol scripts/deploy.sh`（`i/lf w/crlf`）

---

## 2. Adversarial QA（破壊テスト実行結果）※Apply実装レビュー時のみ

対象外（Apply テストレビューフェーズのため `checklist_apply_test.md` に基づくテストコード批判的レビュー・ミューテーション思考・隔離安全性検証を実施）。

---

## 3. 指摘事項

なし（理由: Cycle 1 の指摘 1〜4 がすべて適切に解消され、テストの厳格性・ミューテーション耐性・並列実行直列化・例外時クリーンアップ安全性・隔離網羅性および非 Deploy テスト全 389 件 Pass・実設定ファイル不変を確認済みのため。なお `SettingsIsolationHarness` の `transientBackups` は `tasks.md` 通り Task 2.1 での `State.fs` 保存先切替までの暫定保護措置として機能している）。

---

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし（`Critical`: 0 件、`Major`: 0 件、`Minor`: 0 件、`Suggestion`: 0 件）
- **保留指摘（申し送り）**: なし
- **次のアクション**: 次タスクへ（Task 1.1 完了、`[x]` 更新・Gate 2 確認の上で Task 1.2 へ進行）
- **テスト品質評価**: 堅牢（合格）
- **承認者**: QA Reviewer Agent

---

# レビュー記録: renamer-settings-resolution-and-serialization / apply / Task 1.2 (Cycle 1)

- **日時**: 2026-11-22
- **フェーズ**: apply
- **タスク**: 1.2 不具合再現テスト（Root Cause Reproduction）と Settings.fs 先行単体・結合テストの作成・Red 確認
- **レビュアー**: qa
- **使用プロトコル**: openspec/templates/checklist_apply_test.md
- **対象成果物**: src/TagBasedVideoManager.Renamer/Settings.fs, test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: トートロジーテスト（実装式のコピペ）と形式的アサーションの告発（checklist §1）

- **確認内容・回答**:
  - 新規追加テスト（53 件）の期待値は原則として具体的な固定リテラルで検証されており、実装ロジックのコピペによるトートロジー計算は存在しない。
  - `UT-ENV-01`（L2507）の `l.Contains(dotEnvPath) && l.Contains(".env")` および `UT-DIR-11`（L1919-1920）の `should equal` 直後の `Assert.False(savePath.StartsWith(appDataDir...))` は軽微な重複条件となっている。
  - 90 件中 39 件 Pass・51 件 Red（意図通り未実装失敗）の内訳は、Task 1.1 既存 37 件＋.NET 10 標準 `System.Text.Json` 挙動による `UT-RED-02` / `UT-CONV-10` の 2 件 Pass であり、`StringOptionConverter` を明示適用する `UT-CONV-01`〜`08` 他 51 件は確実に Red となっている。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs` L1370-1402, L1442-1677, L1912-1923, L2484-2511

### 設問2: 甘いアサーション・無意味なモックの告発（checklist §2）

- **確認内容・回答**:
  - 外部境界（`ConfigLoadContext`・一時ディレクトリ・ReadOnly 属性）のみ隔離しており、内部モックによる空洞化はない。
  - ただし以下の甘いアサーションがある：
    1. `UT-DIR-05`（L1834-1837）で `traceStep9 |> List.exists (fun l -> l.Contains("8") || l.Contains("上限") || l.Contains(unrelatedAppDir))` と `|| l.Contains(unrelatedAppDir)` が OR に含まれており、上限 8 階層到達の不採用トレース欠落でも Pass する。
    2. `DT-PATCH-06`（L2196-2205）で失敗段階（`CreateDirectory` 等）および `%APPDATA%` 非フォールバック確認が不足。
    3. `IT-CONF-01`（L2650）の移行通知確認が `appDataDir` 含有のみ。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs` L1359, L1829-1837, L2196-2205, L2650

### 設問3: ミューテーション思考（バグのすり抜け検証）（checklist §3）

- **確認内容・回答**:
  1. `UT-DIR-05` は `unrelatedAppDir` 採用ログのみで上限到達ログ未出力のバグをすり抜ける。
  2. `UT-ENV-03~06`（L2536-2559）が環境変数 `DOTNET_ENVIRONMENT` / `ASPNETCORE_ENVIRONMENT` を両方 `null` にして `configNameOpt` しかテストしておらず、`Settings.resolveEnvironmentName ()` への不正文字・優先順位・フォールバック破壊が検出できない。
  3. `UT-CONV-02` に `StringOptionConverter.Read` へ `JsonTokenType.Null` を直接渡す検証がなく、`HandleNull = false` 下での未カバー分岐になる。
  4. `normalizeApiKeyString`（L1685-1692）に `"\"   \""`, `" '  ' "` の境界ケースが未追加。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs` L1467-1488, L1685-1692, L1829-1837, L2536-2559
  - `src/TagBasedVideoManager.Renamer/Settings.fs` L266-291

### 設問4: フレーキー要因・環境依存・TDD原則・計画網羅性の摘出（checklist §4）

- **確認内容・回答**:
  - `[<Collection("IsolatedSettingsAndState")>]`・`withTempDir`・`try ... finally` 復元によりフレーキー要因・隔離漏れなし。
  - `Settings.fs` の変更は型 4 種・`StringOptionConverter`・7 関数スタブのみに厳格限定され、Propose Cycle 4 保留指摘 2 も反映済み。`Skip`/`Ignore` 0 件。
- **確認根拠**:
  - `src/TagBasedVideoManager.Renamer/Settings.fs` L11-48, L450-485
  - `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs` L14-15, L201, L1304-1325, L2659-2763

---

## 2. Adversarial QA（破壊テスト実行結果）※Apply実装レビュー時のみ

対象外（Apply テストレビューフェーズのため `checklist_apply_test.md` 観点のみ実施）。

---

## 3. 指摘事項

### 指摘1

- **内容**: `UT-DIR-05`（L1834-1837）に `|| l.Contains(unrelatedAppDir)` が含まれており、親 8 階層到達不採用トレースが欠落しても Pass する（甘いアサーション・ミューテーションすり抜け）。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: `test_plan.md` `UT-DIR-05` の上限到達トレース記録要件を満たすため。
- **対応コミット**: Cycle 2 修正で対応予定

### 指摘2

- **内容**: `UT-ENV-03~06`（L2536-2559）で環境変数を `null` クリアして引数経由のみ検証しており、`Settings.resolveEnvironmentName ()` 直接呼び出しでの優先順位・不正文字サニタイズ・フォールバック検証が欠落。
- **重要度**: Major
- **対応方針**: 採用
- **理由**: `resolveEnvironmentName` のバグすり抜けを防ぐため。
- **対応コミット**: Cycle 2 修正で対応予定

### 指摘3

- **内容**: `UT-CONV-02`（L1467-1488）に `StringOptionConverter.Read` へ `JsonTokenType.Null` を直接渡すテストが欠落。
- **重要度**: Minor
- **対応方針**: 採用
- **理由**: `HandleNull = false` 下での C1 分岐死角を防ぐため。
- **対応コミット**: Cycle 2 修正で対応予定

### 指摘4

- **内容**: `DT-PATCH-06`（L2196-2205）で失敗段階（`CreateDirectory` 等）と `%APPDATA%` 非フォールバック（＋ `.tmp` 非残存）確認が不足、また `IT-CONF-01`（L2650）の移行通知確認が緩い。
- **重要度**: Minor
- **対応方針**: 採用
- **理由**: 失敗段階診断と移行導線の厳密検証のため。
- **対応コミット**: Cycle 2 修正で対応予定

### 指摘5

- **内容**: `normalizeApiKeyString` へ `"\"   \""` / `" '  ' "` 境界ケース追加提案、および `UT-DIR-11`（L1920）・`UT-ENV-01`（L2507）の冗長条件整理提案。
- **重要度**: Suggestion
- **対応方針**: 採用
- **理由**: 境界漏れ防止と簡潔化のため。
- **対応コミット**: Cycle 2 修正で対応予定

---

## 4. 判定

- **LGTM**: false
- **条件付きLGTM**: なし（指摘 1〜5 の修正後に Cycle 2 再レビューを実施）
- **保留指摘（申し送り）**: なし
- **次のアクション**: 対応後再レビュー（`qa-tester` で修正し Red 再確認後、`qa-reviewer` Cycle 2 を実施）
- **テスト品質評価**: 要改善
- **承認者**: QA Reviewer Agent

---

# レビュー記録: renamer-settings-resolution-and-serialization / apply / Task 1.2 (Cycle 2)

- **日時**: 2026-11-22
- **フェーズ**: apply
- **タスク**: 1.2 不具合再現テスト（Root Cause Reproduction）と Settings.fs 先行単体・結合テストの作成・Red 確認（Cycle 2 再レビュー）
- **レビュアー**: qa
- **使用プロトコル**: openspec/templates/checklist_apply_test.md
- **対象成果物**: src/TagBasedVideoManager.Renamer/Settings.fs, test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: トートロジーテスト（実装式のコピペ）と冗長アサーションの検証（checklist §1）

- **確認内容・回答**:
  - Cycle 1 指摘 5 の反映により、`UT-DIR-11`（`SettingsTests.fs` L1925-1934）は `savePath |> should equal (Path.Combine(emptyTempDir, "appsettings.Development.json"))` へ整理され、`UT-ENV-01`（L2527）の重複条件 `&& l.Contains(".env")` も `l.Contains(dotEnvPath)` へ簡潔化された。
  - 全 53 件の新規先行テストにおいて期待値は固定リテラル値で記述されており、実装ロジックのコピペによるトートロジーは存在しない。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs` L1925-1934, L2504-2531

### 設問2: 甘いアサーション・無意味なモックの告発および Cycle 1 指摘 1, 4 の解消確認（checklist §2）

- **確認内容・回答**:
  - **指摘 1 解消（`UT-DIR-05` L1841-1850）**: `|| l.Contains(unrelatedAppDir)` が除去され、「親 8 階層上限到達トレース」と「第 2 段階フォールバック先採用トレース」が 2 つの独立した `Assert.True` で必須検証されるようになった。
  - **指摘 4 解消（`DT-PATCH-06` L2208-2225, `IT-CONF-01` L2689-2692）**: `DT-PATCH-06` で失敗段階名、`.tmp` 非残存、`assertSnapshotUnchanged` による `%APPDATA%` 非フォールバックを検証し、`IT-CONF-01` で `TraceLogs` の `appDataDir`・`devPath`・`"移行"` / `"廃止"` 案内を必須検証するよう強化された。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs` L1838-1850, L2207-2225, L2689-2692

### 設問3: ミューテーション思考（バグのすり抜け検証）および Cycle 1 指摘 2, 3, 5 の解消確認（checklist §3）

- **確認内容・回答**:
  - **指摘 2 解消（`UT-ENV-03~06` L2555-2598）**: `Settings.resolveEnvironmentName ()` 直接呼び出しによる優先順位・不正値サニタイズ・`ASPNETCORE_ENVIRONMENT` フォールバック・既定環境名フォールバック・`loadConfigurationWithMetadataWithContext` の `configNameOpt` サニタイズがすべて検証された。
  - **指摘 3 解消（`UT-CONV-02` L1478-1484）**: `Utf8JsonReader` で `JsonTokenType.Null` を直接 `StringOptionConverter.Read` へ渡して `None` 復元を検証するテストを追加した。
  - **指摘 5 解消（`normalizeApiKeyString` L1698-1699）**: `"\"   \""` と `" '  ' "` の境界ケースを追加した。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs` L1467-1495, L1692-1701, L2555-2598

### 設問4: フレーキー要因・環境依存・TDD原則・実行結果の最終確認（checklist §4）

- **確認内容・回答**:
  - `src/TagBasedVideoManager.Renamer/Settings.fs` は公開型 4 種・`StringOptionConverter`・7 関数（すべて `NotImplementedException` スタブ）のみ。
  - 親エージェント再実行結果（`dotnet test test/TagBasedVideoManager.Renamer.Tests/TagBasedVideoManager.Renamer.Tests.fsproj --no-restore -- --filter-class "TagBasedVideoManager.Renamer.Tests.SettingsTests"`）にてビルドエラー 0 件、**合計: 90, 失敗: 51 (意図通り Red), 成功: 39, スキップ済み: 0** を確認済み。`Skip` / `Ignore` 0 件。
- **確認根拠**:
  - `git diff src/TagBasedVideoManager.Renamer/Settings.fs`（L11-48, L450-485）

---

## 2. Adversarial QA（破壊テスト実行結果）※Apply実装レビュー時のみ

対象外（Apply テストレビューフェーズのため `checklist_apply_test.md` 観点のみ実施）。

---

## 3. 指摘事項

なし（Cycle 1 の指摘 1〜5 すべてが解消され、新たな指摘なし）。

---

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし（`Critical`: 0 件、`Major`: 0 件、`Minor`: 0 件、`Suggestion`: 0 件）
- **保留指摘（申し送り）**: なし
- **次のアクション**: 次タスクへ（Task 1.2 完了処理および Task 2.1 `Settings.fs` 実装・`State.fs` 保存先隔離前倒しへ進行）
- **テスト品質評価**: 堅牢（合格）
- **承認者**: QA Reviewer Agent

---

# レビュー記録: renamer-settings-resolution-and-serialization / apply / Task 2.1 (Cycle 1 - PG Reviewer)

- **日時**: 2026-11-22
- **フェーズ**: apply
- **タスク**: 2.1 Settings.fs の実装および State.fs 保存先隔離の前倒し適用
- **レビュアー**: pg
- **使用プロトコル**: openspec/templates/checklist_apply_impl.md
- **対象成果物**: src/TagBasedVideoManager.Renamer/Settings.fs, src/TagBasedVideoManager.Renamer/State.fs

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: 暗黙の前提（都合の良い思い込み）の告発（checklist §2.1）

- **確認内容・回答**:
  1. `parsePartial`（`Settings.fs` L250）の `File.ReadAllText(filePath, Encoding.UTF8)` が `try ... with` ブロックの外にあり、ファイルロック・権限不足・競合削除時に起動/再読込処理が未捕捉例外でクラッシュする。
  2. `savePatch`（L745-830）全体および `Path.GetFullPath(filePath)`（L749）が `try ... with` 外にあり、`null`/空文字/空白/不正文字/長パス等で未捕捉例外クラッシュする。
  3. `parsePartial`（L310-320）の `rules` 配列が `[ null ]` や必須項目欠落レコード（`{ "id": "r1" }`）を無警告通過させ、`NamingRule` 参照時に `NullReferenceException` を誘発する。
  4. `resolveConfigDirectoryWithOrigins` / `resolveConfigDirectory` の `envName` サニタイズおよび `appBaseDir`/`ctx.CurrentDirectory` の `null`/空文字ガードが不足している。
- **確認根拠**:
  - `src/TagBasedVideoManager.Renamer/Settings.fs` L213-223, L246-333, L514-569, L624-695, L745-830
  - `src/TagBasedVideoManager.Renamer/Domain.fs` L6-13

### 設問2: 例外の握りつぶし・根拠のないフォールバック・API キー漏洩・設計整合（checklist §2.2）

- **確認内容・回答**:
  - `tryDeleteTempFile`（L193-196）が `with _ -> ()` でログ出力なしに例外を握りつぶしている（本体エラーを隠さない範囲で `eprintfn "[WARN] ..."` 出力が必要）。
  - API キー平文のログ/警告出力漏洩はなく、エラー段階コンテキストおよび日本語 XML コメント規約は良好。
- **確認根拠**:
  - `src/TagBasedVideoManager.Renamer/Settings.fs` L192-196, L691-694, L759-827

### 設問3: 30%コード削減・過剰抽象化の告発（YAGNI検証）および AI アンチパターン検証（checklist §3）

- **確認内容・回答**:
  - `moveFileWithRetry` / `readAllTextWithRetry` を `retryIo` に、`Settings.save` / `savePatch` の一時保存＋移動を `writeAtomicWithRetry` に、`savePatch` の二重シリアライズを `JsonSerializer.SerializeToNode` に、`mergePartialsChain` の `pickValue`/`pickRules` を `List.tryPick` へ統合し、到達不能 `MissingApiKey` 分岐整理・`getStringProp` 抽出・`loadDirectoryLayers` の `List.fold` 化を行うことで、対象重複ブロック約 30%（130〜160 行規模）の削減と安全性強化を両立できる。
- **確認根拠**:
  - `src/TagBasedVideoManager.Renamer/Settings.fs` L199-223, L270-292, L341-418, L448-465, L581-605, L796-828

---

## 2. Adversarial QA（破壊テスト実行結果）

対象外（PG Reviewer はコード品質/暗黙仮定担当、Adversarial QA は下記 QA Reviewer 記録を参照）。

---

## 3. 指摘事項

- **指摘1 (Major)**: `parsePartial`（L250）の `File.ReadAllText` を `retryIo` ＋ `try ... with` 保護へ移すこと。
- **指摘2 (Major)**: `savePatch`（L745-830）全体を `try ... with` で保護し `Path.GetFullPath` 等への不正入力で `Error` を返すこと。
- **指摘3 (Major)**: `parsePartial`（L310-320）の `rules` 配列要素に対して `null`・必須文字列フィールド欠損/`null` 検証と `ParseWarnings` 記録を追加すること。
- **指摘4 (Minor)**: `resolveConfigDirectoryWithOrigins`/`resolveConfigDirectory` の `envName` サニタイズと `appBaseDir`/`ctx.CurrentDirectory` の `null`/空文字ガードを追加すること。
- **指摘5 (Minor)**: `tryDeleteTempFile` で削除失敗時に `eprintfn "[WARN] ..."` を出力すること。
- **指摘6 (Suggestion)**: 設問 3 の `retryIo` / `writeAtomicWithRetry` / `SerializeToNode` / `List.tryPick` / `getStringProp` / `loadDirectoryLayers` 簡素化リファクタを実施すること。

---

## 4. 判定

- **LGTM**: false
- **条件付きLGTM**: なし（`Critical`: 0、`Major`: 3、`Minor`: 2、`Suggestion`: 1）
- **保留指摘（申し送り）**: なし
- **次のアクション**: 対応後再レビュー（`pg-implementer` 修正後に Cycle 2 再レビュー）
- **承認者**: PG Reviewer Agent

---

# レビュー記録: renamer-settings-resolution-and-serialization / apply / Task 2.1 (Cycle 1 - QA Reviewer / Adversarial QA)

- **日時**: 2026-11-22
- **フェーズ**: apply
- **タスク**: 2.1 Settings.fs の実装および State.fs 保存先隔離の前倒し適用
- **レビュアー**: qa
- **使用プロトコル**: openspec/templates/checklist_apply_impl.md
- **対象成果物**: src/TagBasedVideoManager.Renamer/Settings.fs, src/TagBasedVideoManager.Renamer/State.fs

---

## 1. 挑戦的設問への回答

### 設問1: 暗黙の前提とその破綻時の挙動

- `parsePartial` の `File.ReadAllText` が `try` 外にありロック中/権限拒否でクラッシュ（`ADV-01`）。
- `savePatch` が既存 JSON の重複キーで `ArgumentException` クラッシュ（`ADV-03a/b`）。
- `parsePartial` の `rules` 配列が `[ null ]` や必須項目欠落・PascalCase 要素を無警告通過させ `NullReferenceException` 源を返す（`ADV-04a/b/c`）。
- `savePatch` L749 が空/空白/`null` パスで `ArgumentException`/`ArgumentNullException` クラッシュ（`ADV-02_*`）。
- `ctx.CurrentDirectory = null` で `ArgumentNullException`（`ADV-06a`）、相対 `jsonPathOpt = Some "appsettings.json"` で `SavePath` 不整合（`ADV-06b`）。

---

## 2. Adversarial QA（破壊テスト実行結果）

- **破壊テストコード**: 一時スクラッチ `adversarial_qa_task2_1.fsx` / `adversarial_qa_task2_1_extra.fsx`（実行後削除済み）
- **実行コマンド**: `dotnet fsi ...\adversarial_qa_task2_1.fsx`
- **実行判定**: **バグ検出（Red / 14 観点中 11 件失敗）**
  - `ADV-01` 排他ロック読込: `IOException` 未処理クラッシュ
  - `ADV-02_{empty,whitespace,null}`: `ArgumentException` / `ArgumentNullException` 未処理クラッシュ
  - `ADV-03a/b` 重複キー既存 JSON の `savePatch`: `ArgumentException: An item with the same key has already been added.`
  - `ADV-04a/b/c` `rules: [null]`, `[{id:"partial-only"}]`, PascalCase 要素: `Warnings=[]` かつ `null` 混入
  - `ADV-06a/b` `CurrentDirectory = null` / 相対 `jsonPathOpt`: `ArgumentNullException` / `SavePath="appsettings.json"`
  - `ADV-05a/b`, `ADV-07a`, `ADV-08`, `ADV-09〜14`: 通過（堅牢）

---

## 3. 指摘事項

- **指摘1 (Critical)**: `parsePartial` のファイル読込に `try ... with` + リトライ保護を追加し、`Error ex.Message` 経由で `Warnings` に記録し安全起動すること。
- **指摘2 (Critical)**: `savePatch` で重複キー既存 JSON に対して `ArgumentException` クラッシュしないよう `JsonDocument` 正規化や例外ハンドラを含め `SettingsError (ParseJson)` または安全な後勝ち上書きで保護すること。
- **指摘3 (Critical)**: `parsePartial` の `rules` 配列要素で `[ null ]`・必須文字列フィールド（`Id`/`Name`/`Pattern`/`PromptInstruction`）欠損/`null`（および不正 PascalCase 不備要素）を検出し `ParseWarnings` に記録して安全フォールバックすること。
- **指摘4 (Major)**: `savePatch` L749 の `String.IsNullOrWhiteSpace(filePath)` ガードと `Path.GetFullPath` の `try ... with` 保護を追加し `Error (IoError ...)` を返すこと。
- **指摘5 (Minor)**: `ctx.CurrentDirectory` の `null`/空白ガードと、`jsonPathOpt` 相対パス時の `Path.GetFullPath` ベース `SavePath` 解決を行うこと。
- **指摘6 (Suggestion)**: `moveFileWithRetry` / `readAllTextWithRetry`・`getStringProp`・`pickRules` の共通化（なお `State.fs` L114 未使用 `defaultSettingsPath` は Task 2.3 で整理）。

---

## 4. 判定

- **LGTM**: false
- **条件付きLGTM**: なし（Critical 3・Major 1・Minor 1・Suggestion 1）
- **保留指摘（申し送り）**: なし
- **次のアクション**: 対応後再レビュー（`pg-implementer` で修正後、全テスト＋Adversarial QA 再実行）
- **承認者**: QA Reviewer Agent

---

# レビュー記録: renamer-settings-resolution-and-serialization / apply / Task 2.1 (Cycle 2 - PG Reviewer)

- **日時**: 2026-11-22
- **フェーズ**: apply
- **タスク**: 2.1 Settings.fs の実装および State.fs 保存先隔離の前倒し適用（Cycle 2 再レビュー）
- **レビュアー**: pg
- **使用プロトコル**: openspec/templates/checklist_apply_impl.md
- **対象成果物**: src/TagBasedVideoManager.Renamer/Settings.fs, src/TagBasedVideoManager.Renamer/State.fs

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: 暗黙の前提（都合の良い思い込み）の告発と Cycle 1 指摘 1〜4 の解消確認（checklist §2.1）

- **確認内容・回答**:
  - 指摘 1 解消（`parsePartial` 読込保護）：`try ... with ex -> Error ex.Message` と `retryIo 3 10` を適用。
  - 指摘 2 解消（`savePatch` 例外保護と重複キー対応）：空パスガード＋外側 `try ... with`、`JsonDocument.Parse`＋`jsonElementToNode` 後勝ち正規化。
  - 指摘 3 解消（`rules` 検証）：`EnumerateArray()` と `NamingRule` 必須文字列 4 項目の `isNull` 検証で `NullReferenceException` を遮断。
  - 指摘 4 解消（境界ガード）：`sanitizeEnvironmentName`・`safeAppBaseDir`・`safeCurrentDir`・`Path.GetFullPath` ガードを徹底。
- **確認根拠**:
  - `src/TagBasedVideoManager.Renamer/Settings.fs` L200-211, L272-365, L510-574, L606-714, L750-835

### 設問2: 例外の握りつぶし・設計整合（checklist §2.2）

- **確認内容・回答**: `tryDeleteTempFile` で `eprintfn "[WARN] ..."` を出力し、API キー平文漏洩なし・`design.md` 整合・日本語 XML コメント準拠。
- **確認根拠**: `src/TagBasedVideoManager.Renamer/Settings.fs` L193-197、`State.fs` L264-270

### 設問3: 30%コード削減・DRY リファクタリング確認（checklist §3）

- **確認内容・回答**: `retryIo`, `writeAtomicWithRetry`, `SerializeToNode`, `List.tryPick`, `getStringProp`, `List.fold` が適切に適用されている。

---

## 2. Adversarial QA（破壊テスト実行結果）

対象外（QA Reviewer 記録を参照）。

---

## 3. 指摘事項

なし（Cycle 1 の指摘 1〜6 全解消確認、非 Deploy テスト全 419 件 Pass）。

---

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし（`Critical`: 0、`Major`: 0、`Minor`: 0、`Suggestion`: 0）
- **保留指摘（申し送り）**: なし
- **次のアクション**: QA Reviewer Cycle 2 記録へ続く
- **承認者**: PG Reviewer Agent

---

# レビュー記録: renamer-settings-resolution-and-serialization / apply / Task 2.1 (Cycle 2 - QA Reviewer / Adversarial QA)

- **日時**: 2026-11-22
- **フェーズ**: apply
- **タスク**: 2.1 Settings.fs の実装および State.fs 保存先隔離の前倒し適用（Cycle 2 再レビュー）
- **レビュアー**: qa
- **使用プロトコル**: openspec/templates/checklist_apply_impl.md
- **対象成果物**: src/TagBasedVideoManager.Renamer/Settings.fs, src/TagBasedVideoManager.Renamer/State.fs

---

## 1. 挑戦的設問への回答

### 設問1: 暗黙の前提解消確認

- `parsePartial` I/O 保護、`savePatch` 重複キー `jsonElementToNode` 正規化、`rules` 要素 `null`/必須項目検証、`savePatch`/`save` の空/`null` パスガード、`ConfigLoadContext`/`jsonPathOpt` 相対パス解決すべて解消を確認（`State.fs` L114 未使用 `defaultSettingsPath` は Task 2.3 で整理予定）。

---

## 2. Adversarial QA（破壊テスト実行結果）

- **破壊テストコード**: 一時スクラッチ `adversarial_qa_task2_1_cycle2.fsx` / `adversarial_qa_task2_1_c2_edge.fsx`（実行後削除済み）
- **実行コマンド**: `dotnet fsi ...\adversarial_qa_task2_1_cycle2.fsx` および `...\adversarial_qa_task2_1_c2_edge.fsx`
- **実行判定**: **堅牢性確認（Green / TOTAL FAILURES: 0）**
  - `ADV-01`（排他ロック読込）、`ADV-02`（空/空白/`null`/不正パス）、`ADV-03a/b/c`（重複キー・`jsonElementToNode` 巨大数値・絵文字・ネスト重複）、`ADV-04a~e`（`[null]`・欠損・PascalCase・混在・型違い）、`ADV-05a/b/c`（深ネスト70・コメントのみ JSONC・`Some null`）、`ADV-06a/b/c`（`CurrentDirectory = null`・相対 `jsonPathOpt`・ロック中 `savePatch`）、`ADV-07〜13`、`ADV-EDGE`（63階層境界・`enableWebSearch` 省略互換）の全件で未処理例外なく仕様通り動作を確認。

---

## 3. 指摘事項

なし（419/419 Pass + Adversarial QA 全ケース堅牢確認）。

---

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし（`Critical`: 0、`Major`: 0、`Minor`: 0、`Suggestion`: 0）
- **保留指摘（申し送り）**: なし
- **次のアクション**: 次タスクへ（Task 2.1 を完了 `[x]` に更新・コミットし、Task 2.2 へ進む）
- **承認者**: QA Reviewer Agent

---

# レビュー記録: renamer-settings-resolution-and-serialization / apply / Task 2.2 (Cycle 1)

- **日時**: 2026-11-22
- **フェーズ**: apply
- **タスク**: 2.2 `State.fs` / `Views.fs` 向け先行失敗テストの作成・Red 確認（`StateTests.fs`, `RenameIntegrationE2ETests.fs`）
- **レビュアー**: qa
- **使用プロトコル**: openspec/templates/checklist_apply_test.md
- **対象成果物**: `src/TagBasedVideoManager.Renamer/State.fs`, `src/TagBasedVideoManager.Renamer/Views.fs`, `test/TagBasedVideoManager.Renamer.Tests/StateTests.fs`, `test/TagBasedVideoManager.Renamer.Tests/RenameIntegrationE2ETests.fs`

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: トートロジーテスト（実装式のコピペ）と形式的アサーションの告発（checklist §1）

- **確認内容・回答**:
  - 期待値は具体的固定リテラル・フィクスチャ値で記述され実装計算式のコピペはない。
  - ただし `RenameIntegrationE2ETests.fs` L2117-2146（`E2E-07`）で `capturedDispatch ReloadSettings` 後に `ReloadSettings` 処理完了・`SelectedModel = "model-B"` 更新・UI スレッド再描画完了をアサートしておらず、空振りでも Pass してしまう。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/StateTests.fs` L1534-2345
  - `test/TagBasedVideoManager.Renamer.Tests/RenameIntegrationE2ETests.fs` L1727-2147

### 設問2: 甘いアサーション・無意味なモックの告発（checklist §2）

- **確認内容・回答**:
  - 既存テスト追従 `StateTests.fs` L1062-1091 で `ApiKey = None` のまま `ScanCompleted` を呼び `Option.defaultValue Guid.Empty` で `CandidateAiProposed` を検証しており、Task 2.3 の `runId` ガード導入と衝突する。
  - `E2E-05` の `Bounds` 重なり検査にコントロールパネル他要素（管理/中止ボタン・`ComboBox` 等）が含まれていない。
  - `ST-INIT-02`（L1605）で構文エラー内容文言、`ST-RETRY-01` / `ST-SCAN-01` で `oldCts.Dispose()` 済み検証が不足。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/StateTests.fs` L1062-1126, L1602-1645, L1848-1864
  - `test/TagBasedVideoManager.Renamer.Tests/RenameIntegrationE2ETests.fs` L2004-2068, L2117-2146

### 設問3: ミューテーション思考（バグのすり抜け検証）（checklist §3）

- **確認内容・回答**:
  1. `ST-SCAN-04` / `ST-SCAN-05` で `IsRequestingAi = true` 中から直接 `ScanCompleted`（`Ok []`, `Error _`, `PlaceholderApiKey`, `MissingApiKey`）を呼ぶ検証および `MissingApiKey` 割り込み検証が不足し、`ScanCompleted` 側非 AI 分岐のリセット削除がすり抜ける。
  2. `ST-RELOAD-01`〜`03` で `Warnings` 非空時の警告維持分岐（`💡` で上書きしないこと）や `SelectedModel`/`CurrentThreshold` 更新検証が不足。
  3. `ST-RULE-05`（L2282, L2312-2320）で削除対象と同一 ID 編集中の `EditingRule` 保持と破損 JSON 内容不変が未検証。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/StateTests.fs` L1648-1828, L2025-2094, L2276-2345
  - `openspec/changes/renamer-settings-resolution-and-serialization/design.md` L166-179

### 設問4: フレーキー要因・TDD原則・スタブ最小性の確認（checklist §4）

- **確認内容・回答**:
  - `State.fs` / `Views.fs` は最小スタブ・コンパイル整合のみ、`SettingsLoadedWithMeta` 省略、9 PNG ファイル名一致、`Skip`/`Ignore` 0 件、タブ/行末空白 0 件、`appsettings*.json` 無変更。
  - 親エージェント側で `StateTests` + `RenameIntegrationE2ETests` 合計 95 件（既存 64 Pass、新規 31 全件 Red、Skip 0）を確認済み。

---

## 2. Adversarial QA（破壊テスト実行結果）

対象外（Apply テストレビューフェーズのため `checklist_apply_test.md` 観点のみ実施）。

---

## 3. 指摘事項

- **指摘1 (Major)**: `StateTests.fs` L1062-1091 の既存回帰テスト追従で `ApiKey = None` のまま `CandidateAiProposed (Guid.Empty, ...)` を発行しており、有効 `ApiKey` と有効 `Some runId` 発行＋`CTS` 破棄保護へ修正すること。
- **指摘2 (Major)**: `ST-SCAN-04` / `ST-SCAN-05` に `IsRequestingAi = true` 状態から直接 `ScanCompleted` 全非 AI 分岐（`Ok []`, `Error _`, `PlaceholderApiKey`, `MissingApiKey`）を呼ぶ検証および `MissingApiKey` 割り込み再スキャン検証を追加すること。
- **指摘3 (Major)**: `E2E-07`（L2117-2146）で UI スレッドジョブ消化後に `ReloadSettings` 処理記録・`SelectedModel = "model-B"` 更新・`ComboBox` 更新を必須アサートすること。
- **指摘4 (Major)**: `ST-RELOAD-01`〜`03` に `Warnings` 非空時の警告表示維持分岐と `SelectedModel` / `CurrentThreshold` 更新検証を追加すること。
- **指摘5 (Minor)**: ① `E2E-05` の重なり検査へコントロールパネル隣接ボタン・`ComboBox` を追加、② `ST-RETRY-01` / `ST-SCAN-01` で `oldCts.Dispose()` 済み確認、③ `ST-INIT-02` で構文エラー内容確認、④ `ST-RULE-05` で削除対象と同一 ID 編集中の `EditingRule` 保持と破損 JSON 内容不変を確認すること。

---

## 4. 判定

- **LGTM**: false
- **条件付きLGTM**: なし（Major 1〜4・Minor 5 修正後に Cycle 2 再レビューを実施）
- **保留指摘（申し送り）**: なし
- **次のアクション**: 対応後再レビュー（`qa-tester` で修正し Red 再確認後、`qa-reviewer` Cycle 2 を実施）
- **テスト品質評価**: 要改善
- **承認者**: QA Reviewer Agent

---

# レビュー記録: renamer-settings-resolution-and-serialization / apply / Task 2.2 (Cycle 2)

- **日時**: 2026-11-22
- **フェーズ**: apply
- **タスク**: 2.2 `State.fs` / `Views.fs` 向け先行失敗テストの作成・Red 確認（Cycle 2 再レビュー）
- **レビュアー**: qa
- **使用プロトコル**: openspec/templates/checklist_apply_test.md
- **対象成果物**: `src/TagBasedVideoManager.Renamer/State.fs`, `src/TagBasedVideoManager.Renamer/Views.fs`, `test/TagBasedVideoManager.Renamer.Tests/StateTests.fs`, `test/TagBasedVideoManager.Renamer.Tests/RenameIntegrationE2ETests.fs`

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: トートロジーテスト・形式的アサーションの解消確認（checklist §1）

- **確認内容・回答**:
  - 指摘 3 解消（`E2E-07` L2134-2184）：`Dispatcher.UIThread.RunJobs()` と `w.UpdateLayout()` を挟んで、`ReloadSettings` 処理記録・`SelectedModel = "model-B"` 更新・可視 `ComboBox` 追従・`ModelSelected` 非誤発火を必須アサートするよう修正された。
  - 全 31 件の新規テストとも固定リテラル・フィクスチャ期待値で検証されトートロジーなし。

### 設問2: 甘いアサーション・無意味なモックの解消確認（checklist §2）

- **確認内容・回答**:
  - 指摘 1 解消（`StateTests.fs` L1062-1133）：回帰テストへ有効 API キーと CTS `Dispose()` 保護を追加し Task 2.3 の `runId` ガードと完全整合。
  - 指摘 5-(1)(3) 解消：`E2E-05` 重なり検査に `⚙ 管理...`, `⏹ 中止`, `ComboBox` 群を追加し、`ST-INIT-02` に構文解析エラー文言検証を追加した。

### 設問3: ミューテーション思考（バグのすり抜け検証）の解消確認（checklist §3）

- **確認内容・回答**:
  - 指摘 2 解消（`ST-SCAN-04` L1736-1763, `ST-SCAN-05` L1846-1870）：`ScanCompleted` 全非 AI 分岐（`Ok []`, `Error _`, `PlaceholderApiKey`, `MissingApiKey`）直接リセット検証と `MissingApiKey` 割り込み再スキャン検証を追加。
  - 指摘 4 解消（`ST-RELOAD-01` L2118-2148）：`SelectedModel`/`PathLengthThreshold`/`CurrentThreshold` 更新と構文エラー・旧形式 `Warnings` 非上書き維持を検証。
  - 指摘 5-(2)(4) 解消：`ST-SCAN-01` / `ST-RETRY-01` に `oldCts.Dispose()` 検証、`ST-RULE-05` に同一 ID `"r2"` 編集中 `EditingRule` 保持と破損 JSON 不変検証を追加。

### 設問4: フレーキー要因・環境依存・TDD原則・実行結果の最終確認（checklist §4）

- **確認内容・回答**:
  - 隔離安全性維持、`Skip`/`Ignore` 0 件、行末空白・タブ 0 件、`appsettings*.json` 無変更。
  - 親エージェント再実行結果（`dotnet test ... --filter-class StateTests --filter-class RenameIntegrationE2ETests`）：ビルドエラー 0 件、**合計: 95, 失敗: 31 (新規 31 全件 Red), 成功: 64, スキップ済み: 0** を確認済み。

---

## 2. Adversarial QA（破壊テスト実行結果）

対象外（Apply テストレビューフェーズのため `checklist_apply_test.md` 観点のみ実施）。

---

## 3. 指摘事項

なし（Cycle 1 の指摘 1〜5 すべてが解消され、新たな指摘なし）。

---

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし（`Critical`: 0 件、`Major`: 0 件、`Minor`: 0 件、`Suggestion`: 0 件）
- **保留指摘（申し送り）**: なし
- **次のアクション**: 次タスクへ（Task 2.2 完了処理後、Task 2.3 `State.fs`・`Views.fs`・`OpenRouterClient.fs` の本実装へ進行）
- **テスト品質評価**: 堅牢（合格）
- **承認者**: QA Reviewer Agent

---

# レビュー記録: renamer-settings-resolution-and-serialization / apply / Task 2.3 (Cycle 1 - PG Reviewer)

- **日時**: 2026-11-23
- **フェーズ**: apply
- **タスク**: 2.3 `State.fs`・`Views.fs`・`OpenRouterClient.fs` の実装（Cycle 1）
- **レビュアー**: pg (PG Reviewer Agent)
- **使用プロトコル**: `openspec/templates/checklist_apply_impl.md`
- **対象成果物**: `src/TagBasedVideoManager.Renamer/State.fs`, `src/TagBasedVideoManager.Renamer/Views.fs`, `src/TagBasedVideoManager.Renamer/OpenRouterClient.fs`

---

## 1. 挑戦的設問への回答

### 設問1: 暗黙の前提と破綻シナリオ（checklist §2-1）

1. `RetryCandidateAiProposal`（`State.fs` L606-646）で先行パイプライン中断・キー検証エラー時に他候補の `IsAiProcessing = true` がクリアされず、旧 `AllAiProposalsCompleted` も無視されるため UI 上永久「処理中」ロックになる。
2. `Views.errorBannerWithMargin`（L2531-2534）が `Some null` で `NullReferenceException` になる。
3. `State.fs` `ModelSelected`（L472-489）自体に同値・空白ガードがなく、UI 外からのディスパッチ時に保存が抑止されない。
4. `Views.fs` 命名規則 `ComboBox`（L733-757）で `Rules = []` 時インデックス `0` 問題および再描画時インデックス競合がある。
5. `State.fs` L579 `CandidateAiProposed` が失敗候補（`AiError.IsSome`）にも `IsAiProposed = true` を設定している。

### 設問2: 例外の握りつぶし・サイレントエラー（checklist §2-2）

- `disposeAndClearCts`（`State.fs` L131-139）の `with _ -> ()` と `createAsyncPipelineCmd`（L284-289）の `| _ -> ()` が `ObjectDisposedException` / `OperationCanceledException` 以外の予期せぬ例外までログなしで握りつぶしている。API キー漏洩なし、401 文言統一済み。

### 設問3: 30%コード削減リファクタ提案（checklist §3）

- `resolveEffectiveSavePath`、`resolveSelectedRuleId`、`applySettingsPatch`、`startAiPipelineOrReset` の共通ヘルパー抽出により、`ModelSelected`/`applyRulesPatch` および `ScanCompleted`/`RetryCandidateAiProposal` の重複を統合して約 30% 削減可能。

---

## 2. Adversarial QA（破壊テスト実行結果）

対象外（QA Reviewer 記録を参照）。

---

## 3. 指摘事項

- **指摘1 (Major)**: `RetryCandidateAiProposal` 実行時の他候補 `IsAiProcessing` リセット漏れ解消。
- **指摘2 (Major)**: `disposeAndClearCts` および `createAsyncPipelineCmd` の予期せぬ例外の無ログ握りつぶし廃止（`eprintfn` 警告出力）。
- **指摘3 (Minor)**: `Views.errorBannerWithMargin` の `null`/空白ガード、`CandidateAiProposed` の `IsAiProposed` 整合、`ModelSelected` 同値・空白ガード。
- **指摘4 (Suggestion)**: `State.fs` 共通ヘルパー統合（約 30% 削減リファクタ）。

---

## 4. 判定

- **LGTM**: false
- **条件付きLGTM**: なし（Major 1〜2 解消後に再レビュー）
- **保留指摘（申し送り）**: なし
- **次のアクション**: 対応後再レビュー

---

# レビュー記録: renamer-settings-resolution-and-serialization / apply / Task 2.3 (Cycle 1 - QA Reviewer / Adversarial QA)

- **日時**: 2026-11-23
- **フェーズ**: apply
- **タスク**: 2.3 `State.fs`・`Views.fs`・`OpenRouterClient.fs` の実装（Cycle 1）
- **レビュアー**: qa (QA Reviewer Agent / Adversarial QA 担当)
- **使用プロトコル**: `openspec/templates/checklist_apply_impl.md`
- **対象成果物**: `src/TagBasedVideoManager.Renamer/State.fs`, `src/TagBasedVideoManager.Renamer/Views.fs`, `src/TagBasedVideoManager.Renamer/OpenRouterClient.fs`

---

## 1. 挑戦的設問への回答

- 命名規則 `ComboBox`（L733-757）が `ReloadSettings` / `MoveRuleOrder` 再描画時に `RuleSelected` を誤発火する（Attack 7b, 7c）。
- `RetryCandidateAiProposal` で先行処理中候補がゾンビスピナー化する（Attack 1, 2）。
- `ModelSelected` に同値・`null`/空白ガードがない（Attack 5, 10）。
- `ruleManagerModal` の固定 `960x680` が最小検証幅 `800x650` で画面外にはみ出す（`ModalBounds = (-80, -14.67, 960, 680)`, Attack 6）。
- `RetryCandidateAiProposal` の `IsScanning || IsRenaming` 排他抜け（Attack 3a, 3b）およびルール CRUD ハンドラ（`DeleteRule`/`SaveEditingRule`/`SaveRule`）境界バリデーション不足（Attack 4a/b/c）。

---

## 2. Adversarial QA（破壊テスト実行結果）

- **破壊テストコード**: `adversarial_qa_task23.fsx`（実行後削除済み）
- **実行コマンド**: `dotnet fsi ...\adversarial_qa_task23.fsx`
- **実行判定**: **バグ検出（Red / 16 ケース中 12 件で失敗検出）**
  - `Attack1`, `Attack2`: ゾンビスピナー残留
  - `Attack3a`, `Attack3b`: スキャン/リネーム中 `RetryCandidateAiProposal` 起動
  - `Attack4a`, `Attack4b`, `Attack4c`: 最後の 1 件削除、`Id=null` 混入、空白ルール名許容
  - `Attack5`, `Attack10`: `ModelSelected` `null`/空白/同値保存発火
  - `Attack6`: `800x650` ウィンドウでモーダル座標 `(-80, -14.67, 960, 680)` はみ出し
  - `Attack7b`, `Attack7c`: `ReloadSettings` / `MoveRuleOrder` 再描画で `RuleSelected` 誤発火
  - `Attack7a`, `Attack7d`, `Attack8`, `Attack9`: 通過（堅牢）

---

## 3. 指摘事項

- **指摘1 (Major)**: 命名規則 `ComboBox` の再描画時 `RuleSelected` 誤発火修正（および `Rules = []` 時 `-1` 統一）。
- **指摘2 (Major)**: `RetryCandidateAiProposal` 実行時の他候補 `IsAiProcessing` リセット。
- **指摘3 (Major)**: `State.fs` `ModelSelected` の同値保存抑止・`null`/空白ガード。
- **指摘4 (Major)**: `Views.fs` `ruleManagerModal` 固定サイズ `960x680` 廃止・レスポンシブ制約化（`800x650` 枠内収容）。
- **指摘5 (Minor)**: `RetryCandidateAiProposal` の `IsScanning || IsRenaming` 排他（`State.fs` ＆ `Views.fs` 再試行ボタン無効化）。
- **指摘6 (Minor)**: ルール CRUD（`DeleteRule` 残り 1 件・`SaveEditingRule`/`SaveRule` 必須項目検証）境界ガード追加。

---

## 4. 判定

- **LGTM**: false
- **条件付きLGTM**: なし
- **保留指摘（申し送り）**: なし
- **次のアクション**: 対応後再レビュー（`pg-implementer` で修正・リファクタ後、450 件テスト＋Adversarial QA 再実施）

---

# レビュー記録: renamer-settings-resolution-and-serialization / apply / Task 2.3 (Cycle 2 - PG Reviewer)

- **日時**: 2026-11-23
- **フェーズ**: apply
- **タスク**: 2.3 `State.fs`・`Views.fs`・`OpenRouterClient.fs` の実装（Cycle 2 再レビュー）
- **レビュアー**: pg (PG Reviewer Agent)
- **使用プロトコル**: `openspec/templates/checklist_apply_impl.md`
- **対象成果物**: `src/TagBasedVideoManager.Renamer/State.fs`, `src/TagBasedVideoManager.Renamer/Views.fs`, `src/TagBasedVideoManager.Renamer/OpenRouterClient.fs`

---

## 1. 挑戦的設問への回答

### 設問1: Cycle 1 指摘の解消および `ConditionalWeakTable` / DRY ヘルパー検証

- `RetryCandidateAiProposal` の `IsScanning || IsRenaming` 排他（`State.fs` L496・`Views.fs` L1303/L1782）と `startAiPipelineOrReset` による全他候補 `IsAiProcessing = false` リセットを確認。
- `Views.errorBannerWithMargin`（L2619）の非空白ガード、`ModelSelected`（L547）の同値/空白ガード、`CandidateAiProposed`（L628）の `IsAiProposed && AiError.IsNone` 整合を確認。
- `ConditionalWeakTable<ComboBox, RuleComboBoxState>`（`Views.fs` L117-206）に逆参照によるメモリリークはなく、`IsSyncing` 排他＋`RuleComboItem` 構造的等値により同名ルール・空リスト・再描画誤発火を構造的に防止。`ruleManagerModal` のレスポンシブ化およびルール CRUD 境界ガードも確認。

### 設問2: 例外の握りつぶし・サイレントエラー検証

- `disposeAndClearCts`（L149-160）は `ObjectDisposedException` のみ、`createAsyncPipelineCmd`（L306-310）は `OperationCanceledException` のみ静かに無視し、予期せぬ例外は `eprintfn "[WARN] ..."` へ記録するよう改善済み。401 文言統一・API キー保護も適切。

### 設問3: コード削減・設計・モック忠実性

- `resolveEffectiveSavePath`, `resolveSelectedRuleId`, `clearAllAiProcessing`, `startAiPipelineOrReset`, `applySettingsPatch`, `updateCandidateByPath`, `mapEditingRule`, `startRename` により重複ロジックを適切に削減。`mockup.html`・`design.md`・日本語 XML コメント規約と整合。

---

## 2. Adversarial QA（破壊テスト実行結果）

対象外（下記 QA Reviewer 記録を参照。親エージェント側で非 Deploy 全 450 件 Pass 確認済み）。

---

## 3. 指摘事項

なし（Cycle 1 指摘 1〜4 解消済み。なお `design.md` §2.6.1 記載の `SettingsLoadedWithMeta` 不採用と後方互換 `SettingsLoaded` 残存の軽微差分のみ Archive 時 `handover.md` 申し送り候補）。

---

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし（`Critical`: 0、`Major`: 0、`Minor`: 0、`Suggestion`: 0）
- **保留指摘（申し送り）**: `design.md` §2.6.1 の `SettingsLoadedWithMeta` 記述と実装（同期ロード＋旧 `SettingsLoaded` 互換維持）の軽微差分
- **次のアクション**: QA Reviewer Cycle 2 記録へ続く

---

# レビュー記録: renamer-settings-resolution-and-serialization / apply / Task 2.3 (Cycle 2 - QA Reviewer / Adversarial QA)

- **日時**: 2026-11-23
- **フェーズ**: apply
- **タスク**: 2.3 `State.fs`・`Views.fs`・`OpenRouterClient.fs` の実装（Cycle 2 再レビュー）
- **レビュアー**: qa (QA Reviewer Agent / Adversarial QA 担当)
- **使用プロトコル**: `openspec/templates/checklist_apply_impl.md`
- **対象成果物**: `src/TagBasedVideoManager.Renamer/State.fs`, `src/TagBasedVideoManager.Renamer/Views.fs`, `src/TagBasedVideoManager.Renamer/OpenRouterClient.fs`

---

## 1. 挑戦的設問への回答

- `RuleComboItem` / `ConditionalWeakTable` により `ReloadSettings` 順序入替（Attack 7b）、`MoveRuleOrder`（Attack 7c）、空リスト往復（Attack 7a+11d, 15）、`Rule.Name = null`（Attack 11a）、同名ルール切替・`ItemsSource` 参照維持（Attack 7d+11b+11c）すべてで誤発火・例外なし。
- `RetryCandidateAiProposal` ゾンビスピナー（Attack 1, 2）、`IsScanning`/`IsRenaming` 排他（Attack 3a, 3b, 13）、`ModelSelected` `null`/空白/同値抑止（Attack 5, 10）、`ruleManagerModal` `800x650`/`1100x720` 内収容（Attack 6+12a, 12b）、ルール CRUD 境界ガード（Attack 4a/b/c, 16）、`CandidateAiProposed` 整合＆既破棄 CTS 保護（Attack 14）すべて堅牢確認。

---

## 2. Adversarial QA（破壊テスト実行結果 — Cycle 2）

- **破壊テストコード**: `adversarial_qa_task23_cycle2.fsx`（実行後削除済み、`appsettings*.json` SHA256 不変確認済み）
- **実行コマンド**: `dotnet fsi ...\adversarial_qa_task23_cycle2.fsx`
- **実行判定**: **堅牢性確認（Green / 失敗・バグ検出 0 件 / 全 22 ケース通過）**

---

## 3. 指摘事項

なし（450/450 Pass + Adversarial QA 全 22 ケース堅牢確認）。

---

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし（`Critical`: 0、`Major`: 0、`Minor`: 0、`Suggestion`: 0）
- **保留指摘（申し送り）**: なし
- **次のアクション**: 次タスクへ（Task 2.3 を完了 `[x]` に更新・コミットし、Phase 3 Task 3.1 実画面視覚検証へ進む）

---

# レビュー記録: renamer-settings-resolution-and-serialization / apply / Task 3.1 (GUI E2E 実画面視覚検証)

- **日時**: 2026-11-23
- **フェーズ**: apply
- **タスク**: 3.1 GUI E2E 実画面キャプチャ検証と `mockup.html` 突合
- **レビュアー**: qa (QA Reviewer Agent) ＋ 親エージェント目視突合
- **使用プロトコル**: `openspec/templates/checklist_apply_test.md`（視覚エビデンス・UI E2E 突合検証）
- **対象成果物**: `test/TagBasedVideoManager.Renamer.Tests/RenameIntegrationE2ETests.fs`, `openspec/changes/renamer-settings-resolution-and-serialization/mockup.html`, `test/TestResults/E2E_Settings_*.png`（9 枚）

---

## 1. 挑戦的設問への回答

- `test/TestResults/E2E_Settings_*.png` 9 枚（`HappyPath`, `ZeroState_MissingKey`, `Placeholder_Error`, `Placeholder_Recovered`, `ModalSaveError`, `ModalSaveRecovered`, `LongPathBanner_800`, `LongPathBanner_1100`, `StartupWarningBanner`）を直接画像検査し、`mockup.html` の Fluent ダークテーマ配色・コントラスト・`ENV` バッジ＆保存先ファイル名・常設 `🔄 設定を再読込` ボタン・3 ボタン `errorBanner`・モーダル開時背面バナー非表示＆モーダル上部配置と忠実に一致していることを確認。
- `E2E-05`（`RenameIntegrationE2ETests.fs` L1973-2090）にて `errorBanner`・アクションボタン・環境バッジ・管理/中止ボタン・`ComboBox` 群の `Bounds` が `800px` / `1100px` ウィンドウ内で重なり・はみ出しゼロであることを機械検証（450/450 Pass）。

---

## 2. Adversarial QA（破壊テスト実行結果）

対象外（Task 3.1 視覚エビデンス検証フェーズ）。

---

## 3. 指摘事項

なし（9 枚すべて最新生成・`mockup.html` 忠実再現・レイアウト破壊ゼロを確認）。

---

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし（`Critical`: 0、`Major`: 0、`Minor`: 0、`Suggestion`: 0）
- **保留指摘（申し送り）**: なし
- **次のアクション**: 次タスクへ（Task 3.1 完了 `[x]`・コミット後、Task 3.2 へ進行）
