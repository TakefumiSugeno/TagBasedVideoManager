# 申し送り事項 (Handover): web-loading-spinners

- **変更名**: `web-loading-spinners`
- **日付**: 2026-11-14
- **ブランチ**: `feature/web-loading-spinners`
- **関連 Issue / PR**: https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/33

---

## 1. 変更サマリ

Web アプリケーション（`src/TagBasedVideoManager/wwwroot/app.js`, `index.html`）において、非同期データ取得中（初回ロード・検索・絞り込み・タグ取得・視聴分析取得）の描画待ちスピナー表示、Zero-State 誤表示の構造的遮断、および通信エラー・タイムアウト時の Error-State（再試行導線）を実装しました。また、SemVer 2.0.0 運用規則（L2 変更）に基づき、Web アプリケーションのバージョンを `0.1.0` から `0.2.0` へインクリメントしました。

### 設計背景とトレードオフ（Why）

- **なぜ 3 系統独立のローディング・エラー変数（`isLoadingVideos`, `isLoadingTags`, `isLoadingAnalytics`）に分けたのか？**:
  動画一覧、タグツリー、視聴分析はそれぞれ非同期かつ並列で API 通信を行うため、単一のグローバルフラグでは「タグが先に読み込み完了したのに動画の完了待ちでタグ側までスピナーが消えない」またはその逆の早期消滅が発生します。3 系統に完全分離することで、各領域が独立して最速で描画・回復できます。
- **なぜ検索・絞り込み時に半透明オーバーレイ＋`pointer-events-auto` を採用したのか？**:
  既存一覧を消して中央スピナーに切り替えると画面全体のレイアウトシフト（ガタつき）が起きるため、既存リストを残したまま `bg-slate-900/40 backdrop-blur-sm pointer-events-auto cursor-wait` のオーバーレイを重ねています。これにより視覚的な安定性と、通信中の背後カード誤クリック・二重送信防止を両立しています。
- **なぜ `latestVideoRequestId` / `latestAnalyticsRequestId` による時系列ローディング維持を導入したのか？**:
  連続検索やタグ連打時に先行リクエスト（Request #1）が遅延し、後続リクエスト（Request #2）より後に完了すると、古い結果で画面が上書きされるレースコンディションが発生します。また、先行リクエストの `finally` 句で無条件に `isLoading* = false` にすると、Request #2 の通信中にスピナーが消えてしまいます。単調増加カウンタを導入し、レスポンス反映・エラー記録・ローディング解除のすべてを `requestId === this.latest*RequestId` の条件付きとすることで、最新リクエスト完了まで途切れなくスピナーを維持します。
- **なぜ `fetchWithTimeout` で `isTimeout` フラグにより外部 `signal` とタイマー中断を分離したのか？**:
  `AbortController` によるタイムアウト（10 秒）と、呼び出し元から渡される外部 `options.signal` のキャンセルはどちらも `AbortError` を発生させます。フラグで原因を区別することで、外部キャンセル時は純粋な `AbortError` を維持し、タイムアウト時のみ HTTP 408 `TimeoutError` を明示的に送出して障害原因の誤認を防ぎます。
- **なぜ命令的 DOM 操作（`_syncZeroStateDOM` 等 約 220 行）を廃止したのか？**:
  初期実装にあった `document.querySelector` や `style.display` 直接代入は Alpine.js の `x-show` と競合し、宣言的状態管理を破壊していました。すべてを Alpine.js のリアクティブプロパティと `x-show` / `x-text` バインディングへ統一し、さらに Lucide アイコン更新も存在確認・デバウンス付きの `this.refreshIcons()` へ集約しました。

---

## 2. 品質・テストエビデンス

- **新規 E2E / フロントエンド状態遷移テスト (`WebLoadingSpinnerE2ETests.fs`)**:
  `dotnet run --no-build --project test/TagBasedVideoManager.Tests/TagBasedVideoManager.Tests.fsproj -- --filter-class TagBasedVideoManager.Tests.WebLoadingSpinnerE2ETests` → **17 件 Pass（失敗 0 / スキップ 0）**
  - `2.1-UT-01`〜`06`（状態変数初期値、`fetchVideos`/`fetchTags`/`fetchAnalytics` 状態遷移、レースコンディション維持、`fetchWithTimeout` 408 タイムアウト vs 外部 AbortError 分離）
  - `1.2-E2E-01`〜`15`（初回スピナー、同期中 Zero-State 抑止、0件回復導線、検索クリア導線、半透明オーバーレイ・背後クリック遮断、Error-State 表示＆再試行成功、タグ領域・分析モーダル検証）
- **Web プロジェクト全テスト (`TagBasedVideoManager.Tests.dll`)**: **189 件 Pass（失敗 0 / スキップ 0）**
- **Renamer プロジェクト全テスト (`TagBasedVideoManager.Renamer.Tests.dll`)**: **343 件 Pass（失敗 0 / スキップ 0）**
- **全ソリューションテスト合計**: **532 件 Pass（失敗 0 / スキップ 0 / 100% Green）**
- **カバレッジ（バックエンド .NET アセンブリ全体）**:
  - Line (C0): **83.4%**（Web: `84.2%` [Coverlet 単独 `85.64%`] / Renamer: `83.1%`）
  - Branch (C1): `43.7%`（F# async/task ステートマシン生成分岐を含む全アセンブリ合計） / Method: `79.2%`
  - 変更対象フロントエンド（`app.js` / `index.html`）の条件分岐・C2 デシジョンテーブル（`DT-01`〜`DT-09`）および仕様 Scenario 22 件は Playwright E2E テストと敵対的 QA スクリプト（`adversarial_cycle2.js`）により 100% 検証済み。
- **エビデンス出力先**:
  - TRX テストレポート:
    - `test/TestResults/TestRun_2026-11-14_01_09_00/TestRun_2026-11-14_01_09_00_net10.0.trx`
    - `test/TestResults/TestRun_2026-11-14_01_09_00/TestRun_2026-11-14_01_09_00_Renamer_net10.0.trx`
  - HTML カバレッジレポート:
    - `test/TestResults/TestRun_2026-11-14_01_09_00/CoverageReport/index.html`
    - `test/TestResults/TestRun_2026-11-14_01_09_00/CoverageReport/Summary.txt`
  - E2E 画面キャプチャ PNG（10 枚）:
    - `test/TestResults/E2E-01_Initial_Loading_Spinner.png`
    - `test/TestResults/E2E-02_Scanning_Zero_State_Suppression.png`
    - `test/TestResults/E2E-03_Zero_State_With_Scan_Button.png`
    - `test/TestResults/E2E-04_Zero_State_With_Clear_Button.png`
    - `test/TestResults/E2E-05_Search_Overlay.png`
    - `test/TestResults/E2E-09_Videos_Error_State.png`
    - `test/TestResults/E2E-10_Tags_Loading_Spinner.png`
    - `test/TestResults/E2E-12_Tags_Error_State.png`
    - `test/TestResults/E2E-13_Analytics_Loading_Spinner.png`
    - `test/TestResults/E2E-15_Analytics_Error_State.png`

---

## 3. 保留指摘・技術的負債

なし（Apply フェーズの `qa-reviewer` Minor 指摘「`fetchAnalytics` 内の `lucide.createIcons()` 直接呼び出し」も含め、`this.refreshIcons()` へ即時統一対応済み）

| 指摘元（フェーズ/タスク/役割） | 内容 | 保留理由 | 推奨対応時期 |
| ------------------------------ | ---- | -------- | ------------ |
| 特になし                       | -    | -        | -            |

---

## 4. 将来の改善・リファクタ候補

- **既存モーダル・非コア API への `fetchWithTimeout` および `this.refreshIcons()` 水平展開**:
  - スコープ対象外の既存処理（`fetchRules`, `fetchSmartFolders`, `triggerScan`, `deleteRule`, `saveRule`, `bulkModal` 等）には素の `fetch` が残存しており、将来的に `fetchWithTimeout` へ統一してアプリ全体のタイムアウト耐性を揃えることが推奨されます。
  - 同様に、既存箇所（`triggerScan`, `playVideo`, `openEditRuleModal`, `openBatchTagModal`, `toggleMobileViewMode`, `index.html` インラインイベント等）に残存する生の `lucide.createIcons()` 呼び出しを `this.refreshIcons()` へ完全統合することを推奨します。
- **フロントエンド JavaScript 単体カバレッジ自動計測の検討**: 現在 `app.js` はビルドツールレス（CDN Alpine.js）構成のため Playwright 経由で関数単位および画面単位のテストを実行していますが、将来必要に応じて V8 JS Coverage（Playwright `page.Coverage` API 等）を用いた数値計測の導入を検討できます。
- **サーバーサイドページネーション（Issue #28）統合時の拡張**: `isLoadingVideos` と `latestVideoRequestId` の基盤が整備されたため、今後のページ切り替え API 化時は同じ `fetchVideos()` パイプラインを流用できます。
- **Step 2（デスクトップアプリ `Renamer` 刷新 & `appsettings.json` 一本化）への引き継ぎ**: Issue #32 / #33 の計画通り、後続 Change で `Renamer` 側の設定ファイル統合およびローディング UI 刷新を実施します。

---

## 5. 運用上の注意事項・トラブルシューティング

- **トラブルシューティング（障害切り分け手順）**:
  1. 画面上で動画・タグ・分析データの Error-State（「再試行」ボタン付きエラー画面）が表示された場合は、ブラウザの開発者ツール（F12 → Console / Network）を開きます。
  2. コンソールに出力される `[fetchVideos Error] Request #...`, `[fetchTags Error]`, `[fetchAnalytics Error] Request #... failed for tab '...'` のエラー詳細および HTTP ステータス（`408` の場合は 10 秒タイムアウト、`500` の場合はサーバー障害、`0` の場合はネットワーク切断）を確認してください。
  3. 連続操作時に古いレスポンスが捨てられたことは `[fetchVideos] Dropping stale response for RequestId #...` の Warning ログで追跡可能です。
- **開発時の注意点（暗黙の結合箇所）**:
  - `app.js` 内で新たに `fetchVideos()` 相当の非同期取得を追加・変更する場合は、必ず `latest*RequestId` パターンを崩さず、`finally` 句のローディング解除にも `if (requestId === this.latest*RequestId)` ガードを維持してください。
  - Alpine.js の `x-for` / `x-if` 等で新しい動的アイコン要素やモーダルを追加する際は、`init()` での `$watch` 登録または描画後に必ず `this.refreshIcons()` を呼び出す規約を守ってください（忘れると `<i data-lucide="...">` が SVG 変換されず空欄になります）。
  - `init()` 内の `_initialized` ガードは二重初期化を防ぐため維持してください。
  - Zero-State の表示条件（`!isLoadingVideos && !isScanning && !loadErrorVideos && videos.length === 0` 等）から `isScanning` や `loadErrorVideos` の除外条件を外すと、スキャン中やエラー時に「動画が見つかりませんでした」が誤表示されるため変更時は注意してください。
- **ロールバック手順**:
  - データベーススキーマ変更やバックエンド API の破壊的変更は含まれていないため、万が一問題が発生した場合は対象コミット（`feature/web-loading-spinners`）を `git revert` するだけで安全に直前の安定状態（`v0.1.0`）へ戻せます。
