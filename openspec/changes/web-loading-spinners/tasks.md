# Tasks: Web アプリケーションの描画待ちスピナーと Zero-State ガード機構

## 1. バージョン更新とテスト基盤準備

- [ ] 1.1 Web アプリケーションのバージョンを `0.2.0`（Minor）に更新する (`src/TagBasedVideoManager/TagBasedVideoManager.fsproj` の `<Version>`）
- [ ] 1.2 `qa-tester`: `test_plan.md` に基づき、Playwright E2E テスト（`test/TagBasedVideoManager.Tests/WebLoadingSpinnerE2ETests.fs`）において非同期遅延・通信エラー（HTTP 500）・タイムアウトモックを用いた失敗テスト（Red）を作成する

## 2. フロントエンド状態管理・レースコンディション・エラー制御の実装 (`app.js`)

- [ ] 2.1 `qa-tester`: Playwright の `page.EvaluateAsync` を用いて、`videoManager()` の状態管理（`isLoadingVideos`, `isLoadingTags`, `isLoadingAnalytics`, `loadErrorVideos`, `loadErrorTags`, `loadErrorAnalytics`, `latestVideoRequestId`）および非同期関数（`fetchVideos`, `fetchTags`, `fetchAnalytics`）の先行失敗テストを作成・検証する
- [ ] 2.2 `pg-implementer`: 10秒タイムアウト中断機能付き `fetchWithTimeout` ヘルパーを導入し、`fetchVideos`, `fetchTags`, `fetchAnalytics` を `try ... catch ... finally` で実装してエラー情報（`loadError*`）とコンソール詳細ログ出力を追加する（Green）
- [ ] 2.3 `pg-implementer`: `latestVideoRequestId` による最新リクエストID判定を実装し、古い応答の破棄と、最新リクエスト完了時のみ `isLoadingVideos = false` とする時系列ローディング維持ロジックを実装する

## 3. UI 描画・スピナー・Zero-State/Error-State ガードの実装 (`index.html`)

- [ ] 3.1 `pg-implementer`: 動画一覧領域の中央スピナー（初回ロード用）および半透明オーバーレイ＋中央スピナー（検索・フィルタ再取得用、`pointer-events-auto` によるクリック遮断付き）を実装する
- [ ] 3.2 `pg-implementer`: 動画一覧の表示判定を `!isLoadingVideos && !isScanning && !loadErrorVideos && videos.length === 0` に改修し、初回ロード中およびスキャン中の Zero-State 誤表示を完全解消する
- [ ] 3.3 `pg-implementer`: 動画一覧の Zero-State 内に「ディレクトリ同期を実行する」「検索条件をクリアする」ボタンを追加し、Error-State 内にエラー表示と「再試行」ボタンを実装する
- [ ] 3.4 `pg-implementer`: 左サイドバーのタグ領域に小型スピナー、Error-State（「再試行」ボタン）、および Zero-State 内の「タグ管理を開く」ボタンを実装する
- [ ] 3.5 `pg-implementer`: 検索バー右端のアイコンを通信中に回転スピナーへ切り替える動的バインディングを実装し、状態復帰時に `refreshIcons()` を呼び出して SVG を再描画する
- [ ] 3.6 `pg-implementer`: 視聴分析モーダル内の各タブにローディングスピナー、Error-State、および Zero-State ガードと「動画一覧へ戻る」ボタンを実装する

## 4. E2E 検証・エビデンス出力・フェーズゲート確認

- [ ] 4.1 Playwright E2E テストを実行し、初回ロード中、同期中、検索オーバーレイ（クリック遮断）、Zero-State、Error-State（再試行成功）の画面キャプチャ（PNG）を `test/TestResults/` に出力して検証する
- [ ] 4.2 バックエンドおよびフロントエンドの全テストを実行し、TRX レポートおよびカバレッジレポートを出力する (`dotnet test --collect:"XPlat Code Coverage"`)
- [ ] 4.3 自動フォーマット（Prettier / dotnet format）を実行し、`pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply -ChangeName "web-loading-spinners"` を実行して検証合格を確認する

## Workflow follow-up

- サブエージェント批判的レビュー（PG/QA Reviewer）の実施と `reviews.md` への記録
- Gate 2 検証を経てユーザー合意を得てコミット・PR作成
