namespace TagBasedVideoManager.Domain

open System
open System.IO
open System.Diagnostics
open System.Globalization
open System.Threading
open System.Collections.Concurrent

module PathHelper =
    /// パスがシンボリックリンク（ReparsePoint）であれば、再帰的にリンク先の実体ファイルを解決する。
    /// そうでなければ元のパスをそのまま返す。
    let resolvePhysicalPath (path: string) : string =
        try
            if File.Exists(path) then
                let fi = FileInfo(path)
                if fi.Attributes.HasFlag(FileAttributes.ReparsePoint) then
                    let target = fi.ResolveLinkTarget(true)
                    if target <> null then target.FullName else path
                else
                    path
            elif Directory.Exists(path) then
                let di = DirectoryInfo(path)
                if di.Attributes.HasFlag(FileAttributes.ReparsePoint) then
                    let target = di.ResolveLinkTarget(true)
                    if target <> null then target.FullName else path
                else
                    path
            else
                path
        with
        | _ -> path

/// メディア処理の抽象化インターフェース (FFmpeg/ffprobeのモック用)
type IMediaProcessor =
    /// 指定された動画の (再生時間（秒）, ファイルサイズ（バイト）) を取得する
    abstract member GetMetadata : videoPath:string -> Async<Result<int64 * int64, MediaError>>
    /// 指定された動画からサムネイル画像を生成し、保存先パスを返す
    abstract member GenerateThumbnail : videoPath:string * outputPath:string -> Async<Result<string, MediaError>>
    /// 実行中のすべてのプロセスをキャンセル（強制終了）する
    abstract member CancelAll : unit -> unit

/// FFmpeg/ffprobeを外部プロセスとして呼び出す実装クラス
type MediaProcessor () =
    
    static let processSemaphore = new SemaphoreSlim(1, 1)
    static let metadataCache = new ConcurrentDictionary<string, int64 * int64>()
    static let activeProcesses = ConcurrentDictionary<Process, byte>()

    /// 外部プロセスを非同期的に実行するヘルパー関数
    let runProcess (useSemaphore: bool) (cmd: string) (args: string) =
        async {
            if useSemaphore then
                do! processSemaphore.WaitAsync() |> Async.AwaitTask
            let mutable pOpt = None
            try
                try
                    let startInfo = ProcessStartInfo(cmd, args)
                    startInfo.RedirectStandardOutput <- true
                    startInfo.RedirectStandardError <- true
                    startInfo.UseShellExecute <- false
                    startInfo.CreateNoWindow <- true
                    
                    let p = new Process()
                    p.StartInfo <- startInfo
                    pOpt <- Some p
                    activeProcesses.TryAdd(p, 0uy) |> ignore

                    let stdOutSb = System.Text.StringBuilder()
                    let stdErrSb = System.Text.StringBuilder()
                    p.OutputDataReceived.Add(fun e -> if e.Data <> null then stdOutSb.AppendLine(e.Data) |> ignore)
                    p.ErrorDataReceived.Add(fun e -> if e.Data <> null then stdErrSb.AppendLine(e.Data) |> ignore)

                    if p.Start() then
                        p.BeginOutputReadLine()
                        p.BeginErrorReadLine()
                        do! p.WaitForExitAsync() |> Async.AwaitTask
                        let stdOut = stdOutSb.ToString()
                        let stdErr = stdErrSb.ToString()
                        if p.ExitCode = 0 then
                            return Ok (stdOut.Trim(), stdErr.Trim())
                        else
                            return Error (FFmpegExecutionError $"Process exited with code {p.ExitCode}. Error: {stdErr}")
                    else
                        return Error (FFmpegExecutionError $"Failed to start process: {cmd}")
                with
                | ex -> return Error (FFmpegExecutionError ex.Message)
            finally
                match pOpt with
                | Some p -> 
                    activeProcesses.TryRemove(p) |> ignore
                    p.Dispose()
                | None -> ()
                if useSemaphore then
                    processSemaphore.Release() |> ignore
        }

    interface IMediaProcessor with
        member this.CancelAll() =
            for kvp in activeProcesses do
                try
                    if not kvp.Key.HasExited then
                        kvp.Key.Kill(true)
                with _ -> ()
            activeProcesses.Clear()
        member this.GetMetadata(videoPath) =
            async {
                let resolvedPath = PathHelper.resolvePhysicalPath videoPath
                if not (File.Exists(resolvedPath)) then
                    return Error (FileNotFound videoPath)
                else
                    match metadataCache.TryGetValue(resolvedPath) with
                    | true, cached -> return Ok cached
                    | false, _ ->
                        // 1. ファイルサイズは.NET標準機能で高速に取得
                        let fileSize = FileInfo(resolvedPath).Length
                        
                        // 2. ffprobeで再生時間（秒）を取得 (未インストール時はデフォルト120秒でフォールバック)
                        let args = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{resolvedPath}\""
                        let! result = runProcess false "ffprobe" args
                        let res =
                            match result with
                            | Ok (stdOut, _) ->
                                match Double.TryParse(stdOut, NumberStyles.Any, CultureInfo.InvariantCulture) with
                                | true, durationDouble ->
                                    let duration = int64 (Math.Round(durationDouble))
                                    Ok (duration, fileSize)
                                | false, _ ->
                                    // 解析失敗時はデフォルト120秒で救済
                                    Ok (120L, fileSize)
                            | Error _ -> 
                                // ffprobe未インストールなどのプロセス起動失敗時もデフォルト120秒で救済
                                Ok (120L, fileSize)
                        match res with
                        | Ok data -> metadataCache.[resolvedPath] <- data
                        | _ -> ()
                        return res
            }

        member this.GenerateThumbnail(videoPath, outputPath) =
            async {
                let resolvedPath = PathHelper.resolvePhysicalPath videoPath
                if not (File.Exists(resolvedPath)) then
                    return Error (FileNotFound videoPath)
                else
                    // 1. 動画の長さを取得して中間地点（秒）を算出 (1秒以下の動画は先頭0.0秒から切り出す)
                    let! metaResult = (this :> IMediaProcessor).GetMetadata(resolvedPath)
                    let ssSec =
                        match metaResult with
                        | Ok (duration, _) when duration > 1L -> float duration / 2.0
                        | _ -> 0.0
                    
                    // 2. まずは高速シーク（-ss を -i の前）で試行（余計なデコードが発生せず一瞬で完了）
                    let fastArgs = $"-y -ss {ssSec.ToString(CultureInfo.InvariantCulture)} -hwaccel auto -i \"{resolvedPath}\" -vframes 1 -q:v 2 -pix_fmt yuvj420p \"{outputPath}\""
                    let! fastResult = runProcess true "ffmpeg" fastArgs
                    
                    let! finalResult =
                        match fastResult with
                        | Ok _ when File.Exists(outputPath) ->
                            async { return Ok (outputPath, "fast") }
                        | _ ->
                            // 高速シークで失敗、またはファイルが出力されなかった場合は、
                            // 従来の安全シーク（-ss を -i の後）でフォールバック実行（デコードを伴うため遅いが確実）
                            let safeArgs = $"-y -hwaccel auto -i \"{resolvedPath}\" -ss {ssSec.ToString(CultureInfo.InvariantCulture)} -vframes 1 -q:v 2 -pix_fmt yuvj420p \"{outputPath}\""
                            async {
                                let! safeResult = runProcess true "ffmpeg" safeArgs
                                match safeResult with
                                | Ok _ -> return Ok (outputPath, "safe")
                                | Error err -> return Error err
                            }
                    
                    match finalResult with
                    | Ok (path, mode) ->
                        if File.Exists(path) then
                            return Ok path
                        else
                            return Error (FFmpegExecutionError $"Output thumbnail file was not created via {mode} seek.")
                    | Error err -> return Error err
            }
