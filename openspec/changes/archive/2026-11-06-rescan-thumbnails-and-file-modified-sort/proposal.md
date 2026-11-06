# Proposal: サムネイル未作成動画の再処理スキップ解消およびファイル更新日時ソート条件の追加

- **規模レベル**: L3 (大規模: DBスキーマ変更・マイグレーション・仕様変更)
- **変更種別**: `fix` (不具合修正) + `feat` (機能改善)
- **関連Issue**:
  - Issue #14: [不具合]サムネイル未作成かつDuration取得済みの動画がディレクトリ同期時にスキップされる不具合
  - Issue #15: [機能改善]登録日だけでなくOS側の動画ファイルの作成日時をソート条件に追加する

## Why

### 1. サムネイル未作成動画が永久スキップされる不具合 (Issue #14)

- **根本原因 (Root Cause)**:
  `src/TagBasedVideoManager/Scanner.fs` において、既存動画の再処理判定 `needsReprocessing` が `v.ThumbnailPath` が `None` の場合に `v.Duration = 0L` を評価していたため、一度再生時間が取得された動画（`Duration > 0`）は `ThumbnailPath` が存在しない（未生成）でも `thumbMissing = false` と判定され、二度とサムネイル生成キューに投入されませんでした。また、初回起動時等に環境変数 `THUMBNAIL_DIR` のディレクトリが自動作成されていなかったため、ffmpeg による出力ファイル書き込みエラーが発生し、本件環境では 6,062 件中 1,068 件もの動画が「Duration取得済み・サムネイル未生成」のゾンビ状態となり、再同期でも復旧できない状態となっていました。
- **解決の動機**:
  ディレクトリ自動生成を担保した上で再処理判定ロジックを修正し、同期実行によって未生成サムネイルが全件正常に再生成・復旧されるようにします。

### 2. ファイル更新日時のソート条件追加 (Issue #15)

- **現状の課題**:
  動画メタデータにはシステム登録日時（`CreatedAt`）しか保持されておらず、過去に撮影・作成された大量の動画を一括インポートした場合、すべての動画が同一の登録日時となり、過去動画を時系列順に並び替えることができません。
- **解決の動機**:
  ファイルシステムから動画の最終更新日時（`LastWriteTimeUtc`）を取得して DB に保持し、Web UI（デスクトップ・モバイル）で「更新日時」による昇順・降順ソートを可能にします。既存動画に対してもスキャン時に自動バックフィルを実行し、過去データもシームレスに時系列ソートできるようにします。

## What Changes

- **サムネイルディレクトリ自動作成**:
  - アプリケーション起動時（`Program.fs`）およびスキャン開始時（`Scanner.fs`）／サムネイル生成直前（`Media.fs`）に、サムネイルディレクトリが存在しない場合は `Directory.CreateDirectory` を確実に呼び出すようにします。
- **サムネイル未作成動画の再処理判定修正**:
  - `Scanner.fs` の判定において、`v.ThumbnailPath` が `None` の場合は無条件で `thumbMissing = true` とし、メディア処理キューへ再投入します。
- **動画メタデータへのファイル更新日時の追加**:
  - `Domain.Video` レコードおよび `Videos` テーブルに `FileModifiedAt`（TEXT / ISO8601形式）を追加します。
  - スキャン時（`Scanner.fs`）に `FileInfo.LastWriteTimeUtc` を取得して保存します。
- **既存動画データの自動バックフィル**:
  - ディレクトリ同期実行時、既存動画レコードの `FileModifiedAt` が未設定または実ファイルと乖離している場合、DB を自動更新します。
- **SQLite 起動時マイグレーション**:
  - `Db.initializeDatabase` で `Videos` テーブルに `FileModifiedAt` カラムが存在しない場合に `ALTER TABLE Videos ADD COLUMN FileModifiedAt TEXT` を実行します。
- **Web UI のソート項目追加**:
  - `app.js` のソート対象に `fileModifiedAt` を追加し、日付比較による昇順・降順ソートを実装します。
  - `index.html` のデスクトップツールバーおよびモバイル用表示設定シートの並び替えセレクトボックスに「更新日時」を追加します。

## Capabilities

### New Capabilities

（新規 capability はありません）

### Modified Capabilities

- `video-scanner`:
  - サムネイル未作成（ThumbnailPath が None または実ファイル不在）の既存動画をディレクトリ同期時に再処理キューへ再投入するよう仕様を改定（従来の「Duration>0かつThumbnailPath=Noneの除外」仕様を廃止）。
  - サムネイル出力先ディレクトリの自動作成を保証する要件を追加。
  - ファイル更新日時（`LastWriteTimeUtc`）の取得・保存および既存レコードへのバックフィル処理の要件を追加。
- `video-library`:
  - 動画一覧・検索APIおよびUI表示におけるソート条件として「ファイル更新日時（`fileModifiedAt`）」を追加。

## Non-goals（やらないこと）

- ファイル作成日時（`CreationTimeUtc`）と更新日時の両方を選択可能にする機能（ファイルコピー等で更新日時が最も撮影・作成時を忠実に保持しているため、今回は更新日時に一本化します）。
- 動画内部の Exif / メタデータタグ（QuickTime creation_time 等）のディープ解析（ffprobe によるオーバーヘッドを避けるため、OS ファイルシステムの `LastWriteTimeUtc` を採用します）。
- デスクトップ Renamer アプリ（Avalonia）へのソート項目追加（Renamer はファイル名変更特化ツールであり、本改修は Web 管理アプリを対象とします）。

## Impact

- **データベース**:
  - `Videos` テーブルに `FileModifiedAt TEXT` カラムが追加されます。
  - 既存データに対しては `CreatedAt` をフォールバック値として読み込み、スキャン実行時に実ファイルの更新日時で順次上書きバックフィルされます。
- **API**:
  - `GET /api/videos` および `POST /api/search` のレスポンス JSON の各動画オブジェクトに `fileModifiedAt` フィールド（ISO 8601 文字列）が含まれるようになります。後方互換性あり。
- **UI**:
  - Web UI の並び替えドロップダウンに「更新日時」が追加されます。
- **パフォーマンス**:
  - 既存の 1,068 件のサムネイル未作成動画に対して ffmpeg の再処理が走ります。セマフォによる同時実行制御（1並列）により CPU やプロセスの枯渇は防止されます。

## 移行計画とロールバック手順 (Migration & Rollback Plan)

### 移行計画 (Migration)

1. アプリケーション起動時に `Db.initializeDatabase` が実行され、`Videos` テーブルに `FileModifiedAt` カラムが自動追加されます。
2. Web UI または API からディレクトリ同期（`POST /api/scan`）を実行すると、以下が自動的に進行します：
   - 既存の全動画ファイルの実ファイル更新日時が DB の `FileModifiedAt` にバックフィルされます。
   - サムネイル未作成だった 1,068 件の動画が `MediaQueue` に投入され、バックグラウンドで順次サムネイルが自動生成されます。

### ロールバック手順 (Rollback)

- SQLite において追加された `FileModifiedAt` カラムは NULL 許容であるため、旧バージョンのバイナリに戻した場合でも旧コード（このカラムを参照しないコード）はそのまま正常動作します。
- DB スキーマを完全に元に戻す必要がある場合は、バックアップ DB からの復元、または新カラムを無視したテーブル再作成を行います。
