namespace TagBasedVideoManager.Renamer

open System
open System.IO
open TagBasedVideoManager.Renamer

module FileScanner =

    let private videoExtensions =
        set [ ".mp4"; ".mkv"; ".avi"; ".mov"; ".wmv"; ".webm"; ".flv" ]

    let private isVideoFile (filePath: string) : bool =
        let ext = Path.GetExtension(filePath)
        if String.IsNullOrEmpty(ext) then false
        else
            videoExtensions
            |> Set.exists (fun targetExt -> String.Equals(targetExt, ext, StringComparison.OrdinalIgnoreCase))

    /// 指定フォルダを再帰走査し、絶対パス長が閾値以上の動画ファイルを抽出する
    let scanLongPaths (targetDirectory: string) (threshold: int) : Result<ScanCandidate list, RenamerError> =
        try
            if not (Directory.Exists(targetDirectory)) then
                Error (IoError ($"指定されたディレクトリが存在しません: {targetDirectory}", None))
            else
                let enumerationOptions = EnumerationOptions(
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                )

                let files = Directory.EnumerateFiles(targetDirectory, "*.*", enumerationOptions)
                let candidates =
                    files
                    |> Seq.filter isVideoFile
                    |> Seq.filter (fun path -> path.Length >= threshold)
                    |> Seq.map (fun path ->
                        let fileInfo = FileInfo(path)
                        {
                            FullPath = path
                            FileName = fileInfo.Name
                            DirectoryPath = fileInfo.DirectoryName
                            PathLength = path.Length
                            FileSizeBytes = fileInfo.Length
                            LastWriteTime = fileInfo.LastWriteTime
                        }
                    )
                    |> Seq.toList

                Ok candidates
        with
        | ex ->
            Error (IoError ($"ディレクトリ走査中にエラーが発生しました: {ex.Message}", Some ex))
