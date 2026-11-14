# レビュー記録: appsettings-unification-and-renamer-spinners (Propose)

---

## Cycle 1 — User Reviewer Agent (2026-11-14)

- **LGTM**: false
- **指摘サマリ**:
  - `Critical`: 0 件
  - `Major`: 2 件（初回フォルダ未設定時 Zero-State 案内欠落、個別ファイル AI 提案失敗 Error-State・単独再試行仕様欠落）
  - `Minor`: 2 件（0件スキャン時の動画0本/全件安全区別、JSON構文エラー時のフォールバック仕様欠落）
  - `Suggestion`: 1 件（`.env` 残存時の Renamer 移行案内通知）
- **対応内容**:
  - `specs/companion-file-rename/spec.md`: `TargetDirectory` 空文字時の Zero-State 案内、動画0本/全件安全出し分け、個別カード失敗赤枠＆「↻ 再試行」、JSON 構文エラー時フォールバック・警告通知、`.env` 残存時移行ログ/案内を追加。
  - `specs/container-deployment-scripts/spec.md`: 不正 JSON 構文およびツール不在時の Fail-Fast シナリオを追加。
  - `mockup.html`: `1a. 未設定初期 (Zero-State)`、`6a/6b` の 0件出し分け、個別失敗カード（`⚠️ AI提案失敗`＋`↻ 再試行`ボタン）を追加。

---

## Cycle 1 — SE Reviewer Agent (2026-11-14)

- **LGTM**: false
- **指摘サマリ**:
  - `Critical`: 0 件
  - `Major`: 3 件（固定 `.tmp` による並行競合リスク、`deploy.sh` 曖昧フォールバック、`IsScanning` 中入力割り込み排他漏れ）
  - `Minor`: 3 件（`DATABASE_PATH` 親ディレクトリ自動作成、Web 起動・スキャン時 `VIDEO_DIRS` ログ出力、Avalonia スピナー方式 `SpinnerControl` 一本化）
- **対応内容**:
  - `design.md`: 一意 `.tmp` (`Guid.NewGuid()`) アトミック置換、`deploy.sh` の `python3`/`jq` 限定＋Fail-Fast 方針、スキャン/リネーム中の `State.update` 入力排他ガード、`Rules` 明示配列の非ゾンビ復元仕様、`DATABASE_PATH` 親ディレクトリ自動作成、`VIDEO_DIRS` 解決ログ、単一 `SpinnerControl`（`Animation` + `RotateTransform.AngleProperty`）方式への絞り込みを明記。
  - `test_plan.md` / `tasks.md`: 並行保存競合 (`IT-01`)・JSON構文エラー (`UT-06`)・空ルール (`UT-05`)・スキャン/リネーム割り込み排他 (`ST-08`)・個別リトライ (`ST-05`) と具体化タスクを追記。

---

## Cycle 2 — User Reviewer Agent 再レビュー (2026-11-14)

- **使用プロトコル**: [checklist_propose.md](file:///D:/programming/repos/MyGitHubRepos/TagBasedVideoManager/openspec/templates/checklist_propose.md)

### 再検証結果

1. **初回起動時（`TargetDirectory` 未設定）の Zero-State ガイダンス**:
   [specs/companion-file-rename/spec.md](file:///D:/programming/repos/MyGitHubRepos/TagBasedVideoManager/openspec/changes/appsettings-unification-and-renamer-spinners/specs/companion-file-rename/spec.md) および [mockup.html](file:///D:/programming/repos/MyGitHubRepos/TagBasedVideoManager/openspec/changes/appsettings-unification-and-renamer-spinners/mockup.html) に未設定時ガイダンスとフォルダ選択 CTA が追加され解消。
2. **AI 提案の個別カード失敗時 Error-State と単独再試行**:
   [specs/companion-file-rename/spec.md](file:///D:/programming/repos/MyGitHubRepos/TagBasedVideoManager/openspec/changes/appsettings-unification-and-renamer-spinners/specs/companion-file-rename/spec.md)、[design.md](file:///D:/programming/repos/MyGitHubRepos/TagBasedVideoManager/openspec/changes/appsettings-unification-and-renamer-spinners/design.md)、[mockup.html](file:///D:/programming/repos/MyGitHubRepos/TagBasedVideoManager/openspec/changes/appsettings-unification-and-renamer-spinners/mockup.html) に個別失敗表示と「↻ 再試行」ボタン・`RetryCandidateAiProposal` が定義され解消。
3. **スキャン 0 件時の出し分け・JSON 構文エラー・`.env` 移行案内**:
   動画 0 本 vs 安全基準達成、構文エラー時フォールバック/警告（Renamer）および Fail-Fast（deploy）、`.env` 残存移行案内がすべて仕様・モックに反映され解消。

### 総合判定（User Reviewer）

- **LGTM**: true
- **条件付きLGTM**: なし
- **指摘サマリ**:
  - `Critical`: 0 件
  - `Major`: 0 件
  - `Minor`: 0 件
  - `Suggestion`: 0 件
- **承認者**: User Reviewer Agent

---

## Cycle 2 — SE Reviewer Agent 再レビュー (2026-11-14)

- **使用プロトコル**: [checklist_propose.md](file:///D:/programming/repos/MyGitHubRepos/TagBasedVideoManager/openspec/templates/checklist_propose.md)

### 再検証結果

1. **故障シミュレーション・競合耐性**:
   GUID 付き一意一時ファイル（`appsettings.{env}.json.{Guid.NewGuid():N}.tmp`）＋例外時クリーンアップ、`deploy.sh` の `python3`/`jq` 限定 Fail-Fast 化、`DATABASE_PATH` 親ディレクトリ自動作成により故障リスクが解消。
2. **可観測性・テスト計画の死角・手戻り**:
   Web 起動/スキャン時の `VIDEO_DIRS` ログ出力、スキャン/リネーム中入力割り込みガード (`ST-08`)、明示空 `rules` 非復元 (`UT-05`)、単一 `SpinnerControl`（`Animation` + `RotateTransform.AngleProperty`）方式確定により手戻りリスク・死角が解消。

### 総合判定（SE Reviewer）

- **LGTM**: true
- **条件付きLGTM**: なし
- **指摘サマリ**:
  - `Critical`: 0 件
  - `Major`: 0 件
  - `Minor`: 0 件
  - `Suggestion`: 0 件
- **承認者**: SE Reviewer Agent

---

## レビュー記録: appsettings-unification-and-renamer-spinners / apply / Cycle 1（SE / QA / PG Reviewers）

- **日時**: 2026-11-14
- **フェーズ**: apply（Cycle 1）
- **使用プロトコル**:
  - [checklist_apply_test.md](file:///D:/programming/repos/MyGitHubRepos/TagBasedVideoManager/openspec/templates/checklist_apply_test.md)
  - [checklist_apply_impl.md](file:///D:/programming/repos/MyGitHubRepos/TagBasedVideoManager/openspec/templates/checklist_apply_impl.md)

### 1. SE Reviewer（Cycle 1）

- **状態整合・障害耐性・モック忠実度**: `.env` / OS `VIDEO_DIR` 依存の完全撤廃、環境名サニタイズ、`Settings.save` アトミック置換＋`%APPDATA%` フォールバック、Web `DATABASE_PATH` 親ディレクトリ自動作成、`deploy.sh` の Fail-Fast JSON 解析、Avalonia 11 ネイティブアニメーション `SpinnerControl` と `mockup.html` 忠実再現（`E2E_12`〜`E2E_16`, `E2E-01` 実描画キャプチャ検証）、SemVer（Web `0.3.0`, Renamer `0.2.0`）を承認。
- **Suggestion**: `State.fs` ルール操作（`SaveEditingRule`, `SaveRule`, `DeleteRule`）での `Settings.save` 戻り値エラーハンドリング統一を推奨。
- **LGTM**: true（Critical: 0, Major: 0, Minor: 0, Suggestion: 1）

### 2. QA Reviewer（Cycle 1）

- **テストレビュー・Adversarial QA**: トートロジーテストなし。実ファイル・実プロセス・実 Elmish 更新・実 Playwright ブラウザによるアサーションを確認。`Settings.fs` / `common.ps1` に対する破壊テスト（F# 4件・pwsh 2件: 破損 JSON、型不整合、パストラバーサル環境名、不正保存パス、不正 PORT）をすべて堅牢性確認（落とせず Green）で通過。
- **Minor 指摘（2件）**:
  1. `ProgramTests.fs` `UT-10` がテストコード内で直接 `Directory.CreateDirectory` を呼び、プロダクションコード側を呼び出していない形式的テストとなっていた。
  2. プロセス共通環境変数一時変更テストの将来的な並行実行干渉可能性（現状は逐次実行＋`try ... finally` 復元により実害なし）。
- **LGTM**: true（Critical: 0, Major: 0, Minor: 2, Suggestion: 0）

### 3. PG Reviewer（Cycle 1）

- **指摘1（Critical）**: `Domain.fs`（`ProposalState`）および `State.fs`（`ModelState`）の `ConditionalWeakTable` 依存により、F# レコードコピー更新（`{ model with ... }`）で新インスタンスが生成されると `HasScanned` や `AiError` の参照等価性が崩れ、スキャン完了後の画面が初期プレースホルダーへ巻き戻る致命的不具合を検出。
- **指摘2（Major）**: `Settings.fs` L399-400 の `mergePartialsChain (appDataPartials @ basePartials)` により `%APPDATA%` がベース層に上書きされ、`design.md` Decision 3 と矛盾している不具合を検出。
- **指摘3（Minor）**: `Settings.fs` `resolveSavePath` における 18行の重複コピペコード。
- **LGTM**: false（Critical: 1, Major: 1, Minor: 1, Suggestion: 0）

---

## レビュー記録: appsettings-unification-and-renamer-spinners / apply / Cycle 2（再レビュー・承認）

- **日時**: 2026-11-14
- **フェーズ**: apply（Cycle 2）

### 1. 実施した是正対応

1. `Domain.fs` / `State.fs` / `OpenRouterClient.fs`: `ProposalState` および `ModelState`（`ConditionalWeakTable`）を完全撤廃し、`RenameProposal.AiError` と `Model` の `HasScanned`, `ScannedVideoCount`, `ActiveEnvironment`, `SettingsSavePath` を純粋イミュータブルレコードフィールドとして直接定義。`StateTests.fs` にコピー更新連続ディスパッチでの状態非消失回帰テストを追加。
2. `Settings.fs`: マージ順を `(basePartials @ appDataPartials)` に是正し、`%APPDATA%` 最優先オーバーライドを回復。不正 JSON 構文時の `eprintfn` 警告ログを追加し、`SettingsTests.fs` に回帰テストを追加。
3. `Settings.fs` `resolveSavePath`: `baseDirOpt |> Option.defaultValue AppDomain.CurrentDomain.BaseDirectory` へ統合し重複コードを削減。
4. `State.fs`: `SaveEditingRule` / `SaveRule` / `DeleteRule` / `MoveRuleOrder` でも `Settings.save` の戻り値エラーを `ErrorMessage` へ通知するよう統一。
5. `src/TagBasedVideoManager/Program.fs` & `ProgramTests.fs`: `Program.ensureParentDirectoryCreated` 関数を抽出し、`UT-10` が同プロダクション関数を直接呼び出して検証するよう改修。

### 2. PG Reviewer（Cycle 2 再レビュー）

- 指摘1（Critical）・指摘2（Major）・指摘3（Minor）および保存エラー通知統一がすべて根本解決されたことを確認。
- **LGTM**: true（Critical: 0, Major: 0, Minor: 0, Suggestion: 0）
- **承認者**: PG Reviewer Agent

### 3. QA Reviewer（Cycle 2 再レビュー）

- `ProgramTests.fs` `UT-10` の形式的テスト解消、`StateTests.fs` 連続ディスパッチ回帰テスト、`SettingsTests.fs` `%APPDATA%` 上書き回帰テスト、`Program.ensureParentDirectoryCreated` に対する境界/異常系 Adversarial QA（null/空白/ルート/不正文字）の堅牢性を確認。
- **保留申し送り（Minor 1件）**: プロセス環境変数変更テスト（`SettingsTests.fs`, `ProgramTests.fs`）の将来的な並列化時の干渉リスク。現状 `try ... finally` 復元＋逐次実行で全 553 件 Pass のため問題なし。
- **LGTM**: true（Critical: 0, Major: 0, Minor: 1 [保留], Suggestion: 0）
- **承認者**: QA Reviewer Agent
