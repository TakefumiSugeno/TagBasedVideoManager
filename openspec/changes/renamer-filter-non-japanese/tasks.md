# Tasks

## 1. 日本語判定ロジックとファイルスキャンフィルタ（TDD）

- [x] 1.1 `FileScannerTests.fs` にファイル名本体の日本語判定（ひらがな・カタカナ・CJK漢字・英数記号）および `scanLongPaths` フィルタの失敗する単体テスト（Red）を作成し、テスト失敗を確認する
- [x] 1.2 `FileScanner.fs` にコンパイル済み正規表現による `containsJapanese` 関数と `scanLongPaths` への `filterNonJapaneseOnly` 引数・フィルタ条件を実装し、テストをすべてパス（Green）させてリファクタする

## 2. 状態管理（Elmish Model / Msg）とUIコントロールの統合

- [x] 2.1 `StateTests.fs` に `ToggleFilterNonJapaneseOnly` メッセージによるモデル状態更新および `ExecuteScanAndPropose` 実行時の引数連携を検証するテストを作成する
- [x] 2.2 `State.fs` に `FilterNonJapaneseOnly` モデルフィールドおよび `ToggleFilterNonJapaneseOnly` メッセージ処理を実装し、テストをパスさせる
- [ ] 2.3 `Views.fs` の抽出基準エリア（Row 1）に `[ ] ファイル名に日本語を含まないもののみ抽出` チェックボックスを配置し、ダークテーマ・フォント指定を `mockup.html` に準拠して実装する

## 3. 総合検証・回帰テストと品質エビデンス出力

- [ ] 3.1 全単体テスト・結合テストを実行して全件パスを確認し、`test/TestResults/` に TRX レポートおよびカバレッジレポートを出力する
- [ ] 3.2 ソースコードおよび OpenSpec アーティファクトの自動フォーマット（`dotnet format` / `npx prettier`）を実行し、フォーマット違反がないことを確認する
