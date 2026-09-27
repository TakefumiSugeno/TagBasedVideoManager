# Tasks: AI File Renamer (TagBasedVideoManager - AI File Renamer)

## 1. フォルダ構成の再編・既存回帰テストおよび新設プロジェクト構築

- [x] 1.1 [フォルダ構成再編] 既存Webアプリケーションのソース一式を `src/TagBasedVideoManager/` へ移動し、テストコード一式を `test/TagBasedVideoManager.Tests/` へ移動する
- [x] 1.2 [構成・参照パス更新] `TagBasedVideoManager.slnx`, `Dockerfile`, `docker-compose.yml`, `TagBasedVideoManager.Tests.fsproj`（プロジェクト参照パス）、`run_tests_with_coverage.ps1`、`README.md` のパスを新ディレクトリ構成に合わせて更新する
- [x] 1.3 [既存回帰テスト検証] 移動後の既存Webアプリケーションの全テストを一括実行（`dotnet test test/TagBasedVideoManager.Tests/`）し、全テスト通過（回帰テスト成功）を確認する
- [x] 1.4 [Renamer プロジェクト新設] `src/TagBasedVideoManager.Renamer/` (F# .NET 10 + Avalonia 11.x + Avalonia.FuncUI) プロジェクトを作成し、ソリューションに追加して空ウィンドウが正常にビルド・起動することを検証する
- [x] 1.5 [Renamer テストプロジェクト新設] `test/TagBasedVideoManager.Renamer.Tests/` (F# .NET 10 + xUnit + FsUnit) テストプロジェクトを作成し、ソリューションに追加して `dotnet test` が通過することを検証する

## 2. Core: 設定管理とファイル走査・リネーム・Undoエンジン (TDD)

- [x] 2.1 [単体テスト・仕様検証] `Settings` モジュールにおける `companion-settings.json` の読み書き（初期閾値 240文字、フォルダ、モデル、命名規則リスト保持）および起動中閾値変更時に設定ファイルが更新されないことの検証テストを作成する（Red）
- [x] 2.2 [実装・リファクタ] `Settings` モジュールを実装し、JSONシリアライズ/デシリアライズおよび設定ファイル非更新のテストをGreenにする
- [x] 2.3 [単体テスト・仕様検証] `FileScanner` の指定閾値文字数（可変）による長パス抽出、および大文字小文字（`.mp4`/`.MP4`）を区別しない抽出判定に対する失敗するテストを作成する（Red）
- [x] 2.4 [実装・リファクタ] `FileScanner` を実装し、大文字小文字不問で指定フォルダを再帰走査して閾値以上の危険ファイルを抽出する単体テストをGreenにする
- [x] 2.5 [単体テスト・仕様検証] `FileRenamer` の物理リネーム、重複回避、および Undo（逆リネームによる復元）に対する失敗するテストを作成する（Red）
- [x] 2.6 [実装・リファクタ] `FileRenamer` を実装してリネームおよびUndo復元テストをGreenにし、ROP（`Result` 型）による安全なエラーハンドリングを適用する

## 3. Core: OpenRouter API クライアント (TDD)

- [x] 3.1 [単体テスト・仕様検証] `OpenRouterClient` の命名規則プロンプト構築および構造化JSONレスポンス（新ファイル名、および問題発生時のみのAIコメント）のデシリアライズに対する失敗するテスト（モックHTTP）を作成する（Red）
- [x] 3.2 [実装・リファクタ] `OpenRouterClient` を実装し、Freeモデル向けのAPI通信・JSONパース（問題時のみAIコメント格納）・タイムアウト処理のテストをGreenにする

## 4. Core: Docker Compose コントローラー (TDD)

- [x] 4.1 [単体テスト・仕様検証] `DockerController` の `docker compose ps --format json` 出力パース（単一JSON配列形式および改行区切りNDJSON形式の両対応、サービス `video-manager` 特定）および HTTPヘルスチェック疎通に対する失敗するテストを作成する（Red）
- [x] 4.2 [実装・リファクタ] `DockerController` を実装し、JSON配列/NDJSONのフォールバックデシリアライズ、コンテナ状態特定（Running/Stopped/Unhealthy）および up/down/restart コマンド発行のテストをGreenにする

## 5. Avalonia.FuncUI UI 実装と統合 (Elmish MVU)

- [x] 5.1 `State.fs` に Elmish の Model, Msg, init, update を実装し、初期閾値ロード・セッション内閾値変更・ワンアクション抽出＆提案・Undo履歴管理・Docker制御の状態遷移ロジックを構築する
- [x] 5.2 `Views.fs` に Docker コントローラーバー（稼働ステータスバッジ、Up/Down/Restartボタン）を構築し、非同期コマンドと連動させる
- [x] 5.3 `Views.fs` に 抽出基準数値入力欄（起動中一時変更・設定非保存）、命名規則マネージャーモーダル（作成・編集・削除・並び替え）を構築する
- [x] 5.4 `Views.fs` に Before / After 対比ビュー（表示形式トグル: 上下並び / 左右並び、インライン編集テキストボックス、問題時のみAIコメント表示、リアルタイム新パス長再計算）を構築する
- [x] 5.5 「リネームしてコンテナ再起動」および「直前のリネームを元に戻す (Undo)」のアクションを実装し、Undo確認時のコンテナ再起動警告ダイアログ・進捗表示を実装する

## 6. 自動E2Eテストの実装と総合検証

- [x] 6.1 [結合E2Eテスト] `TagBasedVideoManager.Renamer.Tests` 内に、一時フォルダに240文字以上の長パスファイルを動的生成し、「走査 → AIモック提案（問題時コメント含む） → 物理リネーム → 整合性確認 → Undo復元検証」を一気通貫で自動検証する結合E2Eテストを実装する
- [x] 6.2 全単体テストおよび結合E2Eテスト、既存Webアプリ回帰テストを実行し、TRXエビデンスおよびカバレッジレポートを出力して品質基準を満たしていることを検証する

## 7. 抽出機能の拡張およびUI視認性・操作性ブラッシュアップ (TDD)

- [x] 7.1 [単体テスト・仕様検証] `FileScanner` におけるディレクトリジャンクション／シンボリックリンク配下の動画ファイル走査、および循環参照（同一ディレクトリ再帰）時の安全スキップに対する失敗するテストを作成する（Red）
- [x] 7.2 [実装・リファクタ] `FileScanner` にリパースポイント対応走査および循環参照検知（`HashSet<string>`）を実装し、テストをGreenにする
- [x] 7.3 [単体テスト・仕様検証] `FileScanner.sortCandidates` における各種ソート基準（パス長 降順/昇順、元ファイル名 昇順/降順、更新日時 新しい順/古い順）の並び替えロジックに対する失敗するテストを作成する（Red）
- [x] 7.4 [実装・リファクタ] `Domain.fs`, `FileScanner.fs`, `State.fs` に `SortCriterion` 型とソート処理および Elmish Msg / Model を追加実装し、テストをGreenにする
- [x] 7.5 [UI実装] `Views.fs` において、対象パス・各カードのパス・BEFOREファイル名をテキスト選択・コピー可能（`SelectableTextBlock`）にする
- [x] 7.6 [UI実装・視覚的整列] `Views.fs` の上下並び表示において、①左側ラベル幅固定（140px）によるファイル名開始X座標の垂直整列、②BEFORE/AFTER行高さ統一（30px）、③ファイル名の等幅フォント（`Consolas`）化、④「〇〇字短縮」バッジ内の文字・数字ベースライン揃え、⑤ソート順選択UIを実装する
- [x] 7.7 [GUI実画面E2E検証] `RenameIntegrationE2ETests.fs` を拡張し、ジャンクション走査・ソート・整列・コピー可能UIの実画面レンダリング検証および画像キャプチャ（PNG）出力・テスト合格を確認する

## 8. 不具合修正・UIモーダル実装・モック＆E2Eテスト更新 (TDD)

- [x] 8.1 [単体テスト・仕様検証] 参照ボタン押下時のフォルダ選択メッセージ処理、および管理ボタン押下時のモーダル開閉（`IsRuleManagerOpen`）状態遷移テストを作成する（Red）
- [x] 8.2 [単体テスト・仕様検証] 長大ファイル名（240文字超）において、カードの枠線幅がウィンドウ幅（親コンテナ幅）を超えないことのレイアウト検証テストを作成する（Red）
- [x] 8.3 [実装・リファクタ] `Views.fs` に「参照...」ボタンの `onClick`（`StorageProvider` によるフォルダピッカーダイアログ呼び出しとパス反映）を実装し、テストをGreenにする
- [x] 8.4 [実装・リファクタ] `Views.fs` に `ruleManagerModal`（命名規則一覧、新規作成、編集、削除、順序入れ替え、閉じる）を実装し、メインビューに組み込んでテストをGreenにする
- [x] 8.5 [実装・リファクタ] `Views.fs` の BEFORE 行 `SelectableTextBlock` を横スクロール対応コンテナでラップし、長大ファイル名時でも青い外枠がウィンドウ内に収まるよう修正してテストをGreenにする
- [x] 8.6 [モック更新] `openspec/changes/ai-file-rename/mockup.html` を最新仕様（ソートComboBox、等幅フォント、垂直整列、短縮バッジ、管理モーダル、参照ボタン）に更新する
- [x] 8.7 [GUI実画面E2E検証] `RenameIntegrationE2ETests.fs` を拡張し、管理モーダル描画キャプチャ（`E2E_05_Rule_Manager_Modal.png`）および長パス外枠非見切れキャプチャ（`E2E_06_Long_Path_No_Overflow.png`）を出力・検証する

## 9. 外部設定ロード・AI提案ステータス正常化・外枠完全描画とフォントメトリクス揃え (TDD)

- [x] 9.1 [単体テスト・仕様検証] `Settings.loadConfiguration` における外部 `.env` および `companion-settings.json` からの設定読み込み優先度、および外部ファイル先頭ルールの既定選択テストを作成する（Red）
- [x] 9.2 [単体テスト・仕様検証] APIキー未設定時に「AI提案済」バッジや固定AIコメントが付与されず「未提案」状態（`IsAiProposed = false`, `AiComment = None`, `ProposedFileName = OriginalFileName`）となることの仕様検証テストを作成する（Red）
- [x] 9.3 [単体テスト・仕様検証] 短縮文字数バッジのフォント（`Yu Gothic UI`）およびカード内各行が親幅を超過しないレイアウト検証テストを作成する（Red）
- [x] 9.4 [実装・リファクタ] `Settings.fs`, `OpenRouterClient.fs`, `State.fs` に外部 `.env` / JSON 設定ロード、外部定義命名規則のデフォルト適用、APIキー未設定時の正直な「未提案」状態管理・案内メッセージを実装し、テストをGreenにする
- [x] 9.5 [実装・リファクタ] `Views.fs` の短縮バッジフォント修正、Line 1 / Line 4 の幅制約（`DockPanel` 化・`TextWrapping`）、親 `ScrollViewer` の水平スクロール抑止による青枠見切れ完全解消を実装し、テストをGreenにする
- [x] 9.6 [モック更新] `openspec/changes/ai-file-rename/mockup.html` を更新し、LLM未接続時の「未提案」表示および枠線・フォント修正を最新同期する
- [x] 9.7 [GUI実画面E2E検証] `RenameIntegrationE2ETests.fs` を拡張し、青い外枠が完全に閉じていること、短縮バッジの文字と数字が揃っていること、およびAPIキー未設定時の未提案表示の実画面キャプチャ（PNG）を出力・検証する

## 10. 設定ファイル標準化 (appsettings.json 改名および AppData/BaseDirectory 格納先パス対応) (TDD)

- [ ] 10.1 [単体テスト・仕様検証] `Settings.loadConfiguration` および `Settings.defaultSavePath` において、`appsettings.json` の探索優先順位（AppData > カレント > BaseDirectory）および保存先パス決定ロジックの失敗するテストを作成する（Red）
- [ ] 10.2 [実装・リファクタ] `Settings.fs` に `appsettings.json` の名称適用、AppData ディレクトリ自動生成、および保存先決定ロジック（`defaultSavePath`）を実装し、テストをGreenにする
- [ ] 10.3 [実装・リファクタ] `State.fs` の設定保存・読み込みパスを `appsettings.json` へ移行し、案内メッセージ内のファイル名表示を更新する
- [ ] 10.4 [ドキュメント更新] `README.md` に AI File Renamer の設定セクション（`appsettings.json` の書式・探索/格納先パス・優先順位、および `.env` 環境変数対応表）を追記する
- [ ] 10.5 [回帰検証・全テスト実行] 全テスト（Renamer 49件＋新設テスト、既存Webアプリ 44件）を実行し、TRXエビデンスおよびカバレッジレポートを出力して品質基準を満たしていることを検証する
