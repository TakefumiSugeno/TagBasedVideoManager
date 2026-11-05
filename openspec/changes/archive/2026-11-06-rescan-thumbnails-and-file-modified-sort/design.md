# Design: サムネイル再生成およびファイル更新日時ソート機能

## Context

詳細は [proposal.md](proposal.md) を参照。

- **バックエンド**: F# (.NET 10) + Giraffe + SQLite (Dapper + Microsoft.Data.Sqlite)
- **メディア処理**: FFmpeg / ffprobe 外部プロセス呼び出し（1並列セマフォ制御、`MediaQueue`）
- **フロントエンド**: HTML5 + Alpine.js + Tailwind CSS (CDN)、クライアント側インメモリソート

## Goals / Non-Goals

**Goals:**

- サムネイル未作成の動画（既存の1,068件含む）がディレクトリ同期時に漏れなく `MediaQueue` に投入され、自動復旧されること。
- サムネイル出力先ディレクトリ（`THUMBNAIL_DIR`）が存在しない場合でも、起動時および処理前に自動作成され、ファイル生成エラーを防ぐこと。
- 動画メタデータに OS ファイルシステムの最終更新日時（`LastWriteTimeUtc`）を記録し、既存動画に対してもスキャン時に自動バックフィルされること。
- SQLite テーブルへのカラム追加と後方互換性のあるマイグレーションを提供すること。
- Web UI のデスクトップツールバーおよびモバイル設定シートにおいて、「更新日時」による昇順・降順ソートが可能になること。

**Non-Goals:**

- ファイル作成日時（`CreationTimeUtc`）と更新日時の両方を選択可能にする機能。
- Avalonia デスクトップ Renamer アプリへのソート追加。
- 破損動画ファイルに対する無限リトライの完全排除（ただしスキャン実行時に1回キュー投入されるのみで、無限ループにはならない）。

## Decisions

### 1. サムネイル再生成判定ロジックの修正

- **決定**: `Scanner.fs` における `needsReprocessing` の判定で、`v.ThumbnailPath` が `None` の場合は無条件で `thumbMissing = true` とする。
  ```fsharp
  let thumbMissing =
      match v.ThumbnailPath with
      | None -> true
      | Some path -> not (File.Exists(path))
  let durationMissing = v.Duration = 0L
  thumbMissing || durationMissing
  ```
- **代替案と理由**:
  - _代替案A (現状維持 + 別修復コマンド)_: ユーザーが専用の修復ボタンを押すまでゾンビ状態が放置されるため、UXが劣る。
  - _代替案B (リトライ回数カウント)_: テーブルに `RetryCount` カラムの追加が必要となり、過剰に複雑化する。通常のスキャンは手動または定期実行であり、1回のスキャンにつき1回キューに入るだけであるため、単純な `None -> true` で十分安全。

### 2. サムネイルディレクトリの自動作成契機

- **決定**:
  1. `Program.fs` の起動時（静的ファイルプロバイダー登録前）
  2. `Scanner.fs` の `syncVideoDirectory` 開始時
  3. `Media.fs` の `GenerateThumbnail` 実行直前
     の3重ガードで `Directory.CreateDirectory(thumbDir)` を実行する。
- **理由**: ディレクトリ作成は idempotent（冪等）であり、どのルートからサムネイル生成が呼ばれてもディレクトリ不在による書き込みエラーを100%防止できる。

### 3. 日時の選定: `LastWriteTimeUtc` (更新日時) の採用

- **決定**: OS ファイルシステムの日時として `FileInfo.LastWriteTimeUtc` を採用し、`Domain.Video` の `FileModifiedAt` に格納する。
- **理由**:
  Windows や多くのファイルシステムにおいて、動画ファイルを別のドライブやストレージからコピー・移動した場合、`CreationTime` は「コピーされた現在時刻」にリセットされるが、`LastWriteTime` は「撮影・レンダリング完了時の日時」を保持する。ユーザーが過去動画を時系列でソートしたいニーズには `LastWriteTime` が最も合致する。

### 4. データベースマイグレーション方式

- **決定**: `Db.initializeDatabase` において、`PRAGMA table_info(Videos)` を照会し、`FileModifiedAt` カラムが存在しない場合に `ALTER TABLE Videos ADD COLUMN FileModifiedAt TEXT` を発行する。
- **データマッピング**:
  - `VideoDbDto`: `FileModifiedAt: string` を追加。
  - DTO からドメインモデルへの変換時、`dto.FileModifiedAt` が null または空の場合は `dto.CreatedAt` をフォールバックとしてパースする（既存レコードでスキャン前の未バックフィル状態でも例外を防止）。
  - `saveVideo`: INSERT INTO Videos (..., FileModifiedAt) VALUES (..., @FileModifiedAt)。
  - `updateVideoFileModifiedAt`: バックフィル用に `UPDATE Videos SET FileModifiedAt = @FileModifiedAt WHERE Id = @Id` を追加。

### 5. スキャン時のバックフィル戦略

- **決定**: `Scanner.fs` の既存動画走査時、ファイルシステムから取得した `fileModifiedAt` と DB 内の `v.FileModifiedAt` を比較し、差分がある場合（または未設定だった場合）に `Db.updateVideoFileModifiedAt` を呼び出す。
- **理由**: 別途の移行バッチを用意せずとも、ユーザーがいつもの「ディレクトリ同期」を実行するだけで、数千件の動画の `FileModifiedAt` が自動的に実ファイル日時に更新される。

### 6. Web UI ソート実装

- **決定**:
  - `app.js` の `sortBy`: `'fileModifiedAt'` を追加。
  - `sortedVideos` において `sortBy === 'fileModifiedAt'` の場合も `new Date(val).getTime()` による数値比較を実施。
  - `index.html`:
    - デスクトップ用セレクトボックス: `<option value="fileModifiedAt">更新日時</option>`
    - モバイル用設定シートセレクトボックス: `<option value="fileModifiedAt">更新日時</option>`

## Risks / Trade-offs

- **[Risk] 破損動画が存在する場合、毎スキャン時に ffmpeg が失敗処理を繰り返す**
  - **Mitigation**: スキャンはユーザー主導またはインターバル実行であり、FFmpeg プロセスはセマフォ（最大並列数 1）で制限されているため、システム全体の CPU を占有してクラッシュさせることはない。また、ffprobe 失敗時の既定値フォールバック（120秒）により Duration 側は保護されている。
- **[Risk] 大量動画（数千件）に対するスキャン時のバックフィルによる初回スキャン時間の増加**
  - **Mitigation**: 更新日時が同一であれば DB 更新をスキップ（no-op）するため、2回目以降のスキャンオーバーヘッドはゼロ。初回バックフィル時もファイル情報読み取りと軽量な UPDATE のみであるため高速。

## Migration Plan

1. アプリケーション起動時に `Db.initializeDatabase` が走り、カラムが自動作成される。
2. ディレクトリ同期を実行すると、バックフィルとサムネイル未作成動画のキュー投入が自動完了する。
3. ロールバック時は、新カラムが NULL 許容であるため旧アプリケーションバイナリをそのまま実行可能。
