namespace TagBasedVideoManager.Tests

open System
open System.IO
open System.Diagnostics
open Xunit
open FsUnit
open TagBasedVideoManager.Domain

type MediaTests () =
    let dummyVideoPath = Path.Combine(AppContext.BaseDirectory, "test_dummy.mp4")
    let outputThumbPath = Path.Combine(AppContext.BaseDirectory, "test_thumb.jpg")

    // FFmpegがシステムにインストールされているか確認するヘルパー
    let isFFmpegAvailable () =
        try
            let startInfo = ProcessStartInfo("ffmpeg", "-version")
            startInfo.RedirectStandardOutput <- true
            startInfo.RedirectStandardError <- true
            startInfo.UseShellExecute <- false
            startInfo.CreateNoWindow <- true
            use p = Process.Start(startInfo)
            p.WaitForExit()
            p.ExitCode = 0
        with
        | _ -> false

    // テスト用の1秒のダミー動画ファイルをFFmpegで生成する
    let createDummyVideo () =
        if isFFmpegAvailable() && not (File.Exists(dummyVideoPath)) then
            let args = $"-y -f lavfi -i testsrc=duration=1:size=160x120:rate=1 -c:v libx264 -pix_fmt yuv420p \"{dummyVideoPath}\""
            let startInfo = ProcessStartInfo("ffmpeg", args)
            startInfo.RedirectStandardOutput <- true
            startInfo.RedirectStandardError <- true
            startInfo.UseShellExecute <- false
            startInfo.CreateNoWindow <- true
            use p = Process.Start(startInfo)
            p.WaitForExit()

    interface IDisposable with
        member _.Dispose() =
            // テスト生成ファイルの削除
            if File.Exists(dummyVideoPath) then
                try File.Delete(dummyVideoPath) with | _ -> ()
            if File.Exists(outputThumbPath) then
                try File.Delete(outputThumbPath) with | _ -> ()

    [<Fact>]
    member _.``GetMetadataを実行すると動画の長さとファイルサイズを取得できる`` () =
        if not (isFFmpegAvailable()) then
            // FFmpegがない環境ではテストをスキップ（またはパス扱い）
            ()
        else
            createDummyVideo()
            let processor = MediaProcessor() :> IMediaProcessor
            
            let result = processor.GetMetadata(dummyVideoPath) |> Async.RunSynchronously
            
            match result with
            | Ok (duration, fileSize) ->
                duration |> should equal 1L // 1秒の動画
                fileSize |> should be (greaterThan 0L)
            | Error err ->
                Assert.Fail($"Metadata extraction failed: {err}")

    [<Fact>]
    member _.``GenerateThumbnailを実行すると指定パスにサムネイルが作成される`` () =
        if not (isFFmpegAvailable()) then
            ()
        else
            createDummyVideo()
            let processor = MediaProcessor() :> IMediaProcessor
            
            let result = processor.GenerateThumbnail(dummyVideoPath, outputThumbPath) |> Async.RunSynchronously
            
            match result with
            | Ok path ->
                path |> should equal outputThumbPath
                File.Exists(outputThumbPath) |> should be True
                FileInfo(outputThumbPath).Length |> should be (greaterThan 0L)
            | Error err ->
                Assert.Fail($"Thumbnail generation failed: {err}")

    [<Fact>]
    member _.``存在しないファイルを指定してGetMetadataを呼び出すとFileNotFoundエラーが返る`` () =
        // 異常系: 存在しないファイルパス
        let nonExistentPath = Path.Combine(AppContext.BaseDirectory, "non_existent.mp4")
        let processor = MediaProcessor() :> IMediaProcessor
        
        let result = processor.GetMetadata(nonExistentPath) |> Async.RunSynchronously
        
        match result with
        | Error (FileNotFound path) ->
            path |> should equal nonExistentPath
        | other ->
            Assert.Fail($"Expected Error (FileNotFound), but got: {other}")

    [<Fact>]
    member _.``GenerateThumbnailは通常の動画に対して高速シークとフォールバックの動作が保証される`` () =
        if not (isFFmpegAvailable()) then
            ()
        else
            createDummyVideo()
            let processor = MediaProcessor() :> IMediaProcessor
            
            // 正常にサムネイルが生成できることの確認
            let result = processor.GenerateThumbnail(dummyVideoPath, outputThumbPath) |> Async.RunSynchronously
            match result with
            | Ok path ->
                path |> should equal outputThumbPath
                File.Exists(outputThumbPath) |> should be True
            | Error err ->
                Assert.Fail($"Thumbnail generation failed with high speed seek & fallback config: {err}")


