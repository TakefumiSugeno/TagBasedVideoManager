namespace TagBasedVideoManager.Renamer.Tests

open System
open System.IO
open Xunit
open FsUnit
open TagBasedVideoManager.Renamer

module FileRenamerTests =

    let createTestEnvironment () =
        let root = Path.Combine(Path.GetTempPath(), "RenamerTest_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(root) |> ignore
        root

    let cleanup (path: string) =
        if Directory.Exists(path) then
            try Directory.Delete(path, true) with _ -> ()

    [<Fact>]
    let ``executeRename は選択されたファイルを物理リネームし、UndoRecord を生成する`` () =
        let root = createTestEnvironment ()
        try
            let origName = "Very_Long_Original_File_Name_20250812_Wakkanai_Motorcycle_Touring_Special.mp4"
            let origPath = Path.Combine(root, origName)
            File.WriteAllText(origPath, "dummy video content 1")

            let proposedName = "20250812_Wakkanai.mp4"
            let expectedNewPath = Path.Combine(root, proposedName)

            let proposal: RenameProposal = {
                OriginalFullPath = origPath
                OriginalFileName = origName
                DirectoryPath = root
                OriginalLength = origPath.Length
                ProposedFileName = proposedName
                ProposedLength = expectedNewPath.Length
                AiComment = None
                IsSelected = true
            }

            let result = FileRenamer.executeRename [ proposal ]
            match result with
            | Error err -> failwith $"executeRename failed: {err}"
            | Ok undoRecords ->
                undoRecords.Length |> should equal 1
                let record = undoRecords.Head
                record.OriginalFullPath |> should equal origPath
                record.RenamedFullPath |> should equal expectedNewPath

                File.Exists(origPath) |> should equal false
                File.Exists(expectedNewPath) |> should equal true
                File.ReadAllText(expectedNewPath) |> should equal "dummy video content 1"
        finally
            cleanup root

    [<Fact>]
    let ``executeRename は移動先に同名ファイルが既に存在する場合、_1 サフィックスを付与して衝突を回避する`` () =
        let root = createTestEnvironment ()
        try
            // 既に存在している同名ファイル
            let conflictTarget = Path.Combine(root, "Target.mp4")
            File.WriteAllText(conflictTarget, "existing target")

            // リネーム対象ファイル
            let origPath = Path.Combine(root, "Source_Long_Name.mp4")
            File.WriteAllText(origPath, "source content")

            let proposal: RenameProposal = {
                OriginalFullPath = origPath
                OriginalFileName = "Source_Long_Name.mp4"
                DirectoryPath = root
                OriginalLength = origPath.Length
                ProposedFileName = "Target.mp4"
                ProposedLength = conflictTarget.Length
                AiComment = None
                IsSelected = true
            }

            let result = FileRenamer.executeRename [ proposal ]
            match result with
            | Error err -> failwith $"executeRename failed: {err}"
            | Ok undoRecords ->
                undoRecords.Length |> should equal 1
                let expectedUniquePath = Path.Combine(root, "Target_1.mp4")
                undoRecords.Head.RenamedFullPath |> should equal expectedUniquePath

                File.Exists(conflictTarget) |> should equal true
                File.ReadAllText(conflictTarget) |> should equal "existing target"

                File.Exists(expectedUniquePath) |> should equal true
                File.ReadAllText(expectedUniquePath) |> should equal "source content"
        finally
            cleanup root

    [<Fact>]
    let ``executeRename は IsSelected が false の提案をリネームしない`` () =
        let root = createTestEnvironment ()
        try
            let origPath = Path.Combine(root, "Unselected.mp4")
            File.WriteAllText(origPath, "unselected content")

            let proposal: RenameProposal = {
                OriginalFullPath = origPath
                OriginalFileName = "Unselected.mp4"
                DirectoryPath = root
                OriginalLength = origPath.Length
                ProposedFileName = "New_Unselected.mp4"
                ProposedLength = 10
                AiComment = None
                IsSelected = false
            }

            let result = FileRenamer.executeRename [ proposal ]
            match result with
            | Error err -> failwith $"executeRename failed: {err}"
            | Ok undoRecords ->
                undoRecords.Length |> should equal 0
                File.Exists(origPath) |> should equal true
                File.Exists(Path.Combine(root, "New_Unselected.mp4")) |> should equal false
        finally
            cleanup root

    [<Fact>]
    let ``executeUndo は直前のリネーム履歴レコードに基づき、完全な元ファイル名に復元する`` () =
        let root = createTestEnvironment ()
        try
            let origPath = Path.Combine(root, "Original_Path_Before_Rename.mp4")
            let renamedPath = Path.Combine(root, "Renamed.mp4")
            File.WriteAllText(renamedPath, "renamed content")

            let record: UndoRecord = {
                Id = Guid.NewGuid()
                Timestamp = DateTime.UtcNow
                OriginalFullPath = origPath
                RenamedFullPath = renamedPath
            }

            let undoResult = FileRenamer.executeUndo [ record ]
            match undoResult with
            | Error err -> failwith $"executeUndo failed: {err}"
            | Ok () ->
                File.Exists(origPath) |> should equal true
                File.Exists(renamedPath) |> should equal false
                File.ReadAllText(origPath) |> should equal "renamed content"
        finally
            cleanup root

    [<Fact>]
    let ``executeUndo は復元先（元パス）に既に別ファイルが存在する場合、UndoConflictError を返して上書きを防ぐ`` () =
        let root = createTestEnvironment ()
        try
            let origPath = Path.Combine(root, "Original.mp4")
            let renamedPath = Path.Combine(root, "Renamed.mp4")

            // 両方のファイルが存在している状態（元パスが別ファイルに占有されている）
            File.WriteAllText(origPath, "occupying foreign file")
            File.WriteAllText(renamedPath, "renamed target")

            let record: UndoRecord = {
                Id = Guid.NewGuid()
                Timestamp = DateTime.UtcNow
                OriginalFullPath = origPath
                RenamedFullPath = renamedPath
            }

            let undoResult = FileRenamer.executeUndo [ record ]
            match undoResult with
            | Ok () -> failwith "Expected executeUndo to fail with UndoConflictError"
            | Error (UndoConflictError (path, msg)) ->
                path |> should equal origPath
                msg |> should not' (be EmptyString)
                // 元ファイルが上書きされていないこと
                File.ReadAllText(origPath) |> should equal "occupying foreign file"
                File.ReadAllText(renamedPath) |> should equal "renamed target"
            | Error other -> failwith $"Unexpected error: {other}"
        finally
            cleanup root

    [<Fact>]
    let ``存在しない元ファイルをリネームしようとした場合 IoError を返す`` () =
        let root = createTestEnvironment ()
        try
            let nonExistent = Path.Combine(root, "NonExistent.mp4")
            let proposal: RenameProposal = {
                OriginalFullPath = nonExistent
                OriginalFileName = "NonExistent.mp4"
                DirectoryPath = root
                OriginalLength = nonExistent.Length
                ProposedFileName = "New.mp4"
                ProposedLength = 10
                AiComment = None
                IsSelected = true
            }

            let result = FileRenamer.executeRename [ proposal ]
            match result with
            | Ok _ -> failwith "Expected executeRename to fail"
            | Error (IoError (msg, _)) -> msg |> should not' (be EmptyString)
            | Error other -> failwith $"Unexpected error: {other}"
        finally
            cleanup root
