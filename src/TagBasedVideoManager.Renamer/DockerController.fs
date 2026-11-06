namespace TagBasedVideoManager.Renamer

open System
open System.Diagnostics
open System.Net.Http
open System.Text.Json
open TagBasedVideoManager.Renamer

module DockerController =

    let private defaultProcessRunner (workingDir: string) (cmd: string) : Async<int * string * string> =
        async {
            try
                let psi = ProcessStartInfo()
                psi.FileName <- "docker"
                psi.Arguments <- cmd
                psi.WorkingDirectory <- workingDir
                psi.RedirectStandardOutput <- true
                psi.RedirectStandardError <- true
                psi.UseShellExecute <- false
                psi.CreateNoWindow <- true

                use proc = new Process()
                proc.StartInfo <- psi
                proc.Start() |> ignore

                let! stdout = proc.StandardOutput.ReadToEndAsync() |> Async.AwaitTask
                let! stderr = proc.StandardError.ReadToEndAsync() |> Async.AwaitTask
                do! proc.WaitForExitAsync() |> Async.AwaitTask
                return proc.ExitCode, stdout, stderr
            with
            | ex ->
                return 1, "", ex.Message
        }

    let private defaultHttpHealthChecker (url: string) : Async<bool> =
        async {
            try
                use client = new HttpClient()
                client.Timeout <- TimeSpan.FromSeconds(2.0)
                let! resp = client.GetAsync(url) |> Async.AwaitTask
                return resp.IsSuccessStatusCode
            with
            | _ ->
                return false
        }

    let private parseContainerState (stateStr: string) (statusStr: string) : ContainerState =
        let s = stateStr.ToLowerInvariant()
        let stat = statusStr.ToLowerInvariant()
        if stat.Contains("unhealthy") then Unhealthy
        elif s = "running" then Running
        elif s = "restarting" then Restarting
        elif s = "exited" || s = "stopped" || s = "dead" then Stopped
        else Stopped

    /// docker compose ps --format json の出力をパースし、(ContainerState, ContainerId option) を抽出する
    let parseDockerComposePsOutput (output: string) : ContainerState * string option =
        let trimmed = output.Trim()
        if String.IsNullOrWhiteSpace(trimmed) then
            NotFound, None
        else
            let parseElement (el: JsonElement) : (string * string * string * string) option =
                try
                    let service =
                        if el.TryGetProperty("Service", ref Unchecked.defaultof<JsonElement>) then
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
                    Some (service, state, status, id)
                with
                | _ -> None

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
                with
                | _ ->
                    // 2. 改行区切りNDJSONとしてのフォールバックパース試行
                    trimmed.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
                    |> Seq.choose (fun line ->
                        try
                            use doc = JsonDocument.Parse(line)
                            parseElement doc.RootElement
                        with
                        | _ -> None
                    )
                    |> Seq.toList

            // video-manager サービスまたは tag-based-video-manager を含むものを検索
            let targetContainer =
                elements
                |> List.tryFind (fun (service, _, _, _) ->
                    String.Equals(service, "video-manager", StringComparison.OrdinalIgnoreCase)
                    || service.IndexOf("video-manager", StringComparison.OrdinalIgnoreCase) >= 0
                )

            match targetContainer with
            | Some (_, state, status, id) ->
                let cState = parseContainerState state status
                let cId = if String.IsNullOrWhiteSpace(id) then None else Some id
                cState, cId
            | None ->
                NotFound, None

    /// コンテナのステータスおよびポート5620の疎通を確認する
    let checkStatus
        (processRunner: (string -> string -> Async<int * string * string>) option)
        (httpHealthChecker: (string -> Async<bool>) option)
        (workingDirectory: string)
        : Async<DockerStatus> =
        async {
            let runner = defaultArg processRunner defaultProcessRunner
            let healthChecker = defaultArg httpHealthChecker defaultHttpHealthChecker

            let! exitCode, stdout, _stderr = runner workingDirectory "compose ps --format json"

            let state, containerId =
                if exitCode = 0 then
                    parseDockerComposePsOutput stdout
                else
                    NotFound, None

            let! isPortAccessible =
                if state = Running then
                    healthChecker "http://localhost:5620/"
                else
                    async { return false }

            return {
                State = state
                IsPortAccessible = isPortAccessible
                ContainerId = containerId
                LastChecked = DateTime.UtcNow
            }
        }

    /// Docker Compose アクションを実行する
    let executeAction
        (processRunner: (string -> string -> Async<int * string * string>) option)
        (workingDirectory: string)
        (action: string)
        : Async<Result<string, RenamerError>> =
        async {
            let runner = defaultArg processRunner defaultProcessRunner
            let cmd = $"compose {action}"
            let! exitCode, stdout, stderr = runner workingDirectory cmd

            if exitCode = 0 then
                return Ok stdout
            else
                return Error (DockerError (cmd, exitCode, stderr))
        }
