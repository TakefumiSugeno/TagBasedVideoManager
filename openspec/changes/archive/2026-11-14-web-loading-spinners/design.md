# Design: Web アプリケーションの描画待ちスピナーと Zero-State ガード機構

## Context

Web アプリケーション（`TagBasedVideoManager/wwwroot/`）は、Node.js やビルドツールを一切使用せず、CDN から読み込まれる Alpine.js と Tailwind CSS で構成されています。
現状の状態管理オブジェクト `videoManager()`（`app.js`）には、ディレクトリ同期用の `isScanning`（真偽値）のみが存在し、動画一覧・タグ一覧・視聴分析の非同期取得を表現するローディング状態変数がありません。
また、`index.html` 側の Zero-State 条件は `x-show="videos.length === 0"` や `x-show="tags.length === 0"` と記述されており、配列初期化時（空配列 `[]`）から通信完了までの間、無条件に「動画が見つかりませんでした」が表示される構造的バグを抱えています。さらに、通信失敗時の Error-State（エラー画面・再試行導線）や、連続操作時のレースコンディションに対する防壁が存在しません。

## Goals / Non-Goals

**Goals:**

- 動画一覧・タグ一覧・視聴分析の 3 系統について、独立したローディング状態（`isLoadingVideos`, `isLoadingTags`, `isLoadingAnalytics`）およびエラー状態（`loadErrorVideos`, `loadErrorTags`, `loadErrorAnalytics`）を導入する。
- 初回ロード時の中央スピナー、検索・絞り込み時の半透明オーバーレイ＋スピナー（クリック遮断 `pointer-events-auto` 付き）を導入し、レイアウトシフト（画面のガタつき）と多重クリックを防止する。
- Zero-State 画面の表示条件を厳密化（`!isLoadingVideos && !isScanning && videos.length === 0`）し、非同期通信中およびディレクトリ同期中の誤表示を 100% 遮断する。
- Zero-State 内にワンクリック回復ボタン（「同期を実行」「検索条件をクリア」「タグ管理を開く」）を配置し、初心者が迷わず操作を継続できるようにする。
- 通信エラー・タイムアウト時の Error-State 画面と「再試行」ボタンを配置し、可観測性（コンソール詳細ログ出力・トースト通知）を向上させる。
- `AbortController` による 10 秒タイムアウト中断および、最新リクエストIDによる時系列ローディング状態維持（レースコンディション完全防止）を実現する。

**Non-Goals:**

- デスクトップアプリ（`TagBasedVideoManager.Renamer`）の UI/設定変更（Step 2 で実施）。
- サーバーサイドページネーション（Issue #28）の実装（本 Change で `isLoadingVideos` の基盤を提供し、次期 Change で統合）。
- 設定ファイルの一本化（`.env` の廃止）。

## Decisions

### 1. 非同期系統ごとの独立した状態変数（3権分立）とエラー状態

- **決定**: 以下の状態変数を `videoManager()` に追加する。
  - 動画一覧: `isLoadingVideos: false`, `loadErrorVideos: null` (エラー情報 `{ message, status }`)
  - タグ一覧: `isLoadingTags: false`, `loadErrorTags: null`
  - 視聴分析: `isLoadingAnalytics: false`, `loadErrorAnalytics: null`
- **理由**: タグの取得と動画一覧の取得は非同期かつ並行に実行される。独立管理により各領域のローディング・エラー表示を完全に同期させる。

### 2. レースコンディション対策と時系列ローディング維持

- **決定**: 単調増加カウンタ `latestVideoRequestId` を導入し、リクエスト開始時に `const requestId = ++this.latestVideoRequestId` を発行する。
  ```javascript
  const requestId = ++this.latestVideoRequestId;
  this.isLoadingVideos = true;
  this.loadErrorVideos = null;
  try {
    const data = await fetchWithTimeout(`/api/videos?${params}`, {
      timeout: 10000,
    });
    // 最新リクエストの応答のみを反映
    if (requestId === this.latestVideoRequestId) {
      this.videos = data;
    }
  } catch (err) {
    if (requestId === this.latestVideoRequestId) {
      this.loadErrorVideos = {
        message: err.message || "通信エラーが発生しました",
        status: err.status || 0,
      };
      console.error(`[fetchVideos Error] Request #${requestId} failed:`, err);
      this.showToast("動画一覧の取得に失敗しました", "error");
    }
  } finally {
    // 【重要】先行リクエストが完了しても、後続リクエストが通信中ならスピナーを解除しない！
    if (requestId === this.latestVideoRequestId) {
      this.isLoadingVideos = false;
    }
  }
  ```
- **理由**: 先行リクエストが遅延して終了した際に無条件で `isLoadingVideos = false` を実行すると、後続リクエストが通信中であるにもかかわらずスピナーが早期解除され、画面チラつきや Zero-State 誤表示が再発する。最新リクエストの終了時のみフラグを下ろすことで、ローディング表示が途切れず維持される。

### 3. 初回スキャン中（`isScanning`）も含めた厳密な Zero-State ガード

- **決定**:
  - 動画一覧: `x-show="!isLoadingVideos && !isScanning && !loadErrorVideos && videos.length === 0"`
  - タグ一覧: `x-show="!isLoadingTags && !loadErrorTags && tags.length === 0"`
  - 視聴分析: `x-show="!isLoadingAnalytics && !loadErrorAnalytics && isCurrentTabEmpty()"`
- **理由**: 初回起動時に動画 0 件で自動同期（`triggerScan()`）が走った際、`isScanning === true` であるため「動画が見つかりませんでした」の誤表示を完璧にブロックする。

### 4. Zero-State と Error-State のアクション回復導線

- **決定**:
  - **動画 Zero-State**:
    - 通常時（動画0件）: 「ディレクトリ同期を実行する」ボタン（`@click="triggerScan()"`）
    - 検索・絞り込み時: 「検索条件をクリアする」ボタン（`@click="clearSearch()"`）
  - **動画 Error-State**:
    - エラーメッセージ ＋ 「再試行」ボタン（`@click="fetchVideos()"`）
  - **タグ Zero-State**:
    - 「タグ管理を開く」ボタン
  - **タグ Error-State**:
    - 「タグの取得に失敗しました」 ＋ 「再試行」ボタン（`@click="fetchTags()"`）
  - **分析 Error-State**:
    - 「分析データの取得に失敗しました」 ＋ 「再試行」ボタン

### 5. 半透明オーバーレイと多重クリック遮断

- **決定**:
  検索中・フィルタ再取得時のオーバーレイ要素に `pointer-events-auto` を指定し、背面の一覧カードやボタンに対するクリック操作を完全に遮断する。また、検索バー右端のアイコンを通信中に回転スピナーへ動的切替し、完了時に `refreshIcons()` を呼んで SVG を確実に再描画する。

### 6. タイムアウト中断（`AbortController`）と可観測性

- **決定**:
  共通の `fetchWithTimeout(url, { timeout = 10000, ...options })` ヘルパーを導入し、10秒でリクエストを自動 Abort してタイムアウト例外を発生させる。エラー時はリクエスト URL、HTTP ステータス、エラーメッセージを構造化して `console.error` に出力し、SRE / 運用者がブラウザコンソールから即座に原因特定できるようにする。

## Risks / Trade-offs

- **[Risk] 超高速ネットワークでのスピナーの瞬間ちらつき**
  - **Mitigation**: Alpine.js の `x-transition:enter="transition ease-out duration-150"` を適用し、滑らかにフェード表示させる。
- **[Risk] タイムアウト（10秒）による大容量DBでの誤切断**
  - **Mitigation**: 通常の動画一覧クエリはインデックスと一括結合により数十ミリ秒で返るため 10 秒は十分な安全マージンである。必要に応じてタイムアウト値をオプション指定可能とする。
