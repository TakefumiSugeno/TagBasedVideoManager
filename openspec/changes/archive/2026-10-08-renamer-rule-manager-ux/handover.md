# 申し送り事項 (Handover): renamer-rule-manager-ux

## 1. 変更完了サマリ

- **変更名**: `renamer-rule-manager-ux`
- **目的**:
  1. DuckDuckGo Search (ddgs) および LLM 提案パイプラインの処理順序を画面の候補表示順（パス長降順等）と完全同期。
  2. 命名規則マネージャーの柔軟な操作UI（左右2ペイン構成、同一位置での既存ルール編集・上書き保存、削除確認ダイアログ、高さ280pxの広域複数行テキストエリア、命名パターンテンプレート表記適正化）。
  3. 命名規則保存時の JSON 出力における日本語 Unicode エスケープ（`\uXXXX`）解消および生 UTF-8 出力化。
  4. リモート `develop`（PR #6: 日本語除外抽出機能）の最新変更マージとテスト整合。
- **実装成果物**:
  - `src/TagBasedVideoManager.Renamer/Settings.fs`（JavaScriptEncoder.Create(UnicodeRanges.All) 設定）
  - `src/TagBasedVideoManager.Renamer/State.fs`（パイプライン順序マッピング、編集・同一位置更新・削除確認ダイアログロジック）
  - `src/TagBasedVideoManager.Renamer/Views.fs`（幅960px/高さ680px 左右2ペインモーダル、削除確認モーダル、複数行テキストエリア）
  - `test/TagBasedVideoManager.Renamer.Tests/StateTests.fs`
  - `test/TagBasedVideoManager.Renamer.Tests/RenameIntegrationE2ETests.fs`
  - `test/TagBasedVideoManager.Renamer.Tests/SettingsTests.fs`
  - `openspec/changes/renamer-rule-manager-ux/mockup.html`

## 2. 品質・テストエビデンス結果

- **単体・結合・E2Eテスト**: 全 112 件 100% Pass（失敗 0 件、スキップ 0 件）
- **テストエビデンス**:
  - TRX レポート: `test/TestResults/TestRun_2026-10-05_23_48_49/TestRun_2026-10-05_23_48_49_net10.0.trx`
  - HTML カバレッジレポート: `test/TestResults/TestRun_2026-10-05_23_48_49/spjao_DENNOH_GITS_2026-10-05_23_48_49.467475.html`
  - E2E 画面キャプチャ: 全 20 枚（`E2E_05_RuleManagerModal_Initial.png`, `E2E_05b_RuleManagerModal_Editing.png`, `E2E_05c_RuleManagerModal_DeleteConfirm.png` 等を含む実画面ビジュアル検証完了）

## 3. 保留指摘・技術的負債・既知の課題

- **Apply フェーズでの保留指摘**: なし（全タスク LGTM）
- **既知の課題・注意事項**:
  1. **設定ファイルの機密情報保護**:
     - 実機動作時、ユーザーが GUI 上で API キーを入力・保存するとローカルの `appsettings.json` にキーが書き込まれる仕様となっている。
     - リポジトリにコミットされる `appsettings*.json` にはダミープレースホルダー（`"[API_KEY]"`）が保持される必要があり、コミット前に `git status` / `git diff` で実キーが紛れ込んでいないか注意すること。
  2. **Tmds.DBus.Protocol 0.20.0 のセキュリティ警告 (NU1903)**:
     - .NET ビルド時に Avalonia の依存関係である `Tmds.DBus.Protocol` 0.20.0 の脆弱性警告（NU1903）が出力される。本アプリケーションの Windows デスクトップ環境では Linux D-Bus を直接使用しないため実害はないが、今後の Avalonia アップデート時にパッケージバージョンを更新することが推奨される。

## 4. 将来的な改善・拡張候補

- **命名パターンのプレビュー機能**:
  - ルール編集時に、サンプルファイル名に対するテンプレート展開結果（例: `{Code}_{Summary}_{Actor}` がどのように置換されるか）を右ペイン内でリアルタイムプレビューできる機能の追加。
- **プロンプト指示文のシンタックスハイライト / マークダウンプレビュー**:
  - プロンプト指示文内に記述された Markdown 見出しや箇条書きを整形表示するモードの検討。
