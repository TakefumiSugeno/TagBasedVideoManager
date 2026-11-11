# Proposal: Web アプリケーションの描画待ちスピナー表示と Zero-State 誤表示解消

## Metadata

- **Change Name**: `web-loading-spinners`
- **Scale Level**: L2（通常変更: UI/UX改善・機能追加）
- **Target Application**: Web アプリケーション (`TagBasedVideoManager`)
- **Version Increment**: `0.1.0` → `0.2.0` (Minor インクリメント)
- **Related Issues**: [#33](https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/33) (Web 側スコープ)

## Why

現在、Web アプリケーション（`TagBasedVideoManager/wwwroot/`）において、非同期通信（初回データロード、検索・タグ絞り込み、視聴分析データ取得）の間にスピナー等のローディングインジケータが存在しません。
その結果、データ取得完了までの間、データ件数が 0 件として先行評価され、初期ロード時に「動画が見つかりませんでした」「登録されたタグはありません」「再生履歴がまだありません」という Zero-State 画面が一瞬〜数秒間誤表示される UX 上の深刻な不快感が生じています。
また、重い検索クエリやソート変更時に画面の反応がなくフリーズしたように見えるため、各非同期状態を明示的に管理し、独立したスピナー表示と Zero-State 誤表示防止を実現する必要があります。

## What Changes

- **独立したローディング状態管理の導入 (`app.js`)**:
  - `isLoadingVideos: false`: 動画一覧（初回ロード・検索・絞り込み・ソート等）の非同期通信状態
  - `isLoadingTags: false`: タグ一覧取得の非同期通信状態
  - `isLoadingAnalytics: false`: 視聴分析モーダルデータ取得の非同期通信状態
- **動画一覧ローディングスピナーおよびオーバーレイ (`index.html`)**:
  - 初回ロード時: メインコンテンツ領域の中央に Lucide スピナー（`animate-spin`）＋「動画を読み込み中...」を表示
  - 検索・絞り込み時: 既存一覧の上に半透明オーバーレイ（`bg-slate-900/40 backdrop-blur-sm`）＋中央スピナーを表示し、レイアウトシフト（ガタつき）を防止
  - Zero-State ガード: `videos.length === 0` を `!isLoadingVideos && videos.length === 0` に改修し、ロード中の誤表示を完全遮断
- **タグ一覧ローディングスピナー (`index.html`)**:
  - 左サイドバーのタグ一覧取得中に小型スピナー＋「タグ読み込み中...」を表示
  - Zero-State ガード: `!isLoadingTags && tags.length === 0` でのみ「登録されたタグはありません」を表示
- **視聴分析モーダルのスピナー表示 (`index.html`)**:
  - モーダル内各タブ（全体サマリー・月別推移・タグ別視聴等）のデータ取得中に中央スピナーを表示
  - Zero-State ガード: `!isLoadingAnalytics && history.length === 0` による誤表示遮断
- **デスクトップツールバー・検索バーの視覚的フィードバック**:
  - 検索入力中（デバウンス待機〜通信中）に検索アイコンをスピナー回転アニメーションに切り替え

## Non-goals（やらないこと）

- デスクトップアプリ（`Renamer`）のローディング表示改修（Step 2 / Issue #32 刷新時に実施）
- サーバーサイドページネーション（Issue #28）の同時実装（本 Change で `isLoadingVideos` の基盤を整えた上で後続 Change で実施）
- 設定ファイル一本化（`.env` 廃止）（Step 2 / 3 で実施）

## Capabilities

### Modified Capabilities

- `video-library`: 動画一覧・検索・ソート時の非同期ローディングスピナー表示および Zero-State 誤表示防止要件を追加
- `tag-management`: タグ一覧取得時のローディングスピナー表示および Zero-State 誤表示防止要件を追加
- `viewing-analytics`: 視聴分析モーダルのデータ取得中におけるスピナー表示要件を追加

## Impact

- **影響対象コード**:
  - `src/TagBasedVideoManager/wwwroot/app.js` (状態管理・非同期関数)
  - `src/TagBasedVideoManager/wwwroot/index.html` (スピナーUI・Zero-State条件)
  - `src/TagBasedVideoManager/TagBasedVideoManager.fsproj` (バージョンインクリメント: `0.2.0`)
- **API変更**: なし（既存 API と完全互換）
- **破壊的変更**: なし

## Acceptance Criteria

- [ ] **Given** 初回アクセス時、**When** 動画データ取得が完了していないとき、**Then** 「動画が見つかりませんでした」が表示されず、中央スピナーが表示されること
- [ ] **Given** 初回アクセス時、**When** タグデータ取得が完了していないとき、**Then** 「登録されたタグはありません」が表示されず、タグ用スピナーが表示されること
- [ ] **Given** 動画一覧表示中、**When** 検索キーワードを入力またはタグをクリックしたとき、**Then** 一覧がガタつかず半透明オーバーレイとスピナーが表示され、取得完了後に新しい一覧に切り替わること
- [ ] **Given** 視聴分析モーダルを開いたとき、**When** 分析データ取得中であるとき、**Then** 「再生履歴がまだありません」が表示されず、スピナーが表示されること
