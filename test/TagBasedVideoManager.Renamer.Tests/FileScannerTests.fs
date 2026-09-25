namespace TagBasedVideoManager.Renamer.Tests

open System
open System.IO
open Xunit
open FsUnit
open TagBasedVideoManager.Renamer

module FileScannerTests =

    let createTestEnvironment () =
        let root = Path.Combine(Path.GetTempPath(), "ScannerTest_" + Guid.NewGuid().ToString("N"))
        let subDir = Path.Combine(root, "SubFolder")
        Directory.CreateDirectory(subDir) |> ignore
        root, subDir

    let cleanup (path: string) =
        if Directory.Exists(path) then
            try Directory.Delete(path, true) with _ -> ()

    [<Fact>]
    let ``scanLongPaths は閾値以上の動画ファイルを抽出し、閾値未満や非動画ファイルを除外する`` () =
        let root, subDir = createTestEnvironment ()
        try
            // 1. 短いパスの動画ファイル (閾値未満)
            let shortVideo = Path.Combine(root, "short.mp4")
            File.WriteAllText(shortVideo, "dummy video content")

            // 2. 非動画ファイル (閾値以上でも無視されるべき)
            let longTxtName = String.replicate 100 "a" + ".txt"
            let longTxtPath = Path.Combine(root, longTxtName)
            File.WriteAllText(longTxtPath, "dummy text")

            // 3. 閾値以上の動画ファイル (抽出されるべき)
            let longVideoName = String.replicate 120 "v" + ".mp4"
            let longVideoPath = Path.Combine(subDir, longVideoName)
            File.WriteAllText(longVideoPath, "dummy video 1")

            let threshold = longVideoPath.Length - 10

            let result = FileScanner.scanLongPaths root threshold
            match result with
            | Error err -> failwith $"scanLongPaths failed: {err}"
            | Ok candidates ->
                candidates.Length |> should equal 1
                let c = candidates.Head
                c.FullPath |> should equal longVideoPath
                c.FileName |> should equal longVideoName
                c.DirectoryPath |> should equal subDir
                c.PathLength |> should equal longVideoPath.Length
                c.FileSizeBytes |> should be (greaterThan 0L)
        finally
            cleanup root

    [<Fact>]
    let ``scanLongPaths は大文字小文字を区別せず .MP4 や .mkv 等の対象拡張子を認識する`` () =
        let root, subDir = createTestEnvironment ()
        try
            let longUpperMp4 = Path.Combine(root, String.replicate 80 "u" + ".MP4")
            let longMkv = Path.Combine(subDir, String.replicate 80 "m" + ".mkv")
            let longAviUpper = Path.Combine(subDir, String.replicate 80 "a" + ".AVI")
            File.WriteAllText(longUpperMp4, "content 1")
            File.WriteAllText(longMkv, "content 2")
            File.WriteAllText(longAviUpper, "content 3")

            let threshold = 50

            let result = FileScanner.scanLongPaths root threshold
            match result with
            | Error err -> failwith $"scanLongPaths failed: {err}"
            | Ok candidates ->
                candidates.Length |> should equal 3
                let fileNames = candidates |> List.map (fun c -> c.FileName)
                fileNames |> should contain (Path.GetFileName(longUpperMp4))
                fileNames |> should contain (Path.GetFileName(longMkv))
                fileNames |> should contain (Path.GetFileName(longAviUpper))
        finally
            cleanup root

    [<Fact>]
    let ``scanLongPaths はパス長境界値（threshold - 1 は除外、threshold 以上は抽出）を正しく判定する`` () =
        let root, _ = createTestEnvironment ()
        try
            // パス長を特定するためにファイル作成
            let targetLength = 120
            let prefix = Path.Combine(root, "f_")
            let padLength = targetLength - prefix.Length - 4 // ".mp4" = 4 chars
            let exactName = "f_" + String.replicate padLength "x" + ".mp4"
            let exactPath = Path.Combine(root, exactName)
            File.WriteAllText(exactPath, "boundary exact")

            let exactLen = exactPath.Length

            // 1. threshold = exactLen の場合 -> 抽出される
            match FileScanner.scanLongPaths root exactLen with
            | Ok list -> list.Length |> should equal 1
            | Error e -> failwith $"{e}"

            // 2. threshold = exactLen + 1 の場合 -> 除外される
            match FileScanner.scanLongPaths root (exactLen + 1) with
            | Ok list -> list.Length |> should equal 0
            | Error e -> failwith $"{e}"

            // 3. threshold = exactLen - 1 の場合 -> 抽出される
            match FileScanner.scanLongPaths root (exactLen - 1) with
            | Ok list -> list.Length |> should equal 1
            | Error e -> failwith $"{e}"
        finally
            cleanup root

    [<Fact>]
    let ``存在しないディレクトリを指定した場合は IoError を返す`` () =
        let nonExistent = Path.Combine(Path.GetTempPath(), "NonExistentDir_" + Guid.NewGuid().ToString("N"))
        let result = FileScanner.scanLongPaths nonExistent 240
        match result with
        | Ok _ -> failwith "Expected failure for non-existent directory"
        | Error (IoError (msg, _)) -> msg |> should not' (be EmptyString)
        | Error other -> failwith $"Unexpected error type: {other}"
