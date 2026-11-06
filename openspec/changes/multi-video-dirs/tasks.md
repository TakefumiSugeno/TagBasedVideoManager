## 1. 設定解析 `VideoDirs.parse` / `fromEnvironment`

- [x] 1.1 [qa-tester] `VideoDirs.parse` および `fromEnvironment` の Red テスト作成・失敗確認（UT-01〜UT-10, UT-12, UT-08a/b、test_plan.md 3.1）
- [x] 1.2 [pg-implementer] `VideoDirs` モジュールの Green 実装・Refactor（純粋 `parse` と環境変数読み込みの分離、`Result` 型、イミュータブル）
- [x] 1.3 [qa-reviewer / pg-reviewer] レビュー・破壊テスト、`reviews.md` 追記、`verify-gate.ps1 -Gate apply`

## 2. Scanner の複数ルート走査・パス正規化・タグ導出

- [ ] 2.1 [qa-tester] パス正規化（大文字小文字照合）、ルート名タグ導出（UT-11/12b/12c, DT-01/02）、および `syncVideoDirectory` の結合 Red テスト（IT-01〜IT-06, IT-05b, IT-06b, IT-09, IT-10, ST-01/02/04）
- [ ] 2.2 [pg-implementer] `Scanner.syncVideoDirectory` の改修（複数ルート走査、ファイル・ディレクトリの物理パス解決、パス正規化による二重登録防止、不在ルートのスキップ＋警告ログ、入れ子ルート対応タグ和集合付与、既存動画への不足タグ安全追記）
- [ ] 2.3 [qa-reviewer / pg-reviewer / se-reviewer] レビュー、`verify-gate.ps1 -Gate apply`

## 3. スキャン API エラー詳細化と UI エラートースト改修

- [ ] 3.1 [qa-tester] API レスポンス（500 エラー時の設定例・移行案内詳細メッセージ）の Red テスト（IT-07, IT-07b, IT-08, ST-03）
- [ ] 3.2 [pg-implementer] `HttpHandlers.triggerScan` を `VideoDirs.fromEnvironment` 読み取りに変更し、エラー時に設定例・移行案内を含む詳細メッセージを返却
- [ ] 3.3 [pg-implementer] `src/TagBasedVideoManager/wwwroot/app.js` を改修し、スキャン失敗時に API から取得した詳細メッセージをトースト内に表示（表示時間 7 秒、`mockup.html` 準拠）
- [ ] 3.4 [qa-reviewer / pg-reviewer / se-reviewer] レビュー、`verify-gate.ps1 -Gate apply`

## 4. デプロイスクリプトと Docker 構成

- [ ] 4.1 [qa-tester] `DeployScriptTests.fs` に Red テスト追加（UT-13〜UT-19, IT-13〜IT-15、早期バリデーション・副作用抑止の検証）
- [ ] 4.2 [pg-implementer] `scripts/common.ps1`（`Resolve-VideoMounts` 実装、WSL パス変換、`-VideoDirs` 対応）、`scripts/deploy.ps1`、`scripts/deploy.sh`（安全な `.env` パーサ実装、既定値・旧引数廃止、イメージビルドおよび既存コンテナ停止前の厳格な入力検証）
- [ ] 4.3 [pg-implementer] `Dockerfile` の `ENV VIDEO_DIR=/app/videos` 削除（`mkdir -p /app/videos` は維持）
- [ ] 4.4 [qa-tester] 実コンテナ利用可能な環境で IT-11 / IT-12 を確認（実行不可環境の場合は引数検証で代替し、その旨を `reviews.md` に記録）
- [ ] 4.5 [se-reviewer / pg-reviewer / qa-reviewer] レビュー（外部プロセス・シェルパースの安全性を重点確認）、`verify-gate.ps1 -Gate apply`

## 5. E2E テストと画面検証

- [ ] 5.1 [qa-tester] Playwright E2E テスト作成（E2E-01 Happy Path, E2E-02 Zero-State エラー詳細トースト表示, E2E-03 復帰 Path, E2E-04 `mockup.html` 整合検証）、PNG キャプチャ出力
- [ ] 5.2 [qa-tester] 既存 E2E / 結合テストの環境変数設定を `VIDEO_DIRS` に更新（テストの期待値は変更せず環境変数名のみ整合）

## 6. ドキュメント・設定の同期

- [ ] 6.1 `README.md` の環境変数表・`.env` 例・移行手順・Docker での `FilePath` 変更警告・同名ルート時の挙動を更新
- [ ] 6.2 `AGENTS.md` の必須環境変数（本体: `VIDEO_DIRS`、Renamer: `VIDEO_DIR`）と `openspec/config.yaml` の context 内記述を同期更新
- [ ] 6.3 `.env` サンプル更新（`VIDEO_DIR`＝Renamer 用、`VIDEO_DIRS`＝本体用の役割明記）

## 7. 最終検証と Gate 2 通過

- [ ] 7.1 全テスト 100% Pass、C0/C1/C2 カバレッジ・TRX・CoverageReport 出力の確認
- [ ] 7.2 Prettier / `dotnet format` 差分 0 の確認
- [ ] 7.3 `pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply -ChangeName multi-video-dirs`
- [ ] 7.4 保留指摘（Minor/Suggestion）の `handover.md` 転記準備
