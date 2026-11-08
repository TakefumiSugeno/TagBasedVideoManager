namespace TagBasedVideoManager.Domain

open System
open System.IO
open System.Security.Cryptography
open System.Buffers.Binary

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
    FileModifiedAt: DateTime
    AccessCount: int
    LastAccessedAt: DateTime option
    Tags: Tag list
    FileUuid: string option
}

/// 動画ファイルの決定論的識別子（UUID v5）算出モジュール
module FileIdentity =
    /// アプリケーション固有の固定 Namespace UUID: 8f9d5a12-4b6c-4e8a-9c1d-2e3f4a5b6c7d (ネットワークバイトオーダー)
    let private NamespaceBytes : byte array = [|
        0x8fuy; 0x9duy; 0x5auy; 0x12uy
        0x4buy; 0x6cuy; 0x4euy; 0x8auy
        0x9cuy; 0x1duy; 0x2euy; 0x3fuy
        0x4auy; 0x5buy; 0x6cuy; 0x7duy
    |]

    let private ChunkSize = 65536 // 64KB
    let private MaxFullReadBytes = 196608L // 192KB

    /// 動画ファイルのサイズと先頭・中間・末尾サンプリングから決定論的UUID（UUID v5）を算出する
    let computeFileUuid (filePath: string) : Result<string, string> =
        if String.IsNullOrWhiteSpace(filePath) then
            Error "File path cannot be null or empty"
        elif not (File.Exists(filePath)) then
            Error $"File does not exist: {filePath}"
        else
            try
                use fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite ||| FileShare.Delete)
                let fileSize = fs.Length
                use hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA1)

                // 1. 固定 Namespace UUID (16 bytes, big-endian / network byte order)
                hasher.AppendData(NamespaceBytes)

                // 2. ファイルサイズ (8 bytes, Little Endian)
                let sizeBytes = Array.zeroCreate<byte> 8
                BinaryPrimitives.WriteInt64LittleEndian(sizeBytes, fileSize)
                hasher.AppendData(sizeBytes)

                // 3. サンプリングバイト列
                let buf = Array.zeroCreate<byte> ChunkSize
                let appendChunk (offset: int64) (count: int) =
                    fs.Seek(offset, SeekOrigin.Begin) |> ignore
                    let mutable remaining = count
                    while remaining > 0 do
                        let read = fs.Read(buf, 0, min remaining buf.Length)
                        if read = 0 then
                            remaining <- 0
                        else
                            hasher.AppendData(buf, 0, read)
                            remaining <- remaining - read

                if fileSize > 0L then
                    if fileSize <= MaxFullReadBytes then
                        appendChunk 0L (int fileSize)
                    else
                        appendChunk 0L ChunkSize
                        appendChunk ((fileSize / 2L) - 32768L) ChunkSize
                        appendChunk (fileSize - 65536L) ChunkSize

                let hash = hasher.GetHashAndReset()
                // RFC 9562 / RFC 4122 Version 5 ビット設定
                hash.[6] <- (hash.[6] &&& 0x0Fuy) ||| 0x50uy // version 5
                hash.[8] <- (hash.[8] &&& 0x3Fuy) ||| 0x80uy // variant 1 (10xx)

                Ok (Guid(ReadOnlySpan<byte>(hash, 0, 16), true).ToString())
            with ex ->
                Error $"Failed to compute file UUID for '{filePath}': {ex.Message}"

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

