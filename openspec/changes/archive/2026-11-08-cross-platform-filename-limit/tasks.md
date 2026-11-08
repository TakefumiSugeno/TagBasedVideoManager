# Tasks: Linux (NAME_MAX 255B) 及び Windows (MAX_PATH 260字) のクロスプラットフォーム動画ファイル名制限対応

## 1. Domain 型定義スタブ先行整備 & Web 再現テスト (Red)

- [x] 1.1 `pg-implementer`: `TagBasedVideoManager.Renamer/Domain.fs` に `ConstraintViolation` および `ConstraintFilter` 型の定義を追加し、`ScanCandidate` と `RenameProposal` に `FileNameBytes` および `Violation` プロパティを定義して、以降のテストプロジェクトのビルド基盤を整備する。
- [x] 1.2 `qa-tester`: `HttpHandlersTests.fs` に、`streamVideo` 実行時にファイル名が 255B 超の場合の Fail-Fast HTTP 422 JSON 返却、`PathTooLongException` 捕捉時の HTTP 422 返却、および `OperationCanceledException` 時の静かな終了を検証する再現失敗テスト（Red）を作成し、失敗することを確認する。
- [x] 1.3 `qa-tester`: `ScannerTests.fs` に、スキャン対象ファイル名が UTF-8 で 255 バイトを超える場合に DB 登録を維持しつつ `ErrorLogs` に警告ログを記録し、`MediaQueue` への投入をスキップすることを検証する先行テスト（Red）を作成し、失敗することを確認する。

## 2. Web バックエンド & スキャナーの実装 (Green) & 結合テスト

- [x] 2.1 `pg-implementer`: `HttpHandlers.fs` の `streamVideo` 内にファイル名 UTF-8 バイト数の Fail-Fast 検査（> 255B で即時 422 返却）、`PathTooLongException` / `FileNotFoundException` 捕捉、診断情報（VideoId, FilePath, Bytes）付きログ記録、および `OperationCanceledException` ハンドリングを実装して 1.2 のテストを Green にする。
- [x] 2.2 `pg-implementer`: `Scanner.fs` の `syncVideoDirectories` において、ファイル名単体の UTF-8 バイト数が 255 バイトを超えるファイルに対して `Db.saveErrorLog` への警告記録を行い、`MediaQueue.enqueue` への投入をスキップする処理を実装して 1.3 のテストを Green にする。
- [x] 2.3 `qa-tester`: `StreamingTests.fs` に TestHost を用いたストリーミング HTTP 422 結合テスト（`IT-WEB-03`）を追加し、PASS することを確認する。

## 3. Renamer の先行テスト作成 (Red)

- [x] 3.1 `qa-tester`: `FileScannerTests.fs` に、Windows 長パス（文字数 ≧ 240）および Linux 長ファイル名（UTF-8 バイト数 ≧ 240）の二重制約による包括抽出と違反種別判定（`UT-REN-01`〜`UT-REN-07`）を網羅するテストケースを作成し、失敗することを確認する。
- [x] 3.2 `qa-tester`: `FileRenamerTests.fs` に、同名衝突時の連番サフィックス（`_1.mp4`）付与時において UTF-8 で 240 バイトを超えないようベース名を安全にトリミングする再検証テスト（`UT-REN-16`）を作成し、失敗することを確認する。
- [x] 3.3 `qa-tester`: `StateTests.fs` に、表示フィルタ（`FilterAll`, `FilterLinuxOnly`, `FilterWindowsOnly`, `FilterBothOnly`）の切り替えメッセージによるリスト絞り込み状態遷移テストを作成し、失敗することを確認する。
- [x] 3.4 `qa-tester`: `OpenRouterClientTests.fs` に、プロンプト生成時に「UTF-8で240バイト以内、フルパス240文字以内」の指示が含まれていることを検証するテストを作成し、失敗することを確認する。

## 4. Renamer の実装 (Green) & UI 表示

- [x] 4.1 `pg-implementer`: `FileScanner.fs` に UTF-8 バイト数計算および二重制約の包括抽出ロジックを実装し、3.1 のテストを Green にする。
- [x] 4.2 `pg-implementer`: `FileRenamer.fs` に連番サフィックス付与時の 240 バイト再検証・トリミング処理を実装し、3.2 のテストを Green にする。
- [x] 4.3 `pg-implementer`: `OpenRouterClient.fs` の `buildUserPrompt` に UTF-8 240 バイト制限およびフルパス 240 文字制限の指示を追加し、3.4 のテストを Green にする。
- [x] 4.4 `pg-implementer`: `State.fs` に `FilterConstraint` の状態とメッセージハンドリングを実装し、3.3 のテストを Green にする。
- [x] 4.5 `pg-implementer`: `Views.fs` のツールバーに表示フィルタ（ComboBox）を配置し、BEFORE 行の制約種別バッジ（`[🪟 Windows: パス長超過]`、`[🐧 Linux: ファイル名超過]`、`[⚠️ 両方超過]`）、AFTER 行の両立安全評価バッジ（安全/注意/危険）、および候補0件時の Empty State（全件安全案内）を描画する。

## 5. Web UI フロントエンド実装 & E2E テスト

- [x] 5.1 `pg-implementer`: `src/TagBasedVideoManager/wwwroot/app.js` に `<video>` の再生エラーハンドラ（`handleVideoError`）を追加し、ストリーミング URL へ `fetch` して HTTP 422 `FILE_NAME_TOO_LONG` を検知した際にトースト通知およびプレイヤーオーバーレイエラー状態を設定する処理を実装する。
- [x] 5.2 `pg-implementer`: `src/TagBasedVideoManager/wwwroot/index.html` にプレイヤー中央の「Linux 255バイト制限超過のため再生不可」エラーオーバーレイ、該当ファイルパス表示、および「ファイルパスをコピー」ボタンUIを追加する。
- [x] 5.3 `qa-tester`: Playwright E2E テスト（`test/TagBasedVideoManager.Tests/E2E/`）に、255バイト超過動画の再生失敗時にトースト通知、プレイヤーオーバーレイ、およびパスコピー導線が表示されることを検証するシナリオを追加し、実行・確認する。

## 6. 全テスト実行・カバレッジ・ゲート検証

- [x] 6.1 `dotnet test` を全プロジェクトで実行し、すべての単体・結合テストが PASS することを確認する。
- [x] 6.2 C0 80%以上 / 差分 90%以上、C1 80%以上 / 差分 85%以上、C2 100%（デシジョンテーブル）の達成を確認する。
- [x] 6.3 `pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply -ChangeName cross-platform-filename-limit` を実行し、Gate 2 が PASS することを確認する。
