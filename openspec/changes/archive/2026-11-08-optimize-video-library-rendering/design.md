# Design: optimize-video-library-rendering

## Context

現状、動画一覧表示（`Db.getVideos`）および検索（`Db.searchVideos`）では、まず動画テーブルを SELECT した後、取得した全動画リストに対して F# の `List.map` と `Async.Sequential` を用いて、1 件ずつ個別に `VideoTags` と `Tags` を直列クエリしている（詳細は `proposal.md` 参照）。動画数が 100〜500 件に達すると DB 往復が数百回発生し、数百 ms 〜 数秒のブロッキングを引き起こしている。

## Goals / Non-Goals

### Goals

- **DB クエリ発行回数の削減**: 動画一覧および検索結果におけるタグ取得を直列 N+1 回から **2 回（動画取得 1 回 + タグ一括取得 1 回）** へ集約する。
- **API 契約の完全互換**: 既存の `/api/videos` および `/api/videos?q=...` の JSON レスポンス構造・型定義を一切変更しない。
- **初期表示時のブラウザ負荷分散**: サムネイル画像にネイティブ遅延ロードを適用し、ビューポート外の画像による通信集中を解消する。
- **アイコン再描画の抑制**: 動画リスト更新時の `lucide.createIcons()` による全 DOM 走査負荷を軽減する。

### Non-Goals

- サーバーサイド LIMIT/OFFSET ページネーション（2 万件対応はフェーズ 2 の独立した変更とする）。
- DB スキーマ変更（テーブル追加・カラム追加は行わない）。

## Decisions

### 1. タグ一括取得方式: 「2 クエリ + インメモリ Map 結合」の採用

- **決定**: 動画レコード取得後、対象の `VideoId` リストをキーとして、1 回のクエリで関連する全タグを取得し、メモリ上でマッピングする。
  ```sql
  -- クエリ 1: 動画一覧取得
  SELECT Id, FileName, FilePath, Duration, FileSize, ThumbnailPath, IsFavorite, CreatedAt, FileModifiedAt, AccessCount, LastAccessedAt FROM Videos (WHERE ...)

  -- クエリ 2: 該当動画に紐付く全タグを一括取得
  SELECT vt.VideoId, t.Id, t.Name, t.ColorCode, t.ParentId
  FROM Tags t
  INNER JOIN VideoTags vt ON t.Id = vt.TagId
  WHERE vt.VideoId IN @VideoIds
  ```
  取得したタグ DTO を `Seq.groupBy (fun t -> t.VideoId)` でグループ化し、各 `Video` レコードの `Tags` フィールドに代入する。
- **代替案（単一 LEFT JOIN クエリ）の不採用理由**:
  動画とタグを 1 回の `LEFT JOIN` で取得した場合、タグを複数持つ動画は動画メタデータ（長いファイルパスや日時等）がタグ数分重複して返却され、Dapper 側のマッピング処理が複雑化する。2 クエリ方式の方がシンプルで安全かつ高速である。

### 2. SQLite の IN 句パラメータ上限対策（チャンク分割）

- **決定**: 動画数が多い場合（数百〜数千件）でも安全に動作するよう、`@VideoIds` の IN 句は 500 件ごとにチャンク分割して取得し、結果をマージする。
- **理由**: SQLite のパラメータ上限（古いバージョンでは 999 個、最新では 32,766 個）に対する防御策として、500 件単位での分割取得を行うことで将来的な件数増加にも耐えうる堅牢性を担保する。対象動画が 0 件の場合はタグクエリを発行せず即座に空リストを返す。

### 3. ブラウザネイティブ Lazy Loading の適用

- **決定**: フロントエンド（`index.html`）の動画カード内の `<img>` タグに `loading="lazy"` および `decoding="async"` を付与する。
- **理由**: 外部 JS ライブラリを導入せず、ブラウザ標準の Intersection Observer 最適化を利用して、初期表示に必要な画像のみを自動的に先読みさせる。

### 4. Lucide アイコン変換処理の最適化

- **決定**: `app.js` の `$watch("pagedVideos", ...)` において、アイコン再生成処理の重複呼び出しを debounce / nextTick で抑制する。

## Risks / Trade-offs

- **[Risk] メモリ消費量**:
  - タグを一括取得してグループ化するため、一時的にタグ一覧のリストがメモリ上に保持される。
  - **Mitigation**: タグ DTO は 1 件数十バイト程度であり、数千件あっても数 MB 未満のためメモリへの影響は極めて軽微である。
- **[Risk] 検索クエリでの動作**:
  - `searchVideos` も同様の構造になっているため、`getVideos` と共通のヘルパー関数（`attachTagsBatch` 等）を作成して両方に適用し、修正漏れを防ぐ。
