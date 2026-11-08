# Proposal: 決定論的UUID（UUID v5）による動画移動・リネーム・マウント変更追従と不在レコード自動削除

## メタデータ

- **適用規模レベル**: `L3`（DBスキーマ `Videos.FileUuid` カラム追加・マイグレーション、スキャン同期パイプライン改修、ストリーミング不在エラー通知修正を含むため）
- **変更種別**: `feat` / `fix`
- **関連 Issue**: [#23 バインドマウントの設定などで、同じ動画ファイルでもパスが変わったものはどのように処理されるのか?](https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/23)

## Why

Docker バインドマウント設定の変更、ホスト OS とコンテナ実行の切り替え、`VIDEO_DIRS` やサブフォルダ構成の整理、あるいは AI File Renamer 等によるファイル名変更によって「動画ファイルの実体は同じだがファイルパス（`FilePath`）やファイル名（`FileName`）が変わった」場合、現在のスキャン処理は正規化フルパス文字列の完全一致のみで同一性を判定しているため、別動画として新規重複登録され、手動付与したタグ・お気に入り・再生回数が引き継がれず、サムネイル作成や動画解析（FFmpeg/ffprobe）も再実行されてしまう。さらに、削除された動画や旧パスのレコード・サムネイルが DB とディスク上に幽霊データとして残り続け、スキャン前に不在ファイルを再生しようとすると HTTP 500 例外かつ画面無反応となる問題を解決する必要がある。

## What Changes

- **ファイル本体を更新しない決定論的 UUID（UUID v5）の算出と DB 保持**:
  - 動画ファイル本体には一切書き込みを行わず（Read-Only）、ファイルサイズとファイル内のサンプリングバイト列（先頭・中間・末尾の各最大 64KB）から RFC 9562 準拠の決定論的 UUID（UUID v5）を算出する仕組みを導入する。
  - SQLite の `Videos` テーブルに `FileUuid TEXT` カラムを自動マイグレーションで追加し、動画の論理的な同一性を `FileUuid` で追跡する。
- **移動・リネーム・バインドマウント変更時の既存レコード引き継ぎとメディア処理スキップ**:
  - スキャン時にパス不一致の動画ファイルを検出した際、UUID v5（および `FileUuid` 未設定の既存レコードに対するファイルサイズ＋更新日時/ファイル名フォールバック）によってパス不在の既存 DB レコードと照合する。
  - 同一動画と判定された場合は、既存の `Video.Id`・付与済みタグ・お気に入り・再生回数・`Duration` をすべて維持したまま `FilePath`・`FileName`・`FileModifiedAt`・`FileUuid` を更新し、移動先フォルダのフォルダ名タグおよび自動ルールタグを追加付与する。
  - サムネイルの存在確認を現在の `THUMBNAIL_DIR/{VideoId}.jpg` 基準で行うよう改善し、バインドマウント変更等で `THUMBNAIL_DIR` のパス表現が変わった場合でも、`Duration > 0` かつサムネイル実ファイルが存在すれば FFmpeg サムネイル生成および ffprobe 動画解析をスキップして `ThumbnailPath` のみを最新化する。
- **不在レコードおよび孤児サムネイルの自動削除（クリーンアップ）**:
  - スキャン同期の最終フェーズにおいて、物理ファイルが存在せず UUID 照合による移動先も見つからなかった DB レコード（`Videos` および `ON DELETE CASCADE` による `VideoTags`）と対応サムネイル画像を自動削除する。また、`THUMBNAIL_DIR` 内の孤児サムネイル（DB に存在しない `v_*.jpg`）も削除する。
  - `Db.getConnection` において SQLite 接続時の外部キー制約（`Foreign Keys=True`）を常時有効化し、`Videos` 削除時に `VideoTags` が確実に CASCADE 削除されるようにする。
- **物理ファイル不在時のストリーミング HTTP 404 応答とエラートースト表示（既存バグ修正）**:
  - **Root Cause（根本原因）**: `HttpHandlers.streamVideo` が物理ファイル不在確認を行わずに `PhysicalFileResult` を実行して `FileNotFoundException`（HTTP 500）を送出していたこと、および `app.js` の `handleVideoError` が HTTP 422 以外のステータスコードを無視していたこと。
  - 物理ファイル不在時は仕様通り HTTP 404 Not Found を返し、フロントエンドで「動画ファイルが見つかりません」のエラートースト通知を表示する。

## Non-goals（やらないこと）

- `.mp4` ファイル本体へのメタデータ埋め込み・バイナリ書き換え（ファイルのハッシュや更新日時を変えない原則を厳守する）。
- サイドカーファイル（`.uuid` 等）の動画ディレクトリ内への生成。
- 既存のフォルダ名由来タグの自動剥奪（DB 上で手動タグとフォルダタグの区別がないため、移動時は既存タグを維持したまま新フォルダのタグを追加する）。
- `VIDEO_DIRS` に指定されたすべてのルートディレクトリが存在しない場合（ドライブ丸ごと未接続時）の DB レコード削除（安全保護のため既存の早期 `Ok 0` 終了を維持する）。

## Capabilities

### New Capabilities

<!-- 新規 Capability の追加はなし（既存の video-scanner および video-library の拡張で対応） -->

### Modified Capabilities

- `video-scanner`: 決定論的 UUID（UUID v5）によるファイル移動・リネーム・バインドマウント変更の自動追従、環境非依存のサムネイル存在確認による再処理スキップ、および削除済みファイルの DB レコード・孤児サムネイル自動削除要件を追加・変更。
- `video-library`: 物理ファイル不在時のストリーミング HTTP 404 応答とフロントエンドでのファイル不在エラートースト通知の振る舞いを明確化。

## 受け入れ基準（Acceptance Criteria）

1. **Given** 登録済み動画（タグ・お気に入り・再生回数・サムネイル・再生時間あり）が存在する状態 / **When** ファイル名変更、別サブフォルダへの移動、またはバインドマウントパス変更を行ってスキャンを実行したとき / **Then** ファイル本体を一切更新せずに同一動画として認識され、`Id`・タグ・お気に入り・再生回数が維持されたまま `FilePath` と `FileName` が更新され、移動先フォルダタグが追加され、サムネイル作成・動画解析（FFmpeg/ffprobe）は再実行されないこと。
2. **Given** データベースに動画レコードとサムネイル画像が存在する状態 / **When** ディスク上の動画ファイルを削除してスキャンを実行したとき / **Then** 該当する `Videos` レコード、紐づく `VideoTags` レコード、およびサムネイル画像ファイルが自動的に削除されること。
3. **Given** ディスク上の動画ファイルが削除され、まだスキャンを実行していない状態 / **When** UI 上で該当動画をクリックして再生しようとしたとき / **Then** ストリーミング API が HTTP 404 を返し、画面上に「動画ファイルが見つかりません」を含むエラートースト通知が表示されること。

## Impact

- `src/TagBasedVideoManager/Domain.fs`: `Video` レコードへの `FileUuid: string option` フィールド追加、および決定論的 UUID v5 算出ヘルパー（`FileIdentity` 等）の追加。
- `src/TagBasedVideoManager/Db.fs`: `Videos` テーブルへの `FileUuid TEXT` カラム定義・自動マイグレーション追加、`Foreign Keys=True` 有効化、パス変更更新クエリ（`updateVideoLocation` 等）および動画削除クエリ（`deleteVideo`）の追加。
- `src/TagBasedVideoManager/Scanner.fs`: 3フェーズ同期（①パス一致・`FileUuid` バックフィル、②UUID v5 照合による移動・リネーム追従、③不在レコード＆サムネイル削除）および `{THUMBNAIL_DIR}/{Id}.jpg` 基準のサムネイル判定の実装。
- `src/TagBasedVideoManager/HttpHandlers.fs`: `streamVideo` における物理ファイル不在時の HTTP 404 応答。
- `src/TagBasedVideoManager/wwwroot/app.js`: `handleVideoError` における HTTP 404 / その他エラー時のエラートースト表示。
- `test/TagBasedVideoManager.Tests/`: 単体テスト・結合テスト・E2E テストの追加・更新。
