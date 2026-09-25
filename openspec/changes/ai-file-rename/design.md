# Design: AI File Renamer (TagBasedVideoManager - AI File Renamer)

## Context

Docker Containerのバインドマウント（WSL2 / 9p / virtiofs）において、パス長制限（MAX_PATH / NAME_MAX 260文字）を超えるファイルが存在するとマウント自体が失敗し、Webアプリから動画が参照できなくなる（GitHub Issue #1）。
コンテナ内からは不可視となるため、ホストOS側で稼働する独立したデスクトップアプリ **TagBasedVideoManager - AI File Renamer** を新設し、ホストファイルシステムを直接走査してAIリネームおよびDockerコンテナ制御を行う。
既存Webアプリ側には一切の機能・UI改修を行わず（案1採用）、完全独立したデスクトップツールとして提供する。

## Goals / Non-Goals

**Goals:**

- ホストOS上の動画フォルダを走査し、マウント破綻原因となる**絶対パス長 ≧ 設定基準文字数（初期値: 240文字）**のファイルを自動抽出
- 抽出基準文字数は設定ファイル（`companion-settings.json`）で初期値を定義し、**アプリ起動中にUI上で自由に変更可能（※設定ファイルは更新しない）**
- アプリ起動時にフォルダ・モデル・優先命名規則・基準文字数を自動ロードし、1クリックで抽出＆AI提案を実行
- 命名規則マネージャー画面によるルールの新規作成・編集・削除・並び替え（先頭ルールが既定）
- **AIコメントの条件付き表示**: 規則通り正常に命名できたファイルは非表示とし、**風変わりな元ファイル名で補完や例外対応を行った問題発生時のみ表示**
- **Before（変更前）と After（変更後）の2通りレイアウト表示**:
  - **上下並び**: 高さを極限まで低く抑え、Before行とAfter行、（存在する場合のみ）AIコメントを隣接させて視線移動ゼロで比較可能
  - **左右並び**: 横長ディスプレイ向けの2列対比
- アプリ起動中リネームの **Undo（元に戻す）** 機能（物理逆リネーム＋コンテナ再起動）
- **100% F# (.NET 10) ＋ Avalonia.FuncUI (Elmish MVU)** による完全な関数型デスクトップアプリ
- 安全な物理リネーム（重複チェック、エラーハンドリング、ロールバック）
- `docker compose ps --format json` および HTTP疎通確認による確実なコンテナ状態特定と Up / Down / Restart 制御
- プロジェクトフォルダ構成の再編（`src/` 配下にWebとRenamer、`test/` 配下にWebテストとRenamerテストを並列化）
- フォルダ移動に伴う Dockerfile / compose / slnx / テストスクリプトのパス更新と、**既存Web機能の全回帰テスト実行**
- 自動テストの徹底（TDD単体テスト ＋ 結合E2Eテスト）

**Non-Goals:**

- 既存Webアプリのソースコード・UI改修（案1採用により変更ゼロ）
- 起動中に変更した抽出基準文字数の設定ファイルへの自動保存（セッション限りの一時変更に留める）
- 正常なファイルに対する不要なAIコメント表示（UIノイズ防止）
- C# や XAML の導入（本プロジェクトの F# ネイティブ方針を堅持）
- コンテナ内部からの直接物理ファイルリネーム
- 有料AIモデルへの依存

## Decisions

### 1. プロジェクト・フォルダ構成の再編と回帰テスト方針

- **決定**: ソリューションおよびフォルダ構造を以下のように階層化・再編する。
  ```
  TagBasedVideoManager/
  ├── TagBasedVideoManager.slnx
  ├── Dockerfile
  ├── docker-compose.yml
  ├── src/
  │   ├── TagBasedVideoManager/             # 既存 Web アプリケーション
  │   │   ├── TagBasedVideoManager.fsproj
  │   │   └── ...
  │   └── TagBasedVideoManager.Renamer/     # 新設 デスクトップアプリ
  │       ├── TagBasedVideoManager.Renamer.fsproj
  │       └── ...
  └── test/
      ├── TagBasedVideoManager.Tests/       # 既存 Web アプリのテスト
      │   ├── TagBasedVideoManager.Tests.fsproj
      │   └── ...
      └── TagBasedVideoManager.Renamer.Tests/ # 新設 デスクトップアプリのテスト
          ├── TagBasedVideoManager.Renamer.Tests.fsproj
          └── ...
  ```
- **移行手順と回帰検証**:
  1. 既存ファイルをサブディレクトリに移動。
  2. `Dockerfile`, `docker-compose.yml`, `TagBasedVideoManager.slnx`, `test/TagBasedVideoManager.Tests/TagBasedVideoManager.Tests.fsproj` 内のプロジェクト参照パスを同期更新。
  3. `dotnet test test/TagBasedVideoManager.Tests/` を実行し、既存テストが100%パスすることを回帰テストとして確認する。

### 2. 抽出基準（パス長閾値）の設計

- **決定**: 設定ファイル `companion-settings.json` に `pathLengthThreshold: 240` を定義し、起動時に初期値として読み込む。UI上でユーザーが数値を一時変更できるが、**設定ファイルには書き戻さない（更新しない）**。
- **理由**:
  - 毎回同じ安全基準（240文字）で即座にスキャンできる安定性を確保。
  - 特定のフォルダや一時的な確認で「200文字以上」「250文字以上」を見たい場合にも、設定ファイルを汚さずに柔軟にUI上で変更できる。

### 3. AIコメントの条件付き描画

- **決定**: 命名規則に正常に従えたファイルは `aiComment` を `None`（非表示）とし、元ファイル名がランダム英数字や記号等で親フォルダや更新日時から代替補完した場合のみ `Some("理由...")` を設定して `⚠️ AIコメント: ...` を表示する。
- **理由**: 全件にコメントを出すとUIの縦幅が増加しノイズになるため、注意が必要な問題ファイルのみを目立たせる。

### 4. Before / After のレイアウト切り替え ＆ 高さコンパクト化

- **決定**: 「上下並び」と「左右並び」のトグル切り替えを提供する。
- **上下並びの最適化**:
  - 1カード内で、上行（BEFORE: 現パス長・赤バッジ・現ファイル名）と下行（AFTER: 新パス長・緑バッジ・新ファイル名インライン入力）を隣接配置。
  - 問題があるファイルにのみAIコメントを1行追加。

### 5. リネームの Undo（元に戻す）機能

- **決定**: アプリ起動中のリネーム履歴をメモリスタック（`undoHistory: { OriginalPath: string; RenamedPath: string } list`）として保持し、UI上の「元に戻す (Undo)」ボタンから逆リネームを実行できるようにする。

### 6. 命名規則マネージャー画面

- **決定**: メイン画面の「管理...」ボタンから開く命名規則マネージャーモーダルを新設する。
- **機能**:
  - ルール一覧の並び替え（一番上に置いたルールが起動時のデフォルト）
  - ルール名・パターン・AI指示の編集
  - タグ挿入ボタン（`{{Date}}`, `{{ParentFolder}}`, `{{Summary}}`, `{{Seq}}`）
  - `companion-settings.json` への自動永続化

### 7. 100% F# によるデスクトップGUI: Avalonia.FuncUI (Elmish MVU)

- **決定**: C# / XAML を一切使用せず、**純粋な F# (.NET 10) ＋ Avalonia.FuncUI** を採用する。

## UI/UX モックアップ (Avalonia.FuncUI - Fluent テーマ)

> ブラウザで操作可能なHTMLモックアップ（UI仕様正本）: [mockup.html](./mockup.html)

```
+---------------------------------------------------------------------------------+
| TagBasedVideoManager - AI File Renamer                                   [-] [x]|
+---------------------------------------------------------------------------------+
| [Docker: tag-based-video-manager] Status: [● RUNNING] (Port: 5620)               |
| [▶ Start] [■ Stop] [🔄 Restart]                                                 |
+---------------------------------------------------------------------------------+
| 対象フォルダ: [ C:\Videos\Touring_2025              ] [参照...]                 |
| 抽出基準: ≧ [ 240 ] 文字 (※一時変更・設定非保存)   (該当: 3件)                 |
| AIモデル: [ Llama-3.3 70B (Free) v ]  命名: [ ★日付＋要約 v ] [⚙ 管理...]       |
|                                                                                 |
| ===> [ 🚀 リネーム対象抽出 ＆ AI提案を実行 (ワンアクション) ]                   |
+---------------------------------------------------------------------------------+
| Before / After 対比確認                            表示形式: [ ▤ 上下並び ] [ ◫ 左右並び ] |
| +-----------------------------------------------------------------------------+ |
| | [x] #1  C:\Videos\Touring_2025\Hokkaido_Very_Long_Directory\...            | |
| |  BEFORE (251字 [危険]): VID_20250812_Wakkanai_Motorcycle_Very_Long_Name.mp4 | |
| |  AFTER  ( 42字 [安全]): [ 20250812_Hokkaido_Soya_Cape.mp4 ] (-209字削減)    | |
| |  (※正常適合のためAIコメントなし)                                           | |
| +-----------------------------------------------------------------------------+ |
| | [x] #3  C:\Videos\Touring_2025\Furano\...                                   | |
| |  BEFORE (249字 [危険]): 7f9a2b8c_sensor_uuid_heavy_dump_4k_final.mp4        | |
| |  AFTER  ( 41字 [安全]): [ 20250814_Furano_Lavender.mp4    ] (-208字削減)    | |
| |  ⚠️ AIコメント: 元名に日時・地名が不記載のため親フォルダ・作成日より補完。   | |
| +-----------------------------------------------------------------------------+ |
|                                                                                 |
| 確定件数: 3 件      [ ↩ 直前のリネームを元に戻す (Undo) ]                       |
| [ リネームのみ実行 ]             [ ⚡ リネームしてコンテナ再起動 (復旧) ]       |
+---------------------------------------------------------------------------------+
```

## アーキテクチャとデータフロー

```
 [Avalonia.FuncUI (Elmish MVU)]
      |
      | 1. アプリ起動: Settings.load() -> フォルダ, モデル, 既定ルール, 初期閾値(240)
      v
 [Elmish Model 初期化] (閾値はModelメモリ内に保持)
      |
      |-- (※UIで閾値を変更した場合: Msg: UpdateThreshold -> Modelのみ更新、ファイル非保存)
      |
      | 2. Msg: ExecuteScanAndPropose (ワンアクション実行)
      v
 [FileScanner.fs] ---> パス長 ≧ Model.CurrentThreshold のファイルを抽出
      |
 [OpenRouterClient.fs] ---> Freeモデルへ一括問い合わせ
      |                      (新ファイル名 + 問題時のみAIコメントを取得)
      |
 [Elmish Model 更新] ---> Before / After 対比ビュー描画 (問題ファイルのみAIコメント表示)
      |
      | 3. Msg: ExecuteRename
      v
 [FileRenamer.fs] ---> 物理リネーム実行 & Undo履歴スタックに記録
 [DockerController.fs] ---> コンテナ再起動
      |
 [Elmish Model 更新] ---> 「↩ 元に戻す (Undo)」ボタンが利用可能に！
```

## Risks / Trade-offs

- **[Risk] フォルダ移動に伴う Docker ビルドやテスト実行の破綻**
  → **Mitigation**: プロジェクト構築直後に、更新されたパス設定で既存全テストを実行し、100%パスする回帰検証エビデンスを取得する。
- **[Risk] Undo実行時に元ファイル名が既に別のファイルで占有されている可能性**
  → **Mitigation**: Undo実行前にも `File.Exists` チェックを行い、衝突がある場合は警告ダイアログを表示して安全に中断する。
- **[Risk] OpenRouter Freeモデルのレートリミットやダウンタイム**
  → **Mitigation**: エラー時は手動リネーム入力欄を開放し、再試行ボタンを用意。
