# 詳細設計書: TagBasedVideoManager - AI File Renamer

## 1. システム構成とデータフロー

### 1.1 物理・データフロー構成

本システムは、ホストOS（Windows / WSL2 / macOS / Linux）上で動作する独立したデスクトップアプリケーションであり、純粋な **100% F# (.NET 10) ＋ Avalonia.FuncUI (Elmish MVU)** で構築されます。
Dockerバインドマウント（WSL2/9p）のパス長制限（260文字）によるマウント失敗問題を解決するため、ホストファイルシステムを直接走査し、OpenRouter APIのAI支援を受けてファイル名を短縮・正規化リネームし、Docker Compose経由でWebアプリコンテナを再起動します。

```mermaid
graph TD
    subgraph HostOS ["ホストOS (Windows / WSL2 / macOS)"]
        User(["ユーザー"]) <---> UI["Avalonia.FuncUI (Elmish MVU)"]
        UI <---> Engine["Renamer Engine (F# Core)"]
        Engine <---> SettingsFile[("companion-settings.json")]
        Engine <---> HostFS[/"ホスト動画ディレクトリ (C:\Videos等)"/]
        Engine <---> OpenRouter["OpenRouter API (Free LLM)"]
        Engine <---> DockerCLI["Docker Compose CLI"]
    end

    subgraph DockerEnv ["Docker コンテナ環境"]
        DockerCLI -. "docker compose restart" .-> Container["TagBasedVideoManager Container"]
        Container --- Mount[/"バインドマウント (/app/videos)"/]
        Mount === HostFS
    end
```

### 1.2 処理シーケンス

```mermaid
sequenceDiagram
    autonumber
    actor User as ユーザー
    participant UI as Avalonia.FuncUI (Elmish)
    participant Core as Renamer Core
    participant OpenRouter as OpenRouter API
    participant Docker as Docker CLI / Kestrel
    participant FS as ホストファイルシステム

    Note over User, UI: アプリ起動
    UI->>Core: Settings.load()
    Core-->>UI: 設定初期値 (閾値: 240, フォルダ, モデル, 既定ルール)
    UI->>Docker: DockerController.checkStatus()
    Docker-->>UI: ステータス取得 (Running / Port 5620 OK)

    Note over User, UI: ワンアクション実行
    User->>UI: 「🚀 リネーム対象抽出 ＆ AI提案を実行」クリック
    UI->>Core: FileScanner.scan(folder, currentThreshold)
    Core->>FS: 再帰走査 (Path.Length >= threshold)
    FS-->>Core: 該当ファイル一覧返却
    Core->>OpenRouter: OpenRouterClient.propose(rules, files)
    OpenRouter-->>Core: JSONレスポンス (新ファイル名 + 問題時AIコメント)
    Core-->>UI: Before / After 対比データ描画

    Note over User, UI: 確認・リネーム・Undo
    User->>UI: 「⚡ リネームしてコンテナ再起動」クリック
    UI->>Core: FileRenamer.rename(proposals)
    Core->>FS: 物理リネーム実行 & Undo履歴記録
    Core->>Docker: DockerController.restart()
    Docker-->>UI: コンテナ再起動完了
    UI-->>User: 完了ダイアログ表示 (「↩ 元に戻す」ボタン活性化)
```

---

## 2. データモデル設計 (`Domain.fs`)

### 2.1 ドメイン型定義

```fsharp
namespace TagBasedVideoManager.Renamer.Domain

open System

/// 命名規則のドメインモデル
type NamingRule = {
    Id: string
    Name: string
    Pattern: string        // 例: "{{Date}}_{{Summary}}"
    PromptInstruction: string // LLMに対する命名指示
    Order: int             // 並び順 (最小値がデフォルトルール)
}

/// スキャン抽出されたファイル情報
type ScanCandidate = {
    FullPath: string
    FileName: string
    DirectoryPath: string
    PathLength: int
    FileSizeBytes: int64
    LastWriteTime: DateTime
}

/// リネーム提案データ
type RenameProposal = {
    OriginalFullPath: string
    OriginalFileName: string
    DirectoryPath: string
    OriginalLength: int
    ProposedFileName: string
    ProposedLength: int
    AiComment: string option // 問題・補完発生時のみ Some
    IsSelected: bool
}

/// リネームUndo履歴レコード
type UndoRecord = {
    Id: Guid
    Timestamp: DateTime
    OriginalFullPath: string
    RenamedFullPath: string
}

/// Dockerコンテナ稼働ステータス
type ContainerState =
    | Running
    | Stopped
    | Restarting
    | Unhealthy
    | NotFound

type DockerStatus = {
    State: ContainerState
    IsPortAccessible: bool // ポート5620のHTTP疎通
    ContainerId: string option
    LastChecked: DateTime
}

/// アプリケーション永続化設定
type RenamerSettings = {
    TargetDirectory: string
    PathLengthThreshold: int // 初期値: 240 (※起動中の変更はメモリ上のみで非保存)
    SelectedModel: string
    ApiKey: string option
    Rules: NamingRule list
}

/// 抽出結果一覧のソート基準
type SortCriterion =
    | PathLengthDesc    // パス長 (降順) - 既定（危険度の高い順）
    | PathLengthAsc     // パス長 (昇順)
    | FileNameAsc       // 元ファイル名 (昇順)
    | FileNameDesc      // 元ファイル名 (降順)
    | LastModifiedDesc  // 更新日時 (新しい順)
    | LastModifiedAsc   // 更新日時 (古い順)

/// 表示レイアウト種別
type LayoutMode =
    | Vertical   // 上下並び (極低ハイト設計・視線移動最小化)
    | Horizontal // 左右並び (横長ワイド対比)

/// ROP (Railway Oriented Programming) のためのエラー型
type RenamerError =
    | IoError of message: string * ex: exn option
    | SettingsError of message: string
    | OpenRouterError of statusCode: int * message: string
    | DockerError of command: string * exitCode: int * stderr: string
    | ValidationError of message: string
    | UndoConflictError of path: string * message: string
```

---

## 3. 設定ファイル・外部連携スキーマ設計

### 3.1 設定ファイル スキーマ (`appsettings.json`)

アプリケーション設定は .NET における標準構成ファイル名である `appsettings.json` にJSON形式で保存されます。

- **格納先・探索パス**:
  - 探索順: ① `%APPDATA%\TagBasedVideoManager\appsettings.json`（ユーザー個別設定）、② `./appsettings.json`（カレントディレクトリ）、③ `{AppDirectory}\appsettings.json`（実行ファイル同階層ポータブル設定）
  - 保存先: 実行ディレクトリに `appsettings.json` が存在する場合はそこへ、存在しない場合は `%APPDATA%\TagBasedVideoManager\appsettings.json`（自動作成）へ安全に保存。
    ※**セキュリティ配慮**: APIキー（`apiKey`）が保存される可能性があるため、ローカル実環境の `appsettings.json` は `.gitignore` に登録し、Git管理から除外します（プロジェクトにはテンプレート `appsettings.example.json` を提供）。

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "title": "RenamerSettings",
  "type": "object",
  "properties": {
    "targetDirectory": { "type": "string" },
    "pathLengthThreshold": { "type": "integer", "default": 240 },
    "selectedModel": {
      "type": "string",
      "default": "meta-llama/llama-3.3-70b-instruct:free"
    },
    "apiKey": { "type": ["string", "null"] },
    "rules": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "id": { "type": "string" },
          "name": { "type": "string" },
          "pattern": { "type": "string" },
          "promptInstruction": { "type": "string" },
          "order": { "type": "integer" }
        },
        "required": ["id", "name", "pattern", "promptInstruction", "order"]
      }
    }
  },
  "required": [
    "targetDirectory",
    "pathLengthThreshold",
    "selectedModel",
    "rules"
  ]
}
```

### 3.2 OpenRouter API連携仕様

- **エンドポイント**: `POST https://openrouter.ai/api/v1/chat/completions`
- **モデル**: Freeモデル（既定: `meta-llama/llama-3.3-70b-instruct:free`）
- **プロンプト構造**:
  - **System Prompt**:
    ```text
    あなたはファイル名の短縮・正規化を行う専門AIです。
    提供されたファイル一覧に対し、指定の命名規則に従って安全で簡潔なファイル名を生成してください。
    出力は必ず指定されたJSONフォーマットのみを返し、余計な解説文やMarkdownタグを含めないでください。
    ```
  - **User Prompt**:
    ```text
    【命名規則】: {RuleName}
    【命名パターン】: {Pattern}
    【命名指示】: {PromptInstruction}

    【ファイル一覧】:
    1. FullPath: "...", FileName: "...", ParentFolder: "...", LastModified: "..."
    ...

    【出力JSON仕様】:
    [
      {
        "originalFileName": "元ファイル名",
        "proposedFileName": "新ファイル名.mp4",
        "aiComment": "問題点・補完理由（※規則通り命名できた場合は null または空文字にすること）"
      }
    ]
    ```

### 3.3 Docker Compose コマンド仕様

- **ステータス確認**: `docker compose ps --format json`
  - **バージョン差異対応**: Docker Compose v2 のマイナーバージョン差異により、「単一JSON配列 `[{...}]`」で返る場合と「改行区切りNDJSON」で返る場合があります。デシリアライザーにて両形式（配列パース失敗時に行区切り分割パース）へ自動フォールバックして耐障害性を担保します。
- **コンテナ再起動**: `docker compose restart tag-based-video-manager`
- **コンテナ起動 / 停止**: `docker compose up -d` / `docker compose stop`

---

## 4. モジュール別詳細設計 & 関数シグネチャ

### 4.1 設定管理モジュール (`Settings.fs`)

```fsharp
namespace TagBasedVideoManager.Renamer

open TagBasedVideoManager.Renamer.Domain

module Settings =
    /// 既定の設定値を生成
    val defaultSettings: unit -> RenamerSettings

    /// 指定パスから設定ファイルを読み込む
    val load: filePath: string -> Result<RenamerSettings, RenamerError>

    /// 指定パスへ設定ファイルを保存する
    val save: filePath: string -> settings: RenamerSettings -> Result<unit, RenamerError>

    /// 命名ルールの並び順を更新する
    val reorderRules: ruleIdsInOrder: string list -> settings: RenamerSettings -> RenamerSettings
```

- **セッション内一時変更の保証**: UI上で閾値（`PathLengthThreshold`）が変更された場合、Elmish の `Model` のみ更新し、`Settings.save` は一切呼び出しません。

### 4.2 ファイル走査モジュール (`FileScanner.fs`)

```fsharp
namespace TagBasedVideoManager.Renamer

open TagBasedVideoManager.Renamer.Domain

module FileScanner =
    /// 指定フォルダを再帰走査し、絶対パス長が閾値以上の動画ファイルを抽出する
    val scanLongPaths:
        targetDirectory: string ->
        threshold: int ->
        Result<ScanCandidate list, RenamerError>

    /// 指定されたソート基準に従って候補リストを並び替える
    val sortCandidates:
        criterion: SortCriterion ->
        candidates: RenameProposal list ->
        RenameProposal list
```

- **アルゴリズム**:
  - **シンボリックリンク・ジャンクション（リパースポイント）の安全追跡**:
    - Windows のディレクトリジャンクションやシンボリックリンク（`FileAttributes.ReparsePoint`）配下も確実に走査するため、カスタムの再帰走査（DFS/BFS）または .NET 10 の `EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.None }` を適用。
    - **循環参照（無限ループ）防止**:
      - 訪問済みディレクトリの正規化された完全パス（`Path.GetFullPath(dir).TrimEnd('\\', '/')`）を `System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)` で記録。
      - 既に訪問済みのディレクトリパスが検出された場合はスキップし、無限ループを確実に防止。
  - 対象拡張子: `.mp4`, `.mkv`, `.avi`, `.mov`, `.wmv`, `.webm`, `.flv`。
  - **大文字小文字不問判定**: Windows/Linux間の差異を吸収するため、`StringComparison.OrdinalIgnoreCase` を用いて拡張子を判定。
  - 各ファイルの完全パス長（`file.Length`）を算出し、`length >= threshold` のものを抽出。
  - アクセス権限エラー（`UnauthorizedAccessException`）発生時は例外を握りつぶさず安全にスキップまたはログ記録。
  - **ソート処理**:
    - `PathLengthDesc`: パス長（降順）→ 同一長はファイル名（昇順）
    - `PathLengthAsc`: パス長（昇順）→ 同一長はファイル名（昇順）
    - `FileNameAsc`: 元ファイル名（昇順）
    - `FileNameDesc`: 元ファイル名（降順）
    - `LastModifiedDesc`: 更新日時（降順）
    - `LastModifiedAsc`: 更新日時（昇順）

### 4.3 OpenRouter APIクライアント (`OpenRouterClient.fs`)

```fsharp
namespace TagBasedVideoManager.Renamer

open TagBasedVideoManager.Renamer.Domain

module OpenRouterClient =
    /// AIモデルへリネーム候補を問い合わせる
    val requestProposals:
        httpHandler: (string -> string -> Async<int * string>) option -> // テスト用モック注入
        apiKey: string option ->
        model: string ->
        rule: NamingRule ->
        candidates: ScanCandidate list ->
        Async<Result<RenameProposal list, RenamerError>>
```

- **AIコメント制御ロジック**:
  - AIレスポンス内の `aiComment` が空文字または `"null"` の場合は `None` に正規化。
  - 元ファイル名が記号の羅列等で日時や地名が存在せず、親フォルダやファイル更新日時から代替補完した場合のみ `Some("...")` を保持。
  - レスポンスのJSONパースに失敗した場合、Markdownのコードブロック記号（`json ... `）を自動トリムして再パースを試行。

### 4.4 物理リネーム・Undoエンジン (`FileRenamer.fs`)

```fsharp
namespace TagBasedVideoManager.Renamer

open TagBasedVideoManager.Renamer.Domain

module FileRenamer =
    /// 選択されたリネーム提案を一括実行し、Undo履歴レコードを生成する
    val executeRename:
        proposals: RenameProposal list ->
        Result<UndoRecord list, RenamerError>

    /// 直前のリネーム履歴に基づいて逆リネームを実行し、元に戻す
    val executeUndo:
        records: UndoRecord list ->
        Result<unit, RenamerError>
```

- **同名衝突回避アルゴリズム**:
  - 移動先パスが既に存在する場合、ファイル名の末尾に `_1`, `_2` のようなサフィックスを付与して安全に一意化。
- **Undoの安全設計**:
  - Undo実行時に、元ファイル名が既に別の新規ファイルで占有されている場合は `UndoConflictError` を返し、上書き破壊を防止。

### 4.5 Dockerコントローラー (`DockerController.fs`)

```fsharp
namespace TagBasedVideoManager.Renamer

open TagBasedVideoManager.Renamer.Domain

module DockerController =
    /// コンテナのステータスおよびポート5620の疎通を確認する
    val checkStatus:
        workingDirectory: string ->
        Async<DockerStatus>

    /// Docker Compose アクションを実行する
    val executeAction:
        workingDirectory: string ->
        action: string -> // "up -d" | "stop" | "restart"
        Async<Result<string, RenamerError>>
```

---

## 5. UI/UX & Elmish アーキテクチャ詳細設計 (`State.fs` & `Views.fs`)

### 5.1 Elmish Model 定義

```fsharp
type Model = {
    // 設定
    Settings: RenamerSettings
    CurrentThreshold: int         // UIで変更可能なセッション限定閾値
    SelectedRuleId: string

    // 状態
    IsScanning: bool
    IsRequestingAi: bool
    IsRenaming: bool
    ErrorMessage: string option

    // データ
    Candidates: RenameProposal list
    UndoStack: UndoRecord list list // 1回のリネーム単位でスタック保持
    Docker: DockerStatus

    // UI表示設定
    Layout: LayoutMode
    SortCriterion: SortCriterion    // 抽出結果の並び順（既定: PathLengthDesc）
    IsRuleManagerOpen: bool
    EditingRule: NamingRule option
}
```

### 5.2 Elmish Msg 定義

```fsharp
type Msg =
    // 初期化・設定
    | SettingsLoaded of Result<RenamerSettings, RenamerError>
    | ThresholdChanged of int
    | TargetDirectoryChanged of string
    | RuleSelected of string

    // 走査 & AI提案 (ワンアクション)
    | ExecuteScanAndPropose
    | ScanCompleted of Result<ScanCandidate list, RenamerError>
    | AiProposeCompleted of Result<RenameProposal list, RenamerError>

    // 候補編集・ソート
    | ToggleCandidateSelect of fullPath: string
    | SelectAllCandidates of bool
    | UpdateProposedName of fullPath: string * newName: string
    | ChangeSortCriterion of SortCriterion

    // リネーム実行 & Undo
    | ExecuteRenameOnly
    | ExecuteRenameAndRestart
    | RenameCompleted of Result<UndoRecord list, RenamerError>
    | ExecuteUndo
    | UndoCompleted of Result<unit, RenamerError>

    // Docker連携
    | DockerStatusUpdated of DockerStatus
    | DockerCommandCompleted of action: string * Result<string, RenamerError>

    // 表示切り替え & ルール管理モーダル
    | SetLayoutMode of LayoutMode
    | OpenRuleManager
    | CloseRuleManager
    | SaveRule of NamingRule
    | DeleteRule of ruleId: string
    | MoveRuleOrder of ruleId: string * direction: int // -1: up, +1: down
    | DismissError
```

### 5.3 UIレイアウト構造 (`Views.fs`)

- **Docker Status Bar**: 画面最上部に常駐。ステータスバッジ（緑: RUNNING, 灰: STOPPED, 赤: UNHEALTHY）と、[▶ Start] [■ Stop] [🔄 Restart] ボタン。
- **Control Panel**:
  - 対象フォルダ入力欄（`SelectableTextBlock` / 編集可能テキスト） ＆ [参照...] ボタン
  - 抽出基準数値ボックス: `≧ [ 240 ] 文字 (※一時変更・設定非保存)`
  - 該当件数バッジ
  - モデル選択ドロップダウン、命名規則選択ドロップダウン ＆ [⚙ 管理...] ボタン
  - メインアクションボタン: `[ 🚀 リネーム対象抽出 ＆ AI提案を実行 (ワンアクション) ]`
- **List Toolbar & Sort Controls**:
  - ソート順ドロップダウン: `[ パス長 (降順) ▼ ]`（選択肢: パス長 降順/昇順、元ファイル名 昇順/降順、更新日時 新しい順/古い順）
  - 表示形式トグル: `[ ▤ 上下並び ] [ ◫ 左右並び ]`
  - 全選択 / 全解除チェックボックス
- **Before / After Comparison List**:
  - **テキスト選択・コピー対応**: 各カード内のフォルダパス、元ファイル名等には `SelectableTextBlock` を採用し、ユーザーが任意の部分を選択して Ctrl+C でコピー可能。
  - **上下並び (Vertical) の厳密な視覚的整列**:
    - **左側ラベル幅の固定**: `BEFORE (251字 [危険])` および `AFTER ( 42字 [安全])` のラベルコンテナ幅を `140.0px` に固定設定。これにより、右側のファイル名欄の開始X座標が垂直に完全に一致。
    - **行高さの統一**: BEFORE行（`SelectableTextBlock`）と AFTER行（`TextBox`）の行高さを `30.0px`（または `MinHeight = 30.0px`）で同一サイズに統一。上下で視線移動した際のガタつきを排除。
    - **等幅フォント (Monospace)**: BEFORE行のテキストおよび AFTER行の入力ボックスに等幅フォント（`Consolas, monospace`）を適用。文字数と文字位置の精密な対比・編集を可能にする。
    - **短縮文字数バッジのベースライン揃え**: 「〇〇字短縮」バッジにおいて、数字部分と「字短縮」の単位文字列の垂直位置を同一ベースライン（`VerticalAlignment.Center`）で整流。
    - 問題発生時のみ下部に `⚠️ AIコメント: 元名に日時情報がないため親フォルダより補完` を表示。
- **Footer Actions**:
  - 選択件数表示、`[ ↩ 直前のリネームを元に戻す (Undo) ]` ボタン（履歴あり時のみ活性化）
  - `[ リネームのみ実行 ]` ボタン ＆ `[ ⚡ リネームしてコンテナ再起動 (復旧) ]` ボタン
  - **Undo確認ダイアログ**: Undo実行時は「変更前の長ファイル名に復元され、コンテナが再起動します」と明示して意図しない再起動による驚きを防止。

### 5.4 フォルダ選択ピッカー連携設計 (`StorageProvider`)

- `TopLevel.GetTopLevel(control).StorageProvider` または `ApplicationLifetime` から `StorageProvider` を取得。
- 「参照...」ボタン押下時に非同期で `OpenFolderPickerAsync` を呼び出し、ユーザーが選択したディレクトリの絶対パスを取得して `TargetDirectoryChanged` メッセージをディスパッチ。
- ユーザーがキャンセルした場合は何もしない（状態変更なし）。

### 5.5 命名規則マネージャーモーダル設計 (`ruleManagerModal`)

- `model.IsRuleManagerOpen = true` のとき、最前面に暗色のオーバーレイ背景とともにモーダルダイアログを描画。
- **ルール一覧表示**: 各ルールの名称、パターン、説明、先頭バッジ（「★ 既定ルール」）。
- **順序入れ替え**: 各行に `[▲] [▼]` ボタンを配置し、`MoveRuleOrder (id, direction)` をディスパッチ。先頭のルールが常に起動時既定値となる。
- **ルールの追加・編集・削除**: `SaveRule`, `DeleteRule` メッセージによる設定ファイル（`companion-settings.json`）の即時永続化。
- **モーダル終了**: `[✕]` ボタンまたは `[閉じる]` ボタンで `CloseRuleManager` をディスパッチ。

### 5.6 長ファイル名表示時の親コンテナ幅維持・外枠見切れ防止設計

- **問題の根本原因**: `SelectableTextBlock` は `TextWrapping.NoWrap` のとき、親コンテナに関係なくテキスト長（240文字超なら1800px以上）を要求（DesiredWidth）し、親の `Border`（青い外枠）を画面外へ押し広げてしまう。
- **解決アプローチ**:
  - BEFORE 行の `SelectableTextBlock` を `ScrollViewer`（`HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden`, `VerticalScrollBarVisibility = ScrollBarVisibility.Disabled`）でラップ。
  - 親の `Border` の `ClipToBounds = true` を設定。
  - これにより、`SelectableTextBlock` の横幅が親カード（ウィンドウ幅に収まる）を突き破ることが物理的に不可能になり、青い選択枠の右端が確実に画面内に描画される。
  - ユーザーはテキストをドラッグ選択して最後まで閲覧・コピー（Ctrl+C）可能。

### 5.7 モックアップ (`mockup.html`) と E2E テストの同期

- `openspec/changes/ai-file-rename/mockup.html` を最新の仕様（ソートドロップダウン、等幅フォント、垂直整列、短縮文字数バッジ整列、管理モーダル、参照ボタン）を忠実に反映したデザインに更新。
- `RenameIntegrationE2ETests.fs` において、管理モーダルの描画状態（`E2E_05_Rule_Manager_Modal.png`）および長パス時のカード外枠非見切れ（`E2E_06_Long_Path_No_Overflow.png`）の実画面キャプチャを自動出力・ビジュアル検証する。

### 5.8 外部設定ファイル（appsettings.json / .env）ロードと命名規則デフォルト化設計

- **設定読み込みアーキテクチャ (`Settings.loadConfiguration`)**:
  - 優先順位 1: `%APPDATA%\TagBasedVideoManager\appsettings.json`、`./appsettings.json`、または `{AppDirectory}\appsettings.json`（.NET標準探索順）。
  - 優先順位 2: プロジェクトルートまたは上位ディレクトリの `.env` ファイルを探索し、`OPENROUTER_API_KEY`, `OPENROUTER_MODEL`, `VIDEO_DIR`, `PATH_LENGTH_THRESHOLD` をパースして未設定項目へフォールバック補完。
  - 優先順位 3: アプリケーション組み込み既定値（`Settings.defaultSettings`）。
  - ※**OS環境変数の非参照**: システム全体や別アプリの環境変数混入を防ぐため、`System.Environment.GetEnvironmentVariable` は呼び出さず、明示的な設定ファイル（`appsettings.json` / `.env`）のみから設定値をロードする。
- **外部ファイルによるデフォルト命名規則の適用**:
  - `appsettings.json`（または外部設定）に定義された `rules` 配列の先頭ルール（`Order = 0`）を、起動時のデフォルト命名規則（`SelectedRuleId`）として自動選択。
  - ユーザーが外部 JSON ファイルを直接編集・ルール追加・順序変更した場合でも、次回起動時にその外部定義が反映される。
- **保存先パスの標準化 (`Settings.defaultSavePath`)**:
  - 実行ディレクトリ（`AppContext.BaseDirectory`）に既に `appsettings.json` が存在する場合は、ポータブルモードとしてそこへ上書き保存。
  - 存在しない場合は、標準的なユーザープロファイル領域（`%APPDATA%\TagBasedVideoManager\appsettings.json`）を保存先とし、必要に応じてディレクトリを自動生成する。

### 5.9 AI提案ステータス正常化とフォントメトリクス・外枠完全描画設計

- **AI提案ステータスの正常化と未接続時の正直な表現**:
  - `RenameProposal` に `ProposalStatus`（`AiGenerated` | `NotProposed`）を持たせるか、`ApiKey` の有無および実際の LLM API 応答成否に基づいて判定。
  - **APIキー未設定 / 未通信時**:
    - バッジ表示: `[ 未提案 ]`（背景: 濃灰 `#27272a`、テキスト: 薄灰 `#a1a1aa`）。絶対に「AI提案済」と偽らない。
    - AFTER ファイル名: 初期値は元のファイル名（未変更状態）とし、ユーザーの手動編集を待つ。
    - AIコメント: `None`（勝手な固定コメントを捏造しない）。
    - 案内バナー: 画面上部に「⚠️ OpenRouter APIキーが未設定です。.env または companion-settings.json に設定してください（手動編集または抽出は可能です）」と明示。
  - **APIキー設定済み・LLM正常応答時**:
    - バッジ表示: `[ AI提案済 ]`（青バッジ `#1e40af`）。
    - AFTER ファイル名: LLM が返した新短縮ファイル名。
    - AIコメント: LLM が返した問題点・補完理由（正常時は非表示）。
- **フォントメトリクス揃え（数字と文字の上下高さ完全一致）**:
  - 短縮バッジ（`-〇〇字 (〇〇%短縮)`）のフォントファミリーを `"Yu Gothic UI", "Segoe UI", sans-serif"` に変更。
  - 欧文専用フォント `Consolas` を廃止することで、フォントフォールバックによる英数字と漢字の行メトリクス（アセント/ベースライン）のズレを解消し、数字と日本語のベースラインおよび高さを完全に整流。
- **カード枠線（青）の完全描画（見切れ解消）**:
  - `candidateCardVertical` 内の Line 1（パス部）: `SelectableTextBlock` に `TextTrimming.CharacterEllipsis` を指定し、右側の短縮バッジを侵食しないよう DockPanel でレイアウト。
  - Line 4（AIコメント部）: `StackPanel (Horizontal)` を廃止し、`DockPanel`（左にラベル、中央に残りの Wrap テキスト）に変更。親幅を超えて横に飛び出す現象を根本排除。
  - 親 `ScrollViewer` の `HorizontalScrollBarVisibility = Disabled` を明示し、子要素が親コンテナ幅（ウィンドウ幅）を突破することを物理的に不可能にし、カードの青い枠線（Border）の右端および下端が常に画面内に綺麗に描画されることを保証。

## 6. テスト・品質検証設計

### 6.1 テスト駆動開発 (TDD) 方針

すべてのCore機能およびロジックは、**Red（失敗する単体テスト）→ Green（最小実装）→ Refactor（リファクタリング）** の順で開発を進めます。

### 6.2 テストコードとテスト対象の 1:1 対応関係マッピング表

| テストファイル                                | テスト対象モジュール             | 検証内容・主要アサーション                                                                                                                                                                                                                   | 異常系・境界値テスト                                                                                                                                                                      |
| :-------------------------------------------- | :------------------------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | :---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **`test/.../SettingsTests.fs`**               | `src/.../Settings.fs`            | ・初期閾値 240文字のロード<br>・命名規則リストのシリアライズ/デシリアライズ<br>・ルールの並び替えと優先度更新                                                                                                                                | ・不正なJSON形式時のフォールバック<br>・**UIで閾値変更時に設定ファイルが書き換えられないことの検証**                                                                                      |
| **`test/.../FileScannerTests.fs`**            | `src/.../FileScanner.fs`         | ・指定フォルダの再帰走査<br>・パス長 ≧ threshold のファイル抽出<br>・対象動画拡張子のフィルタリング<br>・**ジャンクション/シンボリックリンク配下の動画走査**<br>・**各種ソート基準（パス長・ファイル名・更新日時）の並び替え検証**           | ・空フォルダ、存在しないパス<br>・閾値ちょうどの境界値（239文字 / 240文字 / 241文字）<br>・アクセス権限エラー時の安全スキップ<br>・**循環参照（同一ディレクトリ再訪）時の無限ループ回避** |
| **`test/.../OpenRouterClientTests.fs`**       | `src/.../OpenRouterClient.fs`    | ・モックHTTPによるAPIレスポンスパース<br>・命名規則プロンプト構築の妥当性<br>・**正常時 `aiComment = None`、問題時のみ `Some` となる判定**                                                                                                   | ・Markdownコードブロックの自動除去<br>・不正JSON時のフォールバック<br>・HTTPタイムアウトおよびステータスエラー                                                                            |
| **`test/.../FileRenamerTests.fs`**            | `src/.../FileRenamer.fs`         | ・物理ファイル名のリネーム実行<br>・Undo履歴レコードの生成<br>・**Undo実行による完全な元ファイル名復元**                                                                                                                                     | ・同名ファイル存在時の自動連番サフィックス<br>・ファイルロック中のエラーハンドリング<br>・Undo時に元名が占有されている場合の衝突検知                                                      |
| **`test/.../DockerControllerTests.fs`**       | `src/.../DockerController.fs`    | ・`docker compose ps --format json` のパース<br>・コンテナ稼働状態（Running/Stopped/Unhealthy）判定<br>・ポート5620のHTTPヘルスチェック                                                                                                      | ・Docker CLI 未インストール / 未起動時のエラー処理<br>・非0終了コード時の標準エラー捕捉                                                                                                   |
| **`test/.../RenameIntegrationE2ETests.fs`**   | 全モジュール結合・UIレンダリング | ・一時フォルダへの実ファイル生成<br>・スキャン → AI提案 → リネーム → 整合性確認 → Undo復元の一気通貫検証<br>・**GUI実画面レンダリング検証（上下並びの開始X座標一致、行高さ同一性、等幅フォント、短縮バッジ揃え、テキスト選択・コピー検証）** | ・260文字超過ファイルの実リネーム検証<br>・連続リネーム後の連続Undo検証<br>・ジャンクションを含むパス走査のE2E統合検証                                                                    |
| **`test/TagBasedVideoManager.Tests/` (既存)** | 既存Webアプリ全体                | ・フォルダ移動後の全既存テスト一括実行                                                                                                                                                                                                       | ・**フォルダ再編によるリグレッションがゼロであることの回帰検証**                                                                                                                          |
