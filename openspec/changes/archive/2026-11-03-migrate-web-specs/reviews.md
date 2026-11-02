# レビュー記録: migrate-web-specs / propose

## サイクル 1: 初回レビュー

- **日時**: 2026-11-03
- **フェーズ**: propose
- **タスク**: N/A (Proposeフェーズ計画成果物レビュー)
- **レビュアー**: User Reviewer Agent & SE Reviewer Agent
- **使用プロトコル**: `openspec/templates/checklist_propose.md`
- **対象成果物**: `proposal.md`, `specs/**/spec.md`, `design.md`, `test_plan.md`, `tasks.md`

### 1. 指摘事項（サイクル 1）

#### User Reviewer 指摘

1. **[Major] `tag-management/spec.md`**: 階層型タグにおける循環参照防止バリデーションおよび親タグ削除時（`ON DELETE SET NULL`）の仕様未定義。
   - **対応方針**: 採用。`tag-management/spec.md` に循環参照防止要件および親タグ削除時の親解除シナリオを追記。
2. **[Major] `video-library/spec.md`**: 実動画ファイル消失時におけるストリーミング再生のエラー応答（404）仕様未定義。
   - **対応方針**: 採用。`video-library/spec.md` に実ファイル未存在時の 404 Not Found 返却シナリオを追記。
3. **[Minor] `search-and-folders/spec.md`**: 検索DSLにおける不正構文入力時のフォールバック振る舞い（フリーキーワード扱い）および検索0件時のUI仕様（Empty-State）未定義。
   - **対応方針**: 採用。`search-and-folders/spec.md` に未知構文のフリーキーワードフォールバックおよび検索0件シナリオを追記。
4. **[Minor] 各仕様書の初期状態（Zero-State）**: `video-library/spec.md`（動画0件時）、`viewing-analytics/spec.md`（ランキング0件時、エラーログ0件時）のEmpty-State仕様明記漏れ。
   - **対応方針**: 採用。両仕様書に Zero-State / Empty-State シナリオを追記。
5. **[Suggestion] 破損ファイル再スキャン時の課題**: 将来の申し送り事項として `design.md` に記載。
   - **対応方針**: 採用。`design.md` の Risks / Technical Debt に明記。

#### SE Reviewer 指摘

1. **[Major] 外部依存（FFmpeg）ハング時の連鎖崩壊リスクと仕様・設計**: プロセスハング時に同時実行数1のセマフォが永久ロックされ後続パイプラインが完全停止するリスクへの言及欠落。
   - **対応方針**: 採用。`video-scanner/spec.md` に安全中断要件を追記し、`design.md` の Risks / Technical Debt にプロセスハングリスクと将来のタイムアウト導入課題を明記。
2. **[Major] 可観測性の技術的負債**: アプリ再起動時の `ErrorLogs` 自動クリア（証拠隠滅）およびログへの動画ID/ファイルパスコンテキスト欠落。
   - **対応方針**: 採用。`design.md` の Risks / Technical Debt に既知の負債・将来改善方針として明記。
3. **[Major] `test_plan.md` の死角**: FFmpeg未インストールでのサイレントスキップ、Rangeストリーミング切断時の例外耐性、モックによるエッジケース隠蔽。
   - **対応方針**: 採用。`test_plan.md` に「死角・既知の限界と将来の改善課題」セクションを新設して明記。
4. **[Minor] `tasks.md` 完了基準の明確化**: タスク 1.1〜1.5 の完了基準の具体化。
   - **対応方針**: 採用。テストメソッド名突合およびマトリクス確定を明確な完了基準として追記。

### 判定（サイクル 1）

- **LGTM**: false（Major指摘解消のため修正を実施）

---

## サイクル 2: 指摘事項反映後の再判定

- **日時**: 2026-11-03
- **フェーズ**: propose
- **レビュアー**: User Reviewer Agent & SE Reviewer Agent (統合判定)
- **対象成果物**: 修正後の全成果物（`proposal.md`, `specs/**/spec.md`, `design.md`, `test_plan.md`, `tasks.md`）

### 確認結果

- サイクル 1 で指摘された User Reviewer からの Major 2件・Minor 2件、および SE Reviewer からの Major 3件・Minor 1件の指摘事項がすべて各仕様書（`specs/`）、`design.md`、`test_plan.md`、`tasks.md` に完全反映されたことを確認した。
- 仕様書は観測可能な振る舞い（Behavior-Driven）を維持しつつ、エッジケース（実ファイル消失、循環参照、Zero-State、Empty-State、未知構文フォールバック）を漏れなく網羅した。
- 設計書およびテスト計画書において、プロセスハングや可観測性に関する既知の技術的負債・死角が明確化され、実装・運用の透明性が担保された。

### 判定（サイクル 2）

- **LGTM**: true
- **条件付きLGTM**: なし
- **指摘サマリ**:
  - `Critical`: 0 件
  - `Major`: 0 件（全件解消済み）
  - `Minor`: 0 件（全件解消済み）
  - `Suggestion`: 1 件（破損ファイル再投入の将来改善として design.md に記録済み）
- **保留指摘（申し送り）**:
  - 破損動画・未対応ストリームにおける再スキャン時の無限再投入防止の将来改善
- **次のアクション**: フェーズゲート検証（Gate 1）を実行し、ユーザー合意を経て Apply フェーズへ移行する。
