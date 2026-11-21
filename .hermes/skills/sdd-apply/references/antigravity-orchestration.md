# Antigravity CLI オーケストレーション手順（sdd-apply）

本書は **Antigravity CLI で `sdd-apply` を実行する場合のみ** 参照する専用手順です。Claude Code・GitHub Copilot CLI・OpenCode CLI・Hermes Agent では `SKILL.md` 本体のフォールバック手順（各エージェント標準のサブエージェント機構、または独立コンテキストでの直列実行）に従い、本書は不要です。

## 1. ロール別モデル・権限マッピング

ロール定義（`openspec/agents/<role>.md`）の frontmatter（`model_tier` / `thinking` / `write_tools`）から、サブエージェント定義時・呼び出し時の設定を決定する。

| ロール定義                          | `model_tier` | invoke_subagent の Model | Antigravity 上の位置付け | `write_tools` | define_subagent の EnableWriteTools  |
| :---------------------------------- | :----------- | :----------------------- | :----------------------- | :------------ | :----------------------------------- |
| `openspec/agents/qa-tester.md`      | `flash`      | `"flash"`                | Gemini 4 Hydrogen 級     | `true`        | `true`                               |
| `openspec/agents/pg-implementer.md` | `flash`      | `"flash"`                | Gemini 4 Hydrogen 級     | `true`        | `true`                               |
| `openspec/agents/pg-reviewer.md`    | `pro`        | `"pro"`                  | Gemini 4 Neon 級         | `false`       | `false`（読み取り専用）              |
| `openspec/agents/qa-reviewer.md`    | `pro`        | `"pro"`                  | Gemini 4 Neon 級         | `true`        | `true`（破壊テスト作成・実行のため） |
| `openspec/agents/se-reviewer.md`    | `pro`        | `"pro"`                  | Gemini 4 Neon 級         | `false`       | `false`（L3・設計変更時のみ使用）    |

- `model_tier` と `write_tools` は必ずロール定義ファイルの値を正とし、本書の表と食い違う場合はロール定義を優先する。
- 親エージェント（司令塔）は Orchestrator（Neon 級）として動作し、サブエージェントの結果集約・`reviews.md` / `tasks.md` 更新・Gate 2 実行を担う。
- Gemini 3.8 Flash は非推奨のため使用しない。

## 2. define_subagent（物理的な書き込み分離）

作業開始時に、使用するロールごとに `define_subagent` でサブエージェントを定義する。

1. `openspec/agents/<role>.md` を読み、frontmatter の `write_tools` を確認する。
2. `define_subagent` のシステム指示にロール定義の本文を設定し、`EnableWriteTools` に `write_tools` の値（`true` / `false`）をそのまま設定する。
3. `write_tools: false` のロール（`pg-reviewer`, `se-reviewer`）は、`EnableWriteTools=false` により編集ツールを物理的に持たない状態で定義する。
4. `qa-reviewer`（`write_tools: true`）は破壊テストを作成・実行するために書き込みツールを持つが、既存テストおよびプロダクションコードの改変、`reviews.md` への書き込みは行わない。

※ ツールのパラメータ名・指定形式は使用中の Antigravity CLI のバージョンに従う。

## 3. invoke_subagent（Model の明示指定）

定義済みサブエージェントを `invoke_subagent` で呼び出す際は、`model_tier` に対応する Model を**必ず明示**する。省略して既定モデルに任せない。

- `model_tier: flash` のロール（`qa-tester`, `pg-implementer`）: `invoke_subagent(Model: "flash", ...)`
- `model_tier: pro` のロール（`pg-reviewer`, `qa-reviewer`, `se-reviewer`）: `invoke_subagent(Model: "pro", ...)`
- プロンプトには変更名・対象タスク・対象成果物のパス・参照すべきチェックリスト（`openspec/templates/checklist_apply_test.md` / `openspec/templates/checklist_apply_impl.md`）・返却形式を含め、他ロールの思考過程は渡さない。

## 4. 単一ライター原則

`reviews.md` と `tasks.md` への書き込みは親エージェント（司令塔）のみが行い、サブエージェントはファイルへ書き込まず `review_record.md` 形式のレビュー結果テキストまたは作業サマリを親エージェントへ返却する。`qa-tester` / `pg-implementer` が返却するのは作成・更新したファイル一覧と実行結果のサマリで、コード・テストファイル自体の作成は各ロールの責務として行ってよい。

## 5. 実行順序（タスクごとの Red → Green → Review）

Apply フェーズの 1 タスクは次の順で、各ロールを別コンテキストのサブエージェントとして起動する。

1. **`qa-tester`（`Model: "flash"`）**: `specs/`・`design.md`・`test_plan.md` から Red テストを作成する。親は返却サマリと Red の実確認ログを受け取る。
2. **`pg-implementer`（`Model: "flash"`）**: Red テストを通す Green 実装と Refactor を行う。既存テストは改変しない。仕様誤認の疑いがあれば書き換えず親へ差し戻させる。
3. **`pg-reviewer` / `qa-reviewer`（`Model: "pro"`）**: 実装は `pg-reviewer`（`openspec/templates/checklist_apply_impl.md`）、テストは `qa-reviewer`（`openspec/templates/checklist_apply_test.md`）でレビューする。L3 または設計変更時は `se-reviewer`（`Model: "pro"`）も追加する。
4. 親がレビュー結果を `reviews.md` へ追記し、`Critical` / `Major` を修正して再レビューする（最大 3 サイクル）。
5. 親が Gate 2（`verify-gate.ps1 -Gate apply`）を実行し、PASS 後に `tasks.md` を更新・コミットする。

`sdd-apply` 用の `run_workflow` スクリプトは提供しない（Red → Green → Review は依存関係のある直列処理のため）。複数レビュアーを並行させたい場合は、レビュアー呼び出しのみ複数の `invoke_subagent` を同時に発行してよい。

## 6. 部分失敗時の回復手順

並行して発行した `invoke_subagent` や並列実行（`parallel`）の一部ロールが失敗またはタイムアウトした場合は、親エージェントが未完了ロールのみを `invoke_subagent` で単体再実行する（レビュアーなら `Model: "pro"`、`qa-tester` / `pg-implementer` なら `Model: "flash"`）。成功済みロールの結果は保持し、再実行しない。同じロールが 2 回連続で失敗する場合は、`SKILL.md` のフォールバック（独立コンテキストでの直列実行）へ切り替えるか、ユーザーへ報告する。

## 7. L3 規模での swarm 運用（複数ファイル並列実装）

L3（アーキテクチャ変更・複数コンポーネント横断）で、互いに独立した複数のファイル群を並列実装する場合に限り、`swarm` を使用できる。L1 / L2 では使わず、§5 の直列サイクルを守る。

1. **構成**: 司令塔（Orchestrator）は Gemini 4 Neon 級（`pro`）。ワーカーは Gemini 4 Hydrogen 級（`flash`）の `pg-implementer` を複数起動する。
2. **workspace**: ワーカーはすべて `workspace: "share"`（共有ワークスペース）で起動する。
3. **競合回避**: 1 ワーカー 1 担当ファイル群とし、複数ワーカーが同じファイルを編集してはならない。担当割り当ては司令塔が `design.md` / `tasks.md` のモジュール境界から決め、プロンプトに担当ファイルの一覧と「それ以外は編集禁止」を明記する。
4. **共有ファイルの扱い**: `reviews.md` / `tasks.md` は共有ワークスペース内でも司令塔のみが書き込む。ワーカーは作業サマリを返却するだけとする。
5. **Verifier**: ワーカー完了後、`qa-reviewer`（`Model: "pro"`）と `pg-reviewer`（`Model: "pro"`）を Verifier として起動し、全担当分の差分を統合してレビューする。ワーカー自身の自己評価を合否判定に使わない。
6. **失敗時**: 失敗したワーカーの担当分のみを §6 の手順で単体再実行する。他ワーカーの担当ファイルには触れない。
7. 統合後、司令塔が全テスト・カバレッジ・Gate 2 を実行する。
