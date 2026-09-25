# Tasks: AIファイルリネーム・管理コンパニオン (Desktop Companion)

## 1. プロジェクト構造と環境構築

- [ ] 1.1 `src/TagBasedVideoManager.Companion.Core` (F# .NET 10 クラスライブラリ) および `test/TagBasedVideoManager.Companion.Tests` (xUnit + FsUnit) プロジェクトを新設し、ソリューションに追加して `dotnet build` が正常終了することを検証する
- [ ] 1.2 `src/TagBasedVideoManager.Companion.App` (WinUI 3 / C#) プロジェクトを作成し、`TagBasedVideoManager.Companion.Core` をプロジェクト参照に追加して空ウィンドウが正常にビルド・起動することを検証する

## 2. Core: ファイル走査とリネームエンジン (TDD)

- [ ] 2.1 [単体テスト・仕様検証] `FileScanner` のパス長計算・閾値フィルタリング（例: 220文字以上）に対する失敗するテストを作成する（Red）
- [ ] 2.2 [実装・リファクタ] `FileScanner` を実装し、指定フォルダを再帰走査して危険ファイルを抽出する単体テストをGreenにし、重複ロジックを整理する
- [ ] 2.3 [単体テスト・仕様検証] `FileRenamer` の物理リネーム、同名衝突回避、ファイルロック例外ハンドリングに対する失敗するテストを作成する（Red）
- [ ] 2.4 [実装・リファクタ] `FileRenamer` を実装してテストをGreenにし、ROP（`Result` 型）による安全なエラーハンドリングを適用する

## 3. Core: OpenRouter API クライアント (TDD)

- [ ] 3.1 [単体テスト・仕様検証] `OpenRouterClient` の命名規則プロンプト構築および構造化JSONレスポンスのデシリアライズに対する失敗するテスト（モックHTTP）を作成する（Red）
- [ ] 3.2 [実装・リファクタ] `OpenRouterClient` を実装し、Freeモデル向けのAPI通信・JSONパース・タイムアウト処理のテストをGreenにする

## 4. Core: Docker Compose コントローラー (TDD)

- [ ] 4.1 [単体テスト・仕様検証] `DockerController` の `docker compose ps --format json` のパースおよび up/down/restart コマンド引数生成に対する失敗するテストを作成する（Red）
- [ ] 4.2 [実装・リファクタ] `DockerController` を実装し、プロセス実行・ステータス判定・エラー出力取得のテストをGreenにする

## 5. Webアプリ: マウント異常検知

- [ ] 5.1 [単体テスト・回帰検証] Webアプリ側の `Scanner.fs` におけるマウントディレクトリ異常（未存在・0件）の検知ロジックに対するテストを追加する
- [ ] 5.2 [実装] Webサーバー起動時にマウント状態を判定し、マウント異常時に Web UI（`src/wwwroot/index.html`, `app.js`）上部に警告バナーを表示する

## 6. WinUI 3 UI 実装と統合

- [ ] 6.1 WinUI 3 上に Docker コントローラー（稼働ステータスバッジ、Up/Down/Restartボタン）を構築し、`DockerController` と非同期連携させる
- [ ] 6.2 WinUI 3 上に フォルダ参照ピッカー、パス長閾値スライダー/入力、OpenRouter APIキー・モデル選択UIを構築する
- [ ] 6.3 WinUI 3 DataGrid にリネーム前後の比較表示とインライン編集テキストボックスを構築し、リアルタイムパス長再計算・バリデーションを実装する
- [ ] 6.4 「リネームしてコンテナ再起動」および「リネームのみ実行」のアクションを実装し、進捗プログレスバーおよび完了ダイアログを表示する

## 7. 総合検証・エビデンス出力

- [ ] 7.1 長パスファイル（220文字以上）を含むテスト環境を作成し、検出 → AI提案 → 手動微調整 → リネーム → コンテナ再起動の一連の動作を手動・結合検証する
- [ ] 7.2 全単体テストを実行し、TRXエビデンスおよびカバレッジレポートを出力して品質基準を満たしていることを検証する
