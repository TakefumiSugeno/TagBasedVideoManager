# Design: 決定論的UUID（UUID v5）による動画移動・リネーム・マウント変更追従と不在レコード自動削除

## Context

背景および課題の詳細は `proposal.md`（Why / What Changes）を参照。
現在のシステムでは、`Videos` テーブルの主キー `Id`（`v_xxxxxxxx`）に対して `VideoTags`・お気に入り・再生回数・サムネイル（`{Id}.jpg`）が紐づいているが、`Scanner.syncVideoDirectories` は正規化フルパス（`normFilePath`）の辞書検索のみで既存動画を特定している。そのため、パスやファイル名が変化すると既存レコードと結びつかず、新規重複登録と幽霊レコード残存が発生する。

## Goals / Non-Goals

**Goals:**

- 動画ファイル本体を一切変更せず（Read-Only、更新日時・ハッシュ不変）、ミリ秒オーダーの高速サンプリングにより RFC 9562 準拠の決定論的 UUID（UUID v5）を算出する。
- パス変更・ファイル名リネーム・バインドマウント変更時に既存の `Video.Id`・タグ・視聴履歴・サムネイルを維持し、完了済みのメディア処理（FFmpeg/ffprobe）を再実行させない。
- 実体が削除された動画の DB レコード（`Videos` / `VideoTags`）および孤児サムネイル画像をスキャン時に自動クリーンアップする。
- スキャン前に不在動画を再生しようとした際、HTTP 500 ではなく HTTP 404 を返し、UI にエラートーストを表示する。

**Non-Goals:**

- 動画コンテナ（MP4）内部へのメタデータ書き込みやサイドカーファイルの生成。
- `VIDEO_DIRS` の全ディレクトリが不在（外付けドライブ未接続等）の場合における DB レコード削除。

## Decisions

### D1: ファイル本体を更新しない決定論的 UUID（UUID v5）算出アルゴリズム

- **決定内容**:
  - `Domain.FileIdentity` モジュールを新設し、以下の入力バイト列から SHA-1 ハッシュを計算して RFC 9562 Section 5.5（UUID Version 5）フォーマットの `Guid` 文字列（36文字小文字ハイフン区切り）を生成する関数 `computeFileUuid (filePath: string) : Result<string, string>` を実装する。
  - **入力構成**:
    1. アプリ固有の固定 Namespace UUID（`8f9d5a12-4b6c-4e8a-9c1d-2e3f4a5b6c7d`）の 16 バイト（ネットワークバイトオーダー）
    2. ファイルサイズ（`int64`、8 バイト Little Endian）
    3. ファイル内容のサンプリングバイト列（チャンクサイズ `65,536` バイト = 64KB）:
       - `fileSize = 0L` の場合: サンプル 0 バイト（サイズ 0L のみ）
       - `0L < fileSize <= 196,608L`（192KB 以下）の場合: ファイル先頭から全バイトを読み取る
       - `fileSize > 196,608L`（192KB 超）の場合: 先頭 64KB（オフセット `0`）、中間 64KB（オフセット `(fileSize / 2L) - 32768L`）、末尾 64KB（オフセット `fileSize - 65536L`）の計 192KB を `FileStream.Seek` で読み取る
  - ファイルオープン時は `new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite ||| FileShare.Delete)` を使用し、ファイル本体・更新日時を一切変更しない。
- **採用理由と代替案の比較**:
  - _代替案1（ファイル全体の SHA-256 計算）_: 数 GB の動画ファイルが移動した際に数秒〜数十秒のディスク I/O ブロッキングが発生するため不採用。先頭・中間・末尾 64KB + ファイルサイズの組み合わせは 192KB 読み取り（数 ms 以下）で完結し、実用上の衝突確率も極めて低い。
  - _代替案2（OS の NTFS FileId / Linux inode）_: Windows ホストと Docker コンテナの切り替えや別ドライブ移動で ID が変化するため不採用。

### D2: DB スキーマ拡張（`Videos.FileUuid`）と外部キー制約の常時有効化

- **決定内容**:
  - `Videos` テーブルの DDL および `DbInit.initializeDatabase` の自動マイグレーションに `FileUuid TEXT` カラムを追加する。
  - `Domain.Video` レコードおよび `VideoDbDto` に `FileUuid: string option`（DTO は `FileUuid: string`）を追加する。
  - `Db.getConnection` の接続文字列を `$"Data Source={dbPath};Foreign Keys=True"` に変更し、すべての接続で SQLite の外部キー制約を有効化する。これにより `DELETE FROM Videos WHERE Id = @Id` 実行時に `VideoTags` が `ON DELETE CASCADE` で確実に削除される。
  - `Db` モジュールに以下の関数を追加する:
    - `updateVideoFileUuid (conn: IDbConnection) (videoId: string) (fileUuid: string) : Async<Result<unit, DbError>>`
    - `updateVideoLocation (conn: IDbConnection) (videoId: string) (fileName: string) (filePath: string) (fileSize: int64) (fileModifiedAt: DateTime) (fileUuid: string option) (thumbnailPath: string option) : Async<Result<unit, DbError>>`
    - `deleteVideo (conn: IDbConnection) (videoId: string) : Async<Result<unit, DbError>>`

### D3: スキャン時の3フェーズ同期・メディア処理スキップ・孤児クリーンアップ

```text
+-----------------------------------------------------------------------------+
|                    3-PHASE SYNC IN Scanner.syncVideoDirectories             |
+-----------------------------------------------------------------------------+

  [existingVideosList (DB)]                        [uniqueFiles (Disk)]
              |                                              |
              +----------------------+-----------------------+
                                     |
                                     v
  Phase 1: Normalized Path Exact Match
  ------------------------------------
  - normFilePath が existingVideoDict に存在する場合:
    * activeVideoIds に v.Id を登録（削除対象から除外）
    * v.FileUuid が None なら computeFileUuid で算出して DB バックフィル
    * サムネイル解決: "{thumbDir}/{v.Id}.jpg" が存在すれば有効と判定し、
      v.ThumbnailPath と異なれば DB の ThumbnailPath を更新
    * Duration > 0 かつサムネイル実ファイルが存在すれば MediaQueue 投入をスキップ

              |                                              |
    [unmatchedDbVideos]                            [unmatchedDiskFiles]
              |                                              |
              +----------------------+-----------------------+
                                     |
                                     v
  Phase 2: UUID v5 Relocation / Rename / Mount-Change Match
  ---------------------------------------------------------
  - unmatchedDiskFiles の各ファイルについて computeFileUuid を実行
  - unmatchedDbVideos から以下の優先順で一致候補を検索:
    (1) v.FileUuid = Some computedUuid
        (複数候補がある場合は FileName 一致 -> FileModifiedAt 近接の順)
    (2) [旧DB移行フォールバック] v.FileUuid = None かつ v.FileSize = fileSize
        かつ (FileName 一致 または |v.FileModifiedAt - fileModifiedAt| <= 2秒)
  - 候補 v が見つかった場合（移動・リネーム・マウント変更）:
    * unmatchedDbVideos から v を除去し、activeVideoIds に v.Id を登録
    * Db.updateVideoLocation で FilePath, FileName, FileSize, FileModifiedAt,
      FileUuid, ThumbnailPath を UPDATE（Id, Tags, IsFavorite, AccessCount 維持）
    * Duration > 0 かつサムネイル実ファイルが存在すれば MediaQueue 投入をスキップ
    * 新パスのフォルダ名タグ・自動ルールタグを attachTag で追加付与
  - 候補が見つからなかった場合（完全新規動画）:
    * 新規 VideoId を採番し FileUuid = Some computedUuid で Db.saveVideo
    * MediaQueue へ投入し、フォルダ名タグ・自動ルールタグを付与

              |
    [残った unmatchedDbVideos = 物理削除された動画]
              |
              v
  Phase 3: Deleted Video Record & Orphan Thumbnail Cleanup
  --------------------------------------------------------
  - 残った unmatchedDbVideos の各 v について:
    * Db.deleteVideo conn v.Id (Videos 削除 + VideoTags CASCADE 削除)
    * v.ThumbnailPath および "{thumbDir}/{v.Id}.jpg" が存在すれば File.Delete
  - thumbDir 内の "v_*.jpg" を走査し、ファイル名(拡張子除く)が activeVideoIds に
    含まれない孤児サムネイルファイルを File.Delete
```

- **注意点（同一内容のコピーファイルが存在する場合の挙動）**:
  - 元ファイルが旧パスにそのまま存在する場合、そのレコードは Phase 1 でパス一致として消費され `unmatchedDbVideos` に含まれない。そのため、別パスに置かれたコピーファイルは Phase 2 で既存レコードを奪うことなく、安全に別の新規動画として登録される。

### D4: ストリーミング API の物理ファイル不在時 HTTP 404 応答と UI エラートースト・オーバーレイ通知（`mockup.html` 準拠）

- **バックエンド（`HttpHandlers.streamVideo`）**:
  - 既存の Linux 255 バイト制限（`fnBytes > 255` → HTTP 422 `FILE_NAME_TOO_LONG`）の判定直後、`resolvedFinalPath` に対して `File.Exists(resolvedFinalPath)` を確認する。
  - 存在しない場合は `PhysicalFileResult` を呼び出さず、ステータスコード `404` と JSON `{| code = "FILE_NOT_FOUND"; error = "動画ファイルが見つかりません。"; message = "動画ファイルが見つかりません。"; filePath = filePath |}` を返却する。
- **フロントエンド（`src/TagBasedVideoManager/wwwroot/app.js` `handleVideoError` および `index.html`、`mockup.html` 準拠）**:
  - `res.status === 422` の既存処理に加え、`res.status === 404` の場合に `this.playbackError = { isNotFound: true, message: ..., filePath: ... }` を設定してプレイヤー中央に HTTP 404 エラーオーバーレイ（登録パス表示・「プレイヤーを閉じる」ボタン・「今すぐスキャン同期を実行」ボタン）を表示するとともに、`this.showToast("⚠️ 動画ファイルが見つかりません。ファイルが移動または削除された可能性があります。", "error", filePath)` を表示する。
  - その他の HTTP エラーや例外時にも `this.showToast("⚠️ 動画の再生中にエラーが発生しました。", "error")` を表示し、無反応状態を解消する。

## Risks / Trade-offs

- **[Risk] テストコード等で同一のダミー文字列（例: `"dummy"`）を持つ複数ファイルを別名移動した際の UUID 同一視**
  → **Mitigation**: Phase 2 で同一 `FileUuid` を持つ `unmatchedDbVideos` 候補が複数ある場合は、`FileName`（大文字小文字無視）が一致する候補を最優先し、次に `FileModifiedAt` が最も近い候補を選択する決定論的タイブレークを行う。
- **[Risk] 外付けストレージ全体がアンマウントされた状態でスキャンが実行された場合のデータ消失**
  → **Mitigation**: `Scanner.syncVideoDirectories` 冒頭の `if List.isEmpty existingRoots then return Ok 0` ガードを維持し、有効な走査ルートが 1 件も存在しない場合は Phase 3（削除処理）へ進まず即座に安全終了する。
- **[Risk] サムネイルファイル削除時の I/O ロック例外によるスキャン全体の中断**
  → **Mitigation**: サムネイルファイルの `File.Delete` は個別の `try ... with` で保護し、ファイルロック等で削除できなかった場合でも DB レコード削除や他ファイルの同期を中断させない。

## Migration Plan

1. **自動スキーママイグレーション（前方互換）**:
   - アプリ起動時の `DbInit.initializeDatabase` にて `PRAGMA table_info(Videos)` を確認し、`FileUuid` カラムが存在しない場合は `ALTER TABLE Videos ADD COLUMN FileUuid TEXT` を自動実行する（既存データは `NULL` として保持され、ダウンタイムなしで移行完了）。
2. **既存レコードの段階的 UUID 付与とマウント変更移行**:
   - 既存パスでスキャンされた動画は Phase 1 で自動的に `FileUuid` がバックフィルされる。
   - アップグレードと同時にバインドマウントを変更した場合でも、Phase 2 のフォールバック（`FileSize` + `FileName` / `FileModifiedAt` 一致）により既存レコードが引き継がれ、同時に `FileUuid` が設定される。
3. **ロールバック計画（Rollback Plan）**:
   - `Videos.FileUuid` は NULL 許容の追加カラムであり、万一旧バージョンのアプリケーションバイナリへロールバックした場合でも、旧バージョンの `SELECT` / `INSERT` クエリは `FileUuid` カラムを無視してそのまま正常動作する（DB ファイルのリストアや手動 ALTER は不要）。
