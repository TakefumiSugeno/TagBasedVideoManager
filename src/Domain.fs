namespace TagBasedVideoManager.Domain

open System

/// タグのドメインモデル
type Tag = {
    Id: string
    Name: string
    ColorCode: string
    ParentId: string option
    VideoCount: int
}

/// 動画メタデータのドメインモデル
type Video = {
    Id: string
    FileName: string
    FilePath: string
    Duration: int64 // 秒数
    FileSize: int64 // バイト数
    ThumbnailPath: string option
    IsFavorite: bool
    CreatedAt: DateTime
    AccessCount: int
    LastAccessedAt: DateTime option
    Tags: Tag list
}

/// 自動タグ付けルールのドメインモデル
type TaggingRule = {
    Id: string
    Pattern: string
    TagId: string
    MatchType: string
    TargetField: string
    MinSize: int64 option
    MaxSize: int64 option
    CreatedAt: DateTime
}

/// スマートフォルダのドメインモデル
type SmartFolder = {
    Id: string
    Name: string
    Query: string
    CreatedAt: DateTime
}

/// データベースエラーの表現
type DbError =
    | ConnectionError of string
    | QueryError of string
    | RecordNotFound of string
    | UniqueConstraintViolation of string

/// メディア処理エラーの表現
type MediaError =
    | FFmpegExecutionError of string
    | FileNotFound of string
    | InvalidFormat of string

/// 例外エラーログのドメインモデル
type ErrorLog = {
    Id: int
    Message: string
    StackTrace: string option
    CreatedAt: DateTime
}

