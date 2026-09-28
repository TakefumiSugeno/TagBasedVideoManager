namespace TagBasedVideoManager.Renamer

open System
open System.IO
open System.Collections.Generic

module FileScanner =

    let private videoExtensions =
        set [ ".mp4"; ".mkv"; ".avi"; ".mov"; ".wmv"; ".webm"; ".flv" ]

    let private isVideoFile (filePath: string) : bool =
        let ext = Path.GetExtension(filePath)
        if String.IsNullOrEmpty(ext) then false
        else
            videoExtensions
            |> Set.exists (fun targetExt -> String.Equals(targetExt, ext, StringComparison.OrdinalIgnoreCase))

    let private normalizePath (path: string) : string =
        try
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        with _ -> path

    /// 指定フォルダを再帰走査し、絶対パス長が閾値以上の動画ファイルを抽出する（ジャンクション追跡＆循環参照防止）
    let scanLongPaths (targetDirectory: string) (threshold: int) : Result<ScanCandidate list, RenamerError> =
        try
            if not (Directory.Exists(targetDirectory)) then
                Error (IoError ($"指定されたディレクトリが存在しません: {targetDirectory}", None))
            else
                let visitedDirs = HashSet<string>(StringComparer.OrdinalIgnoreCase)
                let candidates = ResizeArray<ScanCandidate>()

                let rec traverse (currentDir: string) =
                    let normalizedCurrent = normalizePath currentDir

                    // リパースポイント（ジャンクション／シンボリックリンク）のターゲット解決
                    let isAlreadyVisited =
                        if visitedDirs.Contains(normalizedCurrent) then
                            true
                        else
                            try
                                let dirInfo = DirectoryInfo(currentDir)
                                if dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint) then
                                    let target = dirInfo.ResolveLinkTarget(true)
                                    if target <> null then
                                        let normalizedTarget = normalizePath target.FullName
                                        if visitedDirs.Contains(normalizedTarget) then
                                            true
                                        else
                                            visitedDirs.Add(normalizedTarget) |> ignore
                                            false
                                    else
                                        false
                                else
                                    false
                            with _ ->
                                false

                    if not isAlreadyVisited && visitedDirs.Add(normalizedCurrent) then
                        // 1. 直下のファイル走査
                        try
                            let files = Directory.EnumerateFiles(currentDir, "*.*", SearchOption.TopDirectoryOnly)
                            for file in files do
                                try
                                    if isVideoFile file && file.Length >= threshold then
                                        let fi = FileInfo(file)
                                        candidates.Add({
                                            FullPath = file
                                            FileName = fi.Name
                                            DirectoryPath = fi.DirectoryName
                                            PathLength = file.Length
                                            FileSizeBytes = fi.Length
                                            LastWriteTime = fi.LastWriteTime
                                        })
                                with
                                | :? UnauthorizedAccessException -> ()
                                | :? PathTooLongException ->
                                    candidates.Add({
                                        FullPath = file
                                        FileName = Path.GetFileName(file)
                                        DirectoryPath = Path.GetDirectoryName(file)
                                        PathLength = file.Length
                                        FileSizeBytes = 0L
                                        LastWriteTime = DateTime.MinValue
                                    })
                                | _ -> ()
                        with
                        | :? UnauthorizedAccessException -> ()
                        | _ -> ()

                        // 2. サブディレクトリ走査（ジャンクションやシンボリックリンクも含む）
                        try
                            let subDirs = Directory.EnumerateDirectories(currentDir, "*.*", SearchOption.TopDirectoryOnly)
                            for subDir in subDirs do
                                traverse subDir
                        with
                        | :? UnauthorizedAccessException -> ()
                        | _ -> ()

                traverse targetDirectory
                Ok (candidates |> Seq.toList)
        with
        | ex ->
            Error (IoError ($"ディレクトリ走査中にエラーが発生しました: {ex.Message}", Some ex))

    /// 指定されたソート基準に従って候補リストを並び替える
    let sortCandidates (criterion: SortCriterion) (candidates: RenameProposal list) : RenameProposal list =
        match criterion with
        | PathLengthDesc ->
            candidates
            |> List.sortWith (fun a b ->
                if a.OriginalLength <> b.OriginalLength then
                    compare b.OriginalLength a.OriginalLength
                else
                    String.Compare(a.OriginalFileName, b.OriginalFileName, StringComparison.OrdinalIgnoreCase)
            )
        | PathLengthAsc ->
            candidates
            |> List.sortWith (fun a b ->
                if a.OriginalLength <> b.OriginalLength then
                    compare a.OriginalLength b.OriginalLength
                else
                    String.Compare(a.OriginalFileName, b.OriginalFileName, StringComparison.OrdinalIgnoreCase)
            )
        | FileNameAsc ->
            candidates
            |> List.sortWith (fun a b ->
                String.Compare(a.OriginalFileName, b.OriginalFileName, StringComparison.OrdinalIgnoreCase)
            )
        | FileNameDesc ->
            candidates
            |> List.sortWith (fun a b ->
                String.Compare(b.OriginalFileName, a.OriginalFileName, StringComparison.OrdinalIgnoreCase)
            )
        | LastModifiedDesc ->
            candidates
            |> List.sortWith (fun a b ->
                if a.LastWriteTime <> b.LastWriteTime then
                    compare b.LastWriteTime a.LastWriteTime
                else
                    String.Compare(a.OriginalFileName, b.OriginalFileName, StringComparison.OrdinalIgnoreCase)
            )
        | LastModifiedAsc ->
            candidates
            |> List.sortWith (fun a b ->
                if a.LastWriteTime <> b.LastWriteTime then
                    compare a.LastWriteTime b.LastWriteTime
                else
                    String.Compare(a.OriginalFileName, b.OriginalFileName, StringComparison.OrdinalIgnoreCase)
            )


