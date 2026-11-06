namespace TagBasedVideoManager.Renamer.Tests

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Sockets
open System.Threading
open Xunit
open FsUnit
open TagBasedVideoManager.Renamer

/// ContainerController テスト用の共通ヘルパー（外部 I/O であるプロセス実行・HTTP 疎通のみを差し替える）
module ContainerTestHelpers =

    /// プロセス実行モックが受け取った呼び出し記録
    type RunnerCall = { FileName: string; WorkDir: string; Args: string }

    /// 作業ディレクトリ（実在不要。モックに渡すのみ）
    let dummyWorkDir = "C:\\TagBasedVideoManager"

    /// 対象コンテナ名（契約: ContainerController.containerName）
    let targetName = "tag-based-video-manager"

    /// 呼び出しを記録し、引数文字列に応じた応答を返すプロセス実行モックを生成する
    let makeRunner (responder: string -> int * string * string) =
        let calls = ResizeArray<RunnerCall>()

        let run (exe: string) (dir: string) (args: string) : Async<int * string * string> =
            async {
                lock calls (fun () -> calls.Add { FileName = exe; WorkDir = dir; Args = args })
                return responder args
            }

        run, (fun () -> lock calls (fun () -> List.ofSeq calls))

    /// 呼び出し回数を数える HTTP ヘルスチェックモックを生成する（引数 URL も記録）
    let makeHealth (result: bool) =
        let urls = ResizeArray<string>()

        let check (url: string) : Async<bool> =
            async {
                lock urls (fun () -> urls.Add url)
                return result
            }

        check, (fun () -> lock urls (fun () -> List.ofSeq urls))

    /// docker / wslc の `ps -a --format json` 相当の JSON 配列を組み立てる
    let psJson (state: string) (id: string) =
        $"""[{{"Names":"tag-based-video-manager","State":"{state}","ID":"{id}"}}]"""

    /// 文字列名からランタイムを解決する（Theory の InlineData 用）
    let runtimeOf (name: string) : ContainerRuntime =
        match name with
        | "Wslc" -> Wslc
        | "Docker" -> Docker
        | "Podman" -> Podman
        | other -> failwith $"unknown runtime {other}"

    /// 標準エラー出力を一時的にキャプチャして関数を実行する
    let captureStderr (f: unit -> 'a) : 'a * string =
        let original = Console.Error
        let sw = new StringWriter()
        Console.SetError(TextWriter.Synchronized sw)

        try
            let result = f ()
            result, sw.ToString()
        finally
            Console.SetError original

    /// 指定ポートで接続は受け付けるが応答を返さない TCP リスナーを開く。
    /// 既に他プロセスがリッスン中（実コンテナ稼働中など）の場合は、localhost 解決先が競合するため None を返す。
    let tryOpenSilentListener (port: int) : TcpListener option =
        let inUse =
            Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
            |> Array.exists (fun ep -> ep.Port = port)

        if inUse then
            None
        else
            try
                let listener = new TcpListener(IPAddress.Any, port)
                listener.Start()
                Some listener
            with _ ->
                None

open ContainerTestHelpers

/// ランタイム自動検出 (UT-RUN-01〜03 / C2 網羅)
module ContainerRuntimeDetectionTests =

    /// 指定コマンド集合のみ存在するとみなす commandExists モック
    let private existsIn (available: string list) : string -> bool =
        fun cmd -> List.contains cmd available

    [<Fact>]
    let ``UT-RUN-01: Windows で wslc があれば Wslc を最優先で選択する`` () =
        ContainerController.detectContainerRuntime true (existsIn [ "wslc"; "docker" ])
        |> should equal (Ok Wslc : Result<ContainerRuntime, string>)

    [<Fact>]
    let ``UT-RUN-01: Windows で wslc のみ存在する場合も Wslc`` () =
        ContainerController.detectContainerRuntime true (existsIn [ "wslc" ])
        |> should equal (Ok Wslc : Result<ContainerRuntime, string>)

    [<Fact>]
    let ``UT-RUN-02: Windows で wslc なし・docker ありなら Docker にフォールバックする`` () =
        ContainerController.detectContainerRuntime true (existsIn [ "docker" ])
        |> should equal (Ok Docker : Result<ContainerRuntime, string>)

    [<Fact>]
    let ``UT-RUN-02: Windows で docker と podman がある場合は Docker を優先する`` () =
        ContainerController.detectContainerRuntime true (existsIn [ "docker"; "podman" ])
        |> should equal (Ok Docker : Result<ContainerRuntime, string>)

    [<Theory>]
    [<InlineData(true)>]
    [<InlineData(false)>]
    let ``UT-RUN-03: 全ランタイム未導入なら No container runtime found を返す`` (isWindows: bool) =
        match ContainerController.detectContainerRuntime isWindows (existsIn []) with
        | Error msg -> msg |> should contain "No container runtime found"
        | Ok rt -> failwith $"Expected Error but got Ok {rt}"

    [<Fact>]
    let ``UT-RUN-03: Windows では podman のみ存在しても未検出扱い`` () =
        match ContainerController.detectContainerRuntime true (existsIn [ "podman" ]) with
        | Error msg -> msg |> should contain "No container runtime found"
        | Ok rt -> failwith $"Expected Error but got Ok {rt}"

    [<Fact>]
    let ``UT-RUN-03: Linux では wslc のみ存在しても未検出扱い`` () =
        match ContainerController.detectContainerRuntime false (existsIn [ "wslc" ]) with
        | Error msg -> msg |> should contain "No container runtime found"
        | Ok rt -> failwith $"Expected Error but got Ok {rt}"

    [<Fact>]
    let ``UT-RUN-02: Linux で docker があれば Docker`` () =
        ContainerController.detectContainerRuntime false (existsIn [ "docker" ])
        |> should equal (Ok Docker : Result<ContainerRuntime, string>)

    [<Fact>]
    let ``UT-RUN-02: Linux で podman のみなら Podman`` () =
        ContainerController.detectContainerRuntime false (existsIn [ "podman" ])
        |> should equal (Ok Podman : Result<ContainerRuntime, string>)

    [<Fact>]
    let ``UT-RUN-02: Linux で docker と podman が両方ある場合は Docker を優先する`` () =
        ContainerController.detectContainerRuntime false (existsIn [ "docker"; "podman" ])
        |> should equal (Ok Docker : Result<ContainerRuntime, string>)

    /// 期待値はテスト側のリテラル表（実装式の複製ではない）
    /// 列: isWindows, wslc, docker, podman, 期待ランタイム ("" は Error)
    [<Theory>]
    [<InlineData(true, false, false, false, "")>]
    [<InlineData(true, false, false, true, "")>]
    [<InlineData(true, false, true, false, "Docker")>]
    [<InlineData(true, false, true, true, "Docker")>]
    [<InlineData(true, true, false, false, "Wslc")>]
    [<InlineData(true, true, false, true, "Wslc")>]
    [<InlineData(true, true, true, false, "Wslc")>]
    [<InlineData(true, true, true, true, "Wslc")>]
    [<InlineData(false, false, false, false, "")>]
    [<InlineData(false, false, false, true, "Podman")>]
    [<InlineData(false, false, true, false, "Docker")>]
    [<InlineData(false, false, true, true, "Docker")>]
    [<InlineData(false, true, false, false, "")>]
    [<InlineData(false, true, false, true, "Podman")>]
    [<InlineData(false, true, true, false, "Docker")>]
    [<InlineData(false, true, true, true, "Docker")>]
    let ``UT-RUN-C2: OS と wslc/docker/podman 存否の全 16 組み合わせ`` (isWindows: bool, wslc: bool, docker: bool, podman: bool, expected: string) =
        let available =
            [ if wslc then "wslc"
              if docker then "docker"
              if podman then "podman" ]

        let actual = ContainerController.detectContainerRuntime isWindows (existsIn available)

        match expected, actual with
        | "", Error msg -> msg |> should contain "No container runtime found"
        | "", Ok rt -> failwith $"Expected Error but got Ok {rt}"
        | exp, Ok rt -> rt |> should equal (runtimeOf exp)
        | exp, Error msg -> failwith $"Expected Ok {exp} but got Error {msg}"

    [<Fact>]
    let ``UT-RUN: コマンド存在確認には拡張子なしの正規名のみを問い合わせる`` () =
        let queried = ResizeArray<string>()

        let probe (cmd: string) =
            lock queried (fun () -> queried.Add cmd)
            false

        ContainerController.detectContainerRuntime true probe |> ignore
        let names = lock queried (fun () -> List.ofSeq queried)
        names |> should contain "wslc"
        names |> should contain "docker"
        names |> List.forall (fun n -> not (n.EndsWith ".exe")) |> should equal true

/// docker / wslc `ps -a --format json` 出力の解析 (UT-CC-01〜06)
module ContainerPsParsingTests =

    [<Fact>]
    let ``UT-CC-01: 単一コンテナ running の JSON 配列は (Running, Some ID)`` () =
        ContainerController.parseContainerPsOutput
            """[{"Names":"tag-based-video-manager","State":"running","ID":"abc"}]"""
        |> should equal (Running, Some "abc")

    [<Fact>]
    let ``UT-CC-01: Names が配列・ID が小文字 Id でも解析できる`` () =
        ContainerController.parseContainerPsOutput
            """[{"Names":["other","tag-based-video-manager"],"State":"running","Id":"id-lower"}]"""
        |> should equal (Running, Some "id-lower")

    [<Fact>]
    let ``UT-CC-01: Names の代わりに Name フィールドでも対象を特定できる`` () =
        ContainerController.parseContainerPsOutput
            """[{"Name":"tag-based-video-manager","State":"running","ID":"n1"}]"""
        |> should equal (Running, Some "n1")

    [<Fact>]
    let ``UT-CC-01: 先頭スラッシュ付きコンテナ名 (/tag-based-video-manager) でも一致する`` () =
        ContainerController.parseContainerPsOutput
            """[{"Names":"/tag-based-video-manager","State":"running","ID":"slash1"}]"""
        |> should equal (Running, Some "slash1")

    [<Fact>]
    let ``UT-CC-01: 整形（複数行インデント）された JSON 配列も解析できる`` () =
        let pretty =
            """
            [
              {
                "Names": "tag-based-video-manager",
                "State": "running",
                "ID": "pretty-1"
              }
            ]
            """

        ContainerController.parseContainerPsOutput pretty
        |> should equal (Running, Some "pretty-1")

    [<Fact>]
    let ``UT-CC-01: 配列でない単一 JSON オブジェクトも解析できる`` () =
        ContainerController.parseContainerPsOutput
            """{"Names":"tag-based-video-manager","State":"running","ID":"single-obj"}"""
        |> should equal (Running, Some "single-obj")

    [<Fact>]
    let ``UT-CC-01: 他コンテナの後ろにある対象を見つけられる`` () =
        ContainerController.parseContainerPsOutput
            """[{"Names":"other-service","State":"running","ID":"xyz"},{"Names":"tag-based-video-manager","State":"running","ID":"target"}]"""
        |> should equal (Running, Some "target")

    [<Theory>]
    [<InlineData("exited")>]
    [<InlineData("stopped")>]
    [<InlineData("dead")>]
    [<InlineData("created")>]
    [<InlineData("Exited")>]
    let ``UT-CC-02: 停止系 State は (Stopped, Some ID)`` (state: string) =
        ContainerController.parseContainerPsOutput(psJson state "abc")
        |> should equal (Stopped, Some "abc")

    [<Fact>]
    let ``UT-CC-02: restarting は Restarting`` () =
        ContainerController.parseContainerPsOutput(psJson "restarting" "rs1")
        |> should equal (Restarting, Some "rs1")

    [<Fact>]
    let ``UT-CC-02: running でも Status に unhealthy を含めば Unhealthy`` () =
        ContainerController.parseContainerPsOutput
            """[{"Names":"tag-based-video-manager","State":"running","Status":"Up 3 minutes (unhealthy)","ID":"uh1"}]"""
        |> should equal (Unhealthy, Some "uh1")

    [<Fact>]
    let ``UT-CC-02: Status が healthy の場合は Unhealthy と誤判定せず Running`` () =
        ContainerController.parseContainerPsOutput
            """[{"Names":"tag-based-video-manager","State":"running","Status":"Up 2 hours (healthy)","ID":"h1"}]"""
        |> should equal (Running, Some "h1")

    [<Theory>]
    [<InlineData("")>]
    [<InlineData(" ")>]
    [<InlineData("   \r\n")>]
    [<InlineData("\t\n\n")>]
    let ``UT-CC-03: 空文字列・空白のみは (NotFound, None)`` (input: string) =
        ContainerController.parseContainerPsOutput input
        |> should equal (NotFound, None)

    [<Fact>]
    let ``UT-CC-04: 対象外コンテナのみの場合は (NotFound, None)`` () =
        ContainerController.parseContainerPsOutput
            """[{"Names":"other-service","State":"running","ID":"xyz"}]"""
        |> should equal (NotFound, None)

    [<Fact>]
    let ``UT-CC-04: 空配列は (NotFound, None)`` () =
        ContainerController.parseContainerPsOutput "[]" |> should equal (NotFound, None)

    [<Fact>]
    let ``UT-CC-05: 破損 JSON は (Unhealthy, None)`` () =
        ContainerController.parseContainerPsOutput """{"invalid-json..."""
        |> should equal (Unhealthy, None)

    [<Fact>]
    let ``UT-CC-05: JSON ではない平文（デーモンエラー文言）も (Unhealthy, None)`` () =
        ContainerController.parseContainerPsOutput "Cannot connect to the Docker daemon at unix:///var/run/docker.sock"
        |> should equal (Unhealthy, None)

    [<Fact>]
    let ``UT-CC-06: NDJSON（改行区切り）形式で対象を特定できる`` () =
        let state, _ =
            ContainerController.parseContainerPsOutput
                "{\"Names\":\"tag-based-video-manager\",\"State\":\"running\"}\n{\"Names\":\"other\"}"

        state |> should equal Running

    [<Fact>]
    let ``UT-CC-06: NDJSON の ID 欠落は None（空 ID を Some にしない）`` () =
        ContainerController.parseContainerPsOutput
            "{\"Names\":\"tag-based-video-manager\",\"State\":\"running\"}\n{\"Names\":\"other\"}"
        |> snd
        |> should equal None

    [<Fact>]
    let ``UT-CC-06: NDJSON で対象が 2 行目 (CRLF 区切り) にあっても見つかる`` () =
        ContainerController.parseContainerPsOutput
            "{\"Names\":\"other\",\"State\":\"running\",\"ID\":\"o1\"}\r\n{\"Names\":\"tag-based-video-manager\",\"State\":\"exited\",\"ID\":\"t2\"}\r\n"
        |> should equal (Stopped, Some "t2")

/// ステータス判定 (UT-CC-07 / DT-01〜04 / IT-CC-02)
module ContainerCheckStatusTests =

    [<Fact>]
    let ``UT-CC-07: ランタイム未検出 (None) は NotInstalled・ポート非到達`` () =
        let runner, _ = makeRunner (fun _ -> 0, "[]", "")
        let health, _ = makeHealth true

        let status =
            ContainerController.checkStatus None (Some runner) (Some health) dummyWorkDir
            |> Async.RunSynchronously

        status.State |> should equal NotInstalled
        status.IsPortAccessible |> should equal false
        status.ContainerId |> should equal None

    [<Fact>]
    let ``DT-01: ランタイムあり・Running・HTTP 疎通成功 は Running かつ到達可能`` () =
        let runner, calls = makeRunner (fun _ -> 0, psJson "running" "cont-1", "")
        let health, urls = makeHealth true

        let status =
            ContainerController.checkStatus (Some Docker) (Some runner) (Some health) dummyWorkDir
            |> Async.RunSynchronously

        status.State |> should equal Running
        status.IsPortAccessible |> should equal true
        status.ContainerId |> should equal (Some "cont-1")
        status.LastChecked |> should not' (equal DateTime.MinValue)
        // 契約: ps -a --format json を作業ディレクトリ付きで 1 回、ヘルスは :5620 を 1 回だけ呼ぶ
        calls ()
        |> should equal [ { FileName = "docker"; WorkDir = dummyWorkDir; Args = "ps -a --format json" } ]
        urls () |> should equal [ "http://localhost:5620/" ]

    [<Fact>]
    let ``DT-02: Running だが HTTP 疎通失敗 は Unhealthy・非到達（ID は保持）`` () =
        let runner, _ = makeRunner (fun _ -> 0, psJson "running" "cont-2", "")
        let health, urls = makeHealth false

        let status =
            ContainerController.checkStatus (Some Docker) (Some runner) (Some health) dummyWorkDir
            |> Async.RunSynchronously

        status.State |> should equal Unhealthy
        status.IsPortAccessible |> should equal false
        status.ContainerId |> should equal (Some "cont-2")
        urls () |> should equal [ "http://localhost:5620/" ]

    [<Theory>]
    [<InlineData("exited", "Stopped")>]
    [<InlineData("restarting", "Restarting")>]
    [<InlineData("dead", "Stopped")>]
    [<InlineData("created", "Stopped")>]
    let ``DT-03: Running 以外では HTTP を呼ばず非到達`` (psState: string, expected: string) =
        let runner, _ = makeRunner (fun _ -> 0, psJson psState "cont-3", "")
        // 疎通が成功する設定でも、Running 以外では到達可能と判定してはならない
        let health, urls = makeHealth true

        let status =
            ContainerController.checkStatus (Some Docker) (Some runner) (Some health) dummyWorkDir
            |> Async.RunSynchronously

        let expectedState =
            match expected with
            | "Stopped" -> Stopped
            | "Restarting" -> Restarting
            | other -> failwith other

        status.State |> should equal expectedState
        status.IsPortAccessible |> should equal false
        urls () |> should be Empty

    [<Fact>]
    let ``DT-03: コンテナ未作成 (ps が空配列) は NotFound・HTTP を呼ばない`` () =
        let runner, _ = makeRunner (fun _ -> 0, "[]", "")
        let health, urls = makeHealth true

        let status =
            ContainerController.checkStatus (Some Wslc) (Some runner) (Some health) dummyWorkDir
            |> Async.RunSynchronously

        status.State |> should equal NotFound
        status.IsPortAccessible |> should equal false
        status.ContainerId |> should equal None
        urls () |> should be Empty

    [<Fact>]
    let ``DT-03: ps が非 0 終了なら NotFound・HTTP を呼ばない`` () =
        let runner, _ = makeRunner (fun _ -> 1, "", "Cannot connect to the Docker daemon")
        let health, urls = makeHealth true

        let status =
            ContainerController.checkStatus (Some Docker) (Some runner) (Some health) dummyWorkDir
            |> Async.RunSynchronously

        status.State |> should equal NotFound
        status.IsPortAccessible |> should equal false
        urls () |> should be Empty

    [<Fact>]
    let ``DT-04: ランタイム None ではプロセス実行も HTTP も一切呼ばない`` () =
        let runner, calls = makeRunner (fun _ -> 0, psJson "running" "x", "")
        let health, urls = makeHealth true

        let status =
            ContainerController.checkStatus None (Some runner) (Some health) dummyWorkDir
            |> Async.RunSynchronously

        status.State |> should equal NotInstalled
        status.IsPortAccessible |> should equal false
        calls () |> should be Empty
        urls () |> should be Empty

    [<Theory>]
    [<InlineData("Wslc", "wslc")>]
    [<InlineData("Docker", "docker")>]
    [<InlineData("Podman", "podman")>]
    let ``UT-RUN: ランタイムごとに正しい実行ファイル名で ps -a --format json を呼ぶ`` (runtimeName: string, exe: string) =
        let runner, calls = makeRunner (fun _ -> 0, "[]", "")
        let health, _ = makeHealth true

        ContainerController.checkStatus (Some(runtimeOf runtimeName)) (Some runner) (Some health) dummyWorkDir
        |> Async.RunSynchronously
        |> ignore

        let recorded = calls ()
        recorded |> List.map (fun c -> c.FileName) |> should equal [ exe ]
        recorded |> List.map (fun c -> c.Args) |> should equal [ "ps -a --format json" ]

    [<Fact>]
    let ``UT-CC-05: ps 出力が破損 JSON の場合は Unhealthy・非到達（クラッシュしない）`` () =
        let runner, _ = makeRunner (fun _ -> 0, "{\"broken", "")
        let health, urls = makeHealth true

        let status =
            ContainerController.checkStatus (Some Docker) (Some runner) (Some health) dummyWorkDir
            |> Async.RunSynchronously

        status.State |> should equal Unhealthy
        status.IsPortAccessible |> should equal false
        status.ContainerId |> should equal None
        urls () |> should be Empty

    [<Fact>]
    let ``IT-CC-02: 既定ヘルスチェッカーは無応答ポートでも有限時間で戻り UI をブロックしない`` () =
        // 5620 を専有できない環境（実コンテナ稼働中など）でも不変条件を検証するため、所有可否で分岐する
        let listener = tryOpenSilentListener 5620

        try
            let runner, _ = makeRunner (fun _ -> 0, psJson "running" "cont-it", "")
            let sw = Stopwatch.StartNew()

            let status =
                ContainerController.checkStatus (Some Docker) (Some runner) None dummyWorkDir
                |> Async.RunSynchronously

            sw.Stop()
            // 2 秒タイムアウト設計に対し、十分な余裕を持って有限時間で戻ること
            (sw.Elapsed < TimeSpan.FromSeconds 8.0) |> should equal true
            // 不変条件: 到達可能 ⇔ Running（Running で疎通失敗は Unhealthy）
            status.IsPortAccessible |> should equal (status.State = Running)

            match listener with
            | Some _ ->
                status.State |> should equal Unhealthy
                status.IsPortAccessible |> should equal false
            | None -> ()
        finally
            listener |> Option.iter (fun l -> l.Stop())

/// stderr ログ出力（可観測性）の検証。Console.Error を差し替えるため並列実行から隔離する
[<Collection("ConsoleErrorCapture")>]
module ContainerLoggingTests =

    [<Fact>]
    let ``UT-CC-05: 破損 JSON 受信時は受信した生テキストを含む警告を stderr に出す`` () =
        let marker = $"BROKEN-JSON-{Guid.NewGuid():N}"

        let result, err =
            captureStderr (fun () -> ContainerController.parseContainerPsOutput $"{{\"invalid-{marker}")

        result |> should equal (Unhealthy, None)
        err |> should contain marker

    [<Fact>]
    let ``UT-CC-03: 正常系（空出力）では警告を出さない`` () =
        let _, err = captureStderr (fun () -> ContainerController.parseContainerPsOutput "")
        String.IsNullOrWhiteSpace err |> should equal true

    [<Fact>]
    let ``DT-03: ps 非 0 終了時は stderr の内容をログに出力する`` () =
        let marker = $"DAEMON-DOWN-{Guid.NewGuid():N}"
        let runner, _ = makeRunner (fun _ -> 1, "", $"Cannot connect: {marker}")
        let health, _ = makeHealth true

        let status, err =
            captureStderr (fun () ->
                ContainerController.checkStatus (Some Docker) (Some runner) (Some health) dummyWorkDir
                |> Async.RunSynchronously)

        status.State |> should equal NotFound
        err |> should contain marker

/// コンテナ操作 (UT-CC-03/04/08/09, IT-CC-01/03/04, ST-01〜04)
module ContainerExecuteActionTests =

    let private short = TimeSpan.FromMilliseconds 200.0

    [<Theory>]
    [<InlineData("start")>]
    [<InlineData("stop")>]
    [<InlineData("restart")>]
    let ``UT-CC-03: 成功時は Ok を返し docker で action とコンテナ名を引数に実行する`` (action: string) =
        let runner, calls = makeRunner (fun _ -> 0, $"{action} done", "")

        let result =
            ContainerController.executeAction (Some Docker) (Some runner) dummyWorkDir action
            |> Async.RunSynchronously

        match result with
        | Ok stdout -> stdout |> should contain $"{action} done"
        | Error e -> failwith $"Expected Ok but got {e}"

        calls ()
        |> should equal [ { FileName = "docker"; WorkDir = dummyWorkDir; Args = $"{action} tag-based-video-manager" } ]

    [<Theory>]
    [<InlineData("Wslc", "wslc")>]
    [<InlineData("Docker", "docker")>]
    [<InlineData("Podman", "podman")>]
    let ``UT-CC-03: ランタイムに応じた実行ファイル名で restart を呼ぶ`` (runtimeName: string, exe: string) =
        let runner, calls = makeRunner (fun _ -> 0, "ok", "")

        ContainerController.executeAction (Some(runtimeOf runtimeName)) (Some runner) dummyWorkDir "restart"
        |> Async.RunSynchronously
        |> ignore

        calls () |> List.map (fun c -> c.FileName, c.Args) |> should equal [ exe, "restart tag-based-video-manager" ]

    [<Fact>]
    let ``UT-CC-04: executeAction ランタイム None は ValidationError（runtime への言及）で、プロセスは起動しない`` () =
        let runner, calls = makeRunner (fun _ -> 0, "ok", "")

        let result =
            ContainerController.executeAction None (Some runner) dummyWorkDir "restart"
            |> Async.RunSynchronously

        match result with
        | Error(ValidationError msg) -> msg.ToLowerInvariant() |> should contain "runtime"
        | other -> failwith $"Expected ValidationError but got {other}"

        calls () |> should be Empty

    [<Fact>]
    let ``UT-CC-08: 非 0 終了は ContainerError (action, exitCode, stderr) を返す`` () =
        let runner, _ = makeRunner (fun _ -> 1, "", "access denied")

        let result =
            ContainerController.executeAction (Some Docker) (Some runner) dummyWorkDir "restart"
            |> Async.RunSynchronously

        match result with
        | Error(ContainerError(cmd, code, stderr)) ->
            cmd |> should equal "restart"
            code |> should equal 1
            stderr |> should equal "access denied"
        | other -> failwith $"Expected ContainerError but got {other}"

    [<Theory>]
    [<InlineData(2)>]
    [<InlineData(125)>]
    [<InlineData(-1)>]
    [<InlineData(255)>]
    let ``UT-CC-08: 終了コードはそのまま保持される（境界値・負数）`` (exitCode: int) =
        let runner, _ = makeRunner (fun _ -> exitCode, "", "boom")

        let result =
            ContainerController.executeAction (Some Wslc) (Some runner) dummyWorkDir "start"
            |> Async.RunSynchronously

        match result with
        | Error(ContainerError(cmd, code, _)) ->
            cmd |> should equal "start"
            code |> should equal exitCode
        | other -> failwith $"Expected ContainerError but got {other}"

    [<Fact>]
    let ``UT-CC-09: プロセスが応答しない場合は TimeoutError action を返し有限時間で戻る`` () =
        let hang (_: string) (_: string) (_: string) : Async<int * string * string> =
            async {
                do! Async.Sleep Timeout.Infinite
                return 0, "", ""
            }

        let sw = Stopwatch.StartNew()

        let result =
            ContainerController.executeActionWithTimeout short (Some Docker) (Some hang) dummyWorkDir "restart"
            |> Async.RunSynchronously

        sw.Stop()

        match result with
        | Error(TimeoutError cmd) -> cmd |> should equal "restart"
        | other -> failwith $"Expected TimeoutError but got {other}"

        // 即時失敗（タイムアウト未待機）でも無限待ちでもないこと
        (sw.Elapsed >= TimeSpan.FromMilliseconds 100.0) |> should equal true
        (sw.Elapsed < TimeSpan.FromSeconds 5.0) |> should equal true

    [<Fact>]
    let ``UT-CC-09: タイムアウト内に完了する遅い応答は Ok（過剰にタイムアウトしない）`` () =
        let slowButOk (_: string) (_: string) (_: string) : Async<int * string * string> =
            async {
                do! Async.Sleep 50
                return 0, "slow ok", ""
            }

        let result =
            ContainerController.executeActionWithTimeout (TimeSpan.FromSeconds 5.0) (Some Docker) (Some slowButOk) dummyWorkDir "start"
            |> Async.RunSynchronously

        match result with
        | Ok stdout -> stdout |> should contain "slow ok"
        | other -> failwith $"Expected Ok but got {other}"

    [<Fact>]
    let ``UT-CC-09: タイムアウト時 stop でも TimeoutError（冪等扱いにならない）`` () =
        let hang (_: string) (_: string) (_: string) : Async<int * string * string> =
            async {
                do! Async.Sleep Timeout.Infinite
                return 0, "", ""
            }

        let result =
            ContainerController.executeActionWithTimeout short (Some Podman) (Some hang) dummyWorkDir "stop"
            |> Async.RunSynchronously

        match result with
        | Error(TimeoutError cmd) -> cmd |> should equal "stop"
        | other -> failwith $"Expected TimeoutError but got {other}"

    [<Fact>]
    let ``UT-CC-04: executeActionWithTimeout もランタイム None は ValidationError`` () =
        let runner, calls = makeRunner (fun _ -> 0, "ok", "")

        let result =
            ContainerController.executeActionWithTimeout short None (Some runner) dummyWorkDir "start"
            |> Async.RunSynchronously

        match result with
        | Error(ValidationError msg) -> msg.ToLowerInvariant() |> should contain "runtime"
        | other -> failwith $"Expected ValidationError but got {other}"

        calls () |> should be Empty

    [<Fact>]
    let ``IT-CC-01: restart が終了コード 1 でも ContainerError を返し例外にならない`` () =
        let runner, _ = makeRunner (fun _ -> 1, "", "failed")

        let result =
            ContainerController.executeAction (Some Docker) (Some runner) dummyWorkDir "restart"
            |> Async.RunSynchronously

        result |> should equal (Error(ContainerError("restart", 1, "failed")) : Result<string, RenamerError>)

    [<Fact>]
    let ``IT-CC-03: 失敗時の複数行・日本語 stderr が欠落せず通知用に保持される`` () =
        let message = "Error response from daemon: ポート 5620 は使用中です\nline2: ボリューム '動画 フォルダ' を解決できません"
        let runner, _ = makeRunner (fun _ -> 125, "", message)

        let result =
            ContainerController.executeAction (Some Wslc) (Some runner) dummyWorkDir "start"
            |> Async.RunSynchronously

        result |> should equal (Error(ContainerError("start", 125, message)) : Result<string, RenamerError>)

    [<Fact>]
    let ``IT-CC-04: 連動再起動がタイムアウトしても TimeoutError を返し、以降のステータス確認は継続動作する`` () =
        let hangingRestart (_: string) (_: string) (args: string) : Async<int * string * string> =
            async {
                if args.StartsWith "restart" then
                    do! Async.Sleep Timeout.Infinite
                    return 0, "", ""
                else
                    return 0, psJson "exited" "after-timeout", ""
            }

        let restartResult =
            ContainerController.executeActionWithTimeout short (Some Wslc) (Some hangingRestart) dummyWorkDir "restart"
            |> Async.RunSynchronously

        restartResult |> should equal (Error(TimeoutError "restart") : Result<string, RenamerError>)

        let health, _ = makeHealth true

        let status =
            ContainerController.checkStatus (Some Wslc) (Some hangingRestart) (Some health) dummyWorkDir
            |> Async.RunSynchronously

        status.State |> should equal Stopped
        status.ContainerId |> should equal (Some "after-timeout")

    [<Theory>]
    [<InlineData("Error response from daemon: No such container: tag-based-video-manager")>]
    [<InlineData("container tag-based-video-manager is not running")>]
    let ``ST-04: 停止済みコンテナへの二重 stop は Ok（冪等）`` (stderr: string) =
        let runner, _ = makeRunner (fun _ -> 1, "", stderr)

        let result =
            ContainerController.executeAction (Some Docker) (Some runner) dummyWorkDir "stop"
            |> Async.RunSynchronously

        match result with
        | Ok _ -> ()
        | Error e -> failwith $"Expected Ok (idempotent stop) but got {e}"

    [<Fact>]
    let ``ST-04: stop の無関係な失敗 (access denied) は冪等扱いにせず ContainerError`` () =
        let runner, _ = makeRunner (fun _ -> 1, "", "permission denied while trying to connect to the daemon")

        let result =
            ContainerController.executeAction (Some Docker) (Some runner) dummyWorkDir "stop"
            |> Async.RunSynchronously

        match result with
        | Error(ContainerError("stop", 1, stderr)) -> stderr |> should contain "permission denied"
        | other -> failwith $"Expected ContainerError but got {other}"

    [<Theory>]
    [<InlineData("restart", "Error response from daemon: No such container: tag-based-video-manager")>]
    [<InlineData("start", "Error response from daemon: No such container: tag-based-video-manager")>]
    [<InlineData("start", "container tag-based-video-manager is not running")>]
    [<InlineData("restart", "container tag-based-video-manager is not running")>]
    let ``ST-04: 冪等扱いは stop 限定（start/restart の No such container は Error）`` (action: string, stderr: string) =
        let runner, _ = makeRunner (fun _ -> 1, "", stderr)

        let result =
            ContainerController.executeAction (Some Docker) (Some runner) dummyWorkDir action
            |> Async.RunSynchronously

        match result with
        | Error(ContainerError(cmd, 1, _)) -> cmd |> should equal action
        | other -> failwith $"Expected ContainerError but got {other}"

    [<Fact>]
    let ``ST-01〜04: 状態遷移 Stopped→start→Running→restart→Running→stop→Stopped→stop(二重) を通しで検証`` () =
        let running = ref false

        // 外部 CLI の振る舞いのみを模擬するステートフルなフェイク
        let fake (args: string) : int * string * string =
            match args with
            | "ps -a --format json" -> 0, psJson (if running.Value then "running" else "exited") "st-1", ""
            | "start tag-based-video-manager" ->
                running.Value <- true
                0, "tag-based-video-manager", ""
            | "restart tag-based-video-manager" ->
                running.Value <- true
                0, "tag-based-video-manager", ""
            | "stop tag-based-video-manager" ->
                if running.Value then
                    running.Value <- false
                    0, "tag-based-video-manager", ""
                else
                    1, "", "Error response from daemon: container tag-based-video-manager is not running"
            | other -> 2, "", $"unexpected args: {other}"

        let runner, _ = makeRunner fake
        let health, _ = makeHealth true

        let status () =
            ContainerController.checkStatus (Some Docker) (Some runner) (Some health) dummyWorkDir
            |> Async.RunSynchronously

        let act action =
            ContainerController.executeAction (Some Docker) (Some runner) dummyWorkDir action
            |> Async.RunSynchronously

        let expectOk label (r: Result<string, RenamerError>) =
            match r with
            | Ok _ -> ()
            | Error e -> failwith $"{label}: expected Ok but got {e}"

        (status ()).State |> should equal Stopped

        act "start" |> expectOk "ST-01"
        let s1 = status ()
        s1.State |> should equal Running
        s1.IsPortAccessible |> should equal true

        act "restart" |> expectOk "ST-02"
        (status ()).State |> should equal Running

        act "stop" |> expectOk "ST-03"
        let s3 = status ()
        s3.State |> should equal Stopped
        s3.IsPortAccessible |> should equal false

        act "stop" |> expectOk "ST-04"
        (status ()).State |> should equal Stopped

    [<Fact>]
    let ``UT-CC-03: containerName は tag-based-video-manager`` () =
        ContainerController.containerName |> should equal "tag-based-video-manager"
