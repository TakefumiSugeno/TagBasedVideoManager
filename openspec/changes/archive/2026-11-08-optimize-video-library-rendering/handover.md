# 申し送り事項 (Handover): optimize-video-library-rendering

- **変更名**: `optimize-video-library-rendering`
- **日付**: 2026-11-08
- **ブランチ**: `fix/optimize-video-library-rendering`
- **関連 Issue / PR**:
  - クローズ対象 Issue: [#25](https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/25)（[機能改善]動画ライブラリのデータ表示が遅い。）
  - 派生 Issue: [#28](https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/28)（[機能改善] 2万件規模に対応するサーバーサイドページネーション（LIMIT/OFFSET）およびサーバーサイドソートの導入）

---

## 1. 変更サマリ

動画ライブラリの表示パフォーマンス向上を目的とし、バックエンドの直列 N+1 クエリ解消、フロントエンドのサムネイル遅延読み込み、およびアイコン生成処理の debounce 化を実施しました。既存の API 契約および UI コンポーネント構成を完全維持したまま、初期表示レスポンスとクライアント描画の高速化を達成しました。

### 原因と対応

- **原因**:
  - バックエンド: `Db.getVideos` および `Db.searchVideos` において、取得した全動画レコードに対して 1 件ずつ同期ループで `getTagsForVideo` を発行する直列 N+1 クエリが発生しており、動画件数に比例して DB 往復オーバーヘッドが増大していた。
  - フロントエンド: 初回レンダリング時に全件のサムネイル画像が一括読み込みされネットワークとメモリを圧迫していた。また、描画更新のたびに `lucide.createIcons()` が同期・連続実行されメインスレッドをブロックしていた。
- **対応**:
  1. `Db.fs`: 全動画 ID に対するタグを単一クエリ（SQLite のパラメータ制限を考慮し 500 件チャンク分割対応）で一括取得・辞書マッピングする `attachTagsBatch` を実装し、直列 N+1 クエリを解消。
  2. `index.html`: 全表示モード（PCグリッド/リスト、モバイルグリッド/リスト、モーダル等）の動画サムネイル `<img>` に `loading="lazy"` および `decoding="async"` を付与し、画面外画像の遅延読み込みを実現。
  3. `app.js`: `lucide.createIcons()` 呼び出しに 16ms の debounce 機構を導入し、インクリメンタルサーチや連続描画更新時のメインスレッド負荷を抑制。

---

## 2. 品質・テストエビデンス

- **バックエンド単体・結合テスト**: `dotnet test test/TagBasedVideoManager.Tests/TagBasedVideoManager.Tests.fsproj` → **136 件 Pass**（失敗 0 / スキップ 0）
  - 新規作成・追加テスト:
    - `DbTests.fs`: `attachTagsBatch` の空配列、単一、複数、500 件超チャンク分割境界値、タグなし動画を含むテスト（10 件追加）
    - `SearchApiTests.fs`: `/api/videos` および `/api/videos/search` のタグバッチ読み込み・タグなし動画統合テスト（7 件追加）
    - `StaticFileTests.fs`: `index.html` の `loading="lazy"` / `decoding="async"` 存在検証、および `app.js` の debounce 実装検証（8 件追加）
- **デスクトップ（Renamer）テスト**: `dotnet test test/TagBasedVideoManager.Renamer.Tests/TagBasedVideoManager.Renamer.Tests.fsproj` → **336 件 Pass**（失敗 0 / スキップ 0）
- **総テスト件数**: **472 件 全件合格（100% Pass）**
- **エビデンス出力先**: `test/TestResults/TestRun_2026-11-08_08_15_17/TestRun_2026-11-08_08_15_17_net10.0.trx`

---

## 3. 保留指摘・技術的負債

| 指摘元（フェーズ/タスク/役割） | 内容                                                                             | 保留理由                                                                                                                                                          | 推奨対応時期                                                                                               |
| ------------------------------ | -------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| Propose / SE Reviewer          | サーバーサイド LIMIT/OFFSET ページネーションおよびサーバーサイドソートの導入     | 2 万件規模に対応するページネーションは API 契約およびフロントエンド状態管理の全面刷新が必要であり、本変更（既存互換の高速化）のスコープを超えるため独立課題とした | フェーズ 2（[Issue #28](https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/28) として起票済み） |
| Apply / QA Reviewer            | Linux 環境（WSL/DevContainer）での長大ファイル名（255 バイト超）再生時エラー対策 | Linux カーネル制約（NAME_MAX）に起因するクロスプラットフォーム課題であり、動画ライブラリ描画最適化とは独立した不具合/改善課題であるため                           | [Issue #27](https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/27) として起票済み               |

---

## 4. 将来の改善・リファクタ候補

- **[Issue #28](https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/28)**: 2 万件規模に対応するサーバーサイド LIMIT/OFFSET ページネーションおよびサーバーサイドソートの導入
- **[Issue #27](https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/27)**: Linux (WSL/DevContainer) のファイル名 255 バイト制限（NAME_MAX）による再生不可へのクロスプラットフォーム対応
- **仮想スクロール（Virtual Scrolling）の検討**: 万単位の動画をブラウザ上に表示する場合、DOM ノード数を一定に保つ仮想スクロールの導入

---

## 5. 運用上の注意事項

- **データベースマイグレーション**: 不要（既存の SQLite テーブル構造に一切の変更はありません）。
- **環境設定・依存関係**: 追加の環境変数や外部ライブラリの追加はありません。完全な後方互換性を保持しています。
- **デプロイ手順**: 通常通りコンテナのビルド・再起動（または `scripts/deploy.ps1` の実行）で即時適用可能です。
