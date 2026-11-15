# 申し送り事項 (Handover): appsettings-unification-and-renamer-spinners

- **変更名**: `appsettings-unification-and-renamer-spinners`
- **日付**: 2026-11-15
- **ブランチ**: `feature/appsettings-unification-and-renamer-spinners`
- **関連 Issue / PR**: Issue #32, #33 / PR 作成予定（Archive 完了後 `develop` 向け作成）

---

## 1. 変更サマリ

`.env`（および `.env.sample`・Renamer の `VIDEO_DIR` OS 環境変数参照）を完全廃止し、Web（`TagBasedVideoManager`）・デスクトップ（`TagBasedVideoManager.Renamer`）・デプロイスクリプト（`scripts/deploy.ps1`, `scripts/deploy.sh`, `scripts/common.ps1`）の設定読み込みを `.NET` 標準の `appsettings.json` および環境別 JSON（`appsettings.Development.json` / `appsettings.Production.json`）へ一本化した。ローカル環境固有パスや API キーを含む環境別設定ファイルは `.gitignore` へ登録して Git 追跡から除外（`git rm --cached` 済み）し、Renamer の設定保存先（`Settings.resolveSavePath`）をアクティブ環境別の `appsettings.{Environment}.json` へ変更し、書込不可時のみ `%APPDATA%\TagBasedVideoManager\appsettings.{Environment}.json` へフォールバック保存（次回起動時に最優先オーバーライド読み込み）する構成へ整合させた。

あわせて、AI File Renamer（Avalonia UI）に Fluent Dark テーマ準拠の回転スピナー `SpinnerControl`（`Animation` + `RotateTransform.AngleProperty`）を導入し、ディレクトリ走査（`IsScanning`）、ファイル単位の AI 提案（`IsRequestingAi` / `c.IsAiProcessing`）、リネーム・Undo 実行（`IsRenaming`）、Web コンテナ操作（`IsDockerBusy`）の 4 非同期処理中に進行状況の視覚フィードバックと入力排他ガード・個別再試行（「↻ この項目を再試行」）・Zero-State / Error-State ガイダンスを実装した。SemVer は Web `0.2.0` -> `0.3.0`、Renamer `0.1.0` -> `0.2.0` へ更新した。

---

## 2. 品質・テストエビデンス

- **単体・結合テスト（Desktop / Renamer / DeployScripts）**: `TagBasedVideoManager.Renamer.Tests.exe` -> **377 件 Pass**（失敗 0 / スキップ 0、Line `87.57%` / Branch `64.66%`）
- **単体・結合・E2Eテスト（Web / Playwright / Visual GUI）**: `TagBasedVideoManager.Tests.exe` -> **201 件 Pass**（失敗 0 / スキップ 0、Line `85.71%` / Branch `38.89%`）
- **合計テスト結果**: **578 件 Pass**（失敗 0 / スキップ 0）
- **カバレッジ（全体・差分）**:
  - **全体**: Line (C0) **`86.4%`**（`5741 / 6637`、基準 80%+ 達成） / Branch (C1) **`48.3%`**（`1864 / 3857`） / Method **`80.4%`**（`416 / 517`）
  - **変更差分（`087e7fe..HEAD`）**: Line (C0) **`96.0%`**（`1028 / 1071`、基準 90%+ 達成） / Branch (C1) **`86.4%`**（`375 / 434`、基準 85%+ 達成）
- **エビデンス出力先**: `test/TestResults/TestRun_2026-11-15_01_56_12/`（TRX レポート 2 本・Coverlet Cobertura XML 2 本・ReportGenerator HTML `CoverageReport/index.html`・E2E/Visual PNG `E2E_12`〜`E2E_20`, `E2E-01`）

---

## 3. 保留指摘・技術的負債

| 指摘元（フェーズ/タスク/役割）        | 内容                                                                                                                                                    | 保留理由                                                                                                          | 推奨対応時期                             |
| ------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- | ---------------------------------------- |
| Apply / Cycle 2 / QA Reviewer         | プロセス共通環境変数を一時変更する一部テスト（`SettingsTests.fs`, `ProgramTests.fs`, `HttpHandlersTests.fs`）を将来並列実行有効化した場合のテスト間干渉 | 現行は xUnit クラス内逐次実行および `try ... finally` / `withEnvVars` 復元により副作用なく全 578 件安定動作のため | テスト並列化を有効化するタイミングで対応 |
| Apply / Cycle 1（参考: 解決済み事項） | `ConditionalWeakTable` 依存だった状態保持と `%APPDATA%` マージ順逆転・ルール保存エラー未通知                                                            | Cycle 2 にて `Domain.fs` / `State.fs` / `Settings.fs` をイミュータブルレコード直接保持へ改修済み（残債なし）      | 対応完了済み                             |

※その他、`TagBasedVideoManager.Renamer.fsproj` の起動エントリポイント類（`App.fs`, `MainWindow.fs`, `Program.fs`）およびデプロイスクリプトの一部分岐は実 GUI / Docker 実環境依存のため単体テスト対象外とし、`ContainerController` と `DeployScriptTests`・`RenderTargetBitmap` E2E で境界をカバーしている。

---

## 4. 将来の改善・リファクタ候補

- テストスイートの完全並列実行を導入する場合、環境変数テストクラスへ `[<Collection("EnvVarSerial")>]` 等のシリアライズグループを明示付与し、将来的な並列干渉を構造的に防止する。
- `TagBasedVideoManager/HttpHandlers.fs`（L149-160 `getEnv` / L168-176 `thumbDir`）と `Program.resolveConfigValue` に分散している設定解決ロジックを、`.fsproj` コンパイル順で `HttpHandlers.fs` より前に配置する共通モジュールへ抽出・完全一本化する。
- `TagBasedVideoManager.Renamer/State.fs`（L288-291）のビジー時入力排他ガード（`when model.IsScanning || model.IsRenaming`）について、新規 `Msg` 追加時の列挙漏れを防ぐため設定・フィルタ系 `Msg` のサブ判別共用体化等を検討する。
- `TagBasedVideoManager.Renamer/Views.fs`（約 3,325 行）の機能単位・コンポーネント単位へのモジュール分割。
- `TagBasedVideoManager.Renamer/Settings.fs` の `findCurrentDir` / `findAppBaseDir`（リポジトリ外単体バイナリ配布時の設定探索分岐）について、パブリッシュバイナリ向けの結合スモークテストを追加する。
- `scripts/deploy.sh`（`python3` / `jq`）と `scripts/common.ps1`（`ConvertFrom-Json`）の設定パーサ拡張時は両 OS 間で同一の Fail-Fast 検証契約を維持する。

---

## 5. 運用上の注意事項

- **`.env` からの移行（既存環境向け）**: 本変更以降は `.env` ファイルおよび `.env.sample` を使用せず、存在しても起動時に無視して `[WARN]` ログのみ出力します。既存環境からアップデートする場合は、`README.md` および `PRODUCTION_ENVIRONMENT.md` の手順に従い、各アプリの `appsettings.Production.json`（開発時は `appsettings.Development.json`）へ `VIDEO_DIRS`, `PORT`, `THUMBNAIL_DIR`, `DATABASE_PATH`, `ApiKey` 等を転記してください。
- **Git 管理除外ファイルの扱い**: `**/appsettings.Development.json` および `**/appsettings.Production.json` は `.gitignore` 対象です。マージや新規クローン直後は各環境のローカルディレクトリ（またはコンテナ環境変数 / `%APPDATA%\TagBasedVideoManager\`）に設定ファイルを配置してください。
- **`%APPDATA%` 最優先オーバーライド時の注意**: GUI で一度設定保存が行われ `%APPDATA%\TagBasedVideoManager\appsettings.{Environment}.json` が生成されると、開発時にプロジェクト側 `appsettings.{Environment}.json` を手動編集しても `%APPDATA%` 側が優先適用されます。設定変更が反映されない場合は画面ヘッダーの保存先バッジ表示を確認してください。
- **`SpinnerControl` の描画条件維持**: `Views.fs` の `SpinnerControl` は `OnAttachedToVisualTree` / `OnDetachedFromVisualTree` でアニメーション起動・停止（`CancellationTokenSource` 解放）を行う設計のため、`IsVisible` 切替ではなく現行の条件付きツリー構築を維持してください。
- **VS Code / DevContainer 利用時の注意**: IDE ローカル設定 `.vscode/launch.json`（Git 管理外）は `.env` を `envFile` として参照しないよう更新し、`ASPNETCORE_ENVIRONMENT`（Debug 時 `Development` / Release 時 `Production`）を渡す構成とします。
- **ロールバック手順**: 本変更はデータベーススキーマ変更を含まず、後方互換な設定解決と UI 更新のため、致命的不具合時は通常の `git revert`（マージコミットまたは機能コミット群）で安全にロールバック可能です。必要に応じて旧 `.env` をバックアップから復元して旧ビルドを再起動してください。
