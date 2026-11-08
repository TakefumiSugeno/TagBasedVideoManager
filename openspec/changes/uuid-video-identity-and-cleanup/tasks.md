# Tasks: uuid-video-identity-and-cleanup

## 1. 決定論的UUID（UUID v5）算出・DBスキーマ拡張・外部キーCASCADE有効化

- [x] 1.1 [`qa-tester`] 根本原因（Root Cause）の特定確認と、`Domain.FileIdentity.computeFileUuid` の境界値テスト（`UT-01`〜`UT-06`）および `Db` の `FileUuid` カラム永続化・`updateVideoLocation`・`deleteVideo`（外部キー `ON DELETE CASCADE` による `VideoTags` 連動削除 `IT-05`）を検証する先行失敗テスト（Red）を `test/TagBasedVideoManager.Tests/Unit/DomainTests.fs` および `DbTests.fs` に作成し、失敗を確認する
- [x] 1.2 [`pg-implementer`] `src/TagBasedVideoManager/Domain.fs` に `Video.FileUuid: string option` および `FileIdentity.computeFileUuid`（Read-Only サンプリングによる RFC 9562 UUID v5 算出）を実装し、`src/TagBasedVideoManager/Db.fs` に `Videos.FileUuid` カラム（DDL・自動マイグレーション）、`Foreign Keys=True` 接続設定、`updateVideoFileUuid`・`updateVideoLocation`・`deleteVideo` を実装してテストを Green にする（既存コンパイル箇所の `Video` レコード初期化も合わせて更新）
- [x] 1.3 [`pg-reviewer` / `qa-reviewer`] タスク 1 の実装コード・テストコードに対する批判的レビュー（破壊テスト実行・境界値検証）を実施し、`reviews.md` に記録して `verify-gate.ps1 -Gate apply` を通過させる

## 2. スキャン3フェーズ同期（UUID移動・リネーム・マウント変更追従と不在データ自動削除）

- [ ] 2.1 [`qa-tester`] `Scanner.syncVideoDirectories` における UUID v5 移動・リネーム・マウント変更追従（`UT-07`〜`UT-09`, `DT-01`〜`DT-05`, `IT-01`〜`IT-04`）、`{THUMBNAIL_DIR}/{Id}.jpg` 基準のサムネイル存在確認と再処理スキップ（`UT-13`〜`UT-17`, `DT-06`〜`DT-10`）、および削除済み動画レコード・タグ紐付け・孤児サムネイル画像の自動削除（`UT-10`〜`UT-12`, `IT-05`〜`IT-07`）を検証する先行失敗テスト（Red）を `test/TagBasedVideoManager.Tests/Unit/ScannerTests.fs` に作成し、失敗を確認する
- [ ] 2.2 [`pg-implementer`] `src/TagBasedVideoManager/Scanner.fs` の `syncVideoDirectories` に 3フェーズ同期（①正規化パス一致＆`FileUuid` バックフィル＆`{thumbDir}/{Id}.jpg` 解決、②UUID v5 および旧レコードフォールバックによる移動・リネーム追従とフォルダタグ追加、③不在 DB レコード・孤児サムネイル削除）を実装し、全テストを Green にする
- [ ] 2.3 [`pg-reviewer` / `qa-reviewer` / `se-reviewer`] タスク 2 の実装コード・テストコードに対する批判的レビュー（破壊テスト実行・故障シミュレーション）を実施し、`reviews.md` に記録して `verify-gate.ps1 -Gate apply` を通過させる

## 3. ストリーミング物理ファイル不在時の HTTP 404 応答とフロントエンドエラートースト通知

- [ ] 3.1 [`qa-tester`] 物理ファイル不在の動画に対する `GET /api/videos/{id}/stream` が HTTP 500 にならず HTTP 404 と `FILE_NOT_FOUND` JSON を返す結合テスト（`UT-18`, `IT-09`）を `test/TagBasedVideoManager.Tests/Integration/StreamingTests.fs` に、再生試行時のエラートースト表示と再スキャン後のカード消去を検証する E2E テスト（`E2E-01`〜`E2E-04`）を `test/TagBasedVideoManager.E2E/` に先行作成（Red）し、失敗を確認する
- [ ] 3.2 [`pg-implementer`] `src/TagBasedVideoManager/HttpHandlers.fs` の `streamVideo` に物理ファイル不在時の HTTP 404 応答を実装し、`mockup.html` に準拠して `src/TagBasedVideoManager/wwwroot/app.js` の `handleVideoError` および `src/TagBasedVideoManager/wwwroot/index.html` に HTTP 404 エラートースト通知とプレイヤーエラーオーバーレイ（スキャン同期実行導線付き）を実装して結合テスト・E2E テストを Green にする
- [ ] 3.3 [`pg-reviewer` / `qa-reviewer`] タスク 3 の実装コード・テストコードに対する批判的レビューを実施し、`reviews.md` に記録して `verify-gate.ps1 -Gate apply` を通過させる

## 4. 全テスト・C0/C1/C2カバレッジ・エビデンス確認とゲート検証

- [ ] 4.1 自動フォーマット（`dotnet format whitespace` / `npx prettier`）および静的解析を実行し、全単体・結合・E2E テストを実行して TRX レポート・HTML カバレッジレポート（C0 80%+/差分90%+、C1 80%+/差分85%+、C2 100%）・E2E 画面キャプチャ（PNG）を `test/TestResults/` に出力・確認する
- [ ] 4.2 `openspec validate "uuid-video-identity-and-cleanup"` および `pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply -ChangeName "uuid-video-identity-and-cleanup"` を実行し、全チェックが PASS することを確認する
