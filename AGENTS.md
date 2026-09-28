# AGENTS

## プロジェクト基本方針（OpenSpec ネイティブ）

本プロジェクトは **OpenSpec** による仕様駆動開発 (Spec-Driven Development) を採用しています。

- **Single Source of Truth**: システム仕様の唯一の真実（正本）は `openspec/specs/` です。OpenSpec 管理外に個別の仕様書を新規作成・二重管理しません（※既存の `doc/` 配下は過去の開発資産・リファレンスとして扱います）。
- **仕様駆動ワークフロー**: すべての機能追加・仕様変更・大規模リファクタリングは、OpenSpec の 3 フェーズ（Propose → Apply → Archive）に則って進めます。
- **サブエージェントレビュー必須**: 各フェーズで専門の役割（User / SE / PG / QA Agent）によるレビューを実施し、品質と整合性を担保します。

---

## 構成

- `src/`
  - ソースコードのルートディレクトリ（F# + Giraffe プロジェクト）
- `src/wwwroot/`
  - フロントエンド静的ファイル（`index.html`, `app.js`, `app.css` 等）の配置ディレクトリ
- `test/`
  - テストコードのルートディレクトリ（xUnit + FsUnit, Playwright E2E プロジェクト）
- `test/TestResults/` および `test-results/`
  - **テスト実行エビデンスおよびカバレッジレポートの出力ディレクトリ**（※Git管理対象外）
  - TRX テストレポートファイル (`TestRun_yyyy-MM-dd_HH_mm_ss_net10.0.trx`)
  - HTML カバレッジレポート (`CoverageReport/index.html`)
  - E2E テスト失敗時の画面キャプチャ画像 (`.png`)
- `doc/`
  - 過去の開発資産（仕様書・設計書・タスク履歴）、調査メモ、外部リファレンス、画像、補足資料等の配置ディレクトリ（※今後のシステム仕様・タスクはすべて `openspec/` で管理）
- `openspec/`
  - **OpenSpecフレームワークのルートディレクトリ**
- `openspec/config.yaml`
  - OpenSpec設定（スキーマ: `spec-driven`、運用ルール・レビュー定義）
- `openspec/specs/`
  - **メイン仕様書・デルタ仕様書の配置ディレクトリ**（システムの恒久的な仕様書）
- `openspec/changes/`
  - **変更管理（提案・タスク・設計・レビュー記録）の配置ディレクトリ**
- `openspec/changes/archive/`
  - 完了した変更のアーカイブ
- `openspec/templates/`
  - **OpenSpecレビュー・チェックリスト用テンプレート**

## 利用可能なMCPサーバ

## 利用可能なSkills

- `openspec-propose` - 新しい変更の提案・計画アーティファクト生成
- `openspec-apply-change` - 変更の実装・タスク実行
- `openspec-archive-change` - 完了した変更のアーカイブ
- `openspec-sync-specs` - デルタ仕様からメイン仕様への同期
- `openspec-update-change` - 既存変更の計画見直し
- `openspec-explore` - アイデア探索・要件明確化

---

## 開発運用ルール

### テストエビデンス・カバレッジレポート構成管理

本プロジェクトは **TDD (Test-Driven Development)** を前提とし、テストの実行結果およびカバレッジを測定・確認しながら開発を進めます。

#### 1. 出力先ディレクトリ構成

| 種別                   | 出力先パス                                                            | 想定ファイル / 形式                                                       | Git管理                       |
| ---------------------- | --------------------------------------------------------------------- | ------------------------------------------------------------------------- | ----------------------------- |
| **テストエビデンス**   | `test/TestResults/` または `test-results/evidence/`                   | TRXレポート (`.trx`), 実行ログ (`.log`), E2E失敗時画面キャプチャ (`.png`) | **不要**（`.gitignore` 対象） |
| **カバレッジレポート** | `test/TestResults/**/CoverageReport/` または `test-results/coverage/` | HTML レポート (`index.html` 等), Cobertura (`coverage.cobertura.xml`)     | **不要**（`.gitignore` 対象） |

- テスト実行結果は TRX 形式でファイル出力します:
  - 出力先例: `test/TestResults/TestRun_yyyy-MM-dd_HH_mm_ss/`
  - ファイル名例: `TestRun_yyyy-MM-dd_HH_mm_ss_net10.0.trx`（同フォルダ内に `CoverageReport` ディレクトリとしてHTMLカバレッジレポートを出力）
- E2Eテスト時、NGケースは画面キャプチャも自動取得します。

#### 2. エビデンスとカバレッジの取り扱い方針

- **Git管理不要**: テストエビデンスやカバレッジレポートは自動生成されるバイナリ/大容量ファイルを含むため、Git 管理には含めない（`.gitignore` に登録済み）。
- **レビューでの確認**: Apply フェーズの QA Agent レビューや Archive フェーズの総括レビューでは、ローカルに出力されたテスト結果・カバレッジレポートを参照・検証して合否判定を行う。レビュー記録（`reviews.md`）には、レポートファイルの本文ではなく「カバレッジ率・全テスト通過のサマリ結果」をテキストで記録する。

#### 3. E2EテストおよびGUIビジュアル検証の厳格運用ルール

- **名ばかりのE2Eテストの禁止**: 単なるバックエンドの関数結合テストを「E2Eテスト」と称してUI・プレゼンテーション層の検証を省略することを厳禁とする。
- **GUI/デスクトップアプリの実画面レンダリング検証義務**:
  - Avalonia等のGUIアプリにおいては、実ウィンドウ（`HostWindow` 等）をインスタンス化してビジュアルツリーを展開し、`RenderTargetBitmap` による実画面レンダリングおよび画像キャプチャ（PNG）の出力・ファイルサイズ・生成検証を行う E2E テストを必須とする。
  - テスト実行エビデンスとして各画面状態（初期状態、実行結果、ダイアログ等）のキャプチャを `test/TestResults/` に出力し、視認性・レイアウト崩れ・テキストコントラストを目視／アサーション検証する。
- **「ゼロ設定・初回起動」ユーザージャーニーのテスト義務**:
  - モックデータを用いた正常系だけでなく、「APIキー未設定」「未ログイン」「初回起動（対象0件）」のデフォルト初期状態から、ユーザーが走査・編集・リネーム・Undo できるまでの現実的なユーザージャーニーを必ずテストシナリオに含める。

### 自動フォーマット実行コマンド（ファイル種別ごと）

| ファイル種別                   | 対象拡張子                       | 使用ツール        | 実行コマンド                                            |
| ------------------------------ | -------------------------------- | ----------------- | ------------------------------------------------------- |
| **Markdown ドキュメント**      | `.md`                            | Prettier          | `npx prettier --write "**/*.md"`                        |
| **JSON 設定ファイル**          | `.json`                          | Prettier          | `npx prettier --write "**/*.json"`                      |
| **OpenSpec アーティファクト**  | `openspec/**/*.md`               | Prettier          | `npx prettier --write "openspec/**/*.md"`               |
| **Web 静的ファイル**           | `src/wwwroot/**/*.{html,js,css}` | Prettier          | `npx prettier --write "src/wwwroot/**/*.{html,js,css}"` |
| **F# / .NET ソースコード**     | `src/**/*.fs`, `test/**/*.fs`    | dotnet format     | `dotnet format whitespace` / `dotnet format`            |
| **F# ソースコード (Fantomas)** | `src/**/*.fs`, `test/**/*.fs`    | Fantomas (導入時) | `dotnet fantomas "src/**/*.fs" "test/**/*.fs"`          |

### Git操作ルール（Commit / Push タイミング）

OpenSpecのアーティファクト（`openspec/` 配下）もGit管理対象とする。

#### Commit タイミング（ローカルリポジトリへの記録）

以下のタイミングで **必ず commit** する:

1. **Propose 合意後** (Phase 1 完了時)
   - `openspec/changes/<name>/` 配下の全アーティファクト（`proposal.md`, `specs/`, `design.md`, `tasks.md`, `reviews.md`）
2. **Apply 中のタスク完了時** (Phase 2 中)
   - 実装成果物（ソースコード、テストコード）
   - `openspec/changes/<name>/tasks.md` の進捗更新
   - `openspec/changes/<name>/reviews.md` 等のレビュー記録
3. **Archive 完了後** (Phase 3 完了時)
   - `openspec sync specs` で更新されたメイン仕様（`openspec/specs/`）
   - アーカイブされた変更（`openspec/changes/archive/`）
   - README や関連ドキュメント等の更新

**Commit メッセージ規約**:

```
<type>(<scope>): <subject>

<body>

<footer>
```

- type: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`, `review`, `spec`
- scope: 変更名 (例: `install-openspec`, `streaming`) または `spec`, `design`, `impl` 等
- subject: 50文字以内で簡潔に（日本語可）

#### Push タイミング（リモートリポジトリへの反映）

1. **日次または作業区切りのタイミング**（1日1回以上推奨）
2. **Archive 完了・ユーザー最終合意後** (Phase 3 完了時)
   - 成果物が完成し、ユーザー確認済みの状態で push
3. **Pull Request 作成時**

#### 禁止事項

- **ユーザー合意なしでの push は禁止**（特にメインブランチへの直接 push）
- **未フォーマット・未テスト状態での commit は禁止**
- **テストエビデンス（`test/TestResults/`, `test-results/`）やカバレッジレポートの commit は禁止（Git管理対象外）**
- **作業中の一時的な変更（WIP）を commit する場合は、メッセージに `wip:` プレフィックスを付与し、後で整理すること**

---

### OpenSpec CLI コマンド リファレンス

```bash
# 変更の作成・一覧
openspec new change "<name>"           # 新規変更作成
openspec list --json                   # 変更一覧取得
openspec status --change "<name>" --json  # 進捗確認

# アーティファクト操作
openspec instructions <artifact> --change "<name>" --json  # 作成指示取得
openspec validate --change "<name>"    # 検証

# 実装・完了
openspec instructions apply --change "<name>" --json  # 実装指示取得
openspec sync specs --change "<name>"  # スペック同期
openspec archive --change "<name>"     # アーカイブ

# コンテキスト・設定
openspec context --json                # プロジェクトルート確認
openspec schemas --json                # 利用可能スキーマ一覧
```

---

### サブエージェントレビュープロセス

OpenSpecの3フェーズ（Propose → Apply → Archive）それぞれで、定義された役割のサブエージェントによるレビューを**必須**とする。レビュー記録は `openspec/changes/<name>/reviews.md` にテンプレート準拠で蓄積する。

#### 1. Propose Phase Review（必須）

- **トリガー**: `openspec new change` 完了、`proposal.md` 作成後、Propose合意前
- **レビュアー**:
  - **User Agent**: 要件妥当性・ビジネス価値・UX・受け入れ基準
  - **SE Agent**: 技術的実現性・アーキテクチャ整合・影響範囲・非機能要件
- **観点チェックリスト** (`checklist_propose.md` 使用):
  - What/Why が明確かつ500語以内か
  - Non-goals（やらないこと）が明記されているか
  - 既存仕様（`openspec/specs/`）との矛盾・影響範囲が整理されているか
  - タスク分解粒度が「2時間以内」目安で妥当か
  - 見積もり・スコープに過不足がないか
- **合否**: 全役割LGTMで次フェーズへ。指摘がある場合は `proposal.md` 更新して再レビュー
- **成果物**: `reviews.md` に記録追記

#### 2. Apply Phase Review（各タスク必須）

- **トリガー**: `tasks.md` のチェックボックス `[x]` 更新前（タスク完了宣言前）
- **レビュアー・観点**:
  - **実装タスク** → **PG Agent**（`checklist_apply_impl.md`）
    - 設計書（`design.md`）との整合性
    - コード品質：可読性・命名・関数分割・DRY
    - 境界値・異常系・エラー処理の網羅（`Result` 型 / ROP）
    - リファクタの妥当性（テスト変更なし）
    - 自動フォーマット実行済みか
  - **テストタスク** → **QA Agent**（`checklist_apply_test.md`）
    - 仕様妥当性検証テスト：仕様書の期待値をコード化できているか
    - 回帰テスト：既存機能を壊さない観点で網羅できているか
    - 境界値・異常系・エッジケースのテストケース
    - E2E/結合テスト：受け入れシナリオから導出できているか (Playwright)
    - カバレッジ基準達成・エビデンス出力済みか（出力先: `test/TestResults/`）
- **フロー**:
  1. 実装者がタスク完了宣言（PR/コミット前）
  2. 該当役割エージェントがレビュー実施、指摘を `reviews.md` に記録
  3. 指摘への対応方針（採用/保留/却下＋理由）を明記
  4. 実装者が対応コミット
  5. 再レビュー → 全指摘クローズ（LGTM）でタスク完了 `[x]`
- **保留指摘の扱い**: 当該タスクでは完了扱いとし、内容を「申し送り事項」として `reviews.md` および Archive時の別ファイル（`handover.md`）に記録
- **成果物**: `reviews.md` 記録更新、`tasks.md` チェックボックス更新

#### 3. Archive Phase Review（必須）

- **トリガー**: `openspec archive` 実行前
- **レビュアー**: 全役割（User Agent / SE Agent / PG Agent / QA Agent）
- **観点チェックリスト** (`checklist_archive.md` 使用):
  - デルタ仕様がメイン仕様（`openspec/specs/`）へ正しく反映されているか（`openspec sync specs` 実行済み）
  - 全テスト通過・カバレッジ基準達成のエビデンスがあるか（`test/TestResults/` 配下で確認）
  - README や関連ドキュメントが同期更新済みか
  - 既知の課題・技術的負債・保留指摘が `handover.md` に整理記録されているか
  - 変更サマリ（何が変わったか、テスト結果、レビュー指摘対応状況）が記録されているか
- **合否**: ユーザー最終合意で Archive 実行
- **成果物**: 総括レビュー記録を `reviews.md` に追記、`handover.md` 作成

---

## 実装ガイドライン（本プロジェクト固有規約）

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
  - 必須環境変数: `PORT`, `VIDEO_DIR`, `THUMBNAIL_DIR`, `DATABASE_PATH`
- **コメント**:
  - ソースコード内のコメントは日本語で記載し、XMLドキュメントコメント形式（`///`）とする。
- **プロジェクト・依存関係管理**:
  - `.fsproj` などの設定ファイルはAIが手動で構築するのではなく、`dotnet` CLI（SDK）を活用して作成・更新する。

---

## 参考情報

- [.NET を使用して最小限の MCP クライアントを作成する](https://learn.microsoft.com/ja-jp/dotnet/ai/quickstarts/build-mcp-client)
- [.NET Agent Skills](https://github.com/dotnet/skills)
- [Giraffe Documentation](https://giraffe.wiki/)
- [Dapper - Tutorial](https://github.com/DapperLib/Dapper)
- [Railway Oriented Programming (F# for Fun and Profit)](https://fsharpforfunandprofit.com/rop/)
