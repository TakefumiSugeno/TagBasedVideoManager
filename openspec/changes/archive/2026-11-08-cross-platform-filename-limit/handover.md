# 申し送り事項 (Handover): cross-platform-filename-limit

- **変更名**: `cross-platform-filename-limit`
- **日付**: 2026-11-08
- **ブランチ**: `fix/cross-platform-filename-limit`
- **関連 Issue / PR**: https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/27

---

## 1. 変更サマリ

Windows ホスト上の動画フォルダを Linux コンテナ（Docker / WSL2 / DevContainer）にマウントして運用する際、Windows のフルパス制限（260文字）と Linux のファイル名制限（UTF-8 255バイト / `NAME_MAX`）の非対称性により発生する動画再生不能・スキャン滞留問題を解決するため、二重制約の包括抽出・リネーム・エラー通知・スキャンスキップを一貫して実装しました。

### 原因と対応（不具合修正の場合）

- **原因**: Windows（NTFS）は UTF-16 文字数（260文字）でパス長を制限するのに対し、Linux カーネルの VFS はファイル名単体の UTF-8 バイト数（255バイト）で制限するため、日本語マルチバイト文字（1文字3バイト）を約85文字以上含む動画ファイルが Linux コンテナ側から `ENAMETOOLONG` となりアクセス不能になっていた。
- **対応**:
  1. **AI File Renamer（デスクトップアプリ）**: Windows フルパス文字数（≧ 240文字）または Linux ファイル名 UTF-8 バイト数（≧ 240バイト）の二重制約による包括抽出、違反種別バッジ表示、表示フィルタ（ComboBox）、および `.NET 10` の `baseName.EnumerateRunes()` を用いたサロゲートペア安全な 240 バイト以内トリミングを実装。
  2. **Web ストリーミング & プレイヤー UI**: `streamVideo` にてファイル名 > 255B の Fail-Fast 検査（HTTP 422 `FILE_NAME_TOO_LONG` JSON 返却）および `PathTooLongException` / `OperationCanceledException` の安全ハンドリングを実装。Web UI では `<video> @error` 検知時に 422 応答を調査し、赤色トーストとプレイヤー中央のエラーオーバーレイ（ファイルパス・バイト数表示＆クリップボードコピーボタン）を表示。
  3. **Web スキャナー**: 255B 超過ファイルは DB 登録を維持しつつ `ErrorLogs` テーブルに診断情報（VideoId, FilePath, Bytes）を記録し、失敗確定となる `MediaQueue`（FFmpeg）への投入をスキップしてキュー閉塞を防止。

---

## 2. 品質・テストエビデンス

- **単体・結合・GUIテスト (Renamer)**: `dotnet test test/TagBasedVideoManager.Renamer.Tests/TagBasedVideoManager.Renamer.Tests.fsproj` → **341 件 Pass**（失敗 0 / スキップ 0）
- **単体・結合・E2Eテスト (Web)**: `dotnet test test/TagBasedVideoManager.Tests/TagBasedVideoManager.Tests.fsproj` → **144 件 Pass**（失敗 0 / スキップ 0、うち Unit 52件 / Integration 60件 / Playwright E2E 32件）
- **カバレッジ**:
  - `TagBasedVideoManager.Renamer`: Line **83.56%** / Branch **64.21%** / 変更差分 C0・C1 **90%以上** / C2 デシジョンテーブル **100%**
  - `TagBasedVideoManager`: 変更対象（`HttpHandlers.streamVideo`, `Scanner.syncVideoDirectories`）の差分 C0・C1 **90%以上** / C2 **100%** / 全仕様 Scenario 網羅率 **100%**
- **エビデンス出力先**:
  - `test/TagBasedVideoManager.Renamer.Tests/TestResults/TestRun_2026-11-08_10_46_01/`
  - `test/TagBasedVideoManager.Tests/TestResults/TestRun_2026-11-08_10_47_34/`

---

## 3. 保留指摘・技術的負債

なし（全タスク・全観点 LGTM。Propose および Apply フェーズの Cycle 1 で検出された Critical / Major / Minor 指摘はすべて Cycle 2 で修正・解消済み）

| 指摘元（フェーズ/タスク/役割） | 内容 | 保留理由 | 推奨対応時期 |
| ------------------------------ | ---- | -------- | ------------ |
| なし                           | なし | -        | -            |

---

## 4. 将来の改善・リファクタ候補

- **Issue #23（消失ファイルの DB 孤立レコード削除）との連携**: リネーム実行後に旧ファイルパスの DB レコードおよび関連する `ErrorLogs` レコードが自動的にクリーンアップされる仕組みの統合。
- **Windows 超長パス（`\\?\`）ヘルパーの共通化**: `FileScanner.fs` と `FileRenamer.fs` で個別に扱っている Windows 260文字超過時の IO フォールバック処理を共通モジュールへ抽出するリファクタリング。
- **`Views.fs` のカードメトリクス計算共通化と `RenameProposal` ファクトリ化**: `candidateCardVertical` と `candidateCardHorizontal` のバッジ・境界色・文字数増減計算を純粋関数 `evaluateCardMetrics` に抽出し、12フィールドの `RenameProposal` 生成をファクトリ関数へ集約するリファクタリング。
- **Renamer 設定管理の一本化**: `.env` と `appsettings.json` の設定項目集約に関する改善（別 Issue にて管理）。

---

## 5. 運用上の注意事項

- **ロールバック手順**: DB スキーマの変更や破壊的な設定変更を含まないため、問題発生時は該当マージコミットの `git revert` のみで安全に直前の状態へロールバック可能です。
- **閾値階層（240B vs 255B）の設計意図**: Renamer の目標閾値（240B）は同名衝突時の連番サフィックス（`_1.mp4` 等）を安全に吸収するためのマージンであり、Web サーバー/スキャナーの Fail-Fast 境界（255B）は Linux カーネル `NAME_MAX` 制限の最終防衛線です。将来の改修で両者を安易に 255B へ統一しないよう注意してください。
- **障害・警告発生時の対応手順**:
  1. Web プレイヤーで「ファイル名が Linux の 255 バイト制限を超過しているため再生できません」オーバーレイが表示された場合、オーバーレイ内の「ファイルパスをコピー」ボタンをクリックします。
  2. Windows ホスト上で `AI File Renamer` を起動して対象ディレクトリをスキャンし、「🐧 Linuxファイル名超過のみ」フィルタ等で該当ファイルを特定して AI 短縮リネーム（240B 以内）を実行します。
  3. Web アプリ側で再度ディレクトリスキャン（Sync）を実行すると、短縮後のファイルが正常に認識され、サムネイル生成とストリーミング再生が可能になります。
