# Tasks: optimize-video-library-rendering

## 1. 再現テストとタグ一括結合テストの先行作成 (Red)

- [x] 1.1 `test/TagBasedVideoManager.Tests/DbTests.fs` に、動画一覧取得（`Db.getVideos`）および検索（`Db.searchVideos`）時のタグ一括結合に関する単体テスト（`UT-01`〜`UT-05`、`DT-01`〜`DT-04`）を作成し、テストが実行可能であることを確認する。
- [x] 1.2 `GET /api/videos` に対する結合テスト（`IT-01`〜`IT-03`）を作成し、タグ情報が正しくレスポンスに含まれることを確認する先行テストを追加する。

## 2. バックエンドタグ一括結合の実装 (Green & Refactor)

- [x] 2.1 `src/TagBasedVideoManager/Db.fs` にタグ一括取得ヘルパー（500件単位チャンク分割対応）を実装し、`getVideos` および `searchVideos` の直列 N+1 クエリを一括取得＋インメモリ Map 結合へ置き換える。
- [x] 2.2 `dotnet test` を実行し、先行作成した単体テストおよび結合テストがすべて Green となることを確認し、リファクタリングを実施する。

## 3. フロントエンドの描画・読み込み最適化

- [x] 3.1 `src/TagBasedVideoManager/wwwroot/index.html` の全表示モード（PCグリッド、PCリスト、モバイルグリッド、モバイルリスト）のサムネイル `<img>` タグに `loading="lazy"` および `decoding="async"` を付与する。
- [x] 3.2 `src/TagBasedVideoManager/wwwroot/app.js` の `lucide.createIcons()` 呼び出しタイミングを最適化し、不要な全画面 DOM 再走査を抑制する。
- [x] 3.3 E2E テストまたは HTML DOM 検証テスト（`E2E-01`）を実行し、サムネイル画像属性が正しく設定されていることを確認する。

## 4. 品質検証・レビュー・ゲート検証

- [x] 4.1 全体テスト実行、TRX レポート出力、および新規・変更差分のカバレッジ（C0 90%+, C1 85%+, C2 100%）達成を確認する。
- [x] 4.2 PG レビューおよび QA レビューを実施し、指摘事項の解消を確認する。
- [x] 4.3 `pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply -ChangeName optimize-video-library-rendering` を実行し、全項目が PASS することを確認する。

## Workflow follow-up

- Gate 2 (Apply) 通過後、ユーザー確認を経て Archive (Gate 3) を実施する。
