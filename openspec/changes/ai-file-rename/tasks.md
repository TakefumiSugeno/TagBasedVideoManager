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
