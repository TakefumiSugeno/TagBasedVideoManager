# テスト計画・テストケース定義書 (Test Plan): cross-platform-filename-limit

- **変更名**: `cross-platform-filename-limit`
- **適用規模レベル**: `L2`
- **変更種別**: `fix` / `feat`

---

## 1. カバレッジ目標と完了基準

| 指標                          | 定義                                                          | 目標値（全体 / 新規・変更差分） | 適用対象・備考                                  |
| :---------------------------- | :------------------------------------------------------------ | :------------------------------ | :---------------------------------------------- |
| **C0 (命令網羅 / Line)**      | 実行可能な全ステートメントの網羅率                            | **80%以上** / **90%以上**       | 全対象モジュール                                |
| **C1 (分岐網羅 / Branch)**    | `if/else`, `match`, ガード節, 例外経路の真偽両方の網羅率      | **80%以上** / **85%以上**       | 全対象モジュール                                |
| **C2 (条件網羅 / Condition)** | 複合条件式（`A && B \|\| C`）の各基本条件の真偽組み合わせ網羅 | **100%**（対象ロジック）        | 下記「4. デシジョンテーブル」記載の判定ロジック |
| **Scenario 網羅率**           | `specs/**/spec.md` の全 `#### Scenario:` に対するテスト対応率 | **100%**                        | 下記「2. トレーサビリティマトリクス」で対応付け |

- **カバレッジ除外対象**: 自動生成コード、Avalonia App/Program の定型起動ボイラープレート。
- **モック境界方針**:
  - **モック化する外部境界**: OpenRouter API 通信（`HttpClient`）、`PhysicalFileResultExecutor`（例外注入シミュレータ）。
  - **実物を使用する範囲**: SQLite データベース（インメモリ / 一時ファイル）、ファイル名バイト数計算、ドメイン型、Elmish State 更新、Views レンダリング。

---

## 2. シナリオ・トレーサビリティマトリクス

| Spec Capability / Requirement                                    | Scenario 名                                                           | Unit | Integration | E2E | 対応テストケースID        |
| :--------------------------------------------------------------- | :-------------------------------------------------------------------- | :--: | :---------: | :-: | :------------------------ |
| `companion-file-rename` / マウント不可ファイルの自動抽出         | 初期値による自動抽出と起動中の一時閾値変更                            |  ✅  |      -      | ✅  | `UT-REN-01`, `E2E-REN-01` |
| `companion-file-rename` / マウント不可ファイルの自動抽出         | Windows長パスファイルとLinux長ファイル名ファイルの包括抽出            |  ✅  |      -      | ✅  | `UT-REN-02`, `E2E-REN-01` |
| `companion-file-rename` / マウント不可ファイルの自動抽出         | 両方の基準を満たす安全なファイルの除外                                |  ✅  |      -      |  -  | `UT-REN-03`               |
| `companion-file-rename` / OpenRouter Freeモデル提案              | 風変わりなファイル名に対するAIコメント表示                            |  ✅  |      -      |  -  | `UT-REN-04`               |
| `companion-file-rename` / OpenRouter Freeモデル提案              | 通常短縮時およびWeb検索補完時のAIコメント常時表示                     |  ✅  |      -      |  -  | `UT-REN-05`               |
| `companion-file-rename` / OpenRouter Freeモデル提案              | UTF-8バイト数とフルパス文字数の両立短縮プロンプト送信                 |  ✅  |      -      |  -  | `UT-REN-06`               |
| `companion-file-rename` / OpenRouter Freeモデル提案              | AI提案成功時の両制約遵守とAIコメント生成                              |  ✅  |      -      | ✅  | `UT-REN-07`, `E2E-REN-01` |
| `companion-file-rename` / Before/After対比プレビュー             | 上下並びでの素早い目視確認と手動修正                                  |  ✅  |      -      | ✅  | `UT-REN-08`, `E2E-REN-01` |
| `companion-file-rename` / Before/After対比プレビュー             | 3行常時表示による素早い目視確認と手動修正                             |  ✅  |      -      | ✅  | `UT-REN-09`, `E2E-REN-01` |
| `companion-file-rename` / Before/After対比プレビュー             | 両制約クリア時の安全バッジ表示                                        |  ✅  |      -      | ✅  | `UT-REN-10`, `E2E-REN-01` |
| `companion-file-rename` / Before/After対比プレビュー             | 手動編集によるLinuxバイト数超過時の即時警告表示                       |  ✅  |      -      | ✅  | `UT-REN-11`, `E2E-REN-01` |
| `companion-file-rename` / 重複回避と安全な物理リネーム実行       | 一括リネームの安全な実行                                              |  ✅  |      -      |  -  | `UT-REN-15`               |
| `companion-file-rename` / 重複回避と安全な物理リネーム実行       | 同名衝突時の連番サフィックス付加とバイト数再超過防止                  |  ✅  |      -      |  -  | `UT-REN-16`               |
| `companion-file-rename` / 制約違反理由の可視化と表示フィルタ     | 制約種別バッジの正確な表示                                            |  ✅  |      -      | ✅  | `UT-REN-12`, `E2E-REN-01` |
| `companion-file-rename` / 制約違反理由の可視化と表示フィルタ     | 表示フィルタによるLinux超過ファイルの絞り込み                         |  ✅  |      -      | ✅  | `UT-REN-13`, `E2E-REN-01` |
| `companion-file-rename` / 制約違反理由の可視化と表示フィルタ     | 表示フィルタを「すべて」に戻した際の全件再表示                        |  ✅  |      -      | ✅  | `UT-REN-14`, `E2E-REN-01` |
| `companion-file-rename` / 制約違反理由の可視化と表示フィルタ     | 候補0件時のEmpty State案内表示                                        |  ✅  |      -      | ✅  | `UT-REN-17`, `E2E-REN-01` |
| `video-library` / HTTP Range Requests                            | シーク再生時の部分コンテンツ要求                                      |  -   |     ✅      | ✅  | `IT-WEB-01`, `E2E-WEB-01` |
| `video-library` / HTTP Range Requests                            | 実ファイル消失時の 404 エラー応答                                     |  -   |     ✅      |  -  | `IT-WEB-02`               |
| `video-library` / HTTP Range Requests                            | Linuxファイル名長制限超過時のFail-Fast HTTP 422と専用エラーコード返却 |  ✅  |     ✅      |  -  | `UT-WEB-01`, `IT-WEB-03`  |
| `video-library` / HTTP Range Requests                            | クライアント切断時の正常終了                                          |  ✅  |      -      |  -  | `UT-WEB-04`               |
| `video-library` / ストリーミングエラー時のフロントエンド詳細通知 | FILE_NAME_TOO_LONG発生時のトースト通知とプレイヤーオーバーレイ表示    |  -   |      -      | ✅  | `E2E-WEB-02`              |
| `video-library` / ストリーミングエラー時のフロントエンド詳細通知 | 通常のエラー発生時のトースト通知                                      |  -   |      -      | ✅  | `E2E-WEB-03`              |
| `video-scanner` / Linux NAME_MAX 超過ファイルの事前検知          | 255バイト超過ファイルのDB登録と警告ログ出力                           |  ✅  |     ✅      |  -  | `UT-SCN-01`, `IT-SCN-01`  |
| `video-scanner` / Linux NAME_MAX 超過ファイルの事前検知          | 255バイト以内の正常ファイルのスキャン                                 |  ✅  |     ✅      |  -  | `UT-SCN-02`, `IT-SCN-02`  |

---

## 3. 単体テスト：同値分割・境界値テストケース定義

### 3.1 Renamer 抽出判定・バイト数計算 (`FileScanner.fs` / `FileRenamer.fs`)

| ケースID    | 検証観点（区分）                           | パス文字数 / ファイル名UTF-8バイト数 | 期待される抽出判定と違反種別                            |
| :---------- | :----------------------------------------- | :----------------------------------- | :------------------------------------------------------ |
| `UT-REN-01` | 境界値（フルパス 239字 / ファイル名 239B） | 239字 / 239B                         | 抽出対象外（両方安全基準内）                            |
| `UT-REN-02` | 境界値（フルパス 240字 / ファイル名 100B） | 240字 / 100B                         | 抽出対象（`WindowsPathOnly`）                           |
| `UT-REN-03` | 境界値（フルパス 100字 / ファイル名 240B） | 100字 / 240B                         | 抽出対象（`LinuxFileNameOnly`）                         |
| `UT-REN-04` | 境界値（フルパス 240字 / ファイル名 240B） | 240字 / 240B                         | 抽出対象（`BothViolated`）                              |
| `UT-REN-05` | Linux上限境界値（ファイル名 255B）         | 120字 / 255B                         | 抽出対象（`LinuxFileNameOnly`、注意域）                 |
| `UT-REN-06` | Linux上限超過境界値（ファイル名 256B）     | 120字 / 256B                         | 抽出対象（`LinuxFileNameOnly`、危険域）                 |
| `UT-REN-07` | 空文字・極小値                             | 1字 / 1B                             | 抽出対象外                                              |
| `UT-REN-16` | 同名衝突時の連番付加（240B境界）           | 元名 238B + `_1.mp4`                 | 240B を超えないようベース名を安全にトリミングして一意化 |
| `UT-REN-17` | 候補0件時のEmpty State描画                 | 候補リスト空                         | `EmptyState` メッセージと安心案内を描画                 |

### 3.2 Web サーバー `streamVideo` (`HttpHandlers.fs`)

| ケースID    | 検証観点（区分）                     | 状態 / 発生例外                     | 期待されるレスポンス                                        |
| :---------- | :----------------------------------- | :---------------------------------- | :---------------------------------------------------------- |
| `UT-WEB-01` | Fail-Fast バイト数検査 (> 255B)      | ファイル名 256B                     | 実行前に即座に HTTP 422 + `code: "FILE_NAME_TOO_LONG"` JSON |
| `UT-WEB-02` | 通常のストリーミング実行             | ファイル名 200B / 正常              | HTTP 206 または 正常ストリーミング                          |
| `UT-WEB-03` | `PathTooLongException` 捕捉          | `PathTooLongException`              | HTTP 422 + `code: "FILE_NAME_TOO_LONG"` JSON                |
| `UT-WEB-04` | クライアント切断                     | `OperationCanceledException`        | 例外ログなしで静かにハンドラー正常終了                      |
| `UT-WEB-05` | 255B超起因の `FileNotFoundException` | `FileNotFoundException` (Bytes>255) | HTTP 422 + `code: "FILE_NAME_TOO_LONG"` JSON                |

### 3.3 Web スキャナー事前検知 & MediaQueue制御 (`Scanner.fs`)

| ケースID    | 検証観点（区分）    | ファイル名 UTF-8 バイト数 | 期待される動作                                                           |
| :---------- | :------------------ | :------------------------ | :----------------------------------------------------------------------- |
| `UT-SCN-01` | 境界値 (255 バイト) | 255 バイト                | DB 登録完了、警告ログなし、`MediaQueue` へ通常投入                       |
| `UT-SCN-02` | 境界値 (256 バイト) | 256 バイト                | DB 登録完了、`ErrorLogs` に警告ログ記録、`MediaQueue` への投入をスキップ |

---

## 4. デシジョンテーブル（C2 複合条件網羅 100%）

### 4.1 Renamer 抽出判定ロジック

条件式: `isVideoFile && (file.Length >= 240 || utf8Bytes >= 240) && (not filterNonJapanese || not hasJapanese)`

| 規則 | 動画拡張子 | パス長 ≧ 240 (A) | バイト数 ≧ 240 (B) | 日本語除外設定 (C) | 日本語含む (D) | 抽出判定結果 | 違反種別判定               |
| :--: | :--------: | :--------------: | :----------------: | :----------------: | :------------: | :----------: | :------------------------- |
|  1   |     F      |        -         |         -          |         -          |       -        |   **除外**   | -                          |
|  2   |     T      |        F         |         F          |         F          |       -        |   **除外**   | -                          |
|  3   |     T      |        T         |         F          |         F          |       -        |   **抽出**   | `WindowsPathOnly`          |
|  4   |     T      |        F         |         T          |         F          |       -        |   **抽出**   | `LinuxFileNameOnly`        |
|  5   |     T      |        T         |         T          |         F          |       -        |   **抽出**   | `BothViolated`             |
|  6   |     T      |        T         |         F          |         T          |       T        |   **除外**   | - (日本語除外フィルタ有効) |
|  7   |     T      |        T         |         F          |         T          |       F        |   **抽出**   | `WindowsPathOnly`          |
|  8   |     T      |        F         |         T          |         T          |       T        |   **除外**   | - (日本語除外フィルタ有効) |
|  9   |     T      |        F         |         T          |         T          |       F        |   **抽出**   | `LinuxFileNameOnly`        |

### 4.2 AFTER バッジ評価ロジック

判定基準: パス長閾値 (240字) とバイト数階層 (240B, 255B)

| 規則 | フルパス < 240 | ファイル名バイト数 | バッジ表示                             | 背景・境界色 |
| :--: | :------------: | :----------------- | :------------------------------------- | :----------- |
|  1   |       T        | ≦ 240B             | `AFTER (〇〇字 / 〇〇B [安全])`        | 緑色（安全） |
|  2   |       T        | 241〜255B          | `AFTER (〇〇字 / 〇〇B [Linux注意])`   | 黄色（注意） |
|  3   |       T        | ≧ 256B             | `AFTER (〇〇字 / 〇〇B [Linux危険])`   | 赤色（危険） |
|  4   |       F        | ≦ 240B             | `AFTER (〇〇字 / 〇〇B [Windows危険])` | 赤色（危険） |
|  5   |       F        | ≧ 256B             | `AFTER (〇〇字 / 〇〇B [両方危険])`    | 赤色（危険） |

---

## 5. 結合テスト（Integration Test）ケース定義

- **`IT-WEB-03` (ストリーミング Fail-Fast & 422 結合検証)**:
  - ASP.NET Core TestHost を起動し、256 バイトのファイル名を持つ動画レコードの `/api/videos/{id}/stream` をリクエストする。
  - レスポンスが HTTP 422 であり、JSON に `code: "FILE_NAME_TOO_LONG"` が含まれ、`ErrorLogs` テーブルに VideoId, FilePath, Bytes を明記したエラーレコードが記録されることを検証する。
- **`IT-SCN-01` (スキャナー警告ログ記録 & キュースキップ結合検証)**:
  - 一時ディレクトリに UTF-8 で 256 バイトのファイル名を持つ `.mp4` ファイルを作成し、`Scanner.syncVideoDirectories` を実行する。
  - DB の `Videos` テーブルに該当レコードが登録され、`ErrorLogs` テーブルに `NAME_MAX` 超過の警告ログが記録され、かつ `MediaQueue` に投入されていない（滞留がない）ことを検証する。

---

## 6. E2Eテスト（End-to-End）ケース定義

- **`E2E-REN-01` (Renamer 包括抽出・制約バッジ・フィルタ Visual E2E)**:
  - Avalonia.FuncUI のウィンドウを起動し、Windows長パスファイル、Linux長ファイル名ファイル、両方超過ファイルを含むテストフォルダを走査する。
  - 各カードに `[🪟 Windows: パス長超過]`、`[🐧 Linux: ファイル名超過]`、`[⚠️ 両方超過]` バッジが表示されることを検証。
  - 表示フィルタで「Linuxファイル名超過のみ」を選択し、一覧が正しく絞り込まれることを検証。
  - 該当ファイルが 0 件のフィルタを選択した際に Empty State（安心案内）が表示されることを検証。
  - AI提案（または手動編集）後に AFTER バッジが `[安全]` に変化することを視覚キャプチャ（PNG）で検証。
- **`E2E-WEB-02` (Web UI プレイヤーエラーオーバーレイ & トースト E2E)**:
  - Playwright を用いて Web UI を開き、Linux 255バイト制限超過の動画カードをクリックする。
  - 赤色トースト通知が表示され、プレイヤー画面中央にオーバーレイ案内および「ファイルパスをコピー」ボタンが表示されることを検証し、スクリーンショットを取得する。
