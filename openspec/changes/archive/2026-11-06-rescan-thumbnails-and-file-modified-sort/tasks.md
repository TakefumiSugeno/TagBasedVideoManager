# Tasks

## 1. 不具合再現・先行テスト作成 (Red)

- [x] 1.1 `qa-tester`: サムネイル未作成かつ Duration 取得済み動画がスキャン時に再処理キューへ投入されることを検証する再現テスト（`UT-01`）および実ファイル不在時の再投入テスト（`UT-02`）を `ScannerTests.fs` に作成し、修正前コードで失敗すること（Red）を確認する
- [x] 1.2 `qa-tester`: `THUMBNAIL_DIR` が存在しない状態での初期化・サムネイル生成時にディレクトリが自動作成されることを検証するテスト（`UT-04`, `UT-05`）を `DbTests.fs` / `MediaTests.fs` に作成し、失敗すること（Red）を確認する
- [x] 1.3 `qa-tester`: 新規動画の `FileModifiedAt` 取得（`UT-06`）、既存動画へのバックフィル（`UT-07`）、および旧DBスキーマからの自動マイグレーション（`IT-03`）を検証する先行テストを作成し、失敗すること（Red）を確認する
- [x] 1.4 `qa-tester`: クライアントサイド（`app.js`）における `fileModifiedAt` ソート（昇順・降順、未設定レコードのフォールバック）（`UT-08`, `UT-09`）の検証テストを作成する

## 2. データベースマイグレーションとドメインモデル実装 (Green)

- [x] 2.1 `pg-implementer`: `Domain.fs` の `Video` レコード型に `FileModifiedAt: DateTime` を追加する
- [x] 2.2 `pg-implementer`: `Db.fs` の DDL / `VideoDbDto` に `FileModifiedAt` を追加し、`initializeDatabase` での `ALTER TABLE Videos ADD COLUMN FileModifiedAt TEXT` 自動マイグレーションを実装する
- [x] 2.3 `pg-implementer`: `Db.fs` の `getVideos`, `getVideo`, `searchVideos`, `saveVideo` を更新し、バックフィル用の `updateVideoFileModifiedAt` を追加して DB テストをパス（Green）させる

## 3. サムネイルディレクトリ自動作成 & スキャナー再処理・バックフィル実装 (Green)

- [x] 3.1 `pg-implementer`: `Program.fs` 起動時、`Scanner.fs` スキャン開始時、および `Media.fs` サムネイル生成直前に `Directory.CreateDirectory(thumbDir)` を追加してディレクトリ自動作成テストをパス（Green）させる
- [x] 3.2 `pg-implementer`: `Scanner.fs` の `needsReprocessing` 判定において `v.ThumbnailPath` が `None` の場合は無条件で `thumbMissing = true` とし、不具合再現テスト（`UT-01`, `UT-02`）および既存仕様改定テストをパス（Green）させる
- [x] 3.3 `pg-implementer`: `Scanner.fs` でファイル走査時に `FileInfo.LastWriteTimeUtc` を取得し、新規登録時の保存および既存動画の `FileModifiedAt` バックフィルを実装してテストをパス（Green）させる

## 4. Web UI のファイル更新日時ソート実装 (Green)

- [x] 4.1 `pg-implementer`: `src/TagBasedVideoManager/wwwroot/app.js` の `sortBy` に `'fileModifiedAt'` を追加し、`sortedVideos` での日付パースおよび未設定フォールバックを実装する
- [x] 4.2 `pg-implementer`: `src/TagBasedVideoManager/wwwroot/index.html` のデスクトップツールバー（L469付近）およびモバイル設定シート（L3769付近）の並び替えセレクトボックスに `<option value="fileModifiedAt">更新日時</option>` を追加する

## 5. 結合・E2Eテスト検証と品質ゲート検証

- [x] 5.1 `qa-tester` / `pg-implementer`: 結合テスト（`IT-01`〜`IT-04`）および E2E テスト（`E2E-01`〜`E2E-03`）を実行し、TRX レポート、カバレッジ、エビデンス画像を出力する
- [x] 5.2 全体フォーマット（Prettier, dotnet format）を適用し、コミット前のビルド・テストが 100% 成功することを確認する
- [x] 5.3 `pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply -ChangeName rescan-thumbnails-and-file-modified-sort` を実行して Gate 2 (DoD) を機械的に検証する

## Workflow follow-up

- ユーザー合意後に `/openspec-apply-change` または `/opsx-apply` によりタスクを順次実行する
- 完了後に `/openspec-archive-change` により仕様同期とアーカイブを実施する
