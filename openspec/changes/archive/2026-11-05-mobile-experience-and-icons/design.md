# Design: mobile-experience-and-icons

## Context

TagBasedVideoManager は、バックエンドに F# (.NET 10) + Giraffe、フロントエンドに Alpine.js (CDN) + Tailwind CSS (CDN) を採用した構成であり、Node.js ビルドツールやバンドラーを一切持たない純粋な静的ファイル配信構成となっています。
現状のフロントエンド (`src/TagBasedVideoManager/wwwroot/`) はデスクトップ PC 前提のグリッドおよびリスト表示、マウスホバー操作、上部展開式ツールバーを中心に構成されており、ファビコンやアプリアイコンが存在しません。
さらに、モバイル画面においても「キーワード検索」「タグ階層検索」「タグ新規作成・編集」「表示・ソート設定の変更」などの既存機能を一切損なうことなく、各種子画面（モーダル）を含めた完全なモバイル操作性が求められています。

詳細な動機・目的については [proposal.md](file:///D:/programming/repos/MyGitHubRepos/TagBasedVideoManager/openspec/changes/mobile-experience-and-icons/proposal.md) を参照してください。

## Goals / Non-Goals

**Goals:**

- ブランドシンボル（Tag + Play を基調とした Indigo/Violet）の SVG および各解像度アイコン（ICO, PNG）の配備と、404 なしの確実な配信。
- `manifest.webmanifest` による Web App Manifest の提供と、iOS / Android でのスタンドアロン全画面（PWA）起動の実現。
- ヘッダー直下の常設キーワード検索欄（フリーワード・`tag:xxx` DSL 対応、クリアボタン付き）。
- タグ階層ブラウザ（ボトムシート）による全タグ検索・階層ツリー選択と、スマホ完結の「新規タグ作成・編集」機能。
- モバイル表示（画面幅 < 640px）でのワンタップ表示切り替え（「2列グリッド（ギャラリー）」⇄「スリムリスト（高密度エクスプローラー）」）。
- 表示切り替え時における既存機能（ソート順、サムネイルON/OFF、ページング/順次追加、再生数リセット、お気に入り、複数選択一括操作）の完全維持。
- 「表示・ソート設定シート」による、モバイルでの柔軟な並び替え・サムネ・ページング変更。
- モバイル画面での再生ファースト体験（全画面没入型プレイヤー、一覧に戻らない「前へ」「次へ」連続視聴ナビゲーション、横画面追従）。
- すべての子画面（モーダル: 統計、インポート/エクスポート、自動ルール、スマートフォルダ、タグ付与、一括タグ操作）のモバイルレスポンシブ最適化。
- `100dvh` および safe-area-inset による画面崩れ防止、44px タッチターゲットの確保。

**Non-Goals:**

- Avalonia デスクトップアプリ (`TagBasedVideoManager.Renamer`) のアイコン刷新（Webアセットとは独立した別Issueとして管理）。
- Service Worker によるオフライン動画キャッシュ（動画ストリーミングが本質のため、Manifest による PWA 起動に限定し複雑性を排除）。

## Decisions

### 1. ブランドアセットの生成と配置構造

- **決定**: マスターベクター画像として `favicon.svg` を作成し、そこから `favicon.ico`（16x16, 32x32, 48x48 マルチサイズ）、`apple-touch-icon.png` (180x180)、PWA 用 `icon-192.png`, `icon-512.png` を生成して `src/TagBasedVideoManager/wwwroot/` 直下に配置する。
- **理由**: モダンブラウザ（SVG）、レガシー/Windows（ICO）、iOS（apple-touch-icon）、Android PWA（192/512 PNG）のすべての要求を満たす。

### 2. Web App Manifest とスタンドアロン表示

- **決定**: `src/TagBasedVideoManager/wwwroot/manifest.webmanifest` を作成し、`display: "standalone"`, `background_color: "#030712"`, `theme_color: "#030712"`, `orientation: "any"` を指定。
- **理由**: スマホの「ホーム画面に追加」時にブラウザの URL バーが非表示となり、ネイティブアプリ同様の広い表示領域で動画を楽しめる。

### 3. 常設キーワード検索とタグ階層ブラウザ

- **決定**:
  - ヘッダー直下に常にアクセス可能な検索入力欄を配置。`x-model="searchQuery"` により、入力即座にインクリメンタル検索（フリーワードおよび `tag:xxx` DSL）が実行される。
  - クイックタグピルバーの先頭に `[🏷️ 全タグ]` ボタンを配置し、タップで下部からスライドするボトムシート型「タグ階層ブラウザ」を展開。インクリメンタルタグ検索および階層ツリーから選択可能とする。
  - タグブラウザ内、クイックピル末尾、および個別動画タグ付与モーダルから直接起動できる「新規タグ作成モーダル (`tagModal`)」を配備。タグ名・カラーパレット・親タグを指定して即座に作成・反映できるようにする。

### 4. 既存機能を欠落させない表示切り替え & 設定シート

- **決定**:
  - `mobileViewMode` (`'grid'` | `'compact'`) の切り替えは、純粋なカードレイアウト（2列タイル ⇄ 1行高密度）の切り替えに留め、ソート状態、サムネイル表示、再生回数リセット、お気に入り、複数選択、ページング等のロジックは共有のリアクティブプロパティとして完全に維持する。
  - ヘッダーの `[sliders-horizontal]` ボタンから呼び出す「表示・ソート設定シート (`toolbarSheet`)」を新設。モバイル画面でも並び替え基準（登録日時/ファイル名/再生数/時間）、昇順/降順、サムネイル表示ON/OFF、ページング（20/50/100件） vs 順次追加読み込みを快適に切り替えられる。

### 5. モバイル全画面没入型プレイヤーと連続視聴

- **決定**: モバイル画面幅ではプレイヤーモーダルを画面余白ゼロ（`fixed inset-0 z-50 bg-black`）のフルスクリーンとしてレンダリング。
  - プレイヤー下部に「◀ 前の動画」「次の動画 ▶」ボタンを配置。現在ソート・フィルタされている `filteredVideos` 配列のインデックスに基づき、一覧に戻ることなく次々と動画を再生。
  - スマートフォンを横画面（ランドスケープ）にした際、動画表示部が画面全体にフィットし、余計なコントロールは動画タップ時のみオーバーレイ表示。

### 6. 各種子画面（モーダル）のモバイル最適化

- **決定**: 統計・おすすめ (`analyticsModal`)、一括インポート (`bulkModal`)、自動ルール (`ruleModal`)、スマートフォルダ (`smartFolderModal`)、タグ付与 (`addTagModal`)、一括タグ操作 (`batchTagModal`) の各モーダルを、モバイル幅では `max-w-sm w-full mx-auto max-h-[85vh]` のスクロール可能なダイアログまたはボトムシートとしてレンダリングし、画面突き抜けを防ぐ。

### 7. ダイナミックビューポート & Safe Area 対応

- **決定**:
  - `body` および最外層コンテナのスタイルに `min-h-screen` または `h-[100dvh]` を適用。
  - `<meta name="viewport" content="width=device-width, initial-scale=1.0, viewport-fit=cover">` を指定し、CSS の `env(safe-area-inset-top)` / `env(safe-area-inset-bottom)` をパディングに組み込む。
  - タッチ操作を行うボタン類は、タップ判定を最小 44x44px に設計。

## Architecture & Data Flow

```
+-------------------------------------------------------------------------------+
|                                  Client Browser                               |
|                                                                               |
|  [PWA Manifest] -----> Standalone Mode (No browser UI)                        |
|  [Icons / Favicon] --> Tab / Bookmark / Home Screen Icon                      |
|                                                                               |
|  [Main View (100dvh)]                                                         |
|    +-----------------------------------------------------------------------+  |
|    | Header: Logo | Sync [🔄] | Settings [🎛️] | Mode [⊞/≡] | Fav [★] | [≡]   |  |
|    +-----------------------------------------------------------------------+  |
|    | Search Bar: [ 🔍 Keyword or tag:xxx...                       (✕) ]    |  |
|    +-----------------------------------------------------------------------+  |
|    | Tag Pills: [🏷️ All Tags] [All] [#Tag1] [#Tag2] ... [+ Create Tag]     |  |
|    +-----------------------------------------------------------------------+  |
|    | Video View Area (Existing features fully preserved):                  |  |
|    |   Mode 1: 2-Column Visual Gallery (cols-2, thumb, tags, reset, fav)   |  |
|    |   Mode 2: Ultra-dense Explorer List (1-line, compact thumb, fav, tags)|  |
|    |   Pagination: Paging Controls (20/50/100) OR Load More (Incremental)  |  |
|    +-----------------------------------------------------------------------+  |
|    | Batch Selection Bar: Bottom Fixed (safe-area aware)                   |  |
|    +-----------------------------------------------------------------------+  |
|                                                                               |
|  [Sub-screens & Modals (All Mobile-Optimized)]                                |
|    - Fullscreen Player (Continuous Play: Prev / Next, Landscape fit)          |
|    - Tag Hierarchy Browser & Incremental Search Sheet                         |
|    - Tag Create / Edit Modal (Name, Color, Parent)                            |
|    - Display & Sort Settings Sheet (Sort by, Asc/Desc, Thumbnails, Mode)      |
|    - Add Tag to Video Modal / Batch Tag Modal                                 |
|    - Analytics Modal / Bulk TSV Modal / Rule Modal / Smart Folder Modal       |
|    - System Management Drawer                                                 |
+-------------------------------------------------------------------------------+
```

## Risks / Trade-offs

- **[Risk] モーダルの多重起動や階層化による z-index / スクロール衝突**
  - **Mitigation**: Alpine.js において、タグ作成や表示設定を開く際は親シートを閉じるか z-index 階層（シート z-40、モーダル z-50）を明確に分離し、背景スクロール固定を行う。
- **[Risk] 大量タグ登録時のタグブラウザ描画負荷**
  - **Mitigation**: タグブラウザ内にインクリメンタル検索入力を設け、検索時は瞬時に該当タグのみを絞り込み表示する。
