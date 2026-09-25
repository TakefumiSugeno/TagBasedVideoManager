namespace TagBasedVideoManager.Renamer.Tests

open System
open System.IO
open Xunit
open FsUnit
open TagBasedVideoManager.Renamer

module RenameIntegrationE2ETests =

    /// <summary>
    /// 一時テスト用ディレクトリをセットアップし、テスト終了後に確実に削除するユーティリティ
    /// </summary>
    let withTempDirectory (f: string -> unit) =
        let tempDir = Path.Combine(Path.GetTempPath(), "tbvm_e2e_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        try
            f tempDir
        finally
            try
                if Directory.Exists(tempDir) then
                    Directory.Delete(tempDir, true)
            with _ -> ()

    [<Fact>]
    let ``E2E: scan -> propose with AI comments -> physical rename -> verify -> undo restore`` () =
        withTempDirectory (fun tempDir ->
            // -----------------------------------------------------------------
            // 1. テストファイルの作成
            // -----------------------------------------------------------------
            // (A) 240文字以上の長パスファイル (正常系: 日時付き)
            let targetNameLen1 = max 20 (245 - tempDir.Length - 1)
            let longName1 = "2024-01-01_" + String('a', targetNameLen1 - 15) + ".mp4"
            let longPath1 = Path.Combine(tempDir, longName1)
            File.WriteAllText(longPath1, "video-content-1")

            // (B) 240文字以上の長パスファイル (変則系: 日時なし・AIコメント対象)
            let targetNameLen2 = max 20 (245 - tempDir.Length - 1)
            let longName2 = "strange_no_date_" + String('b', targetNameLen2 - 20) + ".mp4"
            let longPath2 = Path.Combine(tempDir, longName2)
            File.WriteAllText(longPath2, "video-content-2")

            // (C) 閾値未満の通常ファイル (走査除外対象)
            let shortName = "short_sample.mp4"
            let shortPath = Path.Combine(tempDir, shortName)
            File.WriteAllText(shortPath, "short-content")

            // パス長の前提条件検証
            (longPath1.Length >= 240) |> should equal true
            (longPath2.Length >= 240) |> should equal true
            (shortPath.Length < 240) |> should equal true

            // -----------------------------------------------------------------
            // 2. 走査 (FileScanner.scanLongPaths)
            // -----------------------------------------------------------------
            let scanResult = FileScanner.scanLongPaths tempDir 240
            match scanResult with
            | Error err -> failwith $"走査エラー: {err}"
            | Ok scannedFiles ->
                scannedFiles.Length |> should equal 2
                scannedFiles |> List.map (fun f -> f.FileName) |> should contain longName1
                scannedFiles |> List.map (fun f -> f.FileName) |> should contain longName2

                // -----------------------------------------------------------------
                // 3. AI提案のモック生成（問題時限定AIコメントの含有をシミュレート）
                // -----------------------------------------------------------------
                let proposedName1 = "2024-01-01_nature_doc_short.mp4"
                let proposedName2 = "2026-09-26_strange_video_short.mp4"
                let proposedPath1 = Path.Combine(tempDir, proposedName1)
                let proposedPath2 = Path.Combine(tempDir, proposedName2)
                let aiComment2 = "元ファイル名に撮影日時が含まれていなかったため、本日の日付で補完しました。"

                let proposal1: RenameProposal = {
                    OriginalFullPath = longPath1
                    OriginalFileName = longName1
                    DirectoryPath = tempDir
                    OriginalLength = longPath1.Length
                    ProposedFileName = proposedName1
                    ProposedLength = proposedPath1.Length
                    AiComment = None // 正常時はコメントなし
                    IsSelected = true
                }

                let proposal2: RenameProposal = {
                    OriginalFullPath = longPath2
                    OriginalFileName = longName2
                    DirectoryPath = tempDir
                    OriginalLength = longPath2.Length
                    ProposedFileName = proposedName2
                    ProposedLength = proposedPath2.Length
                    AiComment = Some aiComment2 // 問題時のみコメントあり
                    IsSelected = true
                }

                // AIコメントの条件付き格納検証
                proposal1.AiComment |> should equal None
                proposal2.AiComment |> should equal (Some aiComment2)

                // -----------------------------------------------------------------
                // 4. 物理リネーム実行 (FileRenamer.executeRename)
                // -----------------------------------------------------------------
                let renameResult = FileRenamer.executeRename [ proposal1; proposal2 ]
                match renameResult with
                | Error err -> failwith $"リネームに失敗しました: {err}"
                | Ok undoEntries ->
                    undoEntries.Length |> should equal 2

                    // 物理ファイルの状態検証
                    File.Exists(longPath1) |> should equal false
                    File.Exists(longPath2) |> should equal false
                    File.Exists(proposedPath1) |> should equal true
                    File.Exists(proposedPath2) |> should equal true

                    // ファイル内容が維持されていることの検証
                    File.ReadAllText(proposedPath1) |> should equal "video-content-1"
                    File.ReadAllText(proposedPath2) |> should equal "video-content-2"

                    // -----------------------------------------------------------------
                    // 5. リネーム後の再走査で対象件数が 0 件になることを検証
                    // -----------------------------------------------------------------
                    let reScanResult = FileScanner.scanLongPaths tempDir 240
                    match reScanResult with
                    | Error err -> failwith $"再走査エラー: {err}"
                    | Ok reScannedFiles ->
                        reScannedFiles.Length |> should equal 0

                    // -----------------------------------------------------------------
                    // 6. Undo逆リネーム実行 (FileRenamer.executeUndo) による完全復元
                    // -----------------------------------------------------------------
                    let undoResult = FileRenamer.executeUndo undoEntries
                    match undoResult with
                    | Error err -> failwith $"Undoに失敗しました: {err}"
                    | Ok () ->
                        // 短縮ファイルが存在しなくなり、元の長パスファイルが完全に復元されていること
                        File.Exists(proposedPath1) |> should equal false
                        File.Exists(proposedPath2) |> should equal false
                        File.Exists(longPath1) |> should equal true
                        File.Exists(longPath2) |> should equal true

                        // 復元されたファイルの内容検証
                        File.ReadAllText(longPath1) |> should equal "video-content-1"
                        File.ReadAllText(longPath2) |> should equal "video-content-2"

                        // 再度走査すると、元通り2件の長パスファイルが抽出されること
                        let restoredScanResult = FileScanner.scanLongPaths tempDir 240
                        match restoredScanResult with
                        | Error err -> failwith $"復元後走査エラー: {err}"
                        | Ok restoredScanned ->
                            restoredScanned.Length |> should equal 2
        )

    [<Fact>]
    let ``E2E: collision auto-uniquification and undo safety check`` () =
        withTempDirectory (fun tempDir ->
            // 同一フォルダ内に、AI提案が同名になってしまう2つの長パスファイルを作成
            let p = String('x', 200)
            let fileA = Path.Combine(tempDir, "video_A_" + p + ".mp4")
            let fileB = Path.Combine(tempDir, "video_B_" + p + ".mp4")
            File.WriteAllText(fileA, "content-A")
            File.WriteAllText(fileB, "content-B")

            // 両者とも "video_short.mp4" を提案
            let targetName = "video_short.mp4"
            let targetPath = Path.Combine(tempDir, targetName)

            let propA: RenameProposal = {
                OriginalFullPath = fileA
                OriginalFileName = Path.GetFileName(fileA)
                DirectoryPath = tempDir
                OriginalLength = fileA.Length
                ProposedFileName = targetName
                ProposedLength = targetPath.Length
                AiComment = None
                IsSelected = true
            }

            let propB: RenameProposal = {
                OriginalFullPath = fileB
                OriginalFileName = Path.GetFileName(fileB)
                DirectoryPath = tempDir
                OriginalLength = fileB.Length
                ProposedFileName = targetName
                ProposedLength = targetPath.Length
                AiComment = None
                IsSelected = true
            }

            // リネーム実行: 自動で 2件目が _1 に一意化される
            let renameResult = FileRenamer.executeRename [ propA; propB ]
            match renameResult with
            | Error err -> failwith $"衝突リネームに失敗しました: {err}"
            | Ok undoEntries ->
                undoEntries.Length |> should equal 2

                let expectedUniqueB = Path.Combine(tempDir, "video_short_1.mp4")
                File.Exists(targetPath) |> should equal true
                File.Exists(expectedUniqueB) |> should equal true
                File.ReadAllText(targetPath) |> should equal "content-A"
                File.ReadAllText(expectedUniqueB) |> should equal "content-B"

                // Undo復元
                let undoResult = FileRenamer.executeUndo undoEntries
                match undoResult with
                | Error err -> failwith $"Undoに失敗しました: {err}"
                | Ok () ->
                    File.Exists(fileA) |> should equal true
                    File.Exists(fileB) |> should equal true
                    File.Exists(targetPath) |> should equal false
                    File.Exists(expectedUniqueB) |> should equal false
        )
