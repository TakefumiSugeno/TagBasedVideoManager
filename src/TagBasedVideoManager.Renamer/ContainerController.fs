namespace TagBasedVideoManager.Renamer

open System
open System.Diagnostics
open System.Net.Http
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open TagBasedVideoManager.Renamer

module ContainerController =

    /// 対象のコンテナ名
    let containerName = "tag-based-video-manager"

    /// コンテナランタイムを自動検出する
    let detectContainerRuntime (isWindows: bool) (commandExists: string -> bool) : Result<ContainerRuntime, string> =
        if isWindows then
            if commandExists "wslc" then
                Ok Wslc
            elif commandExists "docker" then
                Ok Docker
            else
                Error "No container runtime found on Windows (wslc or docker required)"
        else
            if commandExists "docker" then
                Ok Docker
            elif commandExists "podman" then
                Ok Podman
            else
                Error "No container runtime found on Linux/macOS (docker or podman required)"

    /// コンテナランタイムに対応する実行ファイル名
    let runtimeExecutableName (runtime: ContainerRuntime) : string =
        match runtime with
        | Wslc -> "wslc"
        | Docker -> "docker"
        | Podman -> "podman"

    let private defaultProcessRunner (exe: string) (workingDir: string) (args: string) : Async<int * string * string> =
        async {
            try
                let psi = ProcessStartInfo()
                psi.FileName <- exe
                psi.Arguments <- args
                psi.WorkingDirectory <- workingDir
                psi.RedirectStandardOutput <- true
                psi.RedirectStandardError <- true
                psi.UseShellExecute <- false
                psi.CreateNoWindow <- true

                use proc = new Process()
                proc.StartInfo <- psi
                proc.Start() |> ignore

                let stdoutTask = proc.StandardOutput.ReadToEndAsync()
                let stderrTask = proc.StandardError.ReadToEndAsync()

                use cts = new CancellationTokenSource(TimeSpan.FromSeconds(10.0))
                try
                    do! proc.WaitForExitAsync(cts.Token) |> Async.AwaitTask
                    let! stdout = stdoutTask |> Async.AwaitTask
                    let! stderr = stderrTask |> Async.AwaitTask
                    return proc.ExitCode, stdout, stderr
                with
                | :? OperationCanceledException ->
                    try proc.Kill(true) with _ -> ()
                    return -2, "", "プロセスが10秒以内に終了しなかったため強制終了されました (Timeout)"
            with ex ->
                return 1, "", ex.Message
        }

    let private defaultHttpHealthChecker (url: string) : Async<bool> =
        async {
            try
                use client = new HttpClient()
                client.Timeout <- TimeSpan.FromSeconds(2.0)
                let! resp = client.GetAsync(url) |> Async.AwaitTask
                return resp.IsSuccessStatusCode
            with _ ->
                return false
        }

    let private parseContainerState (stateStr: string) (statusStr: string) : ContainerState =
        let s = stateStr.ToLowerInvariant()
        let stat = statusStr.ToLowerInvariant()
        if stat.Contains("unhealthy") then Unhealthy
        elif s = "running" then Running
        elif s = "restarting" then Restarting
        elif s = "exited" || s = "stopped" || s = "dead" || s = "created" then Stopped
        else Stopped

    /// OS 環境から利用可能なランタイムを自動検出する既定関数
    let defaultRuntime () : ContainerRuntime option =
        let isWin = OperatingSystem.IsWindows()
        let cmdExists (cmd: string) : bool =
            try
                let psi = ProcessStartInfo()
                psi.FileName <- if isWin then "where" else "which"
                psi.Arguments <- cmd
                psi.CreateNoWindow <- true
                psi.UseShellExecute <- false
                use proc = new Process()
                proc.StartInfo <- psi
                proc.Start() |> ignore
                proc.WaitForExit()
                proc.ExitCode = 0
            with _ -> false
        match detectContainerRuntime isWin cmdExists with
        | Ok r -> Some r
        | Error _ -> None

    /// ps -a --format json の出力をパースし、(ContainerState, ContainerId option) を抽出する
    let parseContainerPsOutput (output: string) : ContainerState * string option =
        let trimmed = output.Trim()
        if String.IsNullOrWhiteSpace(trimmed) then
            NotFound, None
        else
            let parseElement (el: JsonElement) : (string * string * string * string) option =
                try
                    // Names は文字列または配列の可能性がある
                    let names =
                        if el.TryGetProperty("Names", ref Unchecked.defaultof<JsonElement>) then
                            let n = el.GetProperty("Names")
                            if n.ValueKind = JsonValueKind.Array then
                                n.EnumerateArray() |> Seq.map (fun x -> x.GetString()) |> String.concat " "
                            else
                                n.GetString()
                        elif el.TryGetProperty("Name", ref Unchecked.defaultof<JsonElement>) then
                            el.GetProperty("Name").GetString()
                        elif el.TryGetProperty("Service", ref Unchecked.defaultof<JsonElement>) then
                            el.GetProperty("Service").GetString()
                        else ""

                    let state =
                        if el.TryGetProperty("State", ref Unchecked.defaultof<JsonElement>) then
                            el.GetProperty("State").GetString()
                        else ""

                    let status =
                        if el.TryGetProperty("Status", ref Unchecked.defaultof<JsonElement>) then
                            el.GetProperty("Status").GetString()
                        else ""

                    let id =
                        if el.TryGetProperty("ID", ref Unchecked.defaultof<JsonElement>) then
                            el.GetProperty("ID").GetString()
                        elif el.TryGetProperty("Id", ref Unchecked.defaultof<JsonElement>) then
                            el.GetProperty("Id").GetString()
                        else ""

                    Some (names, state, status, id)
                with _ ->
                    None

            try
                // 1. 単一JSON配列としてのパース試行
                let elements =
                    try
                        use doc = JsonDocument.Parse(trimmed)
                        if doc.RootElement.ValueKind = JsonValueKind.Array then
                            doc.RootElement.EnumerateArray()
                            |> Seq.choose parseElement
                            |> Seq.toList
                        else
                            parseElement doc.RootElement |> Option.toList
                    with _ ->
                        // 2. 改行区切りNDJSONとしてのフォールバックパース試行
                        trimmed.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
                        |> Seq.choose (fun line ->
                            try
                                use doc = JsonDocument.Parse(line)
                                parseElement doc.RootElement
                            with _ -> None
                        )
                        |> Seq.toList

                // 何らかの要素が正常にパースできたが、対象コンテナがいないかチェック
                let target =
                    elements
                    |> List.tryFind (fun (names, _, _, _) ->
                        names.IndexOf(containerName, StringComparison.OrdinalIgnoreCase) >= 0
                        || names.IndexOf("video-manager", StringComparison.OrdinalIgnoreCase) >= 0
                    )

                match target with
                | Some (_, state, status, id) ->
                    let cState = parseContainerState state status
                    let cId = if String.IsNullOrWhiteSpace(id) then None else Some id
                    cState, cId
                | None ->
                    if elements.IsEmpty && not (trimmed.StartsWith("[") && trimmed.EndsWith("]")) then
                        // 有効なJSON要素が1つも取れずパース不能だった場合
                        eprintfn $"[警告] ContainerController: JSON 出力のパースに失敗しました。生テキスト: {trimmed}"
                        Unhealthy, None
                    else
                        NotFound, None
            with ex ->
                eprintfn $"[警告] ContainerController: JSON パース例外が発生しました: {ex.Message}。生テキスト: {trimmed}"
                Unhealthy, None

    /// コンテナのステータスおよびポート5620の疎通を確認する
    let checkStatus
        (runtime: ContainerRuntime option)
        (processRunner: (string -> string -> string -> Async<int * string * string>) option)
        (httpHealthChecker: (string -> Async<bool>) option)
        (workingDirectory: string)
        : Async<DockerStatus> =
        async {
            match runtime with
            | None ->
                return {
                    State = NotInstalled
                    IsPortAccessible = false
                    ContainerId = None
                    LastChecked = DateTime.UtcNow
                }
            | Some r ->
                let runner = defaultArg processRunner defaultProcessRunner
                let healthChecker = defaultArg httpHealthChecker defaultHttpHealthChecker
                let exe = runtimeExecutableName r

                let! exitCode, stdout, stderr = runner exe workingDirectory "ps -a --format json"

                let state, containerId =
                    if exitCode = 0 then
                        parseContainerPsOutput stdout
                    else
                        eprintfn $"[警告] ContainerController: ps コマンドが終了コード {exitCode} で終了しました。stderr: {stderr}"
                        NotFound, None

                let! finalState, isPortAccessible =
                    if state = Running then
                        async {
                            let! healthy = healthChecker "http://localhost:5620/"
                            if healthy then
                                return Running, true
                            else
                                return Unhealthy, false
                        }
                    else
                        async { return state, false }

                return {
                    State = finalState
                    IsPortAccessible = isPortAccessible
                    ContainerId = containerId
                    LastChecked = DateTime.UtcNow
                }
        }

    /// タイムアウト指定付きでコンテナアクションを実行する
    let executeActionWithTimeout
        (timeout: TimeSpan)
        (runtime: ContainerRuntime option)
        (processRunner: (string -> string -> string -> Async<int * string * string>) option)
        (workingDirectory: string)
        (action: string)
        : Async<Result<string, RenamerError>> =
        async {
            match runtime with
            | None ->
                return Error (ValidationError "Container runtime is not available or not installed")
            | Some r ->
                let runner = defaultArg processRunner defaultProcessRunner
                let exe = runtimeExecutableName r
                let cmd = $"{action} {containerName}"

                let runTask = Async.StartAsTask (runner exe workingDirectory cmd)
                let delayTask = Task.Delay(timeout)

                let! completed = Task.WhenAny(runTask, delayTask) |> Async.AwaitTask
                if completed = (runTask :> Task) then
                    let exitCode, stdout, stderr = runTask.Result
                    if exitCode = 0 then
                        return Ok stdout
                    else
                        // 冪等性: stop 時に既に停止または存在しない場合は成功扱いとする
                        if action = "stop" && (stderr.Contains("No such container") || stderr.Contains("is not running")) then
                            return Ok stdout
                        else
                            return Error (ContainerError (action, exitCode, stderr))
                else
                    return Error (TimeoutError action)
        }

    /// 既定のタイムアウト (10秒) でコンテナアクションを実行する
    let executeAction
        (runtime: ContainerRuntime option)
        (processRunner: (string -> string -> string -> Async<int * string * string>) option)
        (workingDirectory: string)
        (action: string)
        : Async<Result<string, RenamerError>> =
        executeActionWithTimeout (TimeSpan.FromSeconds(10.0)) runtime processRunner workingDirectory action
