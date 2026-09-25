# Tasks: AIファイルリネーム・管理コンパニオン (Desktop Companion)

## 1. プロジェクト構造と環境構築 (100% F#)

- [ ] 1.1 `src/TagBasedVideoManager.Companion/` (F# .NET 10 + Avalonia 11.x + Avalonia.FuncUI) プロジェクトを作成し、ソリューションに追加して空ウィンドウが正常にビルド・起動することを検証する
- [ ] 1.2 `test/TagBasedVideoManager.Companion.Tests/` (F# .NET 10 + xUnit + FsUnit) テストプロジェクトを作成し、ソリューションに追加して `dotnet test` が通過することを検証する

## 2. Core: 設定管理とファイル走査・リネームエンジン (TDD)

- [ ] 2.1 [単体テスト・仕様検証] `Settings` モジュールにおける `companion-settings.json` の読み書き・デフォルト値復元に対する失敗するテストを作成する（Red）
- [ ] 2.2 [実装・リファクタ] `Settings` モジュールを実装し、JSONシリアライズ/デシリアライズのテストをGreenにする
- [ ] 2.3 [単体テスト・仕様検証] `FileScanner` のパス長計算・抽出条件（絶対パス長 ≧ 240文字）に対する失敗するテストを作成する（Red）
- [ ] 2.4 [実装・リファクタ] `FileScanner` を実装し、指定フォルダを再帰走査して危険ファイル（240文字以上）を抽出する単体テストをGreenにする
- [ ] 2.5 [単体テスト・仕様検証] `FileRenamer` の物理リネーム、同名衝突回避、ファイルロック例外ハンドリングに対する失敗するテストを作成する（Red）
- [ ] 2.6 [実装・リファクタ] `FileRenamer` を実装してテストをGreenにし、ROP（`Result` 型）による安全なエラーハンドリングを適用する

## 3. Core: OpenRouter API クライアント (TDD)

- [ ] 3.1 [単体テスト・仕様検証] `OpenRouterClient` の命名規則プロンプト構築および構造化JSONレスポンスのデシリアライズに対する失敗するテスト（モックHTTP）を作成する（Red）
- [ ] 3.2 [実装・リファクタ] `OpenRouterClient` を実装し、Freeモデル向けのAPI通信・JSONパース・タイムアウト処理のテストをGreenにする

## 4. Core: Docker Compose コントローラー (TDD)

- [ ] 4.1 [単体テスト・仕様検証] `DockerController` の `docker compose ps --format json` 出力パース（サービス `video-manager` 特定）および HTTPヘルスチェック疎通に対する失敗するテストを作成する（Red）
- [ ] 4.2 [実装・リファクタ] `DockerController` を実装し、コンテナ状態特定（Running/Stopped/Unhealthy）および up/down/restart コマンド発行のテストをGreenにする

## 5. Webアプリ: マウント異常検知

- [ ] 5.1 [単体テスト・回帰検証] Webアプリ側の `Scanner.fs` におけるマウントディレクトリ異常（未存在・0件）の検知ロジックに対するテストを追加する
- [ ] 5.2 [実装] Webサーバー起動時にマウント状態を判定し、マウント異常時に Web UI（`src/wwwroot/index.html`, `app.js`）上部に警告バナーを表示する

## 6. Avalonia.FuncUI UI 実装と統合 (Elmish MVU)

- [ ] 6.1 `State.fs` に Elmish の Model, Msg, init, update を実装し、スキャン・AI提案・リネーム・Docker制御の状態遷移ロジックを構築する
- [ ] 6.2 `Views.fs` に Docker コントローラーバー（稼働ステータスバッジ、Up/Down/Restartボタン）を構築し、非同期コマンドと連動させる
- [ ] 6.3 `Views.fs` に フォルダ参照ピッカー、OpenRouter設定、命名規則プリセット選択ドロップダウンおよびカスタム入力欄を構築する
- [ ] 6.4 `Views.fs` に Before / After 対比比較カードビュー（元パス・元ファイル名 vs 新パス・新ファイル名インライン編集、リアルタイム新パス長再計算、短縮効果表示）を構築する
- [ ] 6.5 「リネームしてコンテナ再起動」および「リネームのみ実行」のアクションを実装し、進捗表示および完了ダイアログを実装する

## 7. 自動E2Eテストの実装と総合検証

- [ ] 7.1 [結合E2Eテスト] `TagBasedVideoManager.Companion.Tests` 内に、一時フォルダに240文字以上の長パスファイルを動的生成し、「走査 → AIモック提案 → 物理リネーム → 整合性確認」を一気通貫で自動検証する結合E2Eテストを実装する
- [ ] 7.2 [Web E2Eテスト] Playwright を用いて、`VIDEO_DIR` が空の状態でWebアプリを起動した際に、トップ画面に「マウント異常・長パス警告バナー」が正しく描画されることを自動検証する E2E テストを実装する
- [ ] 7.3 全単体テストおよびE2Eテストを実行し、TRXエビデンスおよびカバレッジレポートを出力して品質基準を満たしていることを検証する
