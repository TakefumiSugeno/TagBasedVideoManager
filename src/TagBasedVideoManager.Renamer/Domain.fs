namespace TagBasedVideoManager.Renamer

open System

/// 命名規則の定義
type NamingRule = {
    Id: string
    Name: string
    Pattern: string
    PromptInstruction: string
    Order: int
    EnableWebSearch: bool
}

/// DuckDuckGo 検索結果アイテム
type SearchResultItem = {
    Title: string
    Snippet: string
    Url: string
}

/// 制約違反の種別
type ConstraintViolation =
    | WindowsPathOnly     // Windows フルパス文字数のみ超過 (>= 240字)
    | LinuxFileNameOnly   // Linux ファイル名 UTF-8 バイト数のみ超過 (>= 240B)
    | BothViolated        // 両方超過

/// 表示フィルタの選択肢
type ConstraintFilter =
    | FilterAll           // すべて
    | FilterLinuxOnly     // Linuxファイル名超過のみ
    | FilterWindowsOnly   // Windowsパス長超過のみ
    | FilterBothOnly      // 両方超過

/// スキャン結果の候補ファイル情報
type ScanCandidate = {
    FullPath: string
    FileName: string
    DirectoryPath: string
    PathLength: int
    FileNameBytes: int
    Violation: ConstraintViolation
    FileSizeBytes: int64
    LastWriteTime: DateTime
}

/// リネーム提案データ
type RenameProposal = {
    OriginalFullPath: string
    OriginalFileName: string
    DirectoryPath: string
    OriginalLength: int
    OriginalFileNameBytes: int
    OriginalViolation: ConstraintViolation
    ProposedFileName: string
    ProposedLength: int
    ProposedFileNameBytes: int
    AiComment: string option // 問題・補完発生時のみ Some
    IsSelected: bool
    LastWriteTime: DateTime
    IsAiProposed: bool      // LLMによる提案完了フラグ (未提案時はfalse)
    IsAiProcessing: bool    // 現在AI提案処理中フラグ (行アニメーション表示用)
}

/// 提案データ操作ヘルパー
module Proposal =
    /// スキャン直後の初期リネーム候補データ生成（即時IO描画用）
    let createInitial (candidate: ScanCandidate) : RenameProposal = {
        OriginalFullPath = candidate.FullPath
        OriginalFileName = candidate.FileName
        DirectoryPath = candidate.DirectoryPath
        OriginalLength = candidate.PathLength
        OriginalFileNameBytes = candidate.FileNameBytes
        OriginalViolation = candidate.Violation
        ProposedFileName = candidate.FileName
        ProposedLength = candidate.PathLength
        ProposedFileNameBytes = candidate.FileNameBytes
        AiComment = Some "（AI提案の開始を待機しています...）"
        IsSelected = true
        LastWriteTime = candidate.LastWriteTime
        IsAiProposed = false
        IsAiProcessing = false
    }

/// リネームUndo履歴レコード
type UndoRecord = {
    Id: Guid
    Timestamp: DateTime
    OriginalFullPath: string
    RenamedFullPath: string
}

/// コンテナランタイム種別
type ContainerRuntime =
    | Wslc
    | Docker
    | Podman

/// コンテナ稼働ステータス
type ContainerState =
    | Running
    | Stopped
    | Restarting
    | Unhealthy
    | NotFound
    | NotInstalled

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
    | ContainerError of command: string * exitCode: int * stderr: string
    | TimeoutError of command: string
    | ValidationError of message: string
    | UndoConflictError of path: string * message: string
