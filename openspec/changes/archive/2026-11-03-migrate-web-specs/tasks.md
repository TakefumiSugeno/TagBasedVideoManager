# Tasks

## 1. 既存テストと仕様シナリオのトレーサビリティ検証

- [x] 1.1 `video-library` 仕様（ストリーミング配信、アクセス記録、お気に入り、グリッド表示、Zero-State）のシナリオと既存テスト（`StreamingTests.fs`, `HttpHandlersTests.fs`, `E2ETests.fs`）のテストメソッド名を突き合わせ、`test_plan.md` のマトリクスを確定した上でテストが全件成功することを確認する
- [x] 1.2 `video-scanner` 仕様（ディレクトリ走査、即時DB登録、2段階シーク、並行制御、中断、破損フォールバック）のシナリオと既存テスト（`ScannerTests.fs`, `MediaTests.fs`）のテストメソッド名を突き合わせ、`test_plan.md` のマトリクスを確定した上でテストが全件成功することを確認する
- [x] 1.3 `tag-management` 仕様（階層タグCRUD、動的件数集計、自動ルール遡及適用、TSVインポート、SET NULL、循環参照防止）のシナリオと既存テスト（`TagApiTests.fs`, `RuleApiTests.fs`, `TaggingEngineTests.fs`）のテストメソッド名を突き合わせ、`test_plan.md` のマトリクスを確定した上でテストが全件成功することを確認する
- [x] 1.4 `search-and-folders` 仕様（検索DSL構文、再帰CTE階層ヒット、スマートフォルダCRUD、フォールバック、Empty-State）のシナリオと既存テスト（`SearchApiTests.fs`, `SearchDslTests.fs`, `DbTests.fs`, `E2ETests.fs`）のテストメソッド名を突き合わせ、`test_plan.md` のマトリクスを確定した上でテストが全件成功することを確認する
- [x] 1.5 `viewing-analytics` 仕様（再生ランキング、ハイブリッドキーワード抽出おすすめ、エラーログ、ヘルスチェック、Zero-State）のシナリオと既存テスト（`HttpHandlersTests.fs`, `DbTests.fs`）のテストメソッド名を突き合わせ、`test_plan.md` のマトリクスを確定した上でテストが全件成功することを確認する

## 2. 旧仕様ドキュメントへの移行案内注記の追加

- [x] 2.1 `doc/spec.md` の冒頭に「本ドキュメントは過去の初期仕様であり、現在の正本は `openspec/specs/` 配下に移行済みである」旨の案内注記を追加し、記載内容を検証する

## 3. 全体検証とエビデンス確認

- [x] 3.1 Webアプリの全テストスイートを実行し、リグレッションなく全件PASSすることを確認する (`dotnet test test/TagBasedVideoManager.Tests/`)
- [x] 3.2 OpenSpecバリデーションを実行し、すべての仕様と変更成果物が妥当であることを確認する (`openspec validate "migrate-web-specs"`)
- [x] 3.3 フェーズゲート検証（Gate 2）を実行し、DoDを満たすことを確認する (`pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply -ChangeName "migrate-web-specs"`)

## Workflow follow-up

- アーカイブを実行してメイン仕様（`openspec/specs/`）へデルタ仕様を同期する (`openspec-archive-change`)
