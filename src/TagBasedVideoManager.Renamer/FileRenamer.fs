namespace TagBasedVideoManager.Renamer

open System
open System.IO
open TagBasedVideoManager.Renamer

module FileRenamer =

    /// 移動先ファイルパスが既に存在する場合、末尾に _1, _2 等のサフィックスを付与して一意なパスを生成する
    let rec private resolveUniqueDestination (directory: string) (fileName: string) : string =
        let targetPath = Path.Combine(directory, fileName)
        if not (File.Exists(targetPath)) then
            targetPath
        else
            let baseName = Path.GetFileNameWithoutExtension(fileName)
            let ext = Path.GetExtension(fileName)

            let rec findAvailableIndex idx =
                let candidateName = $"{baseName}_{idx}{ext}"
                let candidatePath = Path.Combine(directory, candidateName)
                if not (File.Exists(candidatePath)) then candidatePath
                else findAvailableIndex (idx + 1)

            findAvailableIndex 1

    /// 選択されたリネーム提案を一括実行し、Undo履歴レコードを生成する
    let executeRename (proposals: RenameProposal list) : Result<UndoRecord list, RenamerError> =
        try
            let selectedProposals = proposals |> List.filter (fun p -> p.IsSelected)
            let mutable executedRecords = []

            let rec processProposals (remaining: RenameProposal list) =
                match remaining with
                | [] -> Ok (List.rev executedRecords)
                | p :: rest ->
                    if not (File.Exists(p.OriginalFullPath)) then
                        Error (IoError ($"リネーム対象のファイルが存在しません: {p.OriginalFullPath}", None))
                    else
                        try
                            let targetPath = resolveUniqueDestination p.DirectoryPath p.ProposedFileName
                            File.Move(p.OriginalFullPath, targetPath)

                            let undoRecord = {
                                Id = Guid.NewGuid()
                                Timestamp = DateTime.UtcNow
                                OriginalFullPath = p.OriginalFullPath
                                RenamedFullPath = targetPath
                            }
                            executedRecords <- undoRecord :: executedRecords
                            processProposals rest
                        with
                        | ex ->
                            Error (IoError ($"ファイル '{p.OriginalFullPath}' のリネーム中にエラーが発生しました: {ex.Message}", Some ex))

            processProposals selectedProposals
        with
        | ex ->
            Error (IoError ($"一括リネーム実行中に予期しないエラーが発生しました: {ex.Message}", Some ex))

    /// 直前のリネーム履歴レコードに基づき、完全な元ファイル名に復元（逆リネーム）する
    let executeUndo (records: UndoRecord list) : Result<unit, RenamerError> =
        try
            // 衝突検査: 復元先（OriginalFullPath）が既に別のファイルに占有されていないか
            let conflictOpt =
                records
                |> List.tryFind (fun r ->
                    // RenamedFullPath と OriginalFullPath が同一でない場合で、かつ OriginalFullPath が既に存在しているか
                    not (String.Equals(r.OriginalFullPath, r.RenamedFullPath, StringComparison.OrdinalIgnoreCase))
                    && File.Exists(r.OriginalFullPath)
                )

            match conflictOpt with
            | Some conflict ->
                Error (UndoConflictError (conflict.OriginalFullPath, $"復元先のファイルパス '{conflict.OriginalFullPath}' が既に別のファイルによって存在しているため、元に戻せません。上書きを防ぐため中断しました。"))
            | None ->
                for r in records do
                    if File.Exists(r.RenamedFullPath) then
                        File.Move(r.RenamedFullPath, r.OriginalFullPath)
                    else
                        // 対象が見つからない場合はスキップまたはエラー
                        ()
                Ok ()
        with
        | ex ->
            Error (IoError ($"Undo実行中にエラーが発生しました: {ex.Message}", Some ex))
