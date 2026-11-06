# レビュー記録: multi-video-dirs / propose

- **日時**: 2026-11-06
- **フェーズ**: propose
- **対象成果物**:
  - `openspec/changes/multi-video-dirs/proposal.md`
  - `openspec/changes/multi-video-dirs/mockup.html`
  - `openspec/changes/multi-video-dirs/specs/**/spec.md`
  - `openspec/changes/multi-video-dirs/design.md`
  - `openspec/changes/multi-video-dirs/test_plan.md`
  - `openspec/changes/multi-video-dirs/tasks.md`

---

## サイクル 1 レビュー結果

- **日時**: 2026-11-06
- **レビュアー**: `user-reviewer`, `se-reviewer`
- **判定**: **LGTM: false**（User: Major 2 / SE: Major 5）
- **主な指摘事項**:
  - **M1 / A1**: UI がスキャン失敗時に固定文しか出さず、設定不備（`VIDEO_DIRS` 未設定等）のエラー詳細が把握できない（`mockup.html` 必須）。
  - **M2**: デプロイスクリプトの旧引数（`-VideoDir`）や既定値（`/mnt/videos`）の扱いが未定義。
  - **M-1**: `deploy.sh` での `source .env` がセミコロンを誤解釈するリスク。
  - **M-2**: `FilePath` の正規化（大文字小文字・末尾区切り・物理パス解決）未定義による二重登録リスク。
  - **M-3**: タグ導出の基準ルートとリンク・入れ子ルート時のタグ付与方針が未確定。
  - **M-4**: デプロイの検証順序と、`run` 失敗時のサービス断リスク。
  - **M-5**: スペックの `-v` 期待値と実コードの WSL パス変換の矛盾。
- **対応方針**:
  - ユーザーヒアリングを実施し、A1（UI エラー詳細表示のスコープ追加・`mockup.html` 作成）および A2（デプロイスクリプトは検証を stop 前に行う構成を維持し、run 失敗時のサービス断リスクを design に明記）の合意を取得。
  - 全指摘（M1〜M5、m1〜m8）を反映し、成果物一式（`mockup.html`, `proposal.md`, `specs/`, `design.md`, `test_plan.md`, `tasks.md`）を改訂。

---

## サイクル 2 レビュー結果（再レビュー）

- **日時**: 2026-11-06
- **レビュアー**: `user-reviewer`, `se-reviewer`
- **判定**: **LGTM: true**（User: Critical 0, Major 0 / SE: Critical 0, Major 0）

### 1. 設問への回答サマリ

#### 1.1 User Reviewer（ブラインド仕様突合・初心者検証）

- **異常系・境界条件の先出し突合**: 未設定・旧変数残存、一部/全不在、重複・入れ子、ドライブ直下拒否、既存動画へのタグ追記、大文字小文字表記揺れ防止のすべてがシナリオとして過不足なく定義されていることを確認。
- **暗黙の前提の排除**: スキャン失敗時のトースト通知拡張、スクリプトでの既定値廃止と事前入力検証、行単位の安全な `.env` パース仕様が明文化されたことを確認。
- **UI モック検証**: `mockup.html` において、設定不備時にトースト内に詳細エラーと設定例がコード風ボックスで表示され、表示時間 7 秒＋閉じるボタンにより初心者が直感的に復旧できることを確認。

#### 1.2 SE Reviewer（故障シミュレーション・可観測性・手戻り検証）

- **故障シミュレーション**: 走査時の低速・切断ドライブ発生時も例外トラップによりプロセス全体のクラッシュやDB不整合は防がれ、直列走査により SQLite のロック競合も防止されることを確認。
- **可観測性**: 不備パス・設定例を含む詳細エラーが API・UI トースト・ログの全層に通知され、MTTR が最小化されていることを確認。
- **テスト計画・タスク検証**: 外部依存のモック境界が明瞭であり、タスク 2.2（入れ子ルート対応）についても `IT-05b` のテストファースト推進により安全に実装可能であることを確認。

### 2. 指摘事項（保留・申し送り）

| ID         | 種別       | 対象         | 内容                                                                                            | 対応方針                                              |
| :--------- | :--------- | :----------- | :---------------------------------------------------------------------------------------------- | :---------------------------------------------------- |
| **Min-1**  | Minor      | `Scanner.fs` | ファイル走査時にアクセス拒否（`UnauthorizedAccessException`）等が発生した場合の警告ログ出力考慮 | 実装時に考慮し、Archive 時に `handover.md` へ申し送り |
| **Sugg-1** | Suggestion | `app.js`     | トースト内エラーメッセージのクリックコピー補助機能                                              | 次期 UI 改善候補として `handover.md` へ申し送り       |

### 3. 総合判定

- **User Reviewer**: LGTM: true
- **SE Reviewer**: LGTM: true
- **LGTM**: true
- **合意状況**: 全ロール承認済み

---

## Apply タスクレビュー結果: Task 1.3 (VideoDirs モジュール)

- **日時**: 2026-11-07
- **フェーズ**: apply
- **タスク**: Task 1.3: VideoDirs モジュールのレビュー・破壊テスト
- **レビュアー**: `qa-reviewer`, `pg-reviewer`
- **使用プロトコル**: `openspec/templates/checklist_apply_test.md` / `checklist_apply_impl.md`
- **対象成果物**:
  - `src/TagBasedVideoManager/VideoDirs.fs`
  - `test/TagBasedVideoManager.Tests/Unit/VideoDirsTests.fs`

### 1. 挑戦的設問への回答サマリ

#### 1.1 qa-reviewer（Adversarial QA / トートロジー検証）

- **トートロジー・甘いアサーションの検証**:
  - 期待値は固定リテラル値でアサートされており、トートロジーは皆無。
  - UT-12 のアサーションに含まれていた甘い条件（`|| msg.Contains("VIDEO_DIRS")`）を告発し、ドライブ直下専用メッセージのみを判定するよう厳格化修正済み。
- **Adversarial QA（破壊テスト実行結果）**:
  - スクラッチスクリプトにて `null`、1万件の連続セミコロン、DoS 入力、多種多様なルートパス（`C:\`, `C:`, `c:/`, `/`, `\\` 等）と通常パスの混在、Unicode 絵文字パス等、全 42 パターンの破壊テストを作成・実行。
  - 例外終了せず全て適切な `Result.Ok` / `Result.Error` を返却し、**堅牢性を確認（Green / 落とせず）**。

#### 1.2 pg-reviewer（コードシンプルさ / 暗黙の仮定検証）

- **暗黙の仮定・例外握りつぶし**:
  - 純粋関数かつ ROP（`Result` 型）で例外握りつぶしは皆無。健全なエラーハンドリングを確認。
  - 末尾スラッシュ付きパスの正規化差異は後続タスク（Scanner の `Path.GetFullPath`）で吸収することを合意。
- **30% コード削減リファクタ**:
  - `deduplicate` を `List.distinctBy` に簡素化、`StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries` の活用、ブール論理式の単純化により、83 行から 56 行へ**約 32.5% のコード削減**を達成・反映済み。

### 2. 指摘事項（保留・申し送り）

| ID        | 種別  | 対象           | 内容                                                                                     | 対応方針                 |
| :-------- | :---- | :------------- | :--------------------------------------------------------------------------------------- | :----------------------- |
| **Min-2** | Minor | `VideoDirs.fs` | 末尾区切り付きパス（`D:\A\` と `D:\A`）の同一視は Scanner 側の `Path.GetFullPath` で担保 | 後続 Task 2.1/2.2 で検証 |

### 3. 総合判定

- **qa-reviewer**: LGTM: true
- **pg-reviewer**: LGTM: true
- **LGTM**: true
