# テスト計画・テストケース定義書 (Test Plan): migrate-web-specs

- **変更名**: `migrate-web-specs`
- **適用規模レベル**: `L2`
- **変更種別**: `chore` / `docs`

---

## 1. カバレッジ目標と完了基準

| 指標                          | 定義                                                  | 目標値（全体 / 変更差分） | 適用対象・備考                         |
| :---------------------------- | :---------------------------------------------------- | :------------------------ | :------------------------------------- |
| **C0 (命令網羅 / Line)**      | 実行可能な全ステートメントの網羅率                    | **80%以上** / **90%以上** | Webアプリ全対象モジュール（既存維持）  |
| **C1 (分岐網羅 / Branch)**    | `if/else`, `match`, ガード節の網羅率                  | **80%以上** / **85%以上** | Webアプリ全対象モジュール（既存維持）  |
| **C2 (条件網羅 / Condition)** | 複合条件式の真偽組み合わせ網羅                        | **100%**                  | タグ自動ルール評価、検索DSL、再帰CTE   |
| **Scenario 網羅率**           | `specs/**/spec.md` の全 Scenario に対するテスト対応率 | **100%**                  | 全シナリオに対するテストマッピング完了 |

- **カバレッジ除外対象**: 自動生成コード、エントリポイントの定型ボイラープレート
- **モック境界方針**:
  - **モック化する外部境界**: FFmpeg/ffprobe 外部プロセス（`IMediaProcessor`）
  - **実物を使用する範囲**: SQLite（`Microsoft.Data.Sqlite`）、Dapper、Kestrel/Giraffe テストホスト

---

## 2. シナリオ・トレーサビリティマトリクス

| Spec Capability / Requirement           | Scenario 名                                      | Unit | Integration | E2E | 対応テストコード / ケースID                             |
| :-------------------------------------- | :----------------------------------------------- | :--: | :---------: | :-: | :------------------------------------------------------ |
| `video-library` / 動画一覧表示          | グリッド表示とリスト表示の切り替え               |  -   |      -      | ✅  | `E2ETests.fs`                                           |
| `video-library` / 動画一覧表示          | 件数ページング表示                               |  -   |     ✅      | ✅  | `HttpHandlersTests.fs`, `E2ETests.fs`                   |
| `video-library` / 動画一覧表示          | 動画未登録の初期状態 (Zero-State)                |  -   |      -      | ✅  | `E2ETests.fs`                                           |
| `video-library` / カード固定サイズ      | グリッド小サイズでのメタデータ常時表示           |  -   |      -      | ✅  | `E2ETests.fs`                                           |
| `video-library` / 等速marquee           | 長いタイトルの往復等速スクロール                 |  -   |      -      | ✅  | `E2ETests.fs`                                           |
| `video-library` / HTTP Range配信        | シーク再生時の部分コンテンツ要求                 |  -   |     ✅      |  -  | `StreamingTests.fs`                                     |
| `video-library` / HTTP Range配信        | 実ファイル消失時の 404 エラー応答                |  -   |     ✅      |  -  | `StreamingTests.fs`                                     |
| `video-library` / 再生アクセス記録      | 初回再生開始時のアクセス記録                     |  ✅  |     ✅      |  -  | `HttpHandlersTests.fs`                                  |
| `video-library` / 再生アクセス記録      | シーク操作時のカウント不変                       |  -   |     ✅      | ✅  | `StreamingTests.fs`, `E2ETests.fs`                      |
| `video-library` / アクセス履歴リセット  | 再生回数のクリア                                 |  ✅  |     ✅      |  -  | `HttpHandlersTests.fs`                                  |
| `video-library` / お気に入り更新        | 単一動画のお気に入りトグル                       |  ✅  |     ✅      | ✅  | `HttpHandlersTests.fs`, `E2ETests.fs`                   |
| `video-library` / お気に入り更新        | 複数動画のお気に入り一括登録                     |  ✅  |     ✅      |  -  | `HttpHandlersTests.fs`                                  |
| `video-library` / 静的サムネイル        | サムネイル画像の正常表示                         |  -   |     ✅      | ✅  | `StaticFileTests.fs`, `E2ETests.fs`                     |
| `video-library` / 静的サムネイル        | サムネイル未生成動画のプレースホルダー           |  -   |      -      | ✅  | `E2ETests.fs`                                           |
| `video-scanner` / ディレクトリ走査      | 通常およびシンボリックリンク配下の検出           |  ✅  |      -      |  -  | `ScannerTests.fs`                                       |
| `video-scanner` / ディレクトリ走査      | 存在しないディレクトリの安全走査                 |  ✅  |      -      |  -  | `ScannerTests.fs`                                       |
| `video-scanner` / 即時DB登録            | 新規動画の即時DB保存とUIレスポンス               |  ✅  |      -      |  -  | `ScannerTests.fs`                                       |
| `video-scanner` / 無限ループ防止        | 処理失敗レコードの再投入スキップ                 |  ✅  |      -      |  -  | `ScannerTests.fs`                                       |
| `video-scanner` / 無限ループ防止        | メタデータ解析失敗動画の救済フォールバック       |  ✅  |      -      |  -  | `MediaTests.fs`, `ScannerTests.fs`                      |
| `video-scanner` / 2段階シーク           | 高速シーク成功                                   |  ✅  |      -      |  -  | `MediaTests.fs`                                         |
| `video-scanner` / 2段階シーク           | 高速シーク失敗時の安全シークフォールバック       |  ✅  |      -      |  -  | `MediaTests.fs`                                         |
| `video-scanner` / 同時実行制御          | FFmpegの逐次実行・ffprobe並行                    |  ✅  |      -      |  -  | `MediaTests.fs`                                         |
| `video-scanner` / デッドロック防止      | 標準エラーバッファ並行非同期読み出し             |  ✅  |      -      |  -  | `MediaTests.fs`                                         |
| `video-scanner` / フォルダ名タグ        | フォルダ名タグの自動紐付け                       |  ✅  |      -      |  -  | `ScannerTests.fs`                                       |
| `video-scanner` / 手動停止              | メディア処理の中断実行                           |  ✅  |     ✅      |  -  | `MediaTests.fs`, `HttpHandlersTests.fs`                 |
| `tag-management` / 階層タグ管理         | 親タグの更新とツリー構造の変更                   |  ✅  |     ✅      | ✅  | `DbTests.fs`, `TagApiTests.fs`, `E2ETests.fs`           |
| `tag-management` / 階層タグ管理         | 動的件数バッジの取得                             |  ✅  |     ✅      |  -  | `DbTests.fs`, `TagApiTests.fs`                          |
| `tag-management` / 階層タグ管理         | 親タグ削除時における子タグの親解除 (SET NULL)    |  ✅  |     ✅      |  -  | `DbTests.fs`, `TagApiTests.fs`                          |
| `tag-management` / 循環参照防止         | 自分自身を親に指定した不正更新の拒否             |  ✅  |     ✅      |  -  | `DbTests.fs`, `TagApiTests.fs`                          |
| `tag-management` / タグ解除             | 単一動画からのタグ解除 (JSON body)               |  ✅  |     ✅      | ✅  | `DbTests.fs`, `HttpHandlersTests.fs`, `E2ETests.fs`     |
| `tag-management` / 一括タグ操作         | 選択動画への一括タグ付与・削除                   |  -   |     ✅      |  -  | `HttpHandlersTests.fs`                                  |
| `tag-management` / 自動タグ付け         | ルール作成時の遡及適用                           |  ✅  |     ✅      |  -  | `TaggingEngineTests.fs`, `RuleApiTests.fs`              |
| `tag-management` / インポート           | TSV形式によるタグ階層の一括インポート            |  ✅  |     ✅      |  -  | `DbTests.fs`, `TagApiTests.fs`                          |
| `search-and-folders` / 検索DSL          | 複数タグAND / OR論理演算検索                     |  ✅  |     ✅      | ✅  | `SearchDslTests.fs`, `SearchApiTests.fs`, `E2ETests.fs` |
| `search-and-folders` / 検索DSL          | 未知の構文入力時のフリーキーワードフォールバック |  ✅  |     ✅      |  -  | `SearchDslTests.fs`, `SearchApiTests.fs`                |
| `search-and-folders` / 検索DSL          | 検索結果0件時の Empty-State 表示                 |  -   |      -      | ✅  | `E2ETests.fs`                                           |
| `search-and-folders` / 再帰CTE          | 親タグ検索による子タグ動画のヒット               |  ✅  |     ✅      |  -  | `DbTests.fs`, `SearchApiTests.fs`                       |
| `search-and-folders` / スマートフォルダ | 検索条件の保存・ワンクリック適用                 |  ✅  |     ✅      | ✅  | `DbTests.fs`, `HttpHandlersTests.fs`, `E2ETests.fs`     |
| `search-and-folders` / インポート       | スマートフォルダのTSVインポート                  |  ✅  |     ✅      |  -  | `DbTests.fs`, `HttpHandlersTests.fs`                    |
| `viewing-analytics` / 再生ランキング    | 再生数ランキングの表示                           |  ✅  |     ✅      |  -  | `DbTests.fs`, `HttpHandlersTests.fs`                    |
| `viewing-analytics` / 再生ランキング    | 再生履歴が存在しない初期状態 (Zero-State)        |  -   |     ✅      |  -  | `HttpHandlersTests.fs`                                  |
| `viewing-analytics` / レコメンド        | 嗜好キーワードとおすすめ動画の提示               |  ✅  |     ✅      |  -  | `DbTests.fs`, `HttpHandlersTests.fs`                    |
| `viewing-analytics` / レコメンド        | 再生履歴なしの初期状態 (Zero-State)              |  ✅  |     ✅      |  -  | `DbTests.fs`, `HttpHandlersTests.fs`                    |
| `viewing-analytics` / エラーログ        | エラーログ確認とアプリ起動時自動クリア           |  ✅  |     ✅      |  -  | `DbInitTests.fs`, `HttpHandlersTests.fs`                |
| `viewing-analytics` / エラーログ        | エラーログが存在しない初期状態 (Zero-State)      |  -   |     ✅      |  -  | `HttpHandlersTests.fs`                                  |
| `viewing-analytics` / 死活監視          | ヘルスチェック疎通確認 (/api/ping)               |  -   |     ✅      |  -  | `HttpHandlersTests.fs`                                  |

---

## 3. テスト計画の死角・既知の限界と将来の改善課題

SE Reviewer による死角告発に基づき、現行テストスイートの既知の限界を以下に記録する：

1. **FFmpeg未インストール時のサイレントスキップ**:
   - `MediaTests.fs` はローカル環境に FFmpeg がない場合、テストがスキップ（サイレントパス）となる。CI/Docker環境でのテスト実行が必須。
2. **Rangeストリーミング切断時の例外ハンドリング**:
   - クライアントが動画ストリーミング中に接続を切断した際のソケット切断例外（`ConnectionResetException`）やファイルハンドル解放の検証が未網羅である（将来の結合テスト追加課題）。
3. **モックの都合の良い振る舞い**:
   - `ScannerTests.fs` の `mockMediaProcessor` は常に即時成功を返すため、サムネイル画像生成失敗時やプロセスハング時のフォールバックがテスト上で完全に模倣できていない（将来の Fault Injection テスト追加課題）。

---

## 4. 単体テスト：同値分割・境界値テストケース定義

| ケースID | 対象機能 / メソッド           | 検証観点（区分）       | 入力値・前提状態                       | 期待される出力・状態変化                 |
| :------- | :---------------------------- | :--------------------- | :------------------------------------- | :--------------------------------------- |
| `UT-01`  | `TaggingEngine.isRuleMatch`   | 正常系（部分一致）     | pattern="Touring", matchType="partial" | true                                     |
| `UT-02`  | `TaggingEngine.isRuleMatch`   | 境界値（MinSize 未満） | MinSize=100MB, fileSize=99MB           | false                                    |
| `UT-03`  | `TaggingEngine.isRuleMatch`   | 境界値（MinSize 丁度） | MinSize=100MB, fileSize=100MB          | true                                     |
| `UT-04`  | `TaggingEngine.isRuleMatch`   | 境界値（MaxSize 丁度） | MaxSize=500MB, fileSize=500MB          | true                                     |
| `UT-05`  | `TaggingEngine.isRuleMatch`   | 境界値（MaxSize 超過） | MaxSize=500MB, fileSize=501MB          | false                                    |
| `UT-06`  | `SearchDsl.parse`             | 空・空白境界           | `""` / `"   "`                         | 空リスト `[]`                            |
| `UT-07`  | `SearchDsl.parse`             | 1要素境界              | `"tag:バイク"`                         | `[TagName "バイク"]`                     |
| `UT-08`  | `extractKeywordsFromFileName` | 拡張子境界             | `"sample.mp4"`                         | `"mp4"` はストップワードとして除外される |

---

## 5. 単体テスト：複合条件デシジョンテーブル

対象ロジック: `TaggingEngine.isRuleMatch`（パターン一致 AND サイズ最小 AND サイズ最大）

| ケースID | 条件1: パターン一致 | 条件2: MinSize 充足 | 条件3: MaxSize 充足 | 期待される判定結果 |
| :------- | :-----------------: | :-----------------: | :-----------------: | :----------------- |
| `DT-01`  |          T          |          T          |          T          | 一致 (true)        |
| `DT-02`  |          T          |          T          |          F          | 不一致 (false)     |
| `DT-03`  |          T          |          F          |          -          | 不一致 (false)     |
| `DT-04`  |          F          |          -          |          -          | 不一致 (false)     |

---

## 6. 結合テスト（Integration Test）ケース定義

| ケースID | 連携対象                | シナリオ                                   | 期待される振る舞い                                             |
| :------- | :---------------------- | :----------------------------------------- | :------------------------------------------------------------- |
| `IT-01`  | WebAPI / DB             | `POST /api/videos/{id}/tags` 重複付与      | 冪等に追加処理されエラーとならないこと                         |
| `IT-02`  | WebAPI / DB             | `DELETE /api/videos/{id}/tags` (JSON body) | 正常に該当タグが解除され `{ videoId, tagId }` が返却されること |
| `IT-03`  | WebAPI / 静的ファイル   | `/thumbnails/{id}.jpg` 要求                | PhysicalFileProvider から正常に画像ストリームが返却されること  |
| `IT-04`  | WebAPI / ファイル未存在 | 存在しない動画のストリーム要求             | HTTP 404 Not Found が返却されること                            |

---

## 7. E2Eテスト・画面検証ケース定義

| ケースID | ジャーニー種別          | 操作手順                                           | 期待される画面状態・エビデンス                           |
| :------- | :---------------------- | :------------------------------------------------- | :------------------------------------------------------- |
| `E2E-01` | **Happy Path**          | 動画一覧表示 → タグ絞り込み → 再生モーダルオープン | 正確に描画され、再生アクセスAPIが記録される              |
| `E2E-02` | **Zero-State**          | 動画未登録状態での起動                             | クラッシュせず「動画が見つかりません」の案内が表示される |
| `E2E-03` | **Negative / Recovery** | 存在しない検索クエリ入力 → クリアボタン押下        | 0件表示からクリア後に全件表示へ復元される                |
