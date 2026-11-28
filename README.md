# TagBasedVideoManager

TagBasedVideoManagerは、バイクツーリングの記録、旅行動画、大切な家族のホームビデオなどの動画ファイルを、タグ情報や自動適用ルールに基づいて高度に分類・検索・整理できる、Webベースのパーソナル動画管理システムです。
ホストマシンの動画ディレクトリを読み取り専用でマウントし、非同期でメタデータ取得やサムネイル生成をバックグラウンド実行します。

---

## 主な機能 (Features)

### 1. 動画スキャン & メディア処理

- **即時DB登録**: 動画フォルダをスキャンした際は、ファイルシステム情報から最小限の情報だけを即座にデータベースへ登録します。動画の長さやサムネイルはバックグラウンドの非同期キューで並行処理されるため、スキャン操作が非常に軽快です。
- **決定論的UUID（UUID v5）による移動・リネーム・マウント変更自動追従**: 動画ファイル本体を一切変更せず（Read-Only サンプリング）、ファイルサイズと先頭・中間・末尾 64KB のデータから RFC 9562 準拠の UUID v5 を算出。サブフォルダ移動、ファイル名リネーム（AI File Renamer 等）、および Docker バインドマウント変更時でも既存の動画 ID、タグ、お気に入り、再生回数、再生時間を自動引き継ぎし、FFmpeg / ffprobe による重い再処理を完全にスキップします。
- **不在レコード & 孤児サムネイルの自動クリーンアップ**: ディスク上から物理削除された動画の DB レコード・タグ紐付け・サムネイル画像、および DB に対応動画が存在しない孤児サムネイル画像をスキャン時に自動削除します。万一の走査例外時（ネットワーク瞬断や権限エラー）にも誤削除を防ぐ防御ガードを備えています。
- **サムネイル高速生成**: FFmpegのデコード負荷を避けるため、まず「高速シーク（コンテナシーク）」を試行し、失敗時のみ「安全シーク（デコードシーク）」へ自動フォールバックする2段階生成を導入しています。
- **デッドロック防止と並行制御**: ffprobeによるメタデータ取得はセマフォ制限から除外して非同期・並列実行し、FFmpegプロセス呼び出し時の標準出力と標準エラー出力を並行タスクで読み取ることでデッドロックを防止しています。

### 2. 自己参照階層型タグ

- **ツリー構造**: 親子階層関係を持つタグを無制限に定義できます。
- **動的件数バッジ**: 各タグに紐づく動画件数をSQLの再帰CTE（階層問い合わせ）により自動集計し、ツリーの横にバッジとして常時表示します。
- **開閉・レイアウト開閉**: Alpine.jsによりツリー構造 of 開閉状態を保持・トグル可能です。

### 3. 自動適用ルール & 遡及適用

- **詳細なルール条件**: 動画のタイトルやパス、親フォルダ名に対して、部分一致・前方一致・後方一致・正規表現一致、ファイルサイズ範囲などを細かく指定してタグを自動紐付けできます。
- **遡及適用**: 新しいルールを作成した際、すでに登録されている既存の動画に対してもバックグラウンドで非同期にルールを遡及適用できます。

### 4. 高度な AND/OR 検索 & スマートフォルダ

- **検索DSL**: 検索欄に「`tag:タグ名`」や「`is:favorite`」、「`and`/`or`」を用いた構文を入力して高度な論理演算フィルタリングが可能です。
- **スマートフォルダ**: 検索条件をワンクリックで保存し、サイドバーから即座に適用できます。

### 5. 再生履歴の記録・分析・おすすめ機能

- **再生回数と日時の記録**: シークバー操作の多重カウントを排除し、専用のAPIを介して再生開始時のみカウントをインクリメントします。
- **あなたへのおすすめ**: 再生履歴のファイル名形態素/キーワードを独自のハイブリッド抽出アルゴリズムで解析し、未再生動画から優先的にお勧め動画を嗜好キーワード付きで自動レコメンドします。
- **再生数ランキング**: 最多再生動画トップ100を簡単に確認できます。
- **例外ログモニタリング**: アプリ内で発生した例外をDBに保存し、管理者ダッシュボードからスタックトレースを展開表示可能です。

### 6. 一括設定インポート・エクスポート

- タグ階層、自動ルール、スマートフォルダの設定情報を、WebAPIフォーマット（TSV / JSON）で一括インポート・エクスポートできます。

### 7. デスクトップコンパニオン: AI File Renamer (長パス短縮＆Docker制御)

- **Dockerバインドマウント（WSL2/9p）の260文字制限対策**: ホストOS上の動画フォルダを走査し、240文字以上の長パス危険ファイルを自動抽出。
- **スキャン直後の即時IO描画 & 非同期逐次提案パイプライン**: スキャン完了直後に全対象ファイルを即時一覧表示。並行度制御（1〜2並行）により1件ずつ非同期に行アニメーションを表示しながら提案を順次反映。
- **DuckDuckGo Search (ddgs) 連携**: 作品名・タイトルのWeb検索結果（上位スニペット）をLLMプロンプトに注入し、高精度な正式名称・短縮理由を提案（Python未導入時は安全に通常プロンプトへ自動フォールバック）。
- **3行常時固定表示レイアウト & AFTERラベル警告**: 各カードで「旧ファイル名 (BEFORE)」「新ファイル名 (AFTER)」「AIコメント (AI COMMENT)」を常時固定表示。AFTER行は緑色基調とし、閾値抵触時は「AFTER (〇〇字 [危険])」のラベルバッジ背景のみを赤色警告表示。
- **操作性・誤リネーム防止 & 中止機能**: AI提案中はリネームボタンを非活性化して進捗バッジを表示。ツールバーに「⏹ 中止」ボタンを常時配置し、新条件での即時再抽出・提案リスタートにも対応。
- **シンボリックリンク／ジャンクション安全走査**: ディレクトリジャンクションを解釈・追跡し、循環参照（同一ディレクトリ再帰）を自動検知して安全にスキップ。
- **OpenRouter モデルによる短縮AI提案**: 命名規則（日時_タイトル等）に基づき、Free/有料モデルを活用して安全な短縮ファイル名およびAIコメントを常時提示。
- **ワンクリック復旧 & Undo**: 物理リネーム実行と同時にDockerコンテナを自動再起動して即時マウント復旧。直前のリネームを元のファイル名に完全復元する逆リネーム（Undo）に対応。
- **柔軟な設定管理 (.NET標準 `appsettings.json` / `appsettings.{Environment}.json`) & 設定再読込**: 設定ファイルはベーステンプレート `appsettings.json` と環境別設定 `appsettings.{Environment}.json`（`Development` / `Production` 等）の階層マージ構成を採用し、読み込み・保存ともに共通の 2 段階ディレクトリ解決で統一。アクティブ環境名や保存先ファイル名（例: `ENV: Development` / `💾 appsettings.Development.json`）をメイン画面に常時バッジ表示します。GUI 操作時は変更対象（`selectedModel` / `rules`）のみを既存キー非破壊で部分更新（Patch Save）し、ファイル手編集後もコントロールパネルまたはバナーの「🔄 設定を再読込」ボタンからアプリ再起動なしで即座に反映できます。秘密情報・個人パスは Git から完全に除外されます。
- **ローディングスピナー & Zero-State / 個別再試行**: スキャン中の中央回転スピナー、フォルダ未設定時の Zero-State 誘導、スキャン完了後の 0 件出し分け（動画 0 本 vs 全件安全）、個別 AI 提案エラー時の赤枠カード＋「↻ 再試行」ボタン、ファイルリネーム実行中の操作ブロック半透明暗転オーバーレイ、Docker 制御中の小型スピナーなど、すべての非同期処理で洗練された視覚フィードバックを提供。
- **100% F# .NET 10 + Avalonia.FuncUI Elmish MVU**: 既存Webアプリのコードに一切手を加えない（改修行数ゼロ）完全独立デスクトップアプリ。

### 8. モバイルファースト UX & PWA / Web ブランディング

- **モバイル専用レスポンシブ UI (< 768px)**: 常設キーワード検索バー、ボトムシート型タグ階層ブラウザ、6色カラーパレット付き新規タグ作成、表示・ソート設定シート、100dvh & safe-area-inset 対応。
- **2列グリッド ⇄ スリムリストのワンタップ切り替え**: サムネイル重視の2列ギャラリー表示と、ファイル名重視の超高密度スリムリスト表示を即座に相互切り替え可能（既存機能の欠落ゼロ）。
- **全画面没入型プレイヤー & 連続視聴ナビゲーション**: 画面余白を排除した全画面プレイヤーで、一覧に戻ることなく「前の動画」「次の動画」へシームレスに移動可能。横向き（ランドスケープ）自動追従。
- **ブランドアプリアイコン & PWA スタンドアロン起動**: SVG/ICO ファビコンおよびマルチサイズ PNG アイコン（180/192/512）、Web App Manifest によるホーム画面追加と全画面スタンドアロン起動に対応。

### 9. 稼働バージョン表示 & セマンティックバージョニング (SemVer 2.0.0)

- **Web アプリ画面ヘッダーバッジ**: 画面上部のアプリケーションタイトル横に稼働バージョン（例: `v0.3.0`）をピルバッジとして表示。モバイル画面でのタイトル折り返し時も自然に追従し、`<title>` タブタイトルにも反映。
- **キャッシュ制御 & 耐障害非同期取得**: `GET /api/version` エンドポイント（`Cache-Control: no-cache, no-store, must-revalidate`）経由でアセンブリバージョンを配信。2 秒タイムアウトと非ブロック非同期取得（Bulkhead パターン）により、障害時でも動画一覧の読み込みを一切阻害しません。
- **デスクトップアプリ (AI File Renamer) タイトルバー表示**: OS ウィンドウタイトルバー（`TagBasedVideoManager - AI File Renamer v0.3.0`）および起動コンソールログにバージョンを表示・出力。
- **変更規模連動の SemVer 規則**: Web アプリとデスクトップアプリで独立してバージョンを管理し、OpenSpec の変更規模（L1: Patch, L2: Minor, L3/ユーザー指示: Major）に応じてインクリメントします。

### 10. 非同期ローディングスピナー & Zero-State / Error-State ガード (Web UI)

- **3 系統独立ローディング表示**: 動画一覧（初回中央スピナー / 検索時半透明オーバーレイ＋クリック遮断）、左サイドバーのタグ一覧、および視聴分析モーダル（ランキング・おすすめ・エラーログ各タブ）の通信状態を独立管理し、通信中に専用のローディングスピナーを表示。
- **Zero-State 誤表示の構造的遮断**: 初回読み込み中やディレクトリ自動スキャン（`isScanning`）実行中の「動画が見つかりませんでした」誤表示を完全防止。データ 0 件時は「ディレクトリ同期を実行する」「検索条件をクリアする」「タグ管理を開く」等の回復アクションボタンを提示。
- **タイムアウト制御 & レースコンディション防止**: 10 秒タイムアウト付き `fetchWithTimeout` による自動中断と Error-State（「再試行」ボタン）、およびリクエスト ID 管理による連続入力・連続タグ切り替え時の古いレスポンス破棄と途切れなきスピナー維持を実現。

---

## 画面イメージ (Screenshots)

### メイン画面 (動画ライブラリ)

![メイン画面 (動画ライブラリ)](./assets/images/screenshot_main.png)
_(動画の一覧、お気に入り登録、複数選択、タグによる絞り込み等の操作を行うメインUIです)_

### 動画再生モーダル

![動画再生モーダル](./assets/images/screenshot_player.png)
_(HTTP 206 Rangeリクエストによるスムーズなシークに対応した組み込み動画プレイヤーです)_

### 分析ダッシュボード・エラーログ

![分析ダッシュボード](./assets/images/screenshot_dashboard.png)
_(再生ランキング、あなたへのおすすめ動画リスト、およびシステム例外エラーログが確認できます)_

### 設定管理モーダル

![設定管理モーダル](./assets/images/screenshot_settings.png)
_(タグ階層のCRUD、自動ルールの定義、およびTSV/JSONによる設定の一括入出力を行うUIです)_

---

---

## データベース設計 (SQLite Database Schema)

本システムはローカル運用のための軽量・単一ファイルデータベースとして SQLite を採用しています。バックエンド起動時にテーブルが自動マイグレーション・初期化され、`ErrorLogs` テーブルは自動クリーンアップされます。

```sql
-- 1. 動画メタデータテーブル
CREATE TABLE Videos (
    Id TEXT PRIMARY KEY,
    FileName TEXT NOT NULL,
    FilePath TEXT NOT NULL UNIQUE,
    Duration INTEGER NOT NULL,
    FileSize INTEGER NOT NULL,
    ThumbnailPath TEXT,
    IsFavorite INTEGER DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    AccessCount INTEGER DEFAULT 0,
    LastAccessedAt TEXT,
    FileUuid TEXT
);

-- 2. タグ管理テーブル（自己参照階層）
CREATE TABLE Tags (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL UNIQUE,
    ColorCode TEXT DEFAULT '#71717a',
    ParentId TEXT,
    FOREIGN KEY (ParentId) REFERENCES Tags(Id) ON DELETE SET NULL
);

-- 3. ビデオ・タグ中間テーブル（多対多）
CREATE TABLE VideoTags (
    VideoId TEXT NOT NULL,
    TagId TEXT NOT NULL,
    PRIMARY KEY (VideoId, TagId),
    FOREIGN KEY (VideoId) REFERENCES Videos(Id) ON DELETE CASCADE,
    FOREIGN KEY (TagId) REFERENCES Tags(Id) ON DELETE CASCADE
);

-- 4. 自動タグ付けルールテーブル
CREATE TABLE TaggingRules (
    Id TEXT PRIMARY KEY,
    Pattern TEXT NOT NULL,
    TagId TEXT NOT NULL,
    MatchType TEXT DEFAULT 'partial', -- prefix, suffix, partial, regex
    TargetField TEXT DEFAULT 'fileName', -- fileName, filePath, folderName
    MinSize INTEGER, -- 最小ファイルサイズ (MB)
    MaxSize INTEGER, -- 最大ファイルサイズ (MB)
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (TagId) REFERENCES Tags(Id) ON DELETE CASCADE
);

-- 5. スマートフォルダテーブル
CREATE TABLE SmartFolders (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Query TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);

-- 6. 例外エラーログテーブル
CREATE TABLE ErrorLogs (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Message TEXT NOT NULL,
    StackTrace TEXT,
    CreatedAt TEXT NOT NULL
);
```

## 動作環境 (Requirements)

- **OS**: Windows 11 / Linux
- **ランタイム**: .NET 10 SDK (ローカル開発時)
- **データベース**: SQLite 3
- **コンテナ環境**: WSL 3.0 (wslc) / Docker / Podman

### GPUアクセラレーション対応

FFmpegによるサムネイル生成時にホストマシンのグラフィックボードを利用可能です。

- **NVIDIA GPU**: CUDAアクセラレーション
- **AMD GPU**: VAAPI/DirectX支援
- **CPUのみ**: 通常のソフトウェアデコード

---

## セットアップと起動手順 (Setup & Usage)

### 1. リポジトリのクローン

```bash
git clone https://github.com/your-username/TagBasedVideoManager.git
cd TagBasedVideoManager
```

### 2. アプリケーション設定 (`appsettings.json` / `appsettings.{Environment}.json`)

TagBasedVideoManager は .NET 標準の構成思想に準拠し、設定を `appsettings.json`（ベーステンプレート）および環境別設定ファイル `appsettings.{Environment}.json`（本番用: `appsettings.Production.json`、開発用: `appsettings.Development.json`）で管理します（※従来の `.env` 方式は廃止されました）。
環境別設定ファイルは `.gitignore` により Git 管理から除外されているため、個人パスや API キーを安全に管理できます。

#### Web アプリケーション / デプロイ設定

`src/TagBasedVideoManager/` 配下に環境別設定ファイル（通常は本番用 `appsettings.Production.json`）を作成し、管理対象の動画ディレクトリなどを設定します。

```json
{
  "PORT": 5620,
  "VIDEO_DIRS": "C:\\Users\\YourUserName\\Videos;D:\\AnotherVideos",
  "THUMBNAIL_DIR": "thumbnails",
  "DATABASE_PATH": "metadata.db",
  "DATA_DIR": "./data"
}
```

※デプロイスクリプト（`scripts/deploy.ps1` / `scripts/deploy.sh`）は、実行時に `src/TagBasedVideoManager/appsettings.json` および `appsettings.Production.json` を自動的に読み込み、コンテナを構成します（CLI 引数や環境変数で上書きも可能）。

##### `DATA_DIR` / `VIDEO_DIRS` のパス指定仕様

| パス形式                                                                                 | `scripts/deploy.ps1` (Windows / WSL 3.0)                                                                                                                   | `scripts/deploy.sh` (Linux / Docker / Podman)                                                                  |
| :--------------------------------------------------------------------------------------- | :--------------------------------------------------------------------------------------------------------------------------------------------------------- | :------------------------------------------------------------------------------------------------------------- |
| **相対パス**（例: `./data`）                                                             | リポジトリルート基準の絶対パスへ解決し、`D:/.../data` 形式でマウント（`DATA_DIR` 未設定・空白時も `./data` にフォールバック）                              | リポジトリルート（`${REPO_ROOT}`）基準の絶対パスへ正規化してマウント（実行時カレントディレクトリに依存しない） |
| **Windows 絶対パス**（例: `D:\tbvm\data`, `D:\My Videos\Clips`）                         | スラッシュ区切り（`D:/tbvm/data`, `D:/My Videos/Clips`）へ変換してマウント（空白保持）                                                                     | 非対応（ビルド・停止前にエラー終了し Linux パス設定例を案内）                                                  |
| **WSL UNC パス**（例: `\\wsl.localhost\Ubuntu-24.04\home\user\tbvm\data`, `\\wsl$\...`） | `\\wsl.localhost\<Distro>\<Path>` へ正規化し WSL 側へ事前作成・存在確認してマウント                                                                        | `WSL_DISTRO_NAME` が一致する WSL 環境内のみ `/home/user/tbvm/data` へ解決（不一致・未設定時はエラー終了）      |
| **Linux 絶対パス**（例: `/home/user/tbvm/data`）                                         | `docker-desktop` 等を除いた既定ユーザー WSL ディストロを取得し `\\wsl.localhost\<Distro>\home\user\tbvm\data` へ解決（Windows ドライブ直下には作成しない） | そのまま正規化してマウント                                                                                     |

> [!NOTE]
>
> - 空白文字を含むパス（例: `D:\My Data\tbvm`, `/mnt/My Videos/Clips`）も分割されず 1 つのマウント引数として保持されます。
> - ドライブ直下/相対（`D:\`, `D:`, `D:data`, `/` 単体）、セグメント境界の `..` トラバーサル（`v1..v2` のようなフォルダ名の一部は許可）、および通常ファイルの誤指定は、コンテナイメージのビルドや既存コンテナの停止前にエラーとして拒否されます。

#### AI File Renamer (デスクトップアプリ) 設定

`src/TagBasedVideoManager.Renamer/` 配下（リポジトリ外の単体配置時は実行ファイルと同階層）に `appsettings.Development.json`（開発・デバッグ実行時）または `appsettings.Production.json`（Release 実行時）を作成します。API キーのみを設定する場合は、不要項目を省略した以下の最小 JSON で動作します（未指定項目はベース `appsettings.json` または組み込み既定値で自動補完されます）。

```json
{
  "apiKey": "sk-or-v1-..."
}
```

※実際の運用時は `sk-or-v1-...` を取得済みの有効な OpenRouter API キーに置き換えてください。GUI でモデルや命名規則を変更・保存した場合も、ベース `appsettings.json` は変更せず、アクティブ環境に対応する `appsettings.{Environment}.json` へ変更キー（`selectedModel` / `rules`）のみを部分更新（Patch Save）するため、既存の `apiKey` や他設定は保持されます。設定ファイルを手動編集した際は、アプリを再起動せず「🔄 設定を再読込」ボタンから即座に反映できます。

> [!IMPORTANT]
> **VIDEO_DIRS（複数フォルダ指定）に関する重要事項**:
>
> - **旧 `VIDEO_DIR` からの移行**: Web アプリ本体およびコンテナ環境では旧 `VIDEO_DIR` は廃止され、`VIDEO_DIRS` に一本化されました。未設定や旧変数の残存時は設定例付きエラーとなり起動・スキャンできません。
> - **複数物理フォルダの指定**: セミコロン（`;`）で区切ることで、ドライブや物理パスの異なる複数の動画フォルダを同時に管理できます。
> - **ルート名タグの自動導出**: 各フォルダのフォルダ名（Leaf 名）およびそのサブフォルダ名が、スキャン時に動画タグとして自動付与されます（重複タグは統合）。
> - **ドライブ直下の禁止**: `D:\` や `/` などのルートパスは、コンテナマウント先フォルダ名を決定できないため指定できません。必ずサブフォルダを指定してください。
> - **フォルダ名重複の禁止**: 指定する複数フォルダのフォルダ名（大文字小文字を区別せず）が重複している場合、マウント先衝突エラーとなります。異なるフォルダ名を指定してください。
> - **Docker 環境での `FilePath` 変更に関する注意**: Docker / コンテナ環境では、各フォルダが `/app/videos/<フォルダ名>` に読み取り専用マウントされます。ホスト側ローカル実行から Docker コンテナ実行への切り替えやマウント設定の変更により `FilePath` が変化した場合でも、決定論的 UUID（UUID v5）およびファイルサイズ等のフォールバックにより既存の動画 ID・タグ・視聴履歴・サムネイルが自動的に引き継がれます（DB ファイルの再作成は不要です）。

### 3. コンテナ起動 (Web アプリ)

本アプリは単一コンテナ構成であり、直接 OCI ランタイムから起動できます。
Windows 11（WSL 3.0 / Docker Desktop）および Linux（Docker / Podman）向けに、ワンクリックでビルド・再起動・ヘルスチェックおよび GPU アクセラレーション自動設定を行うデプロイスクリプトを用意しています。スクリプトは `src/TagBasedVideoManager/appsettings.Production.json` から設定を自動取得します。

- **Windows 11 (WSL 3.0 または Docker Desktop)**:
  PowerShell で以下を実行します。WSL 3.0 (`wslc`) または `docker` を自動検出し、ポート 5620 の事前チェック、旧コンテナの安全な停止・削除、イメージビルド、コンテナ再起動（NVIDIA GPU 検出時は `--gpus all` を自動付与）、およびヘルスチェック（失敗時は自動で `logs` を表示）を実行します。

  ```powershell
  pwsh ./scripts/deploy.ps1
  ```

  ※WSL 3.0 (`wslc`) 利用時は Docker Desktop なしで直接コンテナを稼働できます。

- **Linux (Docker または Podman)**:
  Bash で以下を実行します。`docker` または `podman` を自動検出し、NVIDIA GPU（`--gpus all`）または AMD GPU（`/dev/dri`）の自動パススルーを含めてデプロイします。

  ```bash
  ./scripts/deploy.sh
  ```

ブラウザから `http://localhost:5620` にアクセスします。

> [!TIP]
>
> - WSL 3.0 側にデータ（SQLite DB やサムネイル）を永続化する場合は、`appsettings.Production.json` の `DATA_DIR` に `/home/<user>/tbvm/data` または `\\wsl.localhost\<Distro>\home\<user>\tbvm\data` を設定してください。
> - 既存コンテナの `/app/data` マウント元から別の `DATA_DIR` へ変更して再デプロイする場合、ビルド・停止前に旧パス・新パス・旧データ非削除・移行コピーコマンド（`Copy-Item` / `cp -a`）を含む警告が出力されます（詳細は [PRODUCTION_ENVIRONMENT.md](./PRODUCTION_ENVIRONMENT.md) を参照）。

### 4. AI File Renamer の構成設定 (`appsettings.json` / `appsettings.{Environment}.json`)

AI File Renamer は ASP.NET Core / .NET 標準の構成思想に準拠し、ベース構成ファイル `appsettings.json` および環境別設定ファイル `appsettings.{Environment}.json`（`appsettings.Development.json` や `appsettings.Production.json` 等）から各種設定を読み込み・マージします（JSON コメント `//` および末尾カンマも許容）。

#### (1) 環境名の解決と階層オーバーライドマージ

- **環境名の解決順序**:
  1. OS 環境変数 `DOTNET_ENVIRONMENT`
  2. OS 環境変数 `ASPNETCORE_ENVIRONMENT`
  3. コンパイル時ビルド構成（Debug ビルド時は `Development`、Release ビルド時は `Production`）
     ※不正文字や空文字（パストラバーサル等）が指定された場合は安全な既定値（`Development` / `Production`）に自動フォールバックします。
- **階層マージとプレースホルダー保護**:
  ベースとなる `appsettings.json` を読み込んだ上で、同一ディレクトリ内の環境別設定ファイル（例: `appsettings.Development.json`）が存在する場合、キー単位でオーバーライドマージされます（未定義キーはベース値または組み込み既定値が維持・補完されます）。
  上位ファイルにプレースホルダー（`"xxx"`, `"sk-or-v1-..."` 等）・空文字・不正形式の `apiKey` が記述されている場合は下位の有効な `apiKey` を上書きせず保護し、警告バナーとログで通知します（明示的に無効化したい場合のみ環境別ファイルへ `"apiKey": null` を指定してください）。

#### (2) 読込・保存共通の 2 段階ディレクトリ解決と優先順位

読み込みと保存で常に同一の共通ディレクトリを使用し、リポジトリ構成と単体配置の双方で設定ファイルのズレを防ぎます。

1. **第 1 段階（リポジトリ内 `src/TagBasedVideoManager.Renamer/` 優先探索）**:
   - まず作業ディレクトリを起点に親方向へ最大 8 階層上（計 9 ステップ）まで遡り、`src/TagBasedVideoManager.Renamer/` 配下に `appsettings.json` または `appsettings.{Environment}.json` が存在する場合に採用します。
   - 見つからない場合は、実行アセンブリ基点ディレクトリ（`AppDomain.CurrentDomain.BaseDirectory`）を起点に同様に親方向へ最大 8 階層上まで遡って探索します。
2. **第 2 段階（単体配置フォールバック）**:
   - リポジトリ外の単体配置等で第 1 段階に該当がない場合は、実行アセンブリ基点ディレクトリ（`AppDomain.CurrentDomain.BaseDirectory`）を共通ディレクトリとして採用します。
3. **共通ディレクトリ内のファイル優先順位**:
   - `appsettings.{Environment}.json`（最優先オーバーライド・GUI 保存先）
   - `appsettings.json`（ベーステンプレート）
   - 組み込み既定値

※**`.env` および汎用 OS 環境変数の非参照**: システム全体や別アプリケーションの意図しない環境変数が混入・干渉することを防ぐため、`.env` ファイルおよび OS 環境変数（`VIDEO_DIR`, `OPENROUTER_API_KEY` 等）からは設定値を一切読み込みません（`.env` や旧保存先ファイルが残存している場合は起動時に移行案内の診断ログを出力します）。

#### (3) GUI 設定保存（Patch Save）と「🔄 設定を再読込」導線

- **部分更新（Patch Save）と既存キー保護**:
  GUI 操作でモデル選択や命名規則を変更・保存すると、ベース `appsettings.json` は書き換えられず、解決された共通ディレクトリ内の **`appsettings.{Environment}.json`** に対して変更対象キー（`selectedModel` または `rules`）のみが部分更新されます。
  - 既に定義されている `apiKey`, `targetDirectory`, `pathLengthThreshold` や未知キーは一切変更・消失しません。
  - `appsettings.{Environment}.json` が未存在の場合は、変更対象キーのみを含む最小 JSON として新規作成され、未変更の `apiKey` 等を `null` で書き出して下位設定を覆い隠すことはありません。
  - ※ JSONC コメント（`//`）および末尾カンマは**読み込み時のみ許容**されます。GUI から設定保存（Patch Save）を実行すると再シリアライズにより対象 `appsettings.{Environment}.json` 内の `//` コメントと末尾カンマは保持されず除去されます（他キー・値はそのまま保持されます）。
  - パス単位の排他制御および一時ファイル（`.tmp`）経由のアトミック置換により、競合や書き込み中の破損を防止します。書き込み不可や既存 JSON 構文エラー時は上書き破壊を行わず、メモリ上の変更・確認ダイアログを安全にロールバック／閉鎖した上でエラー通知を行います。
- **「🔄 設定を再読込」による再起動不要の即時反映**:
  エディタ等で `appsettings.{Environment}.json` を修正した後、アプリを再起動することなく反映できます。
  - メイン画面のコントロールパネルに常設された **「🔄 設定を再読込」** ボタン（スキャン／リネーム実行中は非活性）、またはエラー発生時に表示される **エラーバナー**（ルール管理モーダル開時はモーダル上部バナー）内の **「🔄 設定を再読込」** ボタンをクリックすると最新の設定が即座に読み込まれます。
  - 再読込時も画面上で選択済みの対象フォルダ（`targetDirectory`）は維持されるため、API キー設定後そのまま AI 提案や「↻ 再試行」を実行できます。

#### (4) 設定ファイルの書式例（camelCase）

不要項目は省略可能で、未指定項目はベース `appsettings.json` または組み込み既定値で安全に補完されます。

- **最小 JSON 書式例（`appsettings.Development.json` 等に API キーのみ指定する場合）**:

```json
{
  "apiKey": "sk-or-v1-..."
}
```

- **全項目の記述例（`src/TagBasedVideoManager.Renamer/appsettings.json` 実ファイル準拠）**:

```json
{
  "targetDirectory": "",
  "pathLengthThreshold": 240,
  "selectedModel": "meta-llama/llama-3.3-70b-instruct:free",
  "apiKey": null,
  "rules": [
    {
      "id": "rule-code-name-actor",
      "name": "品番_要約_出演者(推奨)",
      "pattern": "{Code}_{Summary}_{Actor}.mp4",
      "promptInstruction": "既存のファイル名から品番、タイトルの要約、出演者を抽出しアンダースコア繋ぎで命名（短すぎても内容が把握しずらくなるので日本語で200文字前後となるように）してください。日本語タイトルや出演者が不明な場合は品番をWeb検索してそれぞれFANZAから日本語を取得してください。",
      "order": 0,
      "enableWebSearch": true
    },
    {
      "id": "rule-compact",
      "name": "短縮",
      "pattern": "{ShortTitle}.mp4",
      "promptInstruction": "既存のファイル名から動画の内容を表す最も重要で短いキーワード（200文字以内）で命名してください。",
      "order": 1,
      "enableWebSearch": false
    }
  ]
}
```

※上記 `appsettings.json` はリポジトリ同梱テンプレート（`"targetDirectory": ""`）です。個人環境固有の `"targetDirectory": "C:\\Users\\YourUserName\\Videos"` や `"apiKey": "sk-or-v1-..."` は環境別ファイル `appsettings.Development.json` 等で上書きしてください。

※`rules` の先頭に定義されたルールが起動時の既定ルールとして自動適用されます。
※`apiKey` が未設定（`null`）またはプレースホルダーのまま AI 提案を実行した場合、環境別設定ファイルのフルパスと最小 JSON 例がエラーバナーへ表示され、ファイル修正後に「🔄 設定を再読込」で回復できます。

#### (5) 設定項目対応表

##### Web アプリケーション / デプロイ環境

| 設定キー        | 環境変数名 (コンテナ注入/上書き) | 既定値 (appsettings.json)               | 説明                                                                                     |
| :-------------- | :------------------------------- | :-------------------------------------- | :--------------------------------------------------------------------------------------- |
| `PORT`          | `PORT`                           | `5620`                                  | アプリケーションがリッスンする HTTP ポート                                               |
| `VIDEO_DIRS`    | `VIDEO_DIRS`                     | `""`                                    | 走査対象の動画ルートフォルダ群（セミコロン `;` 区切りで複数指定可能、ドライブ直下不可）  |
| `THUMBNAIL_DIR` | `THUMBNAIL_DIR`                  | `thumbnails` (`/app/data/thumbnails`)   | 生成されたサムネイル画像の保存ディレクトリ                                               |
| `DATABASE_PATH` | `DATABASE_PATH`                  | `metadata.db` (`/app/data/metadata.db`) | メタデータ SQLite データベースファイルのパス                                             |
| `DATA_DIR`      | `DATA_DIR`                       | `./data`                                | ホスト側永続データ保存ディレクトリ（相対パス・Windows絶対パス・WSL UNC / Linuxパス対応） |

##### AI File Renamer (デスクトップアプリ)

| appsettings.json キー | 旧環境変数名 (参照廃止) | 既定値                                   | 説明                                                                                     |
| :-------------------- | :---------------------- | :--------------------------------------- | :--------------------------------------------------------------------------------------- |
| `targetDirectory`     | `VIDEO_DIR`             | `""`                                     | 走査対象の動画フォルダパス（単一フォルダ指定、未設定時は案内画面表示）                   |
| `pathLengthThreshold` | `PATH_LENGTH_THRESHOLD` | `240`                                    | 長パス警告・抽出基準文字数                                                               |
| `selectedModel`       | `OPENROUTER_MODEL`      | `meta-llama/llama-3.3-70b-instruct:free` | 短縮提案に使用するAIモデル（GUI 保存時は部分更新）                                       |
| `apiKey`              | `OPENROUTER_API_KEY`    | `null`                                   | OpenRouter APIキー（素の文字列または `null`。未設定/不正時はバナー案内）                 |
| `rules`               | `—`                     | 既定2ルール                              | 命名規則配列（`id`, `name`, `pattern`, `promptInstruction`, `order`, `enableWebSearch`） |

---

## 開発者向け情報とテスト実行 (For Developers & Testing)

### DevContainer による開発環境 (VS Code)

本リポジトリには VS Code DevContainer 定義（`.devcontainer/`）が含まれており、ローカル環境に .NET SDK や FFmpeg を個別にセットアップすることなく、コンテナ内で統一された開発環境を即座に立ち上げることができます。

- **含まれるツール**:
  - .NET 10 SDK
  - FFmpeg / ffprobe
  - SQLite3
  - F# (Ionide-fsharp) / C# DevKit 拡張機能
- **開発用ポートの分離 (5621)**:
  本番用コンテナ（ポート 5620）とのポート競合を防ぐため、DevContainer 開発環境は **ポート 5621**（環境変数 `PORT=5621`）に分離・自動フォワードされています。常用コンテナを起動したまま安全に並行開発が可能です。
- **Windows バインドマウント権限対策 (`remoteUser: "root"`)**:
  Windows ホスト上のドライブをバインドマウントした際に MSBuild のタイムスタンプ更新（`MSB3374`）が権限エラーになるのを防ぐため、コンテナ内ユーザーは `root` に設定されています。
- **利用手順**:
  1. VS Code で本リポジトリを開きます。
  2. コマンドパレット（`Ctrl+Shift+P`）から `Dev Containers: Reopen in Container` を選択します（設定変更時は `Dev Containers: Rebuild Container` を実行）。
  3. コンテナ起動後、ターミナルで `dotnet run --project src/TagBasedVideoManager` を実行すると `http://localhost:5621` でアプリが立ち上がります。
- **デスクトップアプリ (`TagBasedVideoManager.Renamer`) 開発時の使い分け**:
  - **自動テスト・Headless UI テスト**: `Avalonia.Headless.XUnit` を採用しているため、DevContainer 内のターミナルでも `dotnet run --project test/TagBasedVideoManager.Renamer.Tests` で全テストを実行可能です。
  - **実画面（GUI）でのデバッグ実行**: Windows ホスト固有の長パス走査およびホスト側コンテナ CLI（`wslc.exe` / `docker.exe`）制御を行うため、実ウィンドウでの動作確認は **Windows ホスト側**で `dotnet run --project src/TagBasedVideoManager.Renamer` を実行してください。

### ローカル起動 (Web アプリケーション)

```bash
cd src/TagBasedVideoManager
dotnet run
```

### ローカル起動 (AI File Renamer デスクトップアプリ)

```bash
cd src/TagBasedVideoManager.Renamer
dotnet run
```

### 自動テストとカバレッジ測定

本プロジェクトは MTP (Microsoft Testing Platform) および `coverlet.MTP` を用いた品質検証を導入しています。

#### 1. Web アプリケーション テスト (Unit / Integration / Playwright E2E)

```powershell
# Webアプリのテスト実行とカバレッジレポート生成
pwsh ./test/TagBasedVideoManager.Tests/run_tests_with_coverage.ps1
```

- **成果物出力先**: `test/TagBasedVideoManager.Tests/TestResults/TestRun_yyyy-MM-dd_HH_mm_ss/`

#### 2. AI File Renamer デスクトップアプリ テスト (Unit / 結合 E2E)

```powershell
# Renamerアプリのテスト実行とカバレッジレポート生成
pwsh ./test/TagBasedVideoManager.Renamer.Tests/run_tests_with_coverage.ps1
```

- **成果物出力先**: `test/TagBasedVideoManager.Renamer.Tests/TestResults/TestRun_yyyy-MM-dd_HH_mm_ss/`

---

## ライセンス

[MIT License](LICENSE)
