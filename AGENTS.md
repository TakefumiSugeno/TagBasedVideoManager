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
- **フェーズゲート駆動ワークフロー**: すべての変更は **Explore → [Gate 0] → Propose → [Gate 1] → Apply → [Gate 2] → Archive ([Gate 3])** のゲートを通過して進める。
- **サブエージェント6ロール分業（TDD 3権分立）**: テスト作成者（`qa-tester`）・実装者（`pg-implementer`）・批判的レビュアー（`user/se/pg/qa-reviewer`）のコンテキストを分離し、「甘いテスト」「テスト改ざん」「追認レビュー」を構造的に防ぐ。
- 設定: `openspec/config.yaml`、エージェント定義: `openspec/agents/`、テンプレート: `openspec/templates/`、ワークフロー: `.agents/workflows/sdd-*.md`、ゲート検証: `openspec/scripts/verify-gate.ps1`

### ディレクトリ構成

| パス                               | 内容                                                                                                  |
| ---------------------------------- | ----------------------------------------------------------------------------------------------------- |
| `openspec/config.yaml`             | OpenSpec 設定（スキーマ・規模レベル・6ロール定義・TDD/カバレッジ基準・運用ルール）                    |
| `openspec/specs/`                  | メイン仕様（システムの恒久的な仕様）                                                                  |
| `openspec/changes/<name>/`         | 変更単位の成果物（proposal / specs / design / test_plan / tasks / mockup / reviews / handover）       |
| `openspec/changes/archive/`        | 完了した変更のアーカイブ                                                                              |
| `openspec/agents/`                 | サブエージェント6ロールのシステムプロンプト定義（`qa-tester`, `pg-implementer`, `*-reviewer`）        |
| `openspec/templates/`              | テスト計画・レビュー記録・チェックリスト・handover・PR 本文テンプレート                               |
| `openspec/scripts/verify-gate.ps1` | フェーズゲート（Gate 0〜3 / DoD）の機械的検証スクリプト                                               |
| `.agents/workflows/sdd-*.md`       | ゲート・サブエージェント分業を組み込んだ実行ワークフロー（`sdd-propose`, `sdd-apply`, `sdd-archive`） |
| `test/TestResults/`                | テストエビデンス・カバレッジレポート出力先（Git 管理外）                                              |

### 適用規模レベル（L1 / L2 / L3）と変更ディレクトリの成果物

変更開始時に規模レベル（`L1` / `L2` / `L3`）を決定し、`.openspec.yaml`（`level: L2` 等）と `proposal.md` に明記する。

| 成果物 / 運用                       | L1（軽微: typo・設定・局所修正）   | L2（通常: 機能追加・仕様変更・通常バグ）           | L3（大規模: アーキテクチャ・破壊的変更・DB変更） |
| ----------------------------------- | ---------------------------------- | -------------------------------------------------- | ------------------------------------------------ |
| `proposal.md` / `tasks.md`          | 必須                               | 必須                                               | 必須（移行計画・ロールバック手順含む）           |
| `specs/**/spec.md` / `design.md`    | 仕様不変なら `skip_specs: true` 可 | 必須                                               | 必須                                             |
| `test_plan.md`（テストケース定義）  | 任意（`tasks.md` 内に記載可）      | **必須**（境界値・C2表・結合/E2E定義）             | **必須**                                         |
| `mockup.html`（単一ファイルHTML）   | 不要                               | UI/UX変更時は**必須**                              | UI/UX変更時は**必須**                            |
| Propose レビュー                    | `se-reviewer` のみ可               | `user-reviewer` + `se-reviewer`                    | `user-reviewer` + `se-reviewer`                  |
| Apply 時の実装・テスト分業          | メイン兼任可 ＋ PG/QA レビュー     | **`qa-tester` → `pg-implementer` → PG/QAレビュー** | L2 ＋ 設計変更時は `se-reviewer` 必須            |
| Archive 総括レビュー・`handover.md` | 必須                               | 必須                                               | 必須                                             |

#### セマンティックバージョニング（SemVer 2.0.0）規則と運用

Web アプリ（`TagBasedVideoManager`）およびデスクトップアプリ（`TagBasedVideoManager.Renamer`）は、独立して `X.Y.Z` 形式でバージョンを管理する（初期値: `0.1.0`）。OpenSpec の変更規模レベルまたはユーザー指示に基づき、各プロジェクトの `.fsproj`（`<Version>`）を以下の規則に従ってインクリメントする：

| バージョン桁  | 連動トリガー                            | 対象変更内容の例                                                    | リセット規則                           |
| :------------ | :-------------------------------------- | :------------------------------------------------------------------ | :------------------------------------- |
| **X (Major)** | **L3 規模** または **ユーザー明示指示** | アーキテクチャ変更、破壊的API変更、互換性のないDB変更、ユーザー指示 | `Y`, `Z` を `0` にリセット (`X+1.0.0`) |
| **Y (Minor)** | **L2 規模**                             | 後方互換性のある新機能追加、仕様変更、UI/UX変更、通常機能改善       | `Z` を `0` にリセット (`X.Y+1.0`)      |
| **Z (Patch)** | **L1 規模**                             | 後方互換性のある不具合修正、設定微修正、typo修正、局所的リファクタ  | なし (`X.Y.Z+1`)                       |

---

### サブエージェント6ロール分業と批判的レビュープロセス

同一エージェントがテストと実装の両方を担当すると「実装に合わせた甘いテスト」や「テストの期待値改ざん」が起きるため、以下の6ロール（定義: `openspec/agents/*.md`）を別コンテキストのサブエージェントとして起動する。各レビュアーの担当プロトコルは、成果物7種 × 4アプローチ（Adversarial QA・悪魔の代弁者・ブラインド突合・特化ペルソナ）の検討から最適配置されている。

| ロール             | 定義ファイル                        | 担当フェーズ      | 責務・ガードレール                                                                                                                            |
| ------------------ | ----------------------------------- | ----------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| **QA Tester**      | `openspec/agents/qa-tester.md`      | Propose / Apply   | `specs/` と `test_plan.md` から先行失敗テスト（Red）および結合/E2Eテストを実装。**プロダクションコード編集禁止**                              |
| **PG Implementer** | `openspec/agents/pg-implementer.md` | Apply             | Red テストを通すプロダクションコード実装（Green）と Refactor。**既存テストの無断改ざん・skip化・TODO放置禁止**                                |
| **User Reviewer**  | `openspec/agents/user-reviewer.md`  | Propose / Archive | ブラインド仕様比較（異常系・境界先出し突合）・せっかち初心者視点でのZero-State/Error-State体験・Why欠落告発（`checklist_propose/archive.md`） |
| **SE Reviewer**    | `openspec/agents/se-reviewer.md`    | 全フェーズ        | 外部依存ハング・通信瞬断時の故障シミュレーション（連鎖崩壊検証）・状態不整合・隠された負債告発（`checklist_propose/apply_impl/archive.md`）   |
| **PG Reviewer**    | `openspec/agents/pg-reviewer.md`    | Apply / Archive   | コード潔癖症視点で暗黙の仮定（思い込み）・例外握りつぶし告発・30%削減リファクタ提案（`checklist_apply_impl/archive.md`）                      |
| **QA Reviewer**    | `openspec/agents/qa-reviewer.md`    | Apply / Archive   | 敵対的QAとして破壊テスト作成・実行（Adversarial QA）・トートロジー（実装式コピペ）・甘いアサーション告発（`checklist_apply_test/archive.md`） |

#### レビュー・エスカレーション規律

1. **独立コンテキスト**: 実装者自身のバイアスを排除するため、必ずサブエージェント（別会話コンテキスト）に対象成果物・`git diff`・チェックリストのパスを渡して実行する。
2. **挑戦的設問回答と破壊テスト実行**: 各成果物の挑戦的設問への具体的回答・根拠、および実装コードを例外終了させる破壊テスト実行ログ（Adversarial QA）を `openspec/changes/<name>/reviews.md` に追記する。フォーマット・静的解析・テスト合否・カバレッジ数値等の機械的検証は `verify-gate.ps1` に委譲し LLM レビューから除外する。
3. **最大3サイクルとエスカレーション**: `Critical` / `Major` 指摘（破壊テストによるバグ検出含む）が 0 件（`LGTM: true`）になるまで修正・再レビューを繰り返す。**最大 3 サイクル**で解消しない場合は、AI の独断で妥協せずユーザーへエスカレーションする。
4. **保留指摘の引き継ぎ**: `Minor` / `Suggestion` で保留とした指摘は `reviews.md` に記録し、Archive 時に `handover.md` へ全件転記する。

---

### テスト網羅性基準（C0 / C1 / C2・結合・E2E）とテスト設計

L2 / L3 の変更では、コードを書く前に `openspec/templates/test_plan.md` から `openspec/changes/<name>/test_plan.md` を作成し、テストケースを定義する。

#### 1. カバレッジ基準とテストレベル別の網羅方針

| テストレベル                 | 網羅指標                                                                                   | 目標基準（デフォルト）                                                                  | 必須設計技法・検証内容                                                                                                                                                                                                                                                                                                                                                |
| :--------------------------- | :----------------------------------------------------------------------------------------- | :-------------------------------------------------------------------------------------- | :-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **単体テスト (Unit)**        | **C0（命令網羅 / Line）**<br>**C1（分岐網羅 / Branch）**<br>**C2（条件網羅 / Condition）** | 全体 **80%+** / 差分 **90%+**<br>全体 **80%+** / 差分 **85%+**<br>対象複合条件 **100%** | ・**同値分割・境界値分析**: 数値（`min-1`, `min`, `max`, `max+1`, `0`, 負数）、文字列（空, 空白, 1文字, 上限±1, 特殊文字）、配列（`[]`, 1件, 複数, 上限±1）、日時（境界時刻, TZ, うるう日）<br>・**デシジョンテーブル**: 複合条件（`A && B \|\| C`）の真偽組み合わせ<br>・**モック最小化**: 外部I/Oのみモック可。対象ロジックのモック化やトートロジーアサーション禁止 |
| **結合テスト (Integration)** | **IF契約網羅**<br>**状態遷移網羅（0/1-switch）**                                           | 全外部/DB境界 **100%**<br>全状態遷移 **100%**                                           | ・正常往復＋シリアライズ境界＋トランザクションロールバック<br>・**異常系注入（Fault Injection）**: 4xx/5xx応答、Timeout、切断、DB制約違反<br>・**状態遷移**: 有効遷移（0-switch）＋無効遷移の拒否（1-switch）・二重実行時の冪等性                                                                                                                                     |
| **E2Eテスト (End-to-End)**   | **仕様 Scenario 網羅率**<br>**主要ジャーニー網羅**                                         | `specs/` 全 Scenario **100%**                                                           | ・① **Happy Path**（主要正常導線）<br>・② **Zero-State / 初回起動 Path**（データ0件・APIキー未設定・未ログイン）<br>・③ **Negative / Recovery Path**（エラー表示→修正→再試行成功）<br>・**実画面レンダリング検証**: 実ブラウザ/実ウィンドウで描画し PNG キャプチャを出力（名ばかりE2Eの禁止）                                                                         |

#### 2. 不具合修正（`fix/<change-name>`）時の専用 TDD プロトコル

1. **根本原因（Root Cause）の特定**: 現象の対症療法ではなく、なぜ発生したかを `proposal.md` と `design.md` に明記する。
2. **再現テスト（Red）の先行作成**: 修正前のコードで確実に失敗する回帰テストを `qa-tester` で最初に作成する。
3. **最小修正（Green）と横展開チェック**: `pg-implementer` で修正後、同種のバグが他モジュールに存在しないか検索・確認する。

---

### 完了基準（Definition of Done）と機械的フェーズゲート

ドキュメントのルール記述だけに頼らず、各フェーズの終わりに **`openspec/scripts/verify-gate.ps1`** を実行し、機械的チェックを PASS させることを必須とする。

| ゲート     | 実行タイミング                         | 検証コマンド                                                                                                 | 完了基準（DoD / ゲート通過条件）                                                                                                                                                                   |
| :--------- | :------------------------------------- | :----------------------------------------------------------------------------------------------------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Gate 0** | `openspec new change` 直前             | `pwsh ./openspec/scripts/verify-gate.ps1 -Gate pre-propose`                                                  | ・未コミット変更の退避済み<br>・最新 `develop` から切った `feature/*` / `fix/*` 等のブランチ上にいること                                                                                           |
| **Gate 1** | Propose 合意・commit 前                | `openspec validate "<name>"`<br>`pwsh ./openspec/scripts/verify-gate.ps1 -Gate propose -ChangeName "<name>"` | ・規模レベルに応じた必須ファイル（`proposal`, `specs`, `design`, `test_plan`, `tasks`, `mockup`）が揃っていること<br>・`reviews.md` に未判定項目がなく、Propose レビューが `LGTM: true` であること |
| **Gate 2** | Apply 各タスクの `[x]` 更新・commit 前 | `pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply -ChangeName "<name>"`                                   | ・フォーマット差分0、静的解析エラー0、全テストPass、C0/C1/C2基準達成<br>・テストの `skip` 放置が 0 件であること<br>・`reviews.md` の最新タスクレビューが `LGTM: true` であること                   |
| **Gate 3** | `openspec archive` 実行前              | `pwsh ./openspec/scripts/verify-gate.ps1 -Gate archive -ChangeName "<name>"`                                 | ・`tasks.md` の未完了タスク（`- [ ]`）が 0 件であること<br>・`handover.md` が記入済みであること<br>・全4ロール総括レビュー `LGTM: true` かつ DoD（テスト100%Pass、skip放置0件）を達成していること  |

---

### 自動フォーマット

ファイル更新時・コミット前・テスト実行前に、ファイル種別に応じたフォーマッタを実行する。
ファイル種別ごとのコマンドは、本ファイルの **openspec-kit 管理区間外**（「プロジェクト固有ルール」の「自動フォーマット」表）に記載する。

---

### Git 操作ルール

OpenSpec の成果物（`openspec/` 配下）も Git 管理対象とする。

#### 1. ブランチ作成（Explore → Propose 移行時の安全シーケンス / Gate 0）

1. **作業ツリー確認と退避**: `git status --porcelain` で未コミット変更を確認し、存在する場合は `git stash push -u -m "wip: backup before switch to <change-name>"` で退避する。
2. **ベースブランチの最新化とマージ済みブランチ削除・新トピックブランチ作成**:
   ```bash
   git checkout develop
   git pull origin develop
   git fetch --prune
   # マージ済みローカルブランチを一括削除（develop, main, master 等は除外保護）
   # PowerShell: git branch --merged develop | Where-Object { $_.Trim() -notmatch '^(develop|main|master|\*)' } | ForEach-Object { git branch -d $_.Trim() }
   # Bash: git branch --merged develop | grep -vE "^(\*|develop|main|master)$" | xargs -r git branch -d
   git checkout -b feature/<change-name>   # fix/<change-name>, refactor/<change-name>, chore/<change-name>
   ```
3. **Gate 0 検証**: `pwsh ./openspec/scripts/verify-gate.ps1 -Gate pre-propose`

#### 2. Commit タイミング（各ゲート通過後）

1. **Propose 合意後（Gate 1 通過後）**: `openspec/changes/<name>/` 配下の全計画成果物（`proposal`, `specs`, `design`, `test_plan`, `tasks`, `mockup`, `reviews`）
2. **Apply 中の各タスク完了時（Gate 2 通過後）**: 実装・テストコード、`tasks.md` の進捗、`reviews.md` のレビュー記録
3. **Archive 完了後（Gate 3 通過後）**: 更新されたメイン仕様（`openspec/specs/`）、アーカイブされた変更、README 等の関連ドキュメント

**Commit メッセージ規約**（Conventional Commits: `<type>(<scope>): <subject>`、type は `feat|fix|docs|refactor|test|chore|review|spec`、scope は change 名）。

#### 3. Push / Pull Request / Merge

- **Push**: トピックブランチへの push は Archive 完了時の自走タスクとして自動実行する
- **PR 作成**: `openspec/templates/pr_body.md` に従い `gh pr create --base develop --head <branch> --title "<type>(<scope>): <subject>" --body-file <PR本文ファイル>` を自動実行する（ローカルブランチはトピックブランチのままでセッション完了とし、人間レビューは PR 上で実施）
- **マージ**: 人間によるレビュー承認後、**Merge commit**（`gh pr merge <PR番号> --merge --delete-branch`）で統合する（Squash / Rebase merge は禁止）。

#### 4. 禁止事項（ハードガードレール）

- `develop` および `main` への直接コミット・直接 push
- `verify-gate.ps1` を実行・通過せずに行うフェーズ移行・`[x]` 更新・Archive 実行
- 既存テストの期待値改ざん・アサーション削除・`skip` 化による見せかけの Green 化
- **ユーザー承認なしでの PR マージ（ベースブランチへの統合）**
- テストエビデンス（`test/TestResults/`）・カバレッジレポート・秘密情報（`.env` 等）の commit

---

### OpenSpec CLI & ゲートコマンド リファレンス

```bash
# 変更の作成・一覧・進捗
openspec new change "<name>"                              # 新規変更作成（Gate 0 通過後）
openspec list --json                                      # 進行中の変更一覧
openspec list --specs                                     # メイン仕様（capability）一覧
openspec status --change "<name>" --json                  # 成果物の進捗確認

# 成果物の作成指示・検証
openspec instructions <artifact> --change "<name>" --json # 作成指示（context / rules / template）
openspec instructions apply --change "<name>" --json      # 実装指示（operations.apply.guidance を含む）
openspec validate "<name>"                                # 変更の検証

# フェーズゲート検証（openspec-kit: PowerShell 版 / Bash 版）
pwsh ./openspec/scripts/verify-gate.ps1 -Gate pre-propose
pwsh ./openspec/scripts/verify-gate.ps1 -Gate propose -ChangeName "<name>"
pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply   -ChangeName "<name>"
pwsh ./openspec/scripts/verify-gate.ps1 -Gate archive -ChangeName "<name>"
# Bash 環境の場合: ./openspec/scripts/verify-gate.sh --gate <pre-propose|propose|apply|archive> --change-name "<name>"

# 完了
openspec archive "<name>"                                 # アーカイブ（Gate 3 通過後）
```

<!-- openspec-kit:end -->
