# Design: mobile-experience-and-icons

## Context

TagBasedVideoManager は、バックエンドに F# (.NET 10) + Giraffe、フロントエンドに Alpine.js (CDN) + Tailwind CSS (CDN) を採用した構成であり、Node.js ビルドツールやバンドラーを一切持たない純粋な静的ファイル配信構成となっています。
現状のフロントエンド (`src/TagBasedVideoManager/wwwroot/`) はデスクトップ PC 前提のグリッドおよびリスト表示、マウスホバー操作、上部展開式ツールバーを中心に構成されており、ファビコンやアプリアイコンが存在しません。

詳細な動機・目的については [proposal.md](file:///D:/programming/repos/MyGitHubRepos/TagBasedVideoManager/openspec/changes/mobile-experience-and-icons/proposal.md) を参照してください。

## Goals / Non-Goals

**Goals:**

- ブランドシンボル（Tag + Play を基調とした Indigo/Violet）の SVG および各解像度アイコン（ICO, PNG）の配備と、404 なしの確実な配信。
- `manifest.webmanifest` による Web App Manifest の提供と、iOS / Android でのスタンドアロン全画面（PWA）起動の実現。
- モバイル表示（画面幅 < 640px）でのワンタップ表示切り替え（「2列グリッド（ギャラリー）」⇄「スリムリスト（高密度エクスプローラー）」）。
- モバイル画面での再生ファースト体験（全画面没入型プレイヤー、一覧に戻らない「前へ」「次へ」連続視聴ナビゲーション、横画面追従）。
- ヘッダーへの即時ディレクトリ同期ボタン配置と、管理項目のドロワーメニューへの退避。
- `100dvh` および safe-area-inset による画面崩れ防止、44px タッチターゲットの確保。

**Non-Goals:**

- Avalonia デスクトップアプリ (`TagBasedVideoManager.Renamer`) のアイコン刷新（Webアセットとは独立した別Issueとして管理）。
- Service Worker によるオフライン動画キャッシュ（動画ストリーミングが本質のため、Manifest による PWA 起動に限定し複雑性を排除）。
- モバイルでの複雑なタグ階層編集・自動適用ルール編集（モバイルは視聴・検索・簡易タグ付け・お気に入り管理に特化）。

## Decisions

### 1. ブランドアセットの生成と配置構造

- **決定**: マスターベクター画像として `favicon.svg` を作成し、そこから `favicon.ico`（16x16, 32x32, 48x48 マルチサイズ）、`apple-touch-icon.png` (180x180)、PWA 用 `icon-192.png`, `icon-512.png` を生成して `src/TagBasedVideoManager/wwwroot/` 直下に配置する。
- **理由**: ビルドツールなしのプロジェクト規約を維持しつつ、モダンブラウザ（SVG）、レガシー/Windows（ICO）、iOS（apple-touch-icon）、Android PWA（192/512 PNG）のすべての要求を満たす。
- **代替案**:
  - _SVG のみ配信_: iOS Safari や一部の Windows 環境でホーム画面アイコンやファビコンが正常認識されないため不採用。

### 2. Web App Manifest とスタンドアロン表示

- **決定**: `src/TagBasedVideoManager/wwwroot/manifest.webmanifest` を作成し、`display: "standalone"`, `background_color: "#030712"`, `theme_color: "#030712"`, `orientation: "any"` を指定。
- **バックエンド対応**: Giraffe の静的ファイル配信で `.webmanifest` 拡張子に対して `application/manifest+json` の MIME タイプが正しく設定されるよう、ASP.NET Core の `FileExtensionContentTypeProvider` または静的ファイルオプションを確認・設定。
- **理由**: スマホの「ホーム画面に追加」時にブラウザの URL バーが非表示となり、ネイティブアプリ同様の広い表示領域で動画を楽しめる。

### 3. モバイルメイン画面レイアウトと表示モード切り替え

- **決定**: Alpine.js の状態に `mobileViewMode` (`'grid'` | `'compact'`) を追加。
  - **ヘッダー**:
    - 左: ロゴバッジ + アプリタイトル
    - 右: [同期 🔄]、[表示切替 ⊞/≡]、[お気に入り ★]、[メニュー ≡]
    - 管理機能（生成停止、インポート、ルール作成、統計等）はメニュー [≡] ドロワーの最下部へ移動。
  - **クイックタグフィルタ**: ヘッダー直下に横スクロール可能なタグピルバー（`overflow-x-auto flex space-x-2 no-scrollbar`）を配置。
  - **2列グリッドモード**: `grid grid-cols-2 gap-2.5`、アスペクト比 16:9 サムネイル、下部にタイトルと押しやすい ★ ボタン。
  - **スリムリストモード**: 1行高密度リスト、再生アイコン/極小サムネ、タイトル、再生時間、ファイルサイズ、★ ボタンを横並びに配置。
- **理由**: ユーザーが探索したい目的に合わせて、ビジュアル重視と一覧性重視を瞬時に切り替えられる。

### 4. モバイル全画面没入型プレイヤーと連続視聴

- **決定**: モバイル画面幅ではプレイヤーモーダルを画面余白ゼロ（`fixed inset-0 z-50 bg-black`）のフルスクリーンとしてレンダリング。
  - プレイヤー下部に「◀ 前の動画」「次の動画 ▶」ボタンを配置。現在ソート・フィルタされている `sortedVideos` 配列のインデックスに基づき、一覧に戻ることなく次々と動画を再生。
  - スマートフォンを横画面（ランドスケープ）にした際、動画表示部が画面全体にフィットし、余計なコントロールは動画タップ時のみオーバーレイ表示。
- **理由**: スマホでの動画鑑賞体験において「一覧に戻って次の動画を選ぶ」という往復の手間を解消し、スムーズな連続視聴を実現する。

### 5. ダイナミックビューポート & Safe Area 対応

- **決定**:
  - `body` および最外層コンテナのスタイルに `min-h-screen` または `h-[100dvh]` を適用。
  - `<meta name="viewport" content="width=device-width, initial-scale=1.0, viewport-fit=cover">` を指定し、CSS の `env(safe-area-inset-top)` / `env(safe-area-inset-bottom)` をパディングに組み込む。
  - タッチ操作を行うボタン類（★、再生、閉じる、同期、トグル）は、アイコンサイズ自体が小さくても周囲に `p-2.5` などのパディングを持たせ、タップ判定を最小 44x44px に設計。
- **理由**: iOS Safari のアドレスバー伸縮による下部一括操作バーのめり込みや、ノッチ・ホームバーによる操作遮害を完全に防ぐ。

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
|    | Header: Title | Sync [🔄] | Mode Toggle [⊞/≡] | Fav [★] | Drawer [≡]  |  |
|    +-----------------------------------------------------------------------+  |
|    | Tag Pills: [All] [Fav] [#Tag1] [#Tag2] ... (Horizontal Scroll)        |  |
|    +-----------------------------------------------------------------------+  |
|    | Video View Area:                                                      |  |
|    |   If mobileViewMode === 'grid':    2-Column Visual Gallery (cols-2)   |  |
|    |   If mobileViewMode === 'compact': Ultra-dense Explorer List (1-line) |  |
|    +-----------------------------------------------------------------------+  |
|    | Batch Selection Bar: Bottom Fixed (safe-area-inset-bottom aware)      |  |
|    +-----------------------------------------------------------------------+  |
|                                                                               |
|  [Fullscreen Modal Player] (When selectedVideo !== null)                      |
|    - Aspect ratio video stream (HTTP 206)                                     |
|    - Continuous playback: [◀ Prev Video]  [▶ Next Video]                      |
|    - Landscape auto-fit                                                       |
+-------------------------------------------------------------------------------+
                                      |
                      HTTP API Requests / Static Assets
                                      v
+-------------------------------------------------------------------------------+
|                      Giraffe / ASP.NET Core Backend                           |
|  - GET /favicon.ico, /favicon.svg, /apple-touch-icon.png, /manifest.webmanifest |
|  - POST /api/scan (Directory Sync)                                            |
|  - GET /api/videos/{id}/stream (HTTP 206 Range Stream)                        |
+-------------------------------------------------------------------------------+
```

## Risks / Trade-offs

- **[Risk] モバイルとデスクトップの UI 切り替えによる複雑化**
  - **Mitigation**: Tailwind CSS のブレークポイント（`sm:`, `md:`）と Alpine.js の状態管理を組み合わせ、PC表示では従来の多機能ツールバーや3段階グリッド（小・中・大）をそのまま維持し、モバイル幅時のみ最適化されたヘッダーおよび 2 列グリッド/スリムリストが有効になるよう分離する。
- **[Risk] ブラウザごとの Web App Manifest キャッシュ**
  - **Mitigation**: `index.html` にて `<link rel="manifest" href="/manifest.webmanifest?v=1">` のようにバージョンクエリパラメータを付与し、アセット更新が確実に反映されるようにする。
- **[Risk] 大量動画表示時のスリムリスト描画パフォーマンス**
  - **Mitigation**: 既存のページング（20/50/100件）および追加読み込み（incrementalCount）の仕組みをそのままスリムリストにも適用し、DOM要素の過剰生成を防ぐ。

## Migration Plan

1. 新規アセット（SVG, ICO, PNG, Web App Manifest）の配置。
2. `src/TagBasedVideoManager/wwwroot/index.html` および `app.css` のレスポンシブ更新。
3. デスクトップ環境およびモバイルエミュレーション環境での回帰確認。
4. 本変更は純粋なフロントエンド/静的アセットの拡張であり、データベーススキーマ変更や外部依存の変更を伴わないため、ロールバック時は該当ファイルの `git checkout` のみで即座に復元可能。
