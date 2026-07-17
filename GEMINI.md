# GEMINI

## 構成

- `doc/`
  - 仕様書などのドキュメントのルートディレクトリ
- `doc/spec.md`
  - [システム仕様書](file:///D:/programming/repos/TagBasedVideoManager/doc/spec.md)
- `doc/design_detail.md`
  - 詳細設計書（後ほど作成予定）
- `doc/tasks/`
  - タスク管理ドキュメントの配置ディレクトリ
- `doc/tasks/tasks.md`
  - [タスク管理ファイル](file:///D:/programming/repos/TagBasedVideoManager/doc/tasks/tasks.md)
- `doc/work/`
  - 開発に必要なテンポラリのディレクトリ
- `src/`
  - ソースコードのルートディレクトリ（F# + Giraffe プロジェクト）
- `src/wwwroot/`
  - フロントエンド静的ファイル（`index.html` 等）の配置ディレクトリ
- `test/`
  - テストコードのルートディレクトリ（xUnit + FsUnit プロジェクト）

## 利用可能なMCPサーバ

## 利用可能なSkills

## 運用ルール

### 基本原則: 仕様書駆動開発（Specification-Driven Development）

> **原則**: ドキュメントを更新しユーザと合意してから実装に移行する。合意なしに実装を開始してはならない。

開発は必ず以下のフローで進める:

#### Step 1: タスク計画と合意
1. ユーザの指示に対して、`doc/tasks/tasks.md` にタスクを追加・更新し、**進め方をユーザに提示して合意を得る**。
2. 合意後、`doc/tasks/tasks.md` を commit する (ブランチ: `alpha`)。

#### Step 2: 仕様・設計の更新と合意
1. `doc/spec.md`（システム仕様書）および `doc/design_detail.md`（詳細設計書）を更新する。
2. 変更内容をユーザに提示し、**仕様・設計の合意を得る**。
3. 合意後、ドキュメントを commit する (ブランチ: `alpha`)。

#### Step 3: TDD による実装
1. `doc/tasks/tasks.md`、`doc/spec.md`、`doc/design_detail.md` の内容に基づき実装する。
2. **テストコードを実装コードより先行して作成する（Test First）**。
3. テストがグリーンになるまで実装を繰り返す。
4. ソースコード・テストコードを commit する (ブランチ: `alpha`)。

#### Step 4: ドキュメントの事後同期
1. 実装で仕様・設計から変更が生じた場合は、`doc/spec.md`、`doc/design_detail.md` に反映する。
2. ドキュメントを commit する (ブランチ: `alpha`)。
3. **commit 直後、1つ前の履歴と比較して既存情報の欠落がないか確認し、欠落があれば復元すること**。

### 実装ガイドライン

- **F# Idioms & Architecture**:
  - `Result` 型（鉄道指向プログラミング / Railway Oriented Programming）によるエラーハンドリングを徹底し、イミュータブルな設計を優先する。
  - バックエンドは F# (.NET 10) + Giraffe を採用し、非同期処理には `task { ... }` コンピュテーション式を使用する。
- **データアクセス (SQLite)**:
  - `Microsoft.Data.Sqlite` および `Dapper` を使用してSQLiteデータベースへアクセスする。
- **動画ストリーミング**:
  - `PhysicalFileResult` を利用し、`EnableRangeProcessing = true` を設定して HTTP 206 Partial Content (Range Requests) に対応させる。
- **メディア処理 (FFmpeg)**:
  - 外部プロセスとして FFmpeg を呼び出し、メタデータの取得およびサムネイル生成を行う。
- **フロントエンド**:
  - HTML5 + Alpine.js (CDN) + Tailwind CSS (CDN) による構成とし、Node.js環境やビルドツールを一切排除する。
  - 状態管理やインタラクション、インクリメンタルサーチは Alpine.js の機能で完結させる。
- **DSL Utilization**:
  - `design_detail.md` で定義する DSL を活用し、宣言的な記述を行う。
- **TDD（テスト駆動開発）**:
  - **テストコードは実装コードより先行して作成する（Test First）**。フロントエンドなどテスト困難な領域については並行作成も許容するが、必ず実装完了前にテストを用意する。
  - テストフレームワーク: `xUnit` + `FsUnit`（バックエンド）、Playwright（E2E）。
  - テスト結果を TRX 形式でファイル出力する (git管理不要)。
    - 出力先：`test\TestResults\TestRun_yyyy-MM-dd_HH_mm_ss` (実行の都度作成されるタイムスタンプフォルダ)
    - ファイル名：`TestRun_yyyy-MM-dd_HH_mm_ss_net10.0.trx` (および同フォルダ内に `CoverageReport` ディレクトリとしてHTMLカバレッジレポートを出力)
    - E2Eテスト時、NGケースは画面キャプチャも取得する
- **Environment Variables**:
  - 機密情報および環境依存の設定は `.env` で管理し、絶対にソースコードに含めない。
  - 必須環境変数: `PORT`, `VIDEO_DIR`, `THUMBNAIL_DIR`, `DATABASE_PATH`
- **コメント**:
  - ソースコード内のコメントは日本語で記載し、XMLドキュメントコメント形式（`///`）とする。
- **プロジェクト・依存関係管理**:
  - `.fsproj` などの設定ファイルはAIが手動で構築するのではなく、`dotnet` CLI（SDK）を活用して作成・更新する。

## 参考情報

- [.NET を使用して最小限の MCP クライアントを作成する](https://learn.microsoft.com/ja-jp/dotnet/ai/quickstarts/build-mcp-client)
- [.NET Agent Skills](https://github.com/dotnet/skills)
- [Giraffe Documentation](https://giraffe.wiki/)
- [Dapper - Tutorial](https://github.com/DapperLib/Dapper)
- [Railway Oriented Programming (F# for Fun and Profit)](https://fsharpforfunandprofit.com/rop/)
