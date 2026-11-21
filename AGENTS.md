# AGENTS

## プロジェクト概要

TagBasedVideoManager は、タグベースの動画管理システムです。バックエンドに F# (.NET 10) + Giraffe、フロントエンドに Alpine.js + Tailwind CSS、デスクトップアプリに Avalonia.FuncUI を採用しています。

## 構成

- `src/`
  - ソースコードのルートディレクトリ（F# + Giraffe プロジェクト）
- `src/wwwroot/`
  - フロントエンド静的ファイル（`index.html`, `app.js`, `app.css` 等）の配置ディレクトリ
- `test/`
  - テストコードのルートディレクトリ（xUnit + FsUnit, Playwright E2E プロジェクト）
- `test/TestResults/`
  - テスト実行エビデンスおよびカバレッジレポートの出力ディレクトリ（※Git管理対象外）
  - TRX テストレポートファイル (`TestRun_yyyy-MM-dd_HH_mm_ss_net10.0.trx`)
  - HTML カバレッジレポート (`CoverageReport/index.html`)
  - E2E テスト失敗時の画面キャプチャ画像 (`.png`)
- `assets/`
  - 静的アセット（README等で参照されるスクリーンショット画像等）の配置ディレクトリ
- `openspec/`
  - OpenSpecフレームワークのルートディレクトリ
- `openspec/config.yaml`
  - OpenSpec設定（スキーマ: `spec-driven`、運用ルール・レビュー定義）
- `openspec/specs/`
  - メイン仕様書・デルタ仕様書の配置ディレクトリ（システムの恒久的な仕様書）
- `openspec/changes/`
  - 変更管理（提案・タスク・設計・レビュー記録）の配置ディレクトリ
- `openspec/changes/archive/`
  - 完了した変更のアーカイブ
- `openspec/templates/`
  - OpenSpecレビュー・チェックリスト用テンプレート
- `openspec/agents/`
  - サブエージェント6ロールのシステムプロンプト定義
- `openspec/scripts/`
  - フェーズゲート検証スクリプト（`verify-gate.ps1`, `verify-gate.sh`）

## 利用可能なMCPサーバ

## 利用可能なSkills

- `openspec-propose` - 新しい変更の提案・計画アーティファクト生成
- `openspec-apply-change` - 変更の実装・タスク実行
- `openspec-archive-change` - 完了した変更のアーカイブ
- `openspec-sync-specs` - デルタ仕様からメイン仕様への同期
- `openspec-update-change` - 既存変更の計画見直し
- `openspec-explore` - アイデア探索・要件明確化
- `sdd-propose` - Gate 0〜1・規模レベル判定・`test_plan.md`・User/SE レビュー付き変更提案
- `sdd-apply` - 6 ロール TDD 3 権分立・C0/C1/C2 検証・Gate 2 付きタスク実装
- `sdd-archive` - DoD 最終検証・`handover.md`・4 ロール総括レビュー・Gate 3・PR 作成付きアーカイブ

---

## プロジェクト固有ルール

### 自動フォーマット

| ファイル種別                   | 対象拡張子                       | 使用ツール        | 実行コマンド                                            |
| ------------------------------ | -------------------------------- | ----------------- | ------------------------------------------------------- |
| **Markdown ドキュメント**      | `.md`                            | Prettier          | `npx prettier --write "**/*.md"`                        |
| **JSON 設定ファイル**          | `.json`                          | Prettier          | `npx prettier --write "**/*.json"`                      |
| **OpenSpec アーティファクト**  | `openspec/**/*.md`               | Prettier          | `npx prettier --write "openspec/**/*.md"`               |
| **Web 静的ファイル**           | `src/wwwroot/**/*.{html,js,css}` | Prettier          | `npx prettier --write "src/wwwroot/**/*.{html,js,css}"` |
| **F# / .NET ソースコード**     | `src/**/*.fs`, `test/**/*.fs`    | dotnet format     | `dotnet format whitespace` / `dotnet format`            |
| **F# ソースコード (Fantomas)** | `src/**/*.fs`, `test/**/*.fs`    | Fantomas (導入時) | `dotnet fantomas "src/**/*.fs" "test/**/*.fs"`          |

### 実装ガイドライン（本プロジェクト固有規約）

- **F# Idioms & Architecture**:
  - `Result` 型（鉄道指向プログラミング / Railway Oriented Programming）によるエラーハンドリングを徹底し、イミュータブルな設計を優先する。
  - バックエンドは F# (.NET 10) + Giraffe を採用し、非同期処理には `task { ... }` コンピュテーション式を使用する。
- **データアクセス (SQLite)**:
  - `Microsoft.Data.Sqlite` および `Dapper` を使用してSQLiteデータベースへアクセスする。
- **動画ストリーミング**:
  - `PhysicalFileResult` を利用し、`EnableRangeProcessing = true` を設定して HTTP 206 Partial Content (Range Requests) に対応させる。
- **メディア処理 (FFmpeg)**:
  - 外部プロセスとして FFmpeg を呼び出し、メタデータの取得およびサムネイル生成を行う。
- **フロントエンド (Web)**:
  - HTML5 + Alpine.js (CDN) + Tailwind CSS (CDN) による構成とし、Node.js環境やビルドツールを一切排除する。
  - 状態管理やインタラクション、インクリメンタルサーチは Alpine.js の機能で完結させる。
- **デスクトップアプリケーション (Avalonia)**:
  - F# (.NET 10) + Avalonia.FuncUI (Elmish MVU) を採用する。
  - **UI変更時のHTMLモック作成義務**:
    画面レイアウト、インタラクション、配色、コンポーネント構成等の UI/UX に変更が入る場合は、**必ず Propose フェーズまたは設計段階で HTML モックアップ（`openspec/changes/<name>/mockup.html`）を作成・更新しなければならない**。HTML モックは Tailwind CSS (CDN) + Alpine.js (CDN) を用いて、ブラウザで即座にプレビューおよび状態変化・アニメーション等のインタラクション確認が可能な単一ファイルとして作成し、ユーザーとの合意形成および実装の Single Source of Design とする。
  - **UI・モックデザイン忠実再現の原則**: 仕様書や `openspec/` 配下に UI モック（`mockup.html` 等）やデザイン詳細が定義されている場合、実装者はデザイン、テーマ（ダーク/ライトモード）、配色、レイアウト、フォント、各カードやボタンのスタイルを勝手に簡略化・改変してはならない。モックアップのデザイン・情報階層を忠実に再現すること。
  - FluentTheme のダークモードを基本とし、視認性の高いコントラストと適切な余白・コントロール高さを担保する。
- **DSL Utilization**:
  - 検索機能には DSL を活用し、宣言的な記述を行う。
- **TDD（テスト駆動開発）**:
  - **テストコードは実装コードより先行して作成する（Test First）**。フロントエンドなどテスト困難な領域については並行作成も許容するが、必ず実装完了前にテストを用意する。
  - テストフレームワーク: `xUnit` + `FsUnit`（バックエンド）、Playwright（E2E）。
  - テスト結果を TRX 形式でファイル出力する (git管理不要)。
    - 出力先：`test/TestResults/TestRun_yyyy-MM-dd_HH_mm_ss` (実行の都度作成されるタイムスタンプフォルダ)
    - ファイル名：`TestRun_yyyy-MM-dd_HH_mm_ss_net10.0.trx` (および同フォルダ内に `CoverageReport` ディレクトリとしてHTMLカバレッジレポートを出力)
    - E2Eテスト時、NGケースは画面キャプチャも取得する。
- **Environment Variables**:
  - 機密情報および環境依存の設定は `.env` で管理し、絶対にソースコードに含めない。
  - 必須環境変数:
    - 本体 (Web / Docker): `PORT`, `VIDEO_DIRS`（複数フォルダを `;` 区切りで指定）, `THUMBNAIL_DIR`, `DATABASE_PATH`
    - AI File Renamer (デスクトップアプリ): `VIDEO_DIR`（単一フォルダ指定）
- **コメント**:
  - ソースコード内のコメントは日本語で記載し、XMLドキュメントコメント形式（`///`）とする。
- **プロジェクト・依存関係管理**:
  - `.fsproj` などの設定ファイルはAIが手動で構築するのではなく、`dotnet` CLI（SDK）を活用して作成・更新する。
- **セマンティックバージョニング (SemVer 2.0.0)**:
  - Web アプリケーション（`src/TagBasedVideoManager/TagBasedVideoManager.fsproj`）とデスクトップアプリケーション（`src/TagBasedVideoManager.Renamer/TagBasedVideoManager.Renamer.fsproj`）は独立した `<Version>X.Y.Z</Version>` でバージョンを管理する。初期値は双方 `0.1.0`。
  - Web アプリは画面ヘッダー（バッジ）および `<title>` に `vX.Y.Z` を表示し、`GET /api/version`（キャッシュ抑止ヘッダー付き）経由で提供する。
  - デスクトップアプリはウィンドウタイトルバー（`base.Title`）および起動ログに `TagBasedVideoManager - AI File Renamer vX.Y.Z` を表示・出力する。
  - OpenSpec の変更規模レベルまたはユーザー指示に基づき、各プロジェクトの `.fsproj`（`<Version>`）を以下の規則に従ってインクリメントする：

| バージョン桁  | 連動トリガー                            | 対象変更内容の例                                                    | リセット規則                           |
| :------------ | :-------------------------------------- | :------------------------------------------------------------------ | :------------------------------------- |
| **X (Major)** | **L3 規模** または **ユーザー明示指示** | アーキテクチャ変更、破壊的API変更、互換性のないDB変更、ユーザー指示 | `Y`, `Z` を `0` にリセット (`X+1.0.0`) |
| **Y (Minor)** | **L2 規模**                             | 後方互換性のある新機能追加、仕様変更、UI/UX変更、通常機能改善       | `Z` を `0` にリセット (`X.Y+1.0`)      |
| **Z (Patch)** | **L1 規模**                             | 後方互換性のある不具合修正、設定微修正、typo修正、局所的リファクタ  | なし (`X.Y.Z+1`)                       |

## 参考情報

- [.NET を使用して最小限の MCP クライアントを作成する](https://learn.microsoft.com/ja-jp/dotnet/ai/quickstarts/build-mcp-client)
- [.NET Agent Skills](https://github.com/dotnet/skills)
- [Giraffe Documentation](https://giraffe.wiki/)
- [Dapper - Tutorial](https://github.com/DapperLib/Dapper)
- [Railway Oriented Programming (F# for Fun and Profit)](https://fsharpforfunandprofit.com/rop/)

---

<!-- openspec-kit:begin (install.ps1 により管理。手動編集は再同期で上書きされます) -->

## OpenSpec 運用ルール（openspec-kit）

本プロジェクトは **OpenSpec** による仕様駆動開発 (SDD) × テスト駆動開発 (TDD) を採用しています。

- **Single Source of Truth**: システム仕様の正本は `openspec/specs/`。OpenSpec 管理外に仕様書・タスクを新規作成・二重管理しない。
- **フェーズゲート駆動**: すべての変更は **Explore → [Gate 0] → Propose (`sdd-propose`) → [Gate 1] → Apply (`sdd-apply`) → [Gate 2] → Archive (`sdd-archive` / [Gate 3])** のゲートを通過して進める。
- **サブエージェント6ロール分業（TDD 3権分立）**: テスト作成（`qa-tester`）・実装（`pg-implementer`）・批判的レビュー（`user/se/pg/qa-reviewer`）のコンテキストを分離し、甘いテスト・テスト改ざん・追認レビューを構造的に防ぐ。
- **詳細手順とリファレンス**: 各フェーズ実行時は導入先スキル配下の `sdd-{propose,apply,archive}/SKILL.md`（`.agents/skills/`、`.claude/skills/`、`.github/skills/`、`.opencode/skills/`、`.hermes/skills/` のうち導入先ディレクトリ配下、および同 `references/sdd-reference.md`・`references/antigravity-orchestration.md`）を必ず参照すること。

### ディレクトリ構成

| パス                               | 内容                                                                                                     |
| ---------------------------------- | -------------------------------------------------------------------------------------------------------- |
| `openspec/config.yaml`             | OpenSpec 設定（スキーマ・規模レベル・6ロール定義・TDD/カバレッジ基準・運用ルール）                       |
| `openspec/specs/`                  | メイン仕様（システムの恒久的な仕様）                                                                     |
| `openspec/changes/<name>/`         | 変更単位の成果物（`proposal`, `specs`, `design`, `test_plan`, `tasks`, `mockup`, `reviews`, `handover`） |
| `openspec/agents/`                 | サブエージェント6ロール定義（`qa-tester`, `pg-implementer`, `user/se/pg/qa-reviewer`）                   |
| `openspec/templates/`              | `test_plan.md`, `review_record.md`, `checklist_*.md`, `handover.md`, `pr_body.md`                        |
| `openspec/scripts/verify-gate.ps1` | フェーズゲート（Gate 0〜3 / DoD）の機械的検証スクリプト（Bash 版: `verify-gate.sh`）                     |
| `.agents/skills/sdd-*/SKILL.md`    | SDD 実行スキル（`sdd-propose`, `sdd-apply`, `sdd-archive`。`.claude/skills/` 等にも同内容展開）          |
| `test/TestResults/`                | テストエビデンス・カバレッジレポート出力先（Git 管理外）                                                 |

### 適用規模レベル（L1 / L2 / L3）

変更開始時に規模レベルを決定し、`.openspec.yaml`（`level: L2` または CLI 警告回避時は `x-level: L2`）と `proposal.md` に明記する。

| 項目                 | L1（軽微: typo・設定・局所修正） | L2（通常: 機能追加・仕様変更・通常バグ）                                        | L3（大規模: アーキテクチャ・破壊的変更・DB変更） |
| -------------------- | -------------------------------- | ------------------------------------------------------------------------------- | ------------------------------------------------ |
| 必須成果物           | `proposal`, `tasks`              | `proposal`, `specs`, `design`, `test_plan`, `tasks`（UI変更時は `mockup.html`） | L2 全成果物 ＋ 移行計画・ロールバック手順        |
| Propose レビュー     | `se-reviewer` のみ可             | `user-reviewer` + `se-reviewer`                                                 | `user-reviewer` + `se-reviewer`                  |
| Apply 分業           | メイン兼任可 ＋ PG/QA レビュー   | **`qa-tester` → `pg-implementer` → PG/QAレビュー**                              | L2 ＋ 設計変更時は `se-reviewer` 必須            |
| Archive 総括レビュー | 全4ロール ＋ `handover.md`       | 全4ロール ＋ `handover.md`                                                      | 全4ロール ＋ `handover.md`                       |

### サブエージェント6ロール分業とモデルティア・権限契約

同一エージェントによる自作自演 TDD を防ぐため、以下の 6 ロールを独立コンテキストで起動する（Gemini 3.8 Flash は非推奨のため使用しない）。

| ロール             | 定義ファイル                        | `model_tier` / `thinking` / `write_tools` | 担当フェーズ      | 責務・ガードレール                                                                |
| ------------------ | ----------------------------------- | ----------------------------------------- | ----------------- | --------------------------------------------------------------------------------- |
| **QA Tester**      | `openspec/agents/qa-tester.md`      | `flash` / `high` / `true`                 | Propose / Apply   | `specs/` と `test_plan.md` から先行 Red テスト・結合/E2E を実装（本実装編集禁止） |
| **PG Implementer** | `openspec/agents/pg-implementer.md` | `flash` / `medium` / `true`               | Apply             | Red テストを改ざんせず Green 実装＆Refactor（skip化・TODO残し・空catch禁止）      |
| **User Reviewer**  | `openspec/agents/user-reviewer.md`  | `pro` / `high` / `false`                  | Propose / Archive | ブラインド仕様比較・Zero-State/Error-State 体験・Why 欠落告発（Read-only）        |
| **SE Reviewer**    | `openspec/agents/se-reviewer.md`    | `pro` / `high` / `false`                  | 全フェーズ        | 外部依存ハング等の故障シミュレーション・状態不整合・隠された負債告発（Read-only） |
| **PG Reviewer**    | `openspec/agents/pg-reviewer.md`    | `pro` / `high` / `false`                  | Apply / Archive   | 暗黙の仮定・例外握りつぶし告発・30%コード削減リファクタ提案（Read-only）          |
| **QA Reviewer**    | `openspec/agents/qa-reviewer.md`    | `pro` / `high` / `true`                   | Apply / Archive   | 破壊テスト作成・実行（Adversarial QA）・トートロジーと甘いアサーション告発        |

- **単一ライター原則**: `reviews.md` および `tasks.md` への書き込みは親エージェント（司令塔）のみが行い、サブエージェントは `review_record.md` 形式のテキストまたは作業サマリを親へ返却する。
- **最大3サイクルとエスカレーション**: `Critical` / `Major` 指摘が 0 件（`LGTM: true`）になるまで修正・再レビューを繰り返す（最大 3 サイクルで未解消ならユーザーへエスカレーション）。保留とした `Minor` / `Suggestion` は Archive 時に `handover.md` へ全件転記する。

### テスト網羅性基準（C0 / C1 / C2・結合・E2E）要約

- **単体テスト (Unit)**: **C0（命令）** 全体 80%+ / 差分 90%+、**C1（分岐）** 全体 80%+ / 差分 85%+、**C2（複合条件デシジョンテーブル）** 100%。同値分割・境界値（`min-1`, `min`, `max`, `max+1`, 空/0件/1件/上限, 日時境界）必須。外部 I/O 以外のモックとトートロジー禁止。
- **結合テスト (Integration)**: 全外部/DB境界 100%（正常往復＋4xx/5xx/Timeout/切断/制約違反の Fault Injection）、状態遷移 100%（0-switch 正常＋1-switch 不正拒否・冪等性）。
- **E2Eテスト (End-to-End)**: `specs/` 全 Scenario 100%（① Happy Path、② Zero-State、③ Negative/Recovery Path、UI は実画面 PNG キャプチャ検証）。
- **不具合修正 (`fix/*`)**: ① Root Cause 特定 → ② `qa-tester` による再現 Red テスト先行作成 → ③ `pg-implementer` 最小修正＆横展開確認。

### 完了基準（DoD）と機械的フェーズゲート

各フェーズ末尾で **`openspec/scripts/verify-gate.ps1`**（または `verify-gate.sh`）を実行し、`PASSED` を確認してから次へ進む。

| ゲート     | タイミング                           | 検証コマンド                                                                        | 通過条件（要約）                                                                               |
| :--------- | :----------------------------------- | :---------------------------------------------------------------------------------- | :--------------------------------------------------------------------------------------------- |
| **Gate 0** | `openspec new change` 直前           | `pwsh ./openspec/scripts/verify-gate.ps1 -Gate pre-propose`                         | 未コミット変更退避済み、最新 `develop` から切ったトピックブランチ上                            |
| **Gate 1** | Propose 合意・commit 前              | `openspec validate "<name>"` & `verify-gate.ps1 -Gate propose -ChangeName "<name>"` | 規模別必須成果物完備、`reviews.md` 未判定 0 件かつ Propose レビュー `LGTM: true`               |
| **Gate 2** | Apply 各タスク `[x]` 更新・commit 前 | `pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply -ChangeName "<name>"`          | フォーマット/静的解析エラー0、全テストPass、C0/C1/C2達成、skip放置0、最新レビュー `LGTM: true` |
| **Gate 3** | `openspec archive` 実行前            | `pwsh ./openspec/scripts/verify-gate.ps1 -Gate archive -ChangeName "<name>"`        | 未完了タスク 0 件、`handover.md` 記入済み、全4ロール総括レビュー `LGTM: true`、DoD達成         |

### Git 操作ルールと禁止事項（ハードガードレール）

- **Gate 0 ブランチ作成**: 未コミット変更を `git stash` 退避後、`git checkout develop && git pull origin develop && git fetch --prune` を実行し、マージ済みブランチを整理してから `git checkout -b feature/<change-name>`（または `fix/`・`refactor/`・`chore/`）を作成する。
- **Commit / Push / PR**: Gate 1・Gate 2（タスク単位）・Gate 3（Archive 後）の各ゲート通過後に Conventional Commits（`<type>(<change-name>): <subject>`）で commit する。Archive 後は `git push` と `gh pr create --base develop`（`pr_body.md` 準拠）まで自動実行し、人間承認後に **Merge commit**（`gh pr merge --merge --delete-branch`）で統合する。
- **自動フォーマット**: ファイル更新時・コミット前・テスト実行前に、本ファイル管理区間外の「自動フォーマット」表のコマンドを実行する。
- **禁止事項**:
  - `develop` および `main` への直接コミット・直接 push
  - `verify-gate.ps1` を実行・通過せずに行うフェーズ移行・`[x]` 更新・Archive 実行
  - 既存テストの期待値改ざん・アサーション削除・`skip` 化による見せかけの Green 化
  - **ユーザー承認なしでの PR マージ（ベースブランチへの統合）**
  - テストエビデンス（`test/TestResults/`）・カバレッジレポート・秘密情報（`.env` 等）の commit

<!-- openspec-kit:end -->
