# Antigravity CLI オーケストレーション手順（sdd-archive）

本書は **Antigravity CLI で `sdd-archive` を実行する場合のみ** 参照する専用手順です。Claude Code・GitHub Copilot CLI・OpenCode CLI・Hermes Agent では `SKILL.md` 本体のフォールバック手順（各エージェント標準のサブエージェント機構、または独立コンテキストでの直列実行）に従い、本書は不要です。

## 1. ロール別モデル・権限マッピング

ロール定義（`openspec/agents/<role>.md`）の frontmatter（`model_tier` / `thinking` / `write_tools`）から、サブエージェント定義時・呼び出し時の設定を決定する。

| ロール定義                         | `model_tier` | invoke_subagent の Model | Antigravity 上の位置付け | `write_tools` | define_subagent の EnableWriteTools  |
| :--------------------------------- | :----------- | :----------------------- | :----------------------- | :------------ | :----------------------------------- |
| `openspec/agents/user-reviewer.md` | `pro`        | `"pro"`                  | Gemini 4 Neon 級         | `false`       | `false`（読み取り専用）              |
| `openspec/agents/se-reviewer.md`   | `pro`        | `"pro"`                  | Gemini 4 Neon 級         | `false`       | `false`（読み取り専用）              |
| `openspec/agents/pg-reviewer.md`   | `pro`        | `"pro"`                  | Gemini 4 Neon 級         | `false`       | `false`（読み取り専用）              |
| `openspec/agents/qa-reviewer.md`   | `pro`        | `"pro"`                  | Gemini 4 Neon 級         | `true`        | `true`（破壊テスト作成・実行のため） |

- Archive 総括レビューは 4 ロールすべてを `Model: "pro"` で実行する。`Model: "flash"` は Archive フェーズでは使用しない（`flash` は `qa-tester` / `pg-implementer` の実装系ロール用で、Apply フェーズで使う）。
- `model_tier` と `write_tools` は必ずロール定義ファイルの値を正とし、本書の表と食い違う場合はロール定義を優先する（`model_tier` / `thinking` / `write_tools` は CLI 自動構文ではなく、親エージェントが参照する運用規約メタデータ。`thinking` は推論深度の運用指針としてプロンプト指示等に反映する）。
- Gemini 3.8 Flash は非推奨のため使用しない。

## 2. define_subagent（物理的な書き込み分離）

作業開始時に、使用するロールごとに `define_subagent` でサブエージェントを定義する。

1. `openspec/agents/<role>.md` を読み、frontmatter の `write_tools` を確認する。
2. `define_subagent` のシステム指示にロール定義の本文を設定し、`EnableWriteTools` に `write_tools` の値（`true` / `false`）をそのまま設定する。
3. `write_tools: false` のロール（`user-reviewer`, `se-reviewer`, `pg-reviewer`）は、`EnableWriteTools=false` により編集ツールを物理的に持たない状態で定義する。
4. `qa-reviewer`（`write_tools: true`）は破壊テストを作成・実行するために書き込みツールを持つが、既存テストおよびプロダクションコードの改変、`reviews.md` への書き込みは行わない。

※ ツールのパラメータ名・指定形式は使用中の Antigravity CLI のバージョンに従う。

## 3. invoke_subagent（Model の明示指定）

定義済みサブエージェントを `invoke_subagent` で呼び出す際は、`model_tier` に対応する Model を**必ず明示**する。省略して既定モデルに任せない。

- `model_tier: pro` のロール（`user-reviewer`, `se-reviewer`, `pg-reviewer`, `qa-reviewer`）: `invoke_subagent(Model: "pro", ...)`
- `model_tier: flash` のロール（Archive フェーズでは通常使用しない）: `invoke_subagent(Model: "flash", ...)`
- プロンプトには変更名・対象成果物のパス（`openspec/changes/<name>/` 配下と `git diff`）・チェックリスト `openspec/templates/checklist_archive.md` のパス・返却形式（`openspec/templates/review_record.md` 形式のテキスト）を含め、作成者の思考過程は渡さない。

## 4. 単一ライター原則

`reviews.md` と `tasks.md` への書き込みは親エージェント（司令塔）のみが行い、サブエージェントはファイルへ書き込まず `review_record.md` 形式のレビュー結果テキストまたは作業サマリを親エージェントへ返却する。`handover.md` も親エージェントが作成・更新する。

## 5. run_workflow による Archive 総括レビューの並列実行

`scripts/run_archive_review.py` は `run_workflow` ツール専用スクリプトで、`user-reviewer` / `se-reviewer` / `pg-reviewer` / `qa-reviewer` の 4 ロールを `Model: "pro"` で並列起動する。

1. `run_workflow` ツールに `.agents/skills/sdd-archive/scripts/run_archive_review.py`（リポジトリルート基準）を指定し、`args` に `{"change_name": "<change-name>"}` を渡す。
2. スクリプトは `phase("Archive Review")` のもとで `parallel` により 4 ロールを起動し、ロール別のレビュー結果テキスト（`review_record.md` 形式）を返す。
3. 親エージェントが各結果を `openspec/changes/<change-name>/reviews.md` へ追記し、全ロールが `LGTM: true` か判定する（最大 3 サイクル）。

通常の `python` / `python3` で直接実行すると、`run_workflow` 専用スクリプトである旨と代替手順（本書 §3 の `invoke_subagent` による手動実行）を標準エラーへ出力して終了コード 2 で停止する。直接実行しても処理は行われない。

## 6. parallel 部分失敗時の回復手順

`run_workflow` の `parallel` は 1 ロールが失敗またはタイムアウトすると全体がキャンセルされる場合があるため、その場合は親エージェントが未完了ロールのみを `invoke_subagent(Model: "pro")` で単体再実行する（成功済みロールのレビューは再実行しない）。

1. 失敗した `run_workflow` の出力・ログから、結果が返却済みのロールと未完了のロールを特定する。
2. 返却済みロールの結果テキストは保持し、`reviews.md` への追記対象に含める。
3. 未完了ロールのみ、§3 の手順で `invoke_subagent(Model: "pro")` により個別に呼び出し、結果テキストを取得する。
4. 同じロールが 2 回連続で失敗する場合は、`SKILL.md` のフォールバック（独立コンテキストでの直列実行）へ切り替えるか、ユーザーへ報告する。
