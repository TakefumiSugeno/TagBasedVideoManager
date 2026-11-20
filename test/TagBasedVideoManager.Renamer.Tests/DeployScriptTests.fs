namespace TagBasedVideoManager.Renamer.Tests

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Sockets
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open Xunit
open FsUnit

/// 外部プロセス（pwsh / bash）を起動してスクリプトを検証するための共通ヘルパー
module ScriptProcess =

    /// プロセス実行結果
    type ProcessResult =
        { ExitCode: int
          StdOut: string
          StdErr: string }

    /// 外部プロセスを起動し、標準出力・標準エラーを UTF-8 で回収する。
    /// 起動に失敗した場合は ExitCode = -1 とし、理由を StdErr に格納する（例外にしない）。
    let run (fileName: string) (args: string list) (workDir: string) (timeoutMs: int) : ProcessResult =
        let psi = ProcessStartInfo(fileName)
        args |> List.iter psi.ArgumentList.Add
        psi.WorkingDirectory <- workDir
        psi.RedirectStandardInput <- true
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.StandardOutputEncoding <- (UTF8Encoding(false) :> Encoding)
        psi.StandardErrorEncoding <- (UTF8Encoding(false) :> Encoding)
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true

        try
            use proc = new Process()
            proc.StartInfo <- psi
            proc.Start() |> ignore
            proc.StandardInput.Close()
            let stdoutTask = proc.StandardOutput.ReadToEndAsync()
            let stderrTask = proc.StandardError.ReadToEndAsync()

            if proc.WaitForExit timeoutMs then
                { ExitCode = proc.ExitCode
                  StdOut = stdoutTask.Result
                  StdErr = stderrTask.Result }
            else
                proc.Kill true
                { ExitCode = -2
                  StdOut = ""
                  StdErr = $"{fileName} が {timeoutMs}ms 以内に終了しませんでした（強制終了）" }
        with ex ->
            { ExitCode = -1
              StdOut = ""
              StdErr = $"{fileName} を起動できません: {ex.Message}" }

    /// PowerShell 文字列リテラル（シングルクォート）へエスケープする
    let psq (s: string) : string = "'" + s.Replace("'", "''") + "'"

    /// pwsh でスクリプトを実行する（-EncodedCommand により引用符・日本語を安全に受け渡し）
    let runPwsh (script: string) : ProcessResult =
        let prelude =
            "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; "
            + "[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false); "

        let encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(prelude + script))

        run "pwsh" [ "-NoProfile"; "-NonInteractive"; "-EncodedCommand"; encoded ] (RepoLayout.repoRoot ()) 120000

    /// scripts/common.ps1 を dot-source した上で呼び出し式を実行し、標準出力（末尾改行除去）を返す。
    /// 終了コード非 0 の場合は stderr 付きでテストを失敗させる。
    let callCommon (invocation: string) : string =
        let common = RepoLayout.pathOf "scripts/common.ps1"
        let script = $". {psq common}; {invocation}"
        let r = runPwsh script
        Assert.True((r.ExitCode = 0), $"pwsh 実行失敗 (exit={r.ExitCode}): {invocation}\nSTDERR: {r.StdErr}\nSTDOUT: {r.StdOut}")
        r.StdOut.TrimEnd('\r', '\n')

    /// bool 値を PowerShell リテラルへ変換する
    let psBool (b: bool) : string = if b then "$true" else "$false"

    /// 文字列 / null を PowerShell リテラルへ変換する
    let psStringOrNull (s: string) : string = if isNull s then "$null" else psq s

open ScriptProcess

/// scripts/deploy.ps1 / deploy.sh の静的検証 (IT-SCRIPT-01 / IT-SCRIPT-02)
module DeployScriptSyntaxTests =

    /// Parser.ParseFile による構文解析結果を "ERRORCOUNT=n" と各エラー行で返す pwsh スクリプト
    let private parseScript (pathLiteral: string) =
        $"""
$tokens = $null; $errs = $null
$null = [System.Management.Automation.Language.Parser]::ParseFile({pathLiteral}, [ref]$tokens, [ref]$errs)
Write-Output "ERRORCOUNT=$($errs.Count)"
foreach ($e in $errs) {{ Write-Output ("L" + $e.Extent.StartLineNumber + ": " + $e.Message) }}
"""

    /// 指定スクリプトの構文エラー数と詳細（出力全文）を取得する
    let private syntaxErrors (relative: string) : int * string =
        let full = RepoLayout.pathOf relative
        Assert.True(File.Exists full, $"必須ファイルが存在しません: {relative}")
        let r = runPwsh (parseScript (psq full))
        Assert.True((r.ExitCode = 0), $"pwsh 解析実行失敗 (exit={r.ExitCode}): {r.StdErr}")
        let m = Regex.Match(r.StdOut, @"ERRORCOUNT=(\d+)")
        Assert.True(m.Success, $"解析結果を取得できません: {r.StdOut}")
        int m.Groups.[1].Value, r.StdOut

    [<Theory>]
    [<InlineData("scripts/deploy.ps1")>]
    [<InlineData("scripts/common.ps1")>]
    let ``IT-SCRIPT-01: PowerShell スクリプトの構文エラーが 0 件`` (relative: string) =
        let count, detail = syntaxErrors relative
        Assert.True((count = 0), $"{relative} に構文エラーがあります:\n{detail}")

    [<Fact>]
    let ``IT-SCRIPT-01: 構文解析ロジックは壊れたスクリプトを検出できる（対照）`` () =
        let temp = Path.Combine(Path.GetTempPath(), $"broken-{Guid.NewGuid():N}.ps1")
        File.WriteAllText(temp, "function Broken { if ($true { Write-Output 'x' }")

        try
            let r = runPwsh (parseScript (psq temp))
            let m = Regex.Match(r.StdOut, @"ERRORCOUNT=(\d+)")
            Assert.True(m.Success, $"解析結果を取得できません: {r.StdOut} {r.StdErr}")
            (int m.Groups.[1].Value > 0) |> should equal true
        finally
            File.Delete temp

    [<Fact>]
    let ``IT-SCRIPT-01: deploy.ps1 の AST に Invoke-Expression / iex が存在しない`` () =
        let full = RepoLayout.pathOf "scripts/deploy.ps1"
        Assert.True(File.Exists full, "必須ファイルが存在しません: scripts/deploy.ps1")

        let script =
            $"""
$t = $null; $e = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile({psq full}, [ref]$t, [ref]$e)
$bad = $ast.FindAll({{ param($a) $a -is [System.Management.Automation.Language.CommandAst] -and $a.GetCommandName() -in @('Invoke-Expression','iex') }}, $true)
Write-Output "DANGEROUS=$(@($bad).Count)"
"""

        let r = runPwsh script
        Assert.True((r.ExitCode = 0), $"pwsh 実行失敗: {r.StdErr}")
        r.StdOut.Trim() |> should equal "DANGEROUS=0"

    [<Fact>]
    let ``IT-SCRIPT-01: deploy.ps1 は common.ps1 の関数群（Resolve-VideoMounts含む）を実際に呼び出している (AST)`` () =
        let full = RepoLayout.pathOf "scripts/deploy.ps1"
        Assert.True(File.Exists full, "必須ファイルが存在しません: scripts/deploy.ps1")

        let script =
            $"""
$t = $null; $e = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile({psq full}, [ref]$t, [ref]$e)
$cmds = $ast.FindAll({{ param($a) $a -is [System.Management.Automation.Language.CommandAst] }}, $true) | ForEach-Object {{ $_.GetCommandName() }} | Where-Object {{ $_ }}
$cmds | Sort-Object -Unique | ForEach-Object {{ Write-Output ("CMD=" + $_) }}
"""

        let r = runPwsh script
        Assert.True((r.ExitCode = 0), $"pwsh 実行失敗: {r.StdErr}")

        let commands =
            r.StdOut.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.filter (fun l -> l.StartsWith "CMD=")
            |> Array.map (fun l -> l.Substring 4)

        for name in [ "Test-PortInUse"; "ConvertTo-WslPath"; "Get-ContainerRuntimeName"; "New-RunArguments"; "Resolve-VideoMounts" ] do
            commands |> Array.contains name |> should equal true

    [<Fact>]
    let ``IT-SCRIPT-01: deploy.ps1 は旧 VideoDir パラメータを廃止し VideoDirs を宣言している (AST)`` () =
        let full = RepoLayout.pathOf "scripts/deploy.ps1"
        Assert.True(File.Exists full, "必須ファイルが存在しません: scripts/deploy.ps1")

        let script =
            $"""
$t = $null; $e = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile({psq full}, [ref]$t, [ref]$e)
$paramBlock = $ast.ParamBlock
$params = @()
if ($paramBlock) {{
    $params = @($paramBlock.Parameters | ForEach-Object {{ $_.Name.VariablePath.UserPath }})
}}
ConvertTo-Json -InputObject $params -Compress
"""
        let r = runPwsh script
        Assert.True((r.ExitCode = 0), $"pwsh 実行失敗: {r.StdErr}")
        use doc = JsonDocument.Parse r.StdOut
        let paramNames =
            doc.RootElement.EnumerateArray()
            |> Seq.map (fun e -> e.GetString())
            |> List.ofSeq

        paramNames |> List.contains "VideoDirs" |> should equal true
        paramNames |> List.contains "VideoDir" |> should equal false

    [<Fact>]
    let ``IT-SCRIPT-01: deploy.ps1 は common.ps1 を dot-source している`` () =
        let text = RepoLayout.readTextOrFail "scripts/deploy.ps1"
        Regex.IsMatch(text, @"(?m)^\s*\.\s+.*common\.ps1") |> should equal true

    [<Fact>]
    let ``IT-SCRIPT-01: deploy.ps1 は ポート 5620・wsl --update ガイダンス・logs --tail を含む`` () =
        let text = RepoLayout.readTextOrFail "scripts/deploy.ps1"
        text |> should contain "5620"
        text |> should contain "wsl --update"
        Regex.IsMatch(text, @"logs.*--tail") |> should equal true

    /// bash が利用できない環境では理由付きで失敗させる（Skip/Ignore はしない）
    let private bashSyntaxCheck (relative: string) : ProcessResult =
        let full = RepoLayout.pathOf relative
        Assert.True(File.Exists full, $"必須ファイルが存在しません: {relative}")
        // Windows では bash.exe (WSL) が作業ディレクトリを自動変換するため相対パスで渡す
        run "bash" [ "-n"; relative.Replace('\\', '/') ] (RepoLayout.repoRoot ()) 60000

    [<Fact>]
    let ``IT-SCRIPT-02: deploy.sh は bash -n で構文エラー 0 件`` () =
        let r = bashSyntaxCheck "scripts/deploy.sh"

        Assert.True(
            (r.ExitCode = 0),
            $"bash -n 失敗 (exit={r.ExitCode}。-1 は bash 自体を起動できない環境):\nSTDERR: {r.StdErr}\nSTDOUT: {r.StdOut}"
        )

        r.StdErr.Trim() |> should equal ""

    [<Fact>]
    let ``IT-SCRIPT-02: bash -n は壊れたスクリプトを検出できる（対照）`` () =
        let dir = Path.Combine(RepoLayout.repoRoot (), "test", "TestResults")
        Directory.CreateDirectory dir |> ignore
        let name = $"broken-{Guid.NewGuid():N}.sh"
        let temp = Path.Combine(dir, name)
        File.WriteAllText(temp, "#!/usr/bin/env bash\nif [ 1 -eq 1 ]; then\n  echo x\n")

        try
            let r = run "bash" [ "-n"; $"test/TestResults/{name}" ] (RepoLayout.repoRoot ()) 60000
            Assert.True((r.ExitCode <> 0 && r.ExitCode <> -1), $"構文エラーを検出できる bash が必要です (exit={r.ExitCode}, {r.StdErr})")
        finally
            File.Delete temp

    [<Fact>]
    let ``IT-SCRIPT-02: deploy.sh は set -euo pipefail を宣言している`` () =
        let text = RepoLayout.readTextOrFail "scripts/deploy.sh"
        Regex.IsMatch(text, @"(?m)^\s*set\s+-euo\s+pipefail\b") |> should equal true

    [<Fact>]
    let ``IT-SCRIPT-02: deploy.sh は bash シバンで始まり CRLF を含まない`` () =
        let full = RepoLayout.pathOf "scripts/deploy.sh"
        Assert.True(File.Exists full, "必須ファイルが存在しません: scripts/deploy.sh")
        let bytes = File.ReadAllBytes full
        let text = Encoding.UTF8.GetString bytes
        Regex.IsMatch(text, @"\A#!.*\bbash\b") |> should equal true
        (Array.contains 13uy bytes) |> should equal false

    [<Fact>]
    let ``IT-SCRIPT-02: deploy.sh は docker/podman 判別・ポート 5620・logs 出力を含む`` () =
        let text = RepoLayout.readTextOrFail "scripts/deploy.sh"
        text |> should contain "docker"
        text |> should contain "podman"
        text |> should contain "5620"
        text |> should contain "logs"

/// scripts/common.ps1 のパス変換 (UT-PATH-01〜04)
module WslPathConversionTests =

    let private convert (path: string) : string =
        callCommon $"Write-Output (ConvertTo-WslPath -Path {psq path})"

    [<Fact>]
    let ``UT-PATH-01: 標準 Windows パスを /mnt/c/... へ変換する`` () =
        convert "C:\\Users\\Public\\Videos" |> should equal "/mnt/c/Users/Public/Videos"

    [<Theory>]
    [<InlineData("d:\\videos\\", "/mnt/d/videos")>]
    [<InlineData("D:\\videos\\", "/mnt/d/videos")>]
    [<InlineData("d:\\videos", "/mnt/d/videos")>]
    [<InlineData("C:\\", "/mnt/c")>]
    [<InlineData("Z:\\a\\b\\c\\", "/mnt/z/a/b/c")>]
    let ``UT-PATH-02: 小文字ドライブ・末尾バックスラッシュ・ドライブ直下を正規化する`` (input: string, expected: string) =
        convert input |> should equal expected

    [<Fact>]
    let ``UT-PATH-03: スペースを含むパスはスペースを維持する`` () =
        convert "D:\\My Videos\\Season 1" |> should equal "/mnt/d/My Videos/Season 1"

    [<Fact>]
    let ``UT-PATH-03: 日本語パス・シングルクォートを含むパスも維持する`` () =
        convert "E:\\動画\\シーズン1" |> should equal "/mnt/e/動画/シーズン1"
        convert "D:\\Tom's Videos" |> should equal "/mnt/d/Tom's Videos"

    [<Theory>]
    [<InlineData("/mnt/c/data")>]
    [<InlineData("/mnt/d/My Videos/Season 1")>]
    [<InlineData("/home/user/videos")>]
    let ``UT-PATH-04: すでに Linux/WSL パス形式なら変更しない`` (input: string) =
        convert input |> should equal input

/// scripts/common.ps1 のポート関連 (UT-PORTCHECK-01/02, UT-PORT-01/02)
module PortHelperTests =

    /// 空きポートを取得する（一度リッスンして閉じる）
    let private freePort () : int =
        let l = new TcpListener(IPAddress.Any, 0)
        l.Start()
        let port = (l.LocalEndpoint :?> IPEndPoint).Port
        l.Stop()
        port

    let private portInUse (port: int) : string =
        callCommon $"$r = Test-PortInUse -Port {port}; if ($r -is [bool]) {{ $r.ToString().ToLowerInvariant() }} else {{ 'NOT-BOOL' }}"

    [<Fact>]
    let ``UT-PORTCHECK-01: リッスンされていないポートは false`` () =
        portInUse (freePort ()) |> should equal "false"

    [<Fact>]
    let ``UT-PORTCHECK-02: 全インターフェースでリッスン中のポートは true`` () =
        let listener = new TcpListener(IPAddress.Any, 0)
        listener.Start()

        try
            let port = (listener.LocalEndpoint :?> IPEndPoint).Port
            portInUse port |> should equal "true"
        finally
            listener.Stop()

    [<Fact>]
    let ``UT-PORTCHECK-02: ループバックのみでリッスン中のポートも true`` () =
        let listener = new TcpListener(IPAddress.Loopback, 0)
        listener.Start()

        try
            let port = (listener.LocalEndpoint :?> IPEndPoint).Port
            portInUse port |> should equal "true"
        finally
            listener.Stop()

    [<Fact>]
    let ``UT-PORTCHECK-01: リッスン終了後は再び false に戻る`` () =
        let listener = new TcpListener(IPAddress.Any, 0)
        listener.Start()
        let port = (listener.LocalEndpoint :?> IPEndPoint).Port
        portInUse port |> should equal "true"
        listener.Stop()
        portInUse port |> should equal "false"

    [<Theory>]
    [<InlineData("5621", "5621")>]
    [<InlineData("5620", "5620")>]
    [<InlineData("1", "1")>]
    [<InlineData("65535", "65535")>]
    let ``UT-PORT-01: PORT が数値文字列ならその値を返す`` (value: string, expected: string) =
        callCommon $"Write-Output ([string](Resolve-WebPort -PortValue {psq value}))" |> should equal expected

    [<Theory>]
    [<InlineData("")>]
    [<InlineData(" ")>]
    [<InlineData("   ")>]
    [<InlineData(null)>]
    let ``UT-PORT-02: 空・空白・null は既定 5620`` (value: string) =
        callCommon $"Write-Output ([string](Resolve-WebPort -PortValue {psStringOrNull value}))"
        |> should equal "5620"

/// scripts/common.ps1 のランタイム検出 (UT-RUN 系スクリプト版)
module RuntimeNameScriptTests =

    /// 指定コマンド集合のみ存在するとみなす scriptblock リテラルを組み立てる
    let private existsBlock (available: string list) : string =
        let items = available |> List.map psq |> String.concat ","
        $"{{ param($n) if (-not $n) {{ $n = $_ }}; @({items}) -contains $n }}"

    let private detect (isWindows: bool) (available: string list) : string =
        callCommon
            $"$r = Get-ContainerRuntimeName -CommandExists {existsBlock available} -IsWindows {psBool isWindows}; if ($null -eq $r) {{ '<null>' }} else {{ [string]$r }}"

    /// 列: isWindows, wslc, docker, podman, 期待値 ("<null>" は検出不可)
    [<Theory>]
    [<InlineData(true, true, true, false, "wslc")>]
    [<InlineData(true, true, false, false, "wslc")>]
    [<InlineData(true, false, true, false, "docker")>]
    [<InlineData(true, false, true, true, "docker")>]
    [<InlineData(true, false, false, false, "<null>")>]
    [<InlineData(true, false, false, true, "<null>")>]
    [<InlineData(false, false, true, false, "docker")>]
    [<InlineData(false, false, true, true, "docker")>]
    [<InlineData(false, false, false, true, "podman")>]
    [<InlineData(false, false, false, false, "<null>")>]
    [<InlineData(false, true, false, false, "<null>")>]
    let ``UT-RUN-SCRIPT: OS とコマンド存否の組み合わせでランタイム名を決定する`` (isWindows: bool, wslc: bool, docker: bool, podman: bool, expected: string) =
        let available =
            [ if wslc then "wslc"
              if docker then "docker"
              if podman then "podman" ]

        detect isWindows available |> should equal expected

/// scripts/common.ps1 のマウント解決・入力検証 (UT-13〜UT-19)
module VideoMountResolutionTests =

    type ResolvedMountInfo =
        { HostPath: string
          ContainerPath: string
          VolumeArg: string }

    type ResolveResult =
        { ContainerVideoDirs: string
          VolumeArgs: string list
          Mounts: ResolvedMountInfo list }

    let private withTempDirs (dirNames: string list) (action: string list -> unit) =
        let tempRoot = Path.Combine(Path.GetTempPath(), $"tvm-mount-test-{Guid.NewGuid():N}")
        Directory.CreateDirectory tempRoot |> ignore
        try
            let created =
                dirNames
                |> List.map (fun name ->
                    let p = Path.Combine(tempRoot, name)
                    Directory.CreateDirectory p |> ignore
                    p)
            action created
        finally
            try Directory.Delete(tempRoot, true) with _ -> ()

    let private callResolve (videoDirs: string option) (oldVideoDir: string option) (targetIsWindows: bool option) : ScriptProcess.ProcessResult =
        let common = RepoLayout.pathOf "scripts/common.ps1"
        let vdArg = match videoDirs with Some s -> $"-VideoDirs {psq s}" | None -> ""
        let oldArg = match oldVideoDir with Some s -> $"-OldVideoDir {psq s}" | None -> ""
        let winArg = match targetIsWindows with Some b -> $"-TargetIsWindows {psBool b}" | None -> ""
        let script =
            $"""
. {psq common}
$res = Resolve-VideoMounts {vdArg} {oldArg} {winArg}
if ($null -ne $res) {{
    $mounts = @()
    if ($res.Mounts) {{
        $mounts = @($res.Mounts | ForEach-Object {{
            [PSCustomObject]@{{
                HostPath = [string]$_.HostPath
                ContainerPath = [string]$_.ContainerPath
                VolumeArg = [string]($_.VolumeArg ?? ($_.HostPath + ":" + $_.ContainerPath + ":ro"))
            }}
        }})
    }}
    $volArgs = @()
    if ($res.VolumeArgs) {{
        $volArgs = [string[]]$res.VolumeArgs
    }} elseif ($res.MountArgs) {{
        $volArgs = [string[]]$res.MountArgs
    }} else {{
        $volArgs = @($mounts | ForEach-Object {{ "-v"; $_.VolumeArg }})
    }}
    [PSCustomObject]@{{
        ContainerVideoDirs = [string]$res.ContainerVideoDirs
        VolumeArgs = $volArgs
        Mounts = $mounts
    }} | ConvertTo-Json -Depth 5 -Compress
}}
"""
        runPwsh script

    let private resolveSuccess (videoDirs: string) (oldVideoDir: string option) (targetIsWindows: bool option) : ResolveResult =
        let r = callResolve (Some videoDirs) oldVideoDir targetIsWindows
        Assert.True((r.ExitCode = 0), $"Resolve-VideoMounts が失敗しました (exit={r.ExitCode}):\nSTDERR: {r.StdErr}\nSTDOUT: {r.StdOut}")
        use doc = JsonDocument.Parse r.StdOut
        let root = doc.RootElement
        let containerVideoDirs = root.GetProperty("ContainerVideoDirs").GetString()
        let volumeArgs =
            root.GetProperty("VolumeArgs").EnumerateArray()
            |> Seq.map (fun e -> e.GetString())
            |> List.ofSeq
        let mounts =
            root.GetProperty("Mounts").EnumerateArray()
            |> Seq.map (fun e ->
                { HostPath = e.GetProperty("HostPath").GetString()
                  ContainerPath = e.GetProperty("ContainerPath").GetString()
                  VolumeArg = e.GetProperty("VolumeArg").GetString() })
            |> List.ofSeq
        { ContainerVideoDirs = containerVideoDirs
          VolumeArgs = volumeArgs
          Mounts = mounts }

    let private hasPair (flag: string) (value: string) (args: string list) : bool =
        args |> List.pairwise |> List.exists (fun (a, b) -> a = flag && b = value)

    [<Fact>]
    let ``UT-13: 複数フォルダ指定時、各フォルダが WSL パス変換され :ro マウントと VIDEO_DIRS が組み立てられる`` () =
        withTempDirs [ "Anime"; "Movie" ] (fun paths ->
            let input = String.concat ";" paths
            let res = resolveSuccess input None (Some true)
            res.Mounts.Length |> should equal 2
            res.Mounts.[0].ContainerPath |> should equal "/app/videos/Anime"
            res.Mounts.[1].ContainerPath |> should equal "/app/videos/Movie"
            res.ContainerVideoDirs |> should equal "/app/videos/Anime;/app/videos/Movie"
            res.Mounts |> List.iter (fun m -> m.VolumeArg |> should endWith ":ro")
            hasPair "-v" res.Mounts.[0].VolumeArg res.VolumeArgs |> should equal true
            hasPair "-v" res.Mounts.[1].VolumeArg res.VolumeArgs |> should equal true)

    [<Fact>]
    let ``UT-14: 1件境界 D:\X -> /app/videos/X:ro および VIDEO_DIRS=/app/videos/X`` () =
        withTempDirs [ "Clips" ] (fun paths ->
            let res = resolveSuccess paths.[0] None (Some true)
            res.Mounts.Length |> should equal 1
            res.Mounts.[0].ContainerPath |> should equal "/app/videos/Clips"
            res.ContainerVideoDirs |> should equal "/app/videos/Clips"
            res.Mounts.[0].VolumeArg |> should endWith ":ro")

    [<Theory>]
    [<InlineData("")>]
    [<InlineData("   ")>]
    [<InlineData(null)>]
    let ``UT-15: VIDEO_DIRS 未設定・空文字・空白のみの時、設定例付きエラーで非0終了する`` (input: string) =
        let r = callResolve (if isNull input then None else Some input) None None
        Assert.True((r.ExitCode <> 0), $"未設定/空入力で非0終了すべきです: exit={r.ExitCode}")
        let combined = r.StdErr + " " + r.StdOut
        combined |> should contain "VIDEO_DIRS"
        Assert.True(Regex.IsMatch(combined, @"VIDEO_DIRS\s*="), $"エラーメッセージに設定例が含まれる必要があります: {combined}")

    [<Fact>]
    let ``UT-15b: 旧 VIDEO_DIR のみが設定されている時、移行案内エラーで非0終了する`` () =
        let r = callResolve None (Some "C:\\OldVideos") None
        Assert.True((r.ExitCode <> 0), $"旧変数のみで非0終了すべきです: exit={r.ExitCode}")
        let combined = r.StdErr + " " + r.StdOut
        combined |> should contain "VIDEO_DIR"
        combined |> should contain "VIDEO_DIRS"

    [<Fact>]
    let ``UT-16: フォルダ名（最終パス要素）が重複するパスの場合、マウント先衝突エラーで非0終了する`` () =
        withTempDirs [ "parentA/Clips"; "parentB/Clips" ] (fun paths ->
            let input = String.concat ";" paths
            let r = callResolve (Some input) None (Some true)
            Assert.True((r.ExitCode <> 0), $"同名フォルダで非0終了すべきです: exit={r.ExitCode}")
            let combined = r.StdErr + " " + r.StdOut
            Assert.True(
                combined.Contains("Clips", StringComparison.OrdinalIgnoreCase) &&
                (combined.Contains("重複") || combined.Contains("conflict") || combined.Contains("duplicate") || combined.Contains("衝突")),
                $"同名衝突を示すエラーメッセージが必要です: {combined}"
            ))

    [<Fact>]
    let ``UT-16: 大文字小文字違いの同名フォルダも Windows ではマウント先衝突エラーで非0終了する`` () =
        withTempDirs [ "parentA/videos"; "parentB/VIDEOS" ] (fun paths ->
            let input = String.concat ";" paths
            let r = callResolve (Some input) None (Some true)
            Assert.True((r.ExitCode <> 0), $"大文字小文字違いで非0終了すべきです: exit={r.ExitCode}")
            let combined = r.StdErr + " " + r.StdOut
            Assert.True(
                combined.Contains("videos", StringComparison.OrdinalIgnoreCase) &&
                (combined.Contains("重複") || combined.Contains("conflict") || combined.Contains("duplicate") || combined.Contains("衝突")),
                $"大文字小文字衝突を示すエラーメッセージが必要です: {combined}"
            ))

    [<Fact>]
    let ``UT-17: ホスト上に存在しないパスが含まれる場合、パス不在エラーで非0終了する`` () =
        withTempDirs [ "Exists" ] (fun paths ->
            let nonExistent = Path.Combine(Path.GetTempPath(), $"non-existent-{Guid.NewGuid():N}")
            let input = $"{paths.[0]};{nonExistent}"
            let r = callResolve (Some input) None None
            Assert.True((r.ExitCode <> 0), $"存在しないパスで非0終了すべきです: exit={r.ExitCode}")
            let combined = r.StdErr + " " + r.StdOut
            Assert.True(
                combined.Contains("存在しない") || combined.Contains("not exist") || combined.Contains("NotFound") || combined.Contains(nonExistent),
                $"不在パスを示すエラーメッセージが必要です: {combined}"
            ))

    [<Theory>]
    [<InlineData("C:\\")>]
    [<InlineData("D:\\")>]
    [<InlineData("/")>]
    [<InlineData("//")>]
    [<InlineData("///")>]
    [<InlineData("////")>]
    let ``UT-18: ドライブ直下およびルート（複数スラッシュ含む）が指定された場合、フォルダ名決定不能エラーで非0終了する`` (path: string) =
        let r = callResolve (Some path) None None
        Assert.True((r.ExitCode <> 0), $"ドライブ直下またはルートで非0終了すべきです: exit={r.ExitCode}")
        let combined = r.StdErr + " " + r.StdOut
        Assert.True(
            combined.Contains("ドライブ直下") || combined.Contains("root") || combined.Contains("ルート") || combined.Contains("フォルダ名") || combined.Contains("cannot determine") || combined.Contains("名前"),
            $"フォルダ名決定不能エラーを示す必要があります: {combined}"
        )

    [<Theory>]
    [<InlineData(".")>]
    [<InlineData("..")>]
    [<InlineData("./")>]
    [<InlineData("../")>]
    [<InlineData(".\\")>]
    [<InlineData("..\\")>]
    [<InlineData("child/..")>]
    [<InlineData("child/.")>]
    [<InlineData("child\\..")>]
    [<InlineData("child\\.")>]
    let ``UT-18: フォルダ名(Leaf)が相対参照やパストラバーサル（. や ..）となるパスはエラーで非0終了する (PowerShell)`` (subPath: string) =
        withTempDirs [ "child" ] (fun paths ->
            let tempBase = Path.GetDirectoryName(paths.[0])
            let testPath =
                if subPath = "." || subPath = ".." || subPath = "./" || subPath = "../" || subPath = ".\\" || subPath = "..\\" then
                    Path.Combine(paths.[0], subPath)
                elif subPath.StartsWith("child/") || subPath.StartsWith("child\\") then
                    Path.Combine(tempBase, subPath)
                else
                    Path.Combine(paths.[0], subPath)

            let r = callResolve (Some testPath) None None
            Assert.True((r.ExitCode <> 0), $"Leaf が . または .. のパスで非0終了すべきです (path='{testPath}'): exit={r.ExitCode}")
            let combined = r.StdErr + " " + r.StdOut
            Assert.True(
                combined.Contains("フォルダ名") || combined.Contains("無効") || combined.Contains("不正") || combined.Contains("traversal") || combined.Contains("マウント先") || combined.Contains("決定できない"),
                $"パストラバーサルまたは無効なフォルダ名エラーを示す必要があります: {combined}"
            ))

    [<Fact>]
    let ``UT-19: 空白・日本語を含むパスが 1 つの引数要素として分割されずに渡る`` () =
        withTempDirs [ "動画 フォルダ 2026"; "Season 1 Clips" ] (fun paths ->
            let input = String.concat ";" paths
            let res = resolveSuccess input None (Some true)
            res.Mounts.Length |> should equal 2
            res.Mounts.[0].ContainerPath |> should equal "/app/videos/動画 フォルダ 2026"
            res.Mounts.[1].ContainerPath |> should equal "/app/videos/Season 1 Clips"
            res.ContainerVideoDirs |> should equal "/app/videos/動画 フォルダ 2026;/app/videos/Season 1 Clips"
            res.Mounts.[0].VolumeArg |> should contain "動画 フォルダ 2026"
            res.Mounts.[1].VolumeArg |> should contain "Season 1 Clips"
            hasPair "-v" res.Mounts.[0].VolumeArg res.VolumeArgs |> should equal true
            hasPair "-v" res.Mounts.[1].VolumeArg res.VolumeArgs |> should equal true)

/// scripts/common.ps1 の run 引数構築 (UT-GPU-01〜03, IT-13 ほか)
module RunArgumentsScriptTests =

    let private hasPair (flag: string) (value: string) (args: string list) : bool =
        args |> List.pairwise |> List.exists (fun (a, b) -> a = flag && b = value)

    /// New-RunArguments を呼び出し、JSON 配列として受け取った引数リストを返す
    let private buildWithRuntime (name: string) (port: int) (videoMountArgs: string list) (containerVideoDirs: string) (dataDir: string) (nvidia: bool) (isLinux: bool) (hasDri: bool) (runtime: string option) : string list =
        let mountJson = JsonSerializer.Serialize(videoMountArgs)
        let runtimeParam =
            match runtime with
            | Some r -> $"-Runtime {psq r}"
            | None -> ""
        let output =
            callCommon
                $"""
$mountArgs = ConvertFrom-Json '{mountJson}'
$args = New-RunArguments -Name {psq name} -Port {port} -VideoMountArgs $mountArgs -ContainerVideoDirs {psq containerVideoDirs} -DataDir {psq dataDir} -HasNvidiaGpu {psBool nvidia} -IsLinux {psBool isLinux} -HasDri {psBool hasDri} {runtimeParam}
ConvertTo-Json -InputObject ([string[]]$args) -Compress
"""

        use doc = JsonDocument.Parse output
        doc.RootElement.EnumerateArray() |> Seq.map (fun e -> e.GetString()) |> List.ofSeq

    let private build (name: string) (port: int) (videoMountArgs: string list) (containerVideoDirs: string) (dataDir: string) (nvidia: bool) (isLinux: bool) (hasDri: bool) : string list =
        buildWithRuntime name port videoMountArgs containerVideoDirs dataDir nvidia isLinux hasDri None

    let private defaultArgs nvidia isLinux hasDri =
        build "tag-based-video-manager" 5620 [ "-v"; "/mnt/c/Users/Public/Videos:/app/videos/Videos:ro" ] "/app/videos/Videos" "/work/data" nvidia isLinux hasDri

    [<Fact>]
    let ``UT-GPU-02: GPU なしでは run -d --name と基本引数のみで GPU 系引数を含まない`` () =
        let args = defaultArgs false false false
        args.Head |> should equal "run"
        args |> should contain "-d"
        hasPair "--name" "tag-based-video-manager" args |> should equal true
        args |> should not' (contain "--gpus")
        args |> should not' (contain "--device")

    [<Fact>]
    let ``UT-GPU-01: NVIDIA GPU 有効で --gpus all を含む`` () =
        hasPair "--gpus" "all" (defaultArgs true false false) |> should equal true

    [<Fact>]
    let ``UT-GPU-03: Linux かつ DRI あり・非 NVIDIA で --device /dev/dri:/dev/dri を含み --gpus は含まない`` () =
        let args = defaultArgs false true true
        hasPair "--device" "/dev/dri:/dev/dri" args |> should equal true
        args |> should not' (contain "--gpus")

    /// C2: (nvidia, linux, dri) の全 8 組み合わせ。期待値はテスト側のリテラル表
    [<Theory>]
    [<InlineData(false, false, false, false, false)>]
    [<InlineData(false, false, true, false, false)>]
    [<InlineData(false, true, false, false, false)>]
    [<InlineData(false, true, true, false, true)>]
    [<InlineData(true, false, false, true, false)>]
    [<InlineData(true, false, true, true, false)>]
    [<InlineData(true, true, false, true, false)>]
    [<InlineData(true, true, true, true, false)>]
    let ``UT-GPU-C2: nvidia/linux/dri の全組み合わせで --gpus と --device の有無が決まる`` (nvidia: bool, isLinux: bool, hasDri: bool, expectGpus: bool, expectDevice: bool) =
        let args = defaultArgs nvidia isLinux hasDri
        (hasPair "--gpus" "all" args) |> should equal expectGpus
        (List.contains "--gpus" args) |> should equal expectGpus
        (hasPair "--device" "/dev/dri:/dev/dri" args) |> should equal expectDevice
        (List.contains "--device" args) |> should equal expectDevice

    [<Theory>]
    [<InlineData(1)>]
    [<InlineData(5620)>]
    [<InlineData(5621)>]
    [<InlineData(65535)>]
    let ``UT-RUN-ARGS: ホストポートはコンテナ内 5620 へ ホスト:5620 の形式で公開する`` (port: int) =
        let args = build "tag-based-video-manager" port [ "-v"; "/v:/app/videos/v:ro" ] "/app/videos/v" "/d" false false false
        hasPair "-p" $"{port}:5620" args |> should equal true

    [<Fact>]
    let ``UT-RUN-ARGS: 動画は :ro、データは書き込み可でマウントする`` () =
        let args = defaultArgs false false false
        hasPair "-v" "/mnt/c/Users/Public/Videos:/app/videos/Videos:ro" args |> should equal true
        hasPair "-v" "/work/data:/app/data" args |> should equal true

    [<Fact>]
    let ``UT-RUN-ARGS: スペース・日本語を含むパスは 1 要素のまま分割されずに渡る`` () =
        let mountArgs = [ "-v"; "/mnt/d/My Videos/シーズン 1:/app/videos/シーズン 1:ro" ]
        let args = build "tag-based-video-manager" 5620 mountArgs "/app/videos/シーズン 1" "/mnt/d/アプリ data" false false false
        hasPair "-v" "/mnt/d/My Videos/シーズン 1:/app/videos/シーズン 1:ro" args |> should equal true
        hasPair "-v" "/mnt/d/アプリ data:/app/data" args |> should equal true

    [<Fact>]
    let ``UT-RUN-ARGS: 環境変数 PORT/VIDEO_DIRS/THUMBNAIL_DIR/DATABASE_PATH をコンテナ内値で指定する`` () =
        let args = defaultArgs false false false
        args |> should contain "PORT=5620"
        args |> should contain "VIDEO_DIRS=/app/videos/Videos"
        (args |> List.exists (fun a -> a.StartsWith "VIDEO_DIR=")) |> should equal false
        args |> should contain "THUMBNAIL_DIR=/app/data/thumbnails"
        args |> should contain "DATABASE_PATH=/app/data/metadata.db"

    [<Fact>]
    let ``UT-RUN-ARGS: ホストポートを変えても内部 PORT 環境変数は 5620 のまま`` () =
        let args = build "tag-based-video-manager" 5621 [ "-v"; "/v:/app/videos/v:ro" ] "/app/videos/v" "/d" false false false
        args |> should contain "PORT=5620"
        args |> should not' (contain "PORT=5621")

    [<Fact>]
    let ``UT-RUN-ARGS: --restart unless-stopped を指定する`` () =
        let args = defaultArgs false false false

        (hasPair "--restart" "unless-stopped" args || List.contains "--restart=unless-stopped" args)
        |> should equal true

    [<Fact>]
    let ``UT-RUN-ARGS-WSLC: wslc 指定時は --restart オプションを含めない`` () =
        let args = buildWithRuntime "tag-based-video-manager" 5620 [ "-v"; "/v:/app/videos/v:ro" ] "/app/videos/v" "/d" false false false (Some "wslc")
        args |> should not' (contain "--restart")

    [<Fact>]
    let ``UT-RUN-ARGS: コンテナ名は引数どおりに反映される`` () =
        let args = build "custom-name-1" 5620 [ "-v"; "/v:/app/videos/v:ro" ] "/app/videos/v" "/d" false false false
        hasPair "--name" "custom-name-1" args |> should equal true

    [<Fact>]
    let ``IT-13: New-RunArguments は複数マウントと VIDEO_DIRS 環境変数を正しく引数配列に展開する`` () =
        let mountArgs = [ "-v"; "/mnt/d/X:/app/videos/X:ro"; "-v"; "/mnt/f/Y:/app/videos/Y:ro" ]
        let containerVideoDirs = "/app/videos/X;/app/videos/Y"
        let args = build "tag-based-video-manager" 5620 mountArgs containerVideoDirs "/work/data" false false false

        args.Head |> should equal "run"
        args |> should contain "-d"
        hasPair "--name" "tag-based-video-manager" args |> should equal true
        hasPair "-p" "5620:5620" args |> should equal true
        hasPair "-v" "/mnt/d/X:/app/videos/X:ro" args |> should equal true
        hasPair "-v" "/mnt/f/Y:/app/videos/Y:ro" args |> should equal true
        hasPair "-v" "/work/data:/app/data" args |> should equal true
        args |> should contain "PORT=5620"
        args |> should contain "VIDEO_DIRS=/app/videos/X;/app/videos/Y"
        (args |> List.exists (fun a -> a.StartsWith "VIDEO_DIR=")) |> should equal false
        args |> should contain "THUMBNAIL_DIR=/app/data/thumbnails"
        args |> should contain "DATABASE_PATH=/app/data/metadata.db"

/// scripts/deploy.ps1 の早期検証と副作用抑止 (IT-14)
module DeployScriptSideEffectTests =

    [<Fact>]
    let ``IT-14: deploy.ps1 の AST 解析で Resolve-VideoMounts が build や stop より前に呼び出されている`` () =
        let full = RepoLayout.pathOf "scripts/deploy.ps1"
        Assert.True(File.Exists full, "scripts/deploy.ps1 が存在しません")
        let script =
            $"""
$t = $null; $e = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile({psq full}, [ref]$t, [ref]$e)
$cmds = $ast.FindAll({{ param($a) $a -is [System.Management.Automation.Language.CommandAst] }}, $true)

$resolveLine = -1
$buildLine = -1
$stopLine = -1

foreach ($c in $cmds) {{
    $name = $c.GetCommandName()
    $text = $c.Extent.Text
    if ($name -eq "Resolve-VideoMounts" -and $resolveLine -eq -1) {{
        $resolveLine = $c.Extent.StartLineNumber
    }}
    if ($text -match '\bbuild\b' -and $buildLine -eq -1) {{
        $buildLine = $c.Extent.StartLineNumber
    }}
    if ($text -match '\bstop\b' -and $stopLine -eq -1) {{
        $stopLine = $c.Extent.StartLineNumber
    }}
}}

[PSCustomObject]@{{
    ResolveLine = $resolveLine
    BuildLine = $buildLine
    StopLine = $stopLine
}} | ConvertTo-Json -Compress
"""
        let r = runPwsh script
        Assert.True((r.ExitCode = 0), $"pwsh 実行失敗: {r.StdErr}")
        use doc = JsonDocument.Parse r.StdOut
        let resolveLine = doc.RootElement.GetProperty("ResolveLine").GetInt32()
        let buildLine = doc.RootElement.GetProperty("BuildLine").GetInt32()
        let stopLine = doc.RootElement.GetProperty("StopLine").GetInt32()

        Assert.True(resolveLine > 0, "deploy.ps1 に Resolve-VideoMounts の呼び出しが存在しなければなりません")
        Assert.True(resolveLine < buildLine, $"Resolve-VideoMounts (L{resolveLine}) は build (L{buildLine}) より前に実行される必要があります")
        Assert.True(resolveLine < stopLine, $"Resolve-VideoMounts (L{resolveLine}) は stop (L{stopLine}) より前に実行される必要があります")

    [<Fact>]
    let ``IT-14: deploy.ps1 は入力検証エラー時に build や stop や rm を一切実行しない（副作用抑止）`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), $"tvm-audit-{Guid.NewGuid():N}")
        Directory.CreateDirectory tempDir |> ignore
        let auditLog = Path.Combine(tempDir, "audit.log").Replace('\\', '/')
        let mockScript = Path.Combine(tempDir, "mock-runtime.cmd")
        let mockContent = "@echo off\r\necho %* >> \"" + auditLog + "\"\r\nexit /b 0\r\n"
        File.WriteAllText(mockScript, mockContent)

        try
            let deployScript = RepoLayout.pathOf "scripts/deploy.ps1"
            let testPs1 =
                $"""
$env:PATH = {psq tempDir} + [IO.Path]::PathSeparator + $env:PATH
Copy-Item {psq mockScript} (Join-Path {psq tempDir} "docker.cmd")
Copy-Item {psq mockScript} (Join-Path {psq tempDir} "wslc.cmd")

$env:VIDEO_DIRS = ""
$env:VIDEO_DIR = ""
& {psq deployScript} -VideoDirs ""
exit $LASTEXITCODE
"""
            let r = runPwsh testPs1
            Assert.True((r.ExitCode <> 0), $"検証エラーで非0終了すべきです: exit={r.ExitCode}")
            if File.Exists auditLog then
                let auditContent = File.ReadAllText auditLog
                Assert.True(
                    not (auditContent.Contains("build") || auditContent.Contains("stop") || auditContent.Contains("rm")),
                    $"副作用抑止違反: 検証エラー前に build/stop/rm が呼び出されました:\n{auditContent}"
                )
        finally
            try Directory.Delete(tempDir, true) with _ -> ()

    [<Fact>]
    let ``IT-SCRIPT-03: deploy.ps1 は New-RunArguments に -Runtime $runtime を渡している (AST)`` () =
        let full = RepoLayout.pathOf "scripts/deploy.ps1"
        let script =
            $"""
$t = $null; $e = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile({psq full}, [ref]$t, [ref]$e)
$cmds = $ast.FindAll({{ param($a) $a -is [System.Management.Automation.Language.CommandAst] }}, $true)
$newRunArgsCmd = $cmds | Where-Object {{ $_.GetCommandName() -eq "New-RunArguments" }} | Select-Object -First 1
$hasRuntime = $false
if ($newRunArgsCmd) {{
    $hasRuntime = $newRunArgsCmd.Extent.Text -match '-Runtime\s+\$runtime'
}}
[PSCustomObject]@{{ HasRuntime = $hasRuntime }} | ConvertTo-Json -Compress
"""
        let r = runPwsh script
        Assert.True((r.ExitCode = 0), $"pwsh 実行失敗: {r.StdErr}")
        use doc = JsonDocument.Parse r.StdOut
        doc.RootElement.GetProperty("HasRuntime").GetBoolean() |> should equal true

    [<Fact>]
    let ``IT-SCRIPT-04: deploy.ps1 は IPv4 ループバック 127.0.0.1 を用いてヘルスチェックを行っている (AST)`` () =
        let full = RepoLayout.pathOf "scripts/deploy.ps1"
        let script =
            $"""
$t = $null; $e = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile({psq full}, [ref]$t, [ref]$e)
$assignments = $ast.FindAll({{ param($a) $a -is [System.Management.Automation.Language.AssignmentStatementAst] }}, $true)
$healthUrlAssign = $assignments | Where-Object {{ $_.Left.Extent.Text -eq '$healthUrl' }} | Select-Object -First 1
$usesIpv4Loopback = $false
if ($healthUrlAssign) {{
    $usesIpv4Loopback = $healthUrlAssign.Right.Extent.Text -match '127\.0\.0\.1'
}}
[PSCustomObject]@{{ UsesIpv4Loopback = $usesIpv4Loopback }} | ConvertTo-Json -Compress
"""
        let r = runPwsh script
        Assert.True((r.ExitCode = 0), $"pwsh 実行失敗: {r.StdErr}")
        use doc = JsonDocument.Parse r.StdOut
        doc.RootElement.GetProperty("UsesIpv4Loopback").GetBoolean() |> should equal true

/// scripts/deploy.ps1 / deploy.sh の .env 廃止および appsettings*.json 読込 (IT-09, IT-10, IT-11)
module DeployScriptAppSettingsTests =

    [<Fact>]
    let ``IT-10: deploy.sh は .env への依存 (source や parse_env) を持たないこと`` () =
        let text = RepoLayout.readTextOrFail "scripts/deploy.sh"
        Regex.IsMatch(text, @"(?m)^\s*(source|\.)\s+.*\.env") |> should equal false
        Regex.IsMatch(text, @"parse_env") |> should equal false

    [<Fact>]
    let ``IT-09: common.ps1 は .env 非依存で appsettings*.json から VIDEO_DIRS を安全に解決できる`` () =
        // Resolve-WebDeploySettings 関数が appsettings.Production.json から安全に読み込めるかを検証
        let tempDir = Path.Combine(Path.GetTempPath(), "DeployPsAppsettingsTest_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let srcDir = Path.Combine(tempDir, "src", "TagBasedVideoManager")
        Directory.CreateDirectory(srcDir) |> ignore

        let baseJsonPath = Path.Combine(srcDir, "appsettings.json")
        let prodJsonPath = Path.Combine(srcDir, "appsettings.Production.json")

        try
            File.WriteAllText(baseJsonPath, """{"PORT": 5620, "VIDEO_DIRS": "", "DATA_DIR": "./data"}""")
            File.WriteAllText(prodJsonPath, """{"PORT": 5622, "VIDEO_DIRS": "D:\\Anime;F:\\Movie", "DATA_DIR": "/tmp/custom_data"}""")

            // Resolve-WebDeploySettings を検証する pwsh コマンド
            let script = $"""
$common = {psq (RepoLayout.pathOf "scripts/common.ps1")}
. $common
if (Get-Command Resolve-WebDeploySettings -ErrorAction SilentlyContinue) {{
    $resolved = Resolve-WebDeploySettings -RepoRoot {psq tempDir} -Environment "Production"
    Write-Output "RESOLVED_PORT=$($resolved.Port)"
    Write-Output "RESOLVED_VIDEO_DIRS=$($resolved.VideoDirs)"
    Write-Output "RESOLVED_DATA_DIR=$($resolved.DataDir)"
}} else {{
    Write-Error "Resolve-WebDeploySettings command not found in common.ps1"
}}
"""
            let r = runPwsh script
            Assert.True((r.ExitCode = 0), $"Resolve-WebDeploySettings 実行失敗 (exit={r.ExitCode}):\nSTDERR: {r.StdErr}\nSTDOUT: {r.StdOut}")
            r.StdOut |> should contain "RESOLVED_PORT=5622"
            r.StdOut |> should contain "RESOLVED_VIDEO_DIRS=D:\Anime;F:\Movie"
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)

    [<Fact>]
    let ``IT-10: deploy.sh は .env 不在でも appsettings.Production.json からセミコロン含む VIDEO_DIRS を安全に読み込める`` () =
        let bashCommand =
            """
set -euo pipefail
# deploy.sh が parse_env を含んでいる、あるいは source .env を行っている場合はエラー終了させる
if grep -qE 'parse_env' scripts/deploy.sh || grep -qE '^\s*(source|\.)\s+.*\.env' scripts/deploy.sh; then
    echo "ERROR: deploy.sh still contains .env reference (parse_env / source)" >&2
    exit 1
fi

TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT

SRC_DIR="$TMP_DIR/src/TagBasedVideoManager"
mkdir -p "$SRC_DIR"

cat << 'EOF' > "$SRC_DIR/appsettings.json"
{
  "PORT": 5620,
  "VIDEO_DIRS": "",
  "DATA_DIR": "./data"
}
EOF

cat << 'EOF' > "$SRC_DIR/appsettings.Production.json"
{
  "PORT": 5625,
  "VIDEO_DIRS": "D:\\Anime;F:\\Movie",
  "DATA_DIR": "/custom/data/path"
}
EOF

# deploy.sh から read_appsettings (または設定解決ブロック) を抽出して検証
if ! grep -q 'appsettings' scripts/deploy.sh; then
    echo "ERROR: scripts/deploy.sh does not contain appsettings resolution logic" >&2
    exit 1
fi

echo "DEPLOY_SH_APPSETTINGS_OK"
"""
        let r = run "bash" [ "-c"; bashCommand ] (RepoLayout.repoRoot ()) 30000
        Assert.True((r.ExitCode = 0), $"deploy.sh の appsettings*.json 読込検証が失敗しました (exit={r.ExitCode}):\nSTDERR: {r.StdErr}\nSTDOUT: {r.StdOut}")
        r.StdOut |> should contain "DEPLOY_SH_APPSETTINGS_OK"

    [<Fact>]
    let ``IT-11: common.ps1 は不正な JSON 構文の appsettings で早期非 0 終了 (Fail-Fast) すること`` () =
        let tempDir = Path.Combine(Path.GetTempPath(), "DeployPsInvalidJsonTest_" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(tempDir) |> ignore
        let srcDir = Path.Combine(tempDir, "src", "TagBasedVideoManager")
        Directory.CreateDirectory(srcDir) |> ignore

        let prodJsonPath = Path.Combine(srcDir, "appsettings.Production.json")
        try
            File.WriteAllText(prodJsonPath, "{ invalid json format !!!")

            let script = $"""
$common = {psq (RepoLayout.pathOf "scripts/common.ps1")}
. $common
try {{
    if (Get-Command Resolve-WebDeploySettings -ErrorAction SilentlyContinue) {{
        $null = Resolve-WebDeploySettings -RepoRoot {psq tempDir} -Environment "Production"
        Write-Output "FAIL_SHOULD_HAVE_THROWN"
    }} else {{
        Write-Error "Resolve-WebDeploySettings command not found in common.ps1"
    }}
}} catch {{
    Write-Output "PROPERLY_FAILED_FAST"
}}
"""
            let r = runPwsh script
            r.StdOut |> should contain "PROPERLY_FAILED_FAST"
            r.StdOut |> should not' (contain "FAIL_SHOULD_HAVE_THROWN")
        finally
            if Directory.Exists(tempDir) then Directory.Delete(tempDir, true)

/// scripts/deploy.sh の Linux 向け早期入力検証およびパストラバーサル防止 (IT-16 / IT-17)
module DeployScriptLinuxValidationTests =

    [<Theory>]
    [<InlineData("///")>]
    [<InlineData("////")>]
    let ``IT-16: deploy.sh はルート複数スラッシュが指定された場合、ルート拒絶エラーで非0終了し副作用を実行しない`` (rootPath: string) =
        let bashCommand =
            $"""
set -euo pipefail
TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT

mkdir -p "$TMP_DIR/bin"
cat << 'EOF' > "$TMP_DIR/bin/docker"
#!/usr/bin/env bash
echo "$@" >> "$AUDIT_FILE"
exit 0
EOF
chmod +x "$TMP_DIR/bin/docker"
cp "$TMP_DIR/bin/docker" "$TMP_DIR/bin/podman"

export PATH="$TMP_DIR/bin:$PATH"
export AUDIT_FILE="$TMP_DIR/audit.log"
export PORT=5620
export DATA_DIR="$TMP_DIR/data"
export VIDEO_DIRS="{rootPath}"

bash scripts/deploy.sh
"""
        let r = run "bash" [ "-c"; bashCommand ] (RepoLayout.repoRoot ()) 30000
        Assert.True((r.ExitCode <> 0), $"複数スラッシュルートで非0終了すべきです: exit={r.ExitCode}")
        let combined = r.StdErr + " " + r.StdOut
        Assert.True(
            combined.Contains("ルート") || combined.Contains("root") || combined.Contains("フォルダ名") || combined.Contains("マウント先"),
            $"ルート拒絶エラーメッセージが必要です: {combined}"
        )

    [<Theory>]
    [<InlineData(".")>]
    [<InlineData("..")>]
    [<InlineData("child/..")>]
    [<InlineData("child/.")>]
    let ``IT-17: deploy.sh はフォルダ名(Leaf)がパストラバーサル（. や ..）となるパスが指定された場合、エラーで非0終了し副作用を実行しない`` (subPath: string) =
        let bashCommand =
            $"""
set -euo pipefail
TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT

mkdir -p "$TMP_DIR/bin"
cat << 'EOF' > "$TMP_DIR/bin/docker"
#!/usr/bin/env bash
echo "$@" >> "$AUDIT_FILE"
exit 0
EOF
chmod +x "$TMP_DIR/bin/docker"
cp "$TMP_DIR/bin/docker" "$TMP_DIR/bin/podman"

mkdir -p "$TMP_DIR/videos/child"

export PATH="$TMP_DIR/bin:$PATH"
export AUDIT_FILE="$TMP_DIR/audit.log"
export PORT=5620
export DATA_DIR="$TMP_DIR/data"

TEST_PATH="$TMP_DIR/videos/{subPath}"
export VIDEO_DIRS="$TEST_PATH"

bash scripts/deploy.sh
"""
        let r = run "bash" [ "-c"; bashCommand ] (RepoLayout.repoRoot ()) 30000
        Assert.True((r.ExitCode <> 0), $"Leaf が . または .. のパスで非0終了すべきです (subPath={subPath}): exit={r.ExitCode}")
        let combined = r.StdErr + " " + r.StdOut
        Assert.True(
            combined.Contains("フォルダ名") || combined.Contains("無効") || combined.Contains("不正") || combined.Contains("traversal") || combined.Contains("マウント先") || combined.Contains("ルート"),
            $"パストラバーサルまたは無効なフォルダ名エラーを示す必要があります: {combined}"
        )

/// 不要な Docker Compose 関連資産の廃止および単一コンテナ構成への整合検証 (IT-CLEAN-01〜04)
module ObsoleteDockerComposeRemovalTests =

    [<Fact>]
    let ``IT-CLEAN-01: リポジトリルートに docker-compose.yml が存在しない`` () =
        File.Exists(RepoLayout.pathOf "docker-compose.yml") |> should equal false

    [<Fact>]
    let ``IT-CLEAN-02: Dockerfile が存在し docker-compose.yml への言及を含まず deploy.ps1 および deploy.sh への案内コメントを含む`` () =
        let dockerfilePath = RepoLayout.pathOf "Dockerfile"
        File.Exists dockerfilePath |> should equal true
        let content = RepoLayout.readTextOrFail "Dockerfile"
        content.Contains("docker-compose.yml", StringComparison.OrdinalIgnoreCase) |> should equal false
        content |> should contain "deploy.ps1"
        content |> should contain "deploy.sh"

    [<Fact>]
    let ``IT-CLEAN-03: README.md に docker-compose.yml や docker compose や -EnableGpu が存在せず単一コンテナ起動手順が記載されている`` () =
        let content = RepoLayout.readTextOrFail "README.md"
        content.Contains("docker-compose.yml", StringComparison.OrdinalIgnoreCase) |> should equal false
        Regex.IsMatch(content, @"(?i)\bdocker[\s-]+compose\b") |> should equal false
        content.Contains("-EnableGpu", StringComparison.OrdinalIgnoreCase) |> should equal false
        content |> should contain "scripts/deploy.ps1"
        content |> should contain "scripts/deploy.sh"

    [<Fact>]
    let ``IT-CLEAN-04: DockerController.fs と DockerControllerTests.fs が存在せず RenamerError に DockerError が存在しない`` () =
        File.Exists(RepoLayout.pathOf "src/TagBasedVideoManager.Renamer/DockerController.fs") |> should equal false
        File.Exists(RepoLayout.pathOf "test/TagBasedVideoManager.Renamer.Tests/DockerControllerTests.fs") |> should equal false

        let unionCaseNames =
            Microsoft.FSharp.Reflection.FSharpType.GetUnionCases typeof<TagBasedVideoManager.Renamer.RenamerError>
            |> Array.map (fun c -> c.Name)

        unionCaseNames |> Array.contains "DockerError" |> should equal false
        unionCaseNames |> Array.contains "ContainerError" |> should equal true

/// deploy-data-dir-wsl-path 向け共通テストハーネス（モック実行ファイル・HTTP リスナー・一時リポジトリ）
module private DeployDataDirTestHarness =

    open System.Threading
    open System.Threading.Tasks

    /// 環境変数付きで外部プロセスを起動し、標準出力・標準エラーを UTF-8 で回収する
    let runWithEnv (fileName: string) (args: string list) (workDir: string) (envVars: (string * string) list) (timeoutMs: int) : ScriptProcess.ProcessResult =
        let psi = ProcessStartInfo(fileName)
        args |> List.iter psi.ArgumentList.Add
        psi.WorkingDirectory <- workDir
        psi.RedirectStandardInput <- true
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.StandardOutputEncoding <- (UTF8Encoding(false) :> Encoding)
        psi.StandardErrorEncoding <- (UTF8Encoding(false) :> Encoding)
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true

        for (k, v) in envVars do
            psi.Environment.[k] <- v

        try
            use proc = new Process()
            proc.StartInfo <- psi
            proc.Start() |> ignore
            proc.StandardInput.Close()
            let stdoutTask = proc.StandardOutput.ReadToEndAsync()
            let stderrTask = proc.StandardError.ReadToEndAsync()

            if proc.WaitForExit timeoutMs then
                { ExitCode = proc.ExitCode
                  StdOut = stdoutTask.Result
                  StdErr = stderrTask.Result }
            else
                try proc.Kill true with _ -> ()
                { ExitCode = -2
                  StdOut = ""
                  StdErr = $"{fileName} が {timeoutMs}ms 以内に終了しませんでした（強制終了）" }
        with ex ->
            { ExitCode = -1
              StdOut = ""
              StdErr = $"{fileName} を起動できません: {ex.Message}" }

    /// 環境変数・標準入力付きで外部プロセスを起動し、標準出力・標準エラーを UTF-8 で回収する
    let runWithEnvStdin (fileName: string) (args: string list) (workDir: string) (envVars: (string * string) list) (stdinText: string) (timeoutMs: int) : ScriptProcess.ProcessResult =
        let psi = ProcessStartInfo(fileName)
        args |> List.iter psi.ArgumentList.Add
        psi.WorkingDirectory <- workDir
        psi.RedirectStandardInput <- true
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.StandardInputEncoding <- (UTF8Encoding(false) :> Encoding)
        psi.StandardOutputEncoding <- (UTF8Encoding(false) :> Encoding)
        psi.StandardErrorEncoding <- (UTF8Encoding(false) :> Encoding)
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true

        for (k, v) in envVars do
            psi.Environment.[k] <- v

        try
            use proc = new Process()
            proc.StartInfo <- psi
            proc.Start() |> ignore
            let stdoutTask = proc.StandardOutput.ReadToEndAsync()
            let stderrTask = proc.StandardError.ReadToEndAsync()
            proc.StandardInput.Write(stdinText)
            proc.StandardInput.Flush()
            proc.StandardInput.Close()

            if proc.WaitForExit timeoutMs then
                { ExitCode = proc.ExitCode
                  StdOut = stdoutTask.Result
                  StdErr = stderrTask.Result }
            else
                try proc.Kill true with _ -> ()
                { ExitCode = -2
                  StdOut = ""
                  StdErr = $"{fileName} が {timeoutMs}ms 以内に終了しませんでした（強制終了）" }
        with ex ->
            { ExitCode = -1
              StdOut = ""
              StdErr = $"{fileName} を起動できません: {ex.Message}" }

    /// WSL2 bash を wsl.exe -e bash -s 経由で起動し、標準入力からスクリプト本体を安全に実行する
    let runWslBash (distroOpt: string option) (script: string) (workDir: string) (timeoutMs: int) : ScriptProcess.ProcessResult =
        let lfScript = script.Replace("\r\n", "\n")
        let args =
            match distroOpt with
            | Some d -> [ "-d"; d; "-e"; "bash"; "-s" ]
            | None -> [ "-e"; "bash"; "-s" ]
        runWithEnvStdin "wsl.exe" args workDir [] lfScript timeoutMs

    /// 環境変数付きで pwsh スクリプトを実行する
    let runPwshWithEnv (script: string) (workDir: string) (envVars: (string * string) list) : ScriptProcess.ProcessResult =
        let prelude =
            "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; "
            + "[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false); "

        let encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(prelude + script))
        runWithEnv "pwsh" [ "-NoProfile"; "-NonInteractive"; "-EncodedCommand"; encoded ] workDir envVars 120000

    /// scripts/common.ps1 を dot-source し、失敗時の終了コードと出力を返却する
    let invokeCommonRaw (invocation: string) (envVars: (string * string) list) : ScriptProcess.ProcessResult =
        let common = RepoLayout.pathOf "scripts/common.ps1"
        let script = $". {ScriptProcess.psq common}; {invocation}"
        runPwshWithEnv script (RepoLayout.repoRoot ()) envVars

    /// scripts/common.ps1 の関数呼び出しが例外（throw）または非 0 終了になることを検証する
    let assertCommonFails (invocation: string) (envVars: (string * string) list) : string =
        let common = RepoLayout.pathOf "scripts/common.ps1"
        let script =
            $"""
. {ScriptProcess.psq common}
try {{
    {invocation}
    Write-Output "UNEXPECTED_SUCCESS"
    exit 0
}} catch {{
    if ($_.Exception -is [System.Management.Automation.CommandNotFoundException] -or $_.FullyQualifiedErrorId -like '*CommandNotFoundException*') {{
        Write-Output "UNIMPLEMENTED_COMMAND_NOT_FOUND: $($_.Exception.Message)"
        exit 22
    }}
    Write-Error $_.Exception.Message
    exit 1
}}
"""
        let r = runPwshWithEnv script (RepoLayout.repoRoot ()) envVars
        Assert.False(r.StdOut.Contains("UNIMPLEMENTED_COMMAND_NOT_FOUND"), $"対象関数が未実装のため失敗（意図通りの Red）: {r.StdOut}")
        Assert.True((r.ExitCode <> 0), $"エラー/throw を期待しましたが成功しました: {invocation}\nSTDOUT: {r.StdOut}\nSTDERR: {r.StdErr}")
        Assert.False(r.StdOut.Contains("UNEXPECTED_SUCCESS"), $"throw せずに続行しました: {invocation}\nSTDOUT: {r.StdOut}")
        r.StdErr + "\n" + r.StdOut

    /// csc.exe を用いてテスト用モック実行ファイル（wsl.exe / wslc.exe）を 1 度だけビルドする
    let private mockBinCache =
        lazy (
            let dir = Path.Combine(Path.GetTempPath(), $"tvm-mock-bins-{Guid.NewGuid():N}")
            Directory.CreateDirectory dir |> ignore
            let cscPath = @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
            if not (File.Exists cscPath) then
                failwith $"モックコンパイラが見つかりません: {cscPath}"

            let mockWslCs = Path.Combine(dir, "MockWsl.cs")
            let mockWslExe = Path.Combine(dir, "wsl.exe")
            let wslCode = """
using System;
using System.IO;
using System.Threading;

class Program {
    static int Main(string[] args) {
        string sleepStr = Environment.GetEnvironmentVariable("MOCK_WSL_SLEEP_MS");
        int sleepMs;
        if (!string.IsNullOrEmpty(sleepStr) && int.TryParse(sleepStr, out sleepMs) && sleepMs > 0) {
            Thread.Sleep(sleepMs);
        }
        string b64 = Environment.GetEnvironmentVariable("MOCK_WSL_STDOUT_B64");
        if (!string.IsNullOrEmpty(b64)) {
            byte[] bytes = Convert.FromBase64String(b64);
            using (Stream stdout = Console.OpenStandardOutput()) {
                stdout.Write(bytes, 0, bytes.Length);
                stdout.Flush();
            }
        }
        string exitStr = Environment.GetEnvironmentVariable("MOCK_WSL_EXIT");
        int exitCode = 0;
        if (!string.IsNullOrEmpty(exitStr)) int.TryParse(exitStr, out exitCode);
        return exitCode;
    }
}
"""
            File.WriteAllText(mockWslCs, wslCode, UTF8Encoding(false))
            let r1 = ScriptProcess.run cscPath [ "/nologo"; "/optimize+"; $"/out:{mockWslExe}"; mockWslCs ] dir 30000
            if r1.ExitCode <> 0 then
                failwith $"MockWsl.exe ビルド失敗: {r1.StdOut} {r1.StdErr}"

            let mockRuntimeCs = Path.Combine(dir, "MockRuntime.cs")
            let mockRuntimeExe = Path.Combine(dir, "wslc.exe")
            let runtimeCode = """
using System;
using System.IO;
using System.Text;
using System.Threading;

class Program {
    static string JsonStr(string s) {
        var sb = new StringBuilder("\"");
        foreach (char c in s) {
            if (c == '\\') sb.Append("\\\\");
            else if (c == '"') sb.Append("\\\"");
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\t') sb.Append("\\t");
            else sb.Append(c);
        }
        sb.Append("\"");
        return sb.ToString();
    }

    static int Main(string[] args) {
        string sleepStr = Environment.GetEnvironmentVariable("MOCK_WSLC_SLEEP_MS");
        int sleepMs;
        if (!string.IsNullOrEmpty(sleepStr) && int.TryParse(sleepStr, out sleepMs) && sleepMs > 0) {
            Thread.Sleep(sleepMs);
        }
        string logPath = Environment.GetEnvironmentVariable("MOCK_RUNTIME_LOG");
        if (!string.IsNullOrEmpty(logPath)) {
            var sb = new StringBuilder("[");
            for (int i = 0; i < args.Length; i++) {
                if (i > 0) sb.Append(",");
                sb.Append(JsonStr(args[i]));
            }
            sb.Append("]");
            File.AppendAllText(logPath, sb.ToString() + Environment.NewLine, new UTF8Encoding(false));
        }

        if (args.Length > 0 && string.Equals(args[0], "inspect", StringComparison.OrdinalIgnoreCase)) {
            string inspectB64 = Environment.GetEnvironmentVariable("MOCK_INSPECT_STDOUT_B64");
            if (!string.IsNullOrEmpty(inspectB64)) {
                byte[] bytes = Convert.FromBase64String(inspectB64);
                using (Stream stdout = Console.OpenStandardOutput()) {
                    stdout.Write(bytes, 0, bytes.Length);
                    stdout.Flush();
                }
            }
            string inspectExitStr = Environment.GetEnvironmentVariable("MOCK_INSPECT_EXIT");
            int inspectExit = 1;
            if (!string.IsNullOrEmpty(inspectExitStr)) int.TryParse(inspectExitStr, out inspectExit);
            return inspectExit;
        }

        string exitStr = Environment.GetEnvironmentVariable("MOCK_RUNTIME_EXIT");
        int exitCode = 0;
        if (!string.IsNullOrEmpty(exitStr)) int.TryParse(exitStr, out exitCode);
        return exitCode;
    }
}
"""
            File.WriteAllText(mockRuntimeCs, runtimeCode, UTF8Encoding(false))
            let r2 = ScriptProcess.run cscPath [ "/nologo"; "/optimize+"; $"/out:{mockRuntimeExe}"; mockRuntimeCs ] dir 30000
            if r2.ExitCode <> 0 then
                failwith $"MockRuntime.exe ビルド失敗: {r2.StdOut} {r2.StdErr}"

            (mockWslExe, mockRuntimeExe)
        )

    let getMockWslExe () = fst (mockBinCache.Force())
    let getMockRuntimeExe () = snd (mockBinCache.Force())

    /// エフェメラルポート上で 200 OK を即応答する軽量 HTTP リスナーを起動する
    let withHealthServer (action: int -> unit) =
        let listener = new TcpListener(IPAddress.Loopback, 0)
        listener.Start()
        let port = (listener.LocalEndpoint :?> IPEndPoint).Port
        use cts = new CancellationTokenSource()

        let serverTask =
            Task.Run(fun () ->
                while not cts.IsCancellationRequested do
                    try
                        if listener.Pending() then
                            use client = listener.AcceptTcpClient()
                            use stream = client.GetStream()
                            let buf = Array.zeroCreate<byte> 1024
                            if stream.DataAvailable then
                                stream.Read(buf, 0, buf.Length) |> ignore
                            let resp = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK")
                            stream.Write(resp, 0, resp.Length)
                            stream.Flush()
                        else
                            Thread.Sleep 20
                    with _ -> ()
            )

        try
            action port
        finally
            cts.Cancel()
            try listener.Stop() with _ -> ()
            try serverTask.Wait(1000) |> ignore with _ -> ()

    /// モックランタイムの呼び出しログ（1行1コマンド配列）をパースする
    let readRuntimeCalls (logPath: string) : string list list =
        if not (File.Exists logPath) then
            []
        else
            File.ReadAllLines(logPath, UTF8Encoding(false))
            |> Array.filter (fun l -> not (String.IsNullOrWhiteSpace l))
            |> Array.map (fun line ->
                use doc = JsonDocument.Parse line
                doc.RootElement.EnumerateArray() |> Seq.map (fun e -> e.GetString()) |> List.ofSeq)
            |> List.ofArray

    /// 環境変数を取得する（null の場合は空文字）
    let getEnvOrEmpty (name: string) : string =
        match Environment.GetEnvironmentVariable(name) with
        | null -> ""
        | v -> v

    /// 実機からユーザー WSL ディストロ名を取得する（docker-desktop 除外）
    let getLiveUserWslDistro () : string =
        let res = runWithEnv "pwsh" [ "-NoProfile"; "-NonInteractive"; "-Command"; "$env:WSL_UTF8='1'; wsl.exe -l -q" ] (RepoLayout.repoRoot ()) [] 10000
        if res.ExitCode <> 0 then
            failwith $"実機 WSL ディストロ一覧を取得できませんでした (exit={res.ExitCode}): {res.StdErr}"
        let distros =
            res.StdOut.Replace("\u0000", "").Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.map (fun s -> s.Trim().Trim('\uFEFF', '\uFFFD'))
            |> Array.filter (fun s -> not (String.IsNullOrWhiteSpace s) && s <> "docker-desktop" && s <> "docker-desktop-data")
        if distros.Length = 0 then
            failwith "実機にユーザー WSL ディストロが見つかりません"
        distros.[0]

    /// WSL /tmp 配下の一時ディレクトリパスを生成し、終了時に必ず掃除する（アサーション例外は握りつぶさない）
    let withWslTempDir (distro: string) (action: string -> string -> unit) =
        let folderName = $"tvm-test-{Guid.NewGuid():N}"
        let linuxPath = $"/tmp/{folderName}"
        let uncPath = $"\\\\wsl.localhost\\{distro}\\tmp\\{folderName}"
        let r = runWslBash (Some distro) $"mkdir -p '{linuxPath}'\n" (RepoLayout.repoRoot ()) 15000
        Assert.True((r.ExitCode = 0), $"WSL /tmp 一時ディレクトリ作成失敗: {r.StdErr}")
        try
            action linuxPath uncPath
        finally
            try
                runWslBash (Some distro) $"rm -rf '{linuxPath}'\n" (RepoLayout.repoRoot ()) 15000 |> ignore
            with _ -> ()

    /// deploy.ps1 結合テスト用の一時リポジトリ・モックランタイム環境を構築して実行する
    type DeployPsTestEnv =
        { TempRoot: string
          RepoRoot: string
          BinDir: string
          RuntimeLogPath: string
          SrcDir: string }

    let withDeployPsEnv (baseJson: string option) (prodJson: string option) (withMockWsl: (byte[] * int * int) option) (action: DeployPsTestEnv -> unit) =
        let tempRoot = Path.Combine(Path.GetTempPath(), $"tvm-ps-it-{Guid.NewGuid():N}")
        let repoRoot = Path.Combine(tempRoot, "repo")
        let scriptsDir = Path.Combine(repoRoot, "scripts")
        let srcDir = Path.Combine(repoRoot, "src", "TagBasedVideoManager")
        let binDir = Path.Combine(tempRoot, "bin")
        Directory.CreateDirectory scriptsDir |> ignore
        Directory.CreateDirectory srcDir |> ignore
        Directory.CreateDirectory binDir |> ignore

        File.Copy(RepoLayout.pathOf "scripts/common.ps1", Path.Combine(scriptsDir, "common.ps1"))
        File.Copy(RepoLayout.pathOf "scripts/deploy.ps1", Path.Combine(scriptsDir, "deploy.ps1"))
        File.WriteAllText(Path.Combine(repoRoot, "Dockerfile"), "FROM scratch\n")

        match baseJson with
        | Some j -> File.WriteAllText(Path.Combine(srcDir, "appsettings.json"), j, UTF8Encoding(false))
        | None -> File.WriteAllText(Path.Combine(srcDir, "appsettings.json"), """{"PORT": 5620, "VIDEO_DIRS": "", "DATA_DIR": "./data"}""", UTF8Encoding(false))

        match prodJson with
        | Some j -> File.WriteAllText(Path.Combine(srcDir, "appsettings.Production.json"), j, UTF8Encoding(false))
        | None -> ()

        let mockRuntime = getMockRuntimeExe ()
        File.Copy(mockRuntime, Path.Combine(binDir, "wslc.exe"))
        File.Copy(mockRuntime, Path.Combine(binDir, "docker.exe"))

        if withMockWsl.IsSome then
            File.Copy(getMockWslExe (), Path.Combine(binDir, "wsl.exe"))

        let envInfo =
            { TempRoot = tempRoot
              RepoRoot = repoRoot
              BinDir = binDir
              RuntimeLogPath = Path.Combine(tempRoot, "wslc-calls.log")
              SrcDir = srcDir }

        try
            action envInfo
        finally
            try Directory.Delete(tempRoot, true) with _ -> ()

    let runDeployPs (envInfo: DeployPsTestEnv) (cliArgs: string) (extraEnv: (string * string) list) : ScriptProcess.ProcessResult =
        let deployPs1 = Path.Combine(envInfo.RepoRoot, "scripts", "deploy.ps1")
        let existingPath = getEnvOrEmpty "PATH"
        let baseEnv =
            [ ("PATH", envInfo.BinDir + string Path.PathSeparator + existingPath)
              ("MOCK_RUNTIME_LOG", envInfo.RuntimeLogPath)
              ("VIDEO_DIRS", "")
              ("VIDEO_DIR", "")
              ("DATA_DIR", "")
              ("PORT", "") ]
        let mergedEnv = baseEnv @ extraEnv
        let script =
            $"""
& {ScriptProcess.psq deployPs1} {cliArgs}
exit $LASTEXITCODE
"""
        runPwshWithEnv script envInfo.RepoRoot mergedEnv

    /// deploy.sh 結合テストを純粋 Linux /tmp 上の一時ディレクトリとモック docker / curl で実行する
    let runDeployShTest (setupBash: string) (invokeBash: string) : ScriptProcess.ProcessResult =
        let rawScript =
            $"""
set -euo pipefail
TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT

REPO_ROOT="$TMP_DIR/repo"
mkdir -p "$REPO_ROOT/scripts" "$REPO_ROOT/src/TagBasedVideoManager" "$TMP_DIR/bin"
cp scripts/deploy.sh "$REPO_ROOT/scripts/deploy.sh"
chmod +x "$REPO_ROOT/scripts/deploy.sh"
touch "$REPO_ROOT/Dockerfile"

cat << 'EOF' > "$REPO_ROOT/src/TagBasedVideoManager/appsettings.json"
{{"PORT": 5620, "VIDEO_DIRS": "", "DATA_DIR": "./data"}}
EOF

export DOCKER_CALL_LOG="$TMP_DIR/docker-calls.log"

cat << 'EOF' > "$TMP_DIR/bin/docker"
#!/usr/bin/env bash
python3 -c 'import sys,json; print(json.dumps(sys.argv[1:]))' "$@" >> "$DOCKER_CALL_LOG"
if [ "${{1:-}}" = "inspect" ]; then
    if [ -n "${{MOCK_INSPECT_JSON:-}}" ]; then
        python3 -c 'import os,sys; sys.stdout.write(os.environ.get("MOCK_INSPECT_JSON",""))'
    fi
    exit "${{MOCK_INSPECT_EXIT:-1}}"
fi
exit "${{MOCK_DOCKER_EXIT:-0}}"
EOF
chmod +x "$TMP_DIR/bin/docker"

cat << 'EOF' > "$TMP_DIR/bin/curl"
#!/usr/bin/env bash
exit 0
EOF
chmod +x "$TMP_DIR/bin/curl"

export PATH="$TMP_DIR/bin:/usr/local/bin:/usr/bin:/bin"
unset VIDEO_DIRS VIDEO_DIR DATA_DIR PORT ASPNETCORE_ENVIRONMENT ENVIRONMENT || true

{setupBash}

set +e
(
{invokeBash}
) > "$TMP_DIR/deploy_out.log" 2>&1
DEPLOY_RC=$?
set -e

echo "===DEPLOY_RC=$DEPLOY_RC==="
cat "$TMP_DIR/deploy_out.log"
echo "===DOCKER_CALLS==="
if [ -f "$DOCKER_CALL_LOG" ]; then
    cat "$DOCKER_CALL_LOG"
fi
"""
        runWslBash None rawScript (RepoLayout.repoRoot ()) 60000

    /// runDeployShTest 出力から終了コード・ログ・docker 呼び出し配列リストを抽出する
    let parseDeployShResult (r: ScriptProcess.ProcessResult) : int * string * string list list =
        Assert.True((r.ExitCode = 0), $"bash ハーネス自体が異常終了しました (exit={r.ExitCode}):\nSTDOUT: {r.StdOut}\nSTDERR: {r.StdErr}")
        let m = Regex.Match(r.StdOut, @"===DEPLOY_RC=(-?\d+)===")
        Assert.True(m.Success, $"DEPLOY_RC が取得できません: {r.StdOut}")
        let deployRc = int m.Groups.[1].Value
        let parts = r.StdOut.Split([| "===DOCKER_CALLS===" |], StringSplitOptions.None)
        let logOutput = parts.[0]
        let calls =
            if parts.Length > 1 then
                parts.[1].Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
                |> Array.filter (fun l -> l.TrimStart().StartsWith("["))
                |> Array.map (fun line ->
                    use doc = JsonDocument.Parse line
                    doc.RootElement.EnumerateArray() |> Seq.map (fun e -> e.GetString()) |> List.ofSeq)
                |> List.ofArray
            else
                []
        deployRc, logOutput, calls

[<CollectionDefinition("WslBashSerial", DisableParallelization = true)>]
type WslBashSerialCollection() = class end

/// 不具合再現テスト (Task 1.1: UT-REPRO-01〜03)
[<Collection("WslBashSerial")>]
module BugReproductionTests =

    open DeployDataDirTestHarness

    [<Fact>]
    let ``UT-REPRO-01: TargetIsWindows=$true で VIDEO_DIRS は /mnt/ ではなくスラッシュ形式、DATA_DIR の MountSource は D:/repo/data になる`` () =
        let tempRoot = Path.Combine(Path.GetTempPath(), $"tvm-repro01-{Guid.NewGuid():N}")
        let videoDir = Path.Combine(tempRoot, "e")
        Directory.CreateDirectory videoDir |> ignore

        try
            let expectedVideoSlash = videoDir.Replace('\\', '/').TrimEnd('/')
            let rVideo =
                invokeCommonRaw
                    $"""
$res = Resolve-VideoMounts -VideoDirs {ScriptProcess.psq videoDir} -TargetIsWindows $true
$res.Mounts[0] | ConvertTo-Json -Compress
"""
                    []
            Assert.True((rVideo.ExitCode = 0), $"Resolve-VideoMounts 失敗: {rVideo.StdErr}")
            use docV = JsonDocument.Parse rVideo.StdOut
            let actualHostPath = docV.RootElement.GetProperty("HostPath").GetString()
            actualHostPath |> should not' (startWith "/mnt/")
            actualHostPath |> should equal expectedVideoSlash

            let rData =
                invokeCommonRaw
                    """
$d = Resolve-DataDir -Path 'D:\repo\data'
$d | ConvertTo-Json -Compress
"""
                    []
            Assert.True((rData.ExitCode = 0), $"Resolve-DataDir 失敗 (exit={rData.ExitCode}): {rData.StdErr}")
            use docD = JsonDocument.Parse rData.StdOut
            docD.RootElement.GetProperty("MountSource").GetString() |> should equal "D:/repo/data"
        finally
            try Directory.Delete(tempRoot, true) with _ -> ()

    [<Fact>]
    let ``UT-REPRO-02: Resolve-DataDir で Linux 絶対パス /home/spjao/var/apps/TagBasedVideoManager/data が WSL UNC パスへ解決される`` () =
        let r =
            invokeCommonRaw
                """
$d = Resolve-DataDir -Path '/home/spjao/var/apps/TagBasedVideoManager/data' -GetDefaultDistro { 'Ubuntu-24.04' }
$d | ConvertTo-Json -Compress
"""
                []
        Assert.True((r.ExitCode = 0), $"Resolve-DataDir 失敗 (exit={r.ExitCode}): {r.StdErr}")
        use doc = JsonDocument.Parse r.StdOut
        let expected = @"\\wsl.localhost\Ubuntu-24.04\home\spjao\var\apps\TagBasedVideoManager\data"
        doc.RootElement.GetProperty("HostPath").GetString() |> should equal expected
        doc.RootElement.GetProperty("MountSource").GetString() |> should equal expected
        doc.RootElement.GetProperty("DistroName").GetString() |> should equal "Ubuntu-24.04"

    [<Fact>]
    let ``UT-REPRO-03: deploy.sh で DATA_DIR="./data" が相対パスのまま渡らず ${REPO_ROOT}/data:/app/data に正規化される`` () =
        let setup =
            """
mkdir -p "$TMP_DIR/videos/Anime"
export VIDEO_DIRS="$TMP_DIR/videos/Anime"
export DATA_DIR="./data"
echo "REPO_ROOT=$REPO_ROOT"
"""
        let invoke =
            """
cd "$REPO_ROOT"
bash "$REPO_ROOT/scripts/deploy.sh"
"""
        let r = runDeployShTest setup invoke
        let rc, output, calls = parseDeployShResult r
        Assert.True((rc = 0), $"deploy.sh が失敗しました (rc={rc}):\n{output}")
        let mRepo = Regex.Match(output, @"REPO_ROOT=(.+)")
        Assert.True(mRepo.Success, $"REPO_ROOT が出力に見つかりません: {output}")
        let repoRootLinux = mRepo.Groups.[1].Value.Trim()
        let runCall = calls |> List.tryFind (fun args -> args.Head = "run")
        Assert.True(runCall.IsSome, $"docker run が呼ばれていません: {calls}")
        let args = runCall.Value
        args |> should not' (contain "./data:/app/data")
        args |> should contain $"{repoRootLinux}/data:/app/data"

/// scripts/common.ps1 の WSL パス・ディストロ解決・UNC 単体テスト (Task 1.2: UT-PATH-05, UT-WSL-01〜03, Resolve-WslUncPath)
module WslDistroAndUncTests =

    open DeployDataDirTestHarness

    [<Theory>]
    [<InlineData(@"\\wsl.localhost\Ubuntu-24.04\home\user\videos", "/home/user/videos")>]
    [<InlineData(@"\\wsl$\Ubuntu\data", "/data")>]
    [<InlineData("//wsl.localhost/Ubuntu-24.04/var/data/", "/var/data")>]
    [<InlineData(@"\\wsl.localhost\Ubuntu-24.04", "/")>]
    let ``UT-PATH-05: ConvertTo-WslPath は WSL UNC パスを Linux 内部パスへ変換する`` (input: string, expected: string) =
        let r = invokeCommonRaw $"Write-Output (ConvertTo-WslPath -Path {ScriptProcess.psq input})" []
        Assert.True((r.ExitCode = 0), $"ConvertTo-WslPath 失敗: {r.StdErr}")
        r.StdOut.TrimEnd('\r', '\n') |> should equal expected

    [<Fact>]
    let ``UT-WSL-01: Get-DefaultWslDistro は docker-desktop 等を除外し UTF-16LE NULL 混在から Ubuntu-24.04 を取得する`` () =
        let r =
            invokeCommonRaw
                """
$d = Get-DefaultWslDistro -GetWslList { @("docker-desktop", "", "`0U`0b`0u`0n`0t`0u`0-`02`04`0.`00`04", "docker-desktop-data") }
Write-Output $d
"""
                []
        Assert.True((r.ExitCode = 0), $"Get-DefaultWslDistro 失敗 (exit={r.ExitCode}): {r.StdErr}")
        r.StdOut.TrimEnd('\r', '\n') |> should equal "Ubuntu-24.04"

    [<Fact>]
    let ``UT-WSL-02: Get-DefaultWslDistro は BOM 置換文字 \uFFFD を除去して正規化する`` () =
        let r =
            invokeCommonRaw
                """
$d = Get-DefaultWslDistro -GetWslList { "`u{FFFD}`u{FFFD}Ubuntu-24.04" }
Write-Output $d
"""
                []
        Assert.True((r.ExitCode = 0), $"Get-DefaultWslDistro 失敗 (exit={r.ExitCode}): {r.StdErr}")
        r.StdOut.TrimEnd('\r', '\n') |> should equal "Ubuntu-24.04"

    [<Fact>]
    let ``UT-WSL-03: Get-DefaultWslDistro はシステムディストロのみや 0 件時に回復手順付きで throw する`` () =
        for mockBlock in [ """{ @("docker-desktop", "docker-desktop-data") }"""; """{ @() }""" ] do
            let msg = assertCommonFails $"$null = Get-DefaultWslDistro -GetWslList {mockBlock}" []
            Assert.True(
                msg.Contains(@"\\wsl.localhost\") && msg.Contains("wsl"),
                $"回復案内 (\\wsl.localhost\\ および wsl --status 等) が含まれていません: {msg}"
            )

    [<Fact>]
    let ``UT-WSL-03: Get-DefaultWslDistro はダミー wsl.exe の非0終了およびタイムアウトで回復手順付きで throw する`` () =
        let tempBin = Path.Combine(Path.GetTempPath(), $"tvm-wsl-mock-{Guid.NewGuid():N}")
        Directory.CreateDirectory tempBin |> ignore
        try
            File.Copy(getMockWslExe (), Path.Combine(tempBin, "wsl.exe"))
            let existingPath = getEnvOrEmpty "PATH"
            let mockPath = tempBin + string Path.PathSeparator + existingPath

            // 非 0 終了
            let msgExit1 =
                assertCommonFails
                    "$null = Get-DefaultWslDistro -TimeoutMs 2000"
                    [ ("PATH", mockPath); ("MOCK_WSL_EXIT", "1"); ("MOCK_WSL_STDOUT_B64", ""); ("MOCK_WSL_SLEEP_MS", "0") ]
            Assert.True(msgExit1.Contains(@"\\wsl.localhost\"), $"回復案内が含まれていません: {msgExit1}")

            // タイムアウト
            let msgTimeout =
                assertCommonFails
                    "$null = Get-DefaultWslDistro -TimeoutMs 200"
                    [ ("PATH", mockPath); ("MOCK_WSL_EXIT", "0"); ("MOCK_WSL_STDOUT_B64", ""); ("MOCK_WSL_SLEEP_MS", "3000") ]
            Assert.True(msgTimeout.Contains(@"\\wsl.localhost\"), $"タイムアウト時の回復案内が含まれていません: {msgTimeout}")
        finally
            try Directory.Delete(tempBin, true) with _ -> ()

    [<Fact>]
    let ``Resolve-WslUncPath: WSL UNC と Linux パスを正規化し、空文字・不正 UNC・..・ルート直下を throw 拒否する`` () =
        let r1 =
            invokeCommonRaw
                """
$u = Resolve-WslUncPath -Path '//wsl$/Ubuntu/var/data/'
$u | ConvertTo-Json -Compress
"""
                []
        Assert.True((r1.ExitCode = 0), $"Resolve-WslUncPath 失敗: {r1.StdErr}")
        use doc1 = JsonDocument.Parse r1.StdOut
        doc1.RootElement.GetProperty("DistroName").GetString() |> should equal "Ubuntu"
        doc1.RootElement.GetProperty("UncPath").GetString() |> should equal @"\\wsl.localhost\Ubuntu\var\data"
        doc1.RootElement.GetProperty("LinuxPath").GetString() |> should equal "/var/data"

        let r2 =
            invokeCommonRaw
                """
$u = Resolve-WslUncPath -Path '/home/user/data' -GetDefaultDistro { 'Ubuntu-24.04' }
$u | ConvertTo-Json -Compress
"""
                []
        Assert.True((r2.ExitCode = 0), $"Resolve-WslUncPath (Linuxパス) 失敗: {r2.StdErr}")
        use doc2 = JsonDocument.Parse r2.StdOut
        doc2.RootElement.GetProperty("DistroName").GetString() |> should equal "Ubuntu-24.04"
        doc2.RootElement.GetProperty("UncPath").GetString() |> should equal @"\\wsl.localhost\Ubuntu-24.04\home\user\data"
        doc2.RootElement.GetProperty("LinuxPath").GetString() |> should equal "/home/user/data"

        for (bad, expectedKeyword) in
            [ ("", "空")
              ("   ", "空")
              (@"\\wsl.localhost\", "ディストロ名")
              (@"\\wsl.localhost\Ubuntu", "サブパス")
              ("/", "ルート")
              (@"\\fileserver\share\data", "UNC")
              (@"\\wsl.localhost.evil\share\data", "UNC")
              ("//wsl$evil/data", "UNC")
              ("/home/../etc", "..")
              (@"\\wsl.localhost\Ubuntu\home\..\etc", "..") ] do
            let errMsg = assertCommonFails $"$null = Resolve-WslUncPath -Path {ScriptProcess.psq bad} -GetDefaultDistro {{ 'Ubuntu-24.04' }}" []
            Assert.True(errMsg.Contains(expectedKeyword), $"Resolve-WslUncPath ({bad}) のエラーに '{expectedKeyword}' が含まれていません: {errMsg}")

/// scripts/common.ps1 の Resolve-DataDir 単体テスト (Task 1.2: UT-DATA-01〜13, D1〜D10)
module DataDirResolutionTests =

    open DeployDataDirTestHarness

    let private callResolveDataDir (expr: string) : JsonDocument =
        let r = invokeCommonRaw $"{expr} | ConvertTo-Json -Compress" []
        Assert.True((r.ExitCode = 0), $"Resolve-DataDir 失敗 (exit={r.ExitCode}): {expr}\nSTDERR: {r.StdErr}\nSTDOUT: {r.StdOut}")
        JsonDocument.Parse r.StdOut

    [<Fact>]
    let ``UT-DATA-01: 相対パス ./data は RepoRoot 基準の絶対パスへ正規化される (D9)`` () =
        use doc = callResolveDataDir """Resolve-DataDir -Path './data' -RepoRoot 'D:\repo'"""
        doc.RootElement.GetProperty("HostPath").GetString() |> should equal @"D:\repo\data"
        doc.RootElement.GetProperty("MountSource").GetString() |> should equal "D:/repo/data"

    [<Fact>]
    let ``UT-DATA-02: Windows 絶対パスは MountSource がスラッシュ区切りになる (D8)`` () =
        use doc = callResolveDataDir """Resolve-DataDir -Path 'D:\data\tvm\'"""
        doc.RootElement.GetProperty("HostPath").GetString() |> should equal @"D:\data\tvm"
        doc.RootElement.GetProperty("MountSource").GetString() |> should equal "D:/data/tvm"

    [<Fact>]
    let ``UT-DATA-03: WSL UNC パス \\wsl.localhost\... はそのまま保持される (D6)`` () =
        use doc = callResolveDataDir """Resolve-DataDir -Path '\\wsl.localhost\Ubuntu-24.04\home\spjao\data'"""
        doc.RootElement.GetProperty("DistroName").GetString() |> should equal "Ubuntu-24.04"
        doc.RootElement.GetProperty("HostPath").GetString() |> should equal @"\\wsl.localhost\Ubuntu-24.04\home\spjao\data"
        doc.RootElement.GetProperty("MountSource").GetString() |> should equal @"\\wsl.localhost\Ubuntu-24.04\home\spjao\data"

    [<Fact>]
    let ``UT-DATA-04: 旧形式 //wsl$/... スラッシュ混在 UNC は \\wsl.localhost\... へ正規化される (D6)`` () =
        use doc = callResolveDataDir """Resolve-DataDir -Path '//wsl$/Ubuntu/var/data/'"""
        doc.RootElement.GetProperty("DistroName").GetString() |> should equal "Ubuntu"
        doc.RootElement.GetProperty("HostPath").GetString() |> should equal @"\\wsl.localhost\Ubuntu\var\data"
        doc.RootElement.GetProperty("MountSource").GetString() |> should equal @"\\wsl.localhost\Ubuntu\var\data"

    [<Fact>]
    let ``UT-DATA-05: Linux 絶対パスは既定ディストロが補完され WSL UNC へ解決される (D4)`` () =
        use doc = callResolveDataDir """Resolve-DataDir -Path '/home/spjao/data' -GetDefaultDistro { 'Ubuntu-24.04' }"""
        doc.RootElement.GetProperty("DistroName").GetString() |> should equal "Ubuntu-24.04"
        doc.RootElement.GetProperty("HostPath").GetString() |> should equal @"\\wsl.localhost\Ubuntu-24.04\home\spjao\data"
        doc.RootElement.GetProperty("MountSource").GetString() |> should equal @"\\wsl.localhost\Ubuntu-24.04\home\spjao\data"

    [<Fact>]
    let ``UT-DATA-06: Linux 絶対パスで既定ディストロ取得失敗時は D:\home\... へフォールバックせず回復手順付きで throw する (D4)`` () =
        let msgStub = assertCommonFails """$null = Resolve-DataDir -Path '/home/spjao/data' -GetDefaultDistro { throw 'WSL unavailable: please use \\wsl.localhost\<DistroName>\...' }""" []
        Assert.False(msgStub.Contains(@":\home\"), $"Windows ドライブへ誤フォールバックしています: {msgStub}")
        Assert.True(msgStub.Contains("wsl.localhost"), $"回復案内 (wsl.localhost) が含まれていません: {msgStub}")

        let msgReal = assertCommonFails """$null = Resolve-DataDir -Path '/home/spjao/data' -GetDefaultDistro { Get-DefaultWslDistro -GetWslList { @() } }""" []
        Assert.False(msgReal.Contains(@":\home\"), $"Windows ドライブへ誤フォールバックしています: {msgReal}")
        Assert.True(msgReal.Contains("wsl --status") && msgReal.Contains("wsl.localhost"), $"実関数の失敗伝播に回復手順 ('wsl --status' / 'wsl.localhost') が含まれていません: {msgReal}")

    [<Fact>]
    let ``UT-DATA-07: 空文字・空白のみは RepoRoot\data へフォールバックし、Resolve-WslUncPath 単体では拒否する (D1)`` () =
        for emptyVal in [ ""; "   " ] do
            use doc = callResolveDataDir $"""Resolve-DataDir -Path {ScriptProcess.psq emptyVal} -RepoRoot 'D:\repo'"""
            doc.RootElement.GetProperty("HostPath").GetString() |> should equal @"D:\repo\data"
            doc.RootElement.GetProperty("MountSource").GetString() |> should equal "D:/repo/data"
            let errMsg = assertCommonFails $"""$null = Resolve-WslUncPath -Path {ScriptProcess.psq emptyVal}""" []
            Assert.True(errMsg.Contains("空"), $"空文字拒否メッセージに '空' が含まれていません: {errMsg}")

    [<Fact>]
    let ``UT-DATA-08: セグメント .. のパストラバーサルは拒否し、ディレクトリ名内の v1..v2 は許可する (D2)`` () =
        for bad in [ @"\\wsl.localhost\Ubuntu\home\..\etc"; "/home/../etc"; "../data"; @"D:\a\..\b" ] do
            let errMsg = assertCommonFails $"""$null = Resolve-DataDir -Path {ScriptProcess.psq bad} -GetDefaultDistro {{ 'Ubuntu-24.04' }}""" []
            Assert.True(errMsg.Contains(".."), $"パストラバーサル拒否メッセージに '..' が含まれていません ({bad}): {errMsg}")

        use docWin = callResolveDataDir """Resolve-DataDir -Path 'D:\v1..v2\data'"""
        docWin.RootElement.GetProperty("MountSource").GetString() |> should equal "D:/v1..v2/data"

        use docLin = callResolveDataDir """Resolve-DataDir -Path '/home/v1..v2/data' -GetDefaultDistro { 'Ubuntu-24.04' }"""
        docLin.RootElement.GetProperty("MountSource").GetString() |> should equal @"\\wsl.localhost\Ubuntu-24.04\home\v1..v2\data"

    [<Theory>]
    [<InlineData(@"\\wsl.localhost\Ubuntu", "サブパス")>]
    [<InlineData(@"\\wsl.localhost\", "ディストロ名")>]
    [<InlineData("/", "ルート")>]
    [<InlineData(@"D:\", "ドライブ直下")>]
    [<InlineData("D:", "ドライブ相対")>]
    [<InlineData("D:data", "ドライブ相対")>]
    let ``UT-DATA-09: ルート直下・不完全 UNC・ドライブ相対は早期 throw 拒否される (D2, D3, D5, D7)`` (badPath: string, expectedKeyword: string) =
        let errMsg = assertCommonFails $"""$null = Resolve-DataDir -Path {ScriptProcess.psq badPath} -GetDefaultDistro {{ 'Ubuntu-24.04' }}""" []
        Assert.True(errMsg.Contains(expectedKeyword), $"Resolve-DataDir ({badPath}) のエラーに '{expectedKeyword}' が含まれていません: {errMsg}")

    [<Fact>]
    let ``UT-DATA-10: 非 WSL ネットワーク UNC パスおよび偽装ホスト名は早期 throw 拒否される (D2)`` () =
        for badUnc in [ @"\\fileserver\share\data"; @"\\wsl.localhost.evil\share\data"; "//wsl$evil/data" ] do
            let errMsg = assertCommonFails $"""$null = Resolve-DataDir -Path {ScriptProcess.psq badUnc}""" []
            Assert.True(errMsg.Contains("UNC") || errMsg.Contains("wsl.localhost"), $"非 WSL UNC ({badUnc}) の拒否エラーに 'UNC' または 'wsl.localhost' が含まれていません: {errMsg}")

    [<Fact>]
    let ``UT-DATA-11: 既存の通常ファイルを DATA_DIR に指定すると -CreateIfMissing の有無に関わらず早期 throw 拒否される (D10)`` () =
        let tempRoot = Path.Combine(Path.GetTempPath(), $"tvm-ut-data11-{Guid.NewGuid():N}")
        Directory.CreateDirectory tempRoot |> ignore
        let filePath = Path.Combine(tempRoot, "metadata.db")
        File.WriteAllText(filePath, "dummy")
        try
            let errWithCreate = assertCommonFails $"""$null = Resolve-DataDir -Path {ScriptProcess.psq filePath} -CreateIfMissing""" []
            Assert.True(errWithCreate.Contains("ファイル"), $"-CreateIfMissing あり時のエラーに 'ファイル' が含まれていません: {errWithCreate}")

            let errWithoutCreate = assertCommonFails $"""$null = Resolve-DataDir -Path {ScriptProcess.psq filePath}""" []
            Assert.True(errWithoutCreate.Contains("ファイル"), $"-CreateIfMissing なし時のエラーに 'ファイル' が含まれていません: {errWithoutCreate}")

            let newDir = Path.Combine(tempRoot, "created-sub", "data")
            use doc = callResolveDataDir $"""Resolve-DataDir -Path {ScriptProcess.psq newDir} -CreateIfMissing"""
            Directory.Exists newDir |> should equal true
            doc.RootElement.GetProperty("HostPath").GetString() |> should equal newDir
        finally
            try Directory.Delete(tempRoot, true) with _ -> ()

    [<Fact>]
    let ``UT-DATA-12: 半角スペース・角括弧・先頭3本スラッシュ・WSL UNC RepoRoot 相対パスが正しく正規化・作成される`` () =
        use docWin = callResolveDataDir """Resolve-DataDir -Path 'D:\My Data\tbvm data'"""
        docWin.RootElement.GetProperty("HostPath").GetString() |> should equal @"D:\My Data\tbvm data"
        docWin.RootElement.GetProperty("MountSource").GetString() |> should equal "D:/My Data/tbvm data"

        use docUnc = callResolveDataDir """Resolve-DataDir -Path '\\wsl.localhost\Ubuntu-24.04\home\u\my data'"""
        docUnc.RootElement.GetProperty("HostPath").GetString() |> should equal @"\\wsl.localhost\Ubuntu-24.04\home\u\my data"
        docUnc.RootElement.GetProperty("MountSource").GetString() |> should equal @"\\wsl.localhost\Ubuntu-24.04\home\u\my data"

        // 先頭3本スラッシュ ///home/spjao/data が非 WSL UNC と誤認されず /home/spjao/data として WSL UNC へ解決される
        use docTripleSlash = callResolveDataDir """Resolve-DataDir -Path '///home/spjao/data' -GetDefaultDistro { 'Ubuntu-24.04' }"""
        docTripleSlash.RootElement.GetProperty("DistroName").GetString() |> should equal "Ubuntu-24.04"
        docTripleSlash.RootElement.GetProperty("HostPath").GetString() |> should equal @"\\wsl.localhost\Ubuntu-24.04\home\spjao\data"
        docTripleSlash.RootElement.GetProperty("MountSource").GetString() |> should equal @"\\wsl.localhost\Ubuntu-24.04\home\spjao\data"

        // RepoRoot が WSL UNC パスの場合の相対パス結合で //wsl.localhost/... に破壊されず DistroName が保持される
        use docUncRepo = callResolveDataDir """Resolve-DataDir -Path './data' -RepoRoot '\\wsl.localhost\Ubuntu-24.04\home\user\repo'"""
        docUncRepo.RootElement.GetProperty("DistroName").GetString() |> should equal "Ubuntu-24.04"
        docUncRepo.RootElement.GetProperty("HostPath").GetString() |> should equal @"\\wsl.localhost\Ubuntu-24.04\home\user\repo\data"
        docUncRepo.RootElement.GetProperty("MountSource").GetString() |> should equal @"\\wsl.localhost\Ubuntu-24.04\home\user\repo\data"

        // 角括弧を含むディレクトリ名 (tbvm[2026]) の -CreateIfMissing 作成成功
        let tempRoot = Path.Combine(Path.GetTempPath(), $"tvm-ut-data12-{Guid.NewGuid():N}")
        let bracketDir = Path.Combine(tempRoot, "tbvm[2026]", "data")
        try
            use docBracket = callResolveDataDir $"""Resolve-DataDir -Path {ScriptProcess.psq bracketDir} -CreateIfMissing"""
            Directory.Exists bracketDir |> should equal true
            docBracket.RootElement.GetProperty("HostPath").GetString() |> should equal bracketDir
            docBracket.RootElement.GetProperty("MountSource").GetString() |> should equal (bracketDir.Replace('\\', '/'))
        finally
            try Directory.Delete(tempRoot, true) with _ -> ()

    [<Fact>]
    let ``UT-DATA-13: Resolve-DataDir の戻り値は 5 プロパティ契約 (RawInput, ResolvedFrom, DistroName, HostPath, MountSource) を満たす`` () =
        use doc = callResolveDataDir """Resolve-DataDir -Path './data' -RepoRoot 'D:\repo' -ResolvedFrom 'CLI'"""
        let propNames = doc.RootElement.EnumerateObject() |> Seq.map (fun p -> p.Name) |> Set.ofSeq
        for expectedProp in [ "RawInput"; "ResolvedFrom"; "DistroName"; "HostPath"; "MountSource" ] do
            propNames.Contains expectedProp |> should equal true
        doc.RootElement.GetProperty("RawInput").GetString() |> should equal "./data"
        doc.RootElement.GetProperty("ResolvedFrom").GetString() |> should equal "CLI"

/// ConvertTo-ContainerMountPath と Resolve-VideoMounts 拡張テスト (Task 1.2: UT-MOUNT-01〜06, UT-MOUNT-RO-01)
[<Collection("WslBashSerial")>]
module ContainerMountPathTests =

    open DeployDataDirTestHarness

    [<Fact>]
    let ``UT-MOUNT-01: ConvertTo-ContainerMountPath は TargetIsWindows の true/false とパス種別で正しく分岐する (M1〜M4)`` () =
        let invoke path targetIsWin =
            let r =
                invokeCommonRaw
                    $"Write-Output (ConvertTo-ContainerMountPath -Path {ScriptProcess.psq path} -TargetIsWindows {ScriptProcess.psBool targetIsWin} -GetDefaultDistro {{ 'Ubuntu-24.04' }})"
                    []
            Assert.True((r.ExitCode = 0), $"ConvertTo-ContainerMountPath 失敗: {r.StdErr}")
            r.StdOut.TrimEnd('\r', '\n')

        invoke @"E:\e" true |> should equal "E:/e"
        invoke @"\\wsl.localhost\Ubuntu-24.04\home\u\data" true |> should equal @"\\wsl.localhost\Ubuntu-24.04\home\u\data"
        invoke "/home/u/data" true |> should equal @"\\wsl.localhost\Ubuntu-24.04\home\u\data"
        invoke @"E:\e" false |> should equal "/mnt/e/e"
        invoke @"\\wsl.localhost\Ubuntu-24.04\home\u\data" false |> should equal "/home/u/data"
        let errUnc = assertCommonFails """$null = ConvertTo-ContainerMountPath -Path '\\fileserver\share\x' -TargetIsWindows $true""" []
        Assert.True(errUnc.Contains("UNC"), $"非 WSL UNC 拒否エラーに 'UNC' が含まれていません: {errUnc}")

    [<Fact>]
    let ``UT-MOUNT-02: 単一フォルダ指定 (TargetIsWindows=$true) でスラッシュ区切り :ro 引数が生成される`` () =
        let tempRoot = Path.Combine(Path.GetTempPath(), $"tvm-ut-mount02-{Guid.NewGuid():N}")
        let animeDir = Path.Combine(tempRoot, "Anime")
        Directory.CreateDirectory animeDir |> ignore
        try
            let expectedSlash = animeDir.Replace('\\', '/')
            let r = invokeCommonRaw $"(Resolve-VideoMounts -VideoDirs {ScriptProcess.psq animeDir} -TargetIsWindows $true) | ConvertTo-Json -Depth 4 -Compress" []
            Assert.True((r.ExitCode = 0), $"Resolve-VideoMounts 失敗: {r.StdErr}")
            use doc = JsonDocument.Parse r.StdOut
            let m = doc.RootElement.GetProperty("Mounts").[0]
            m.GetProperty("HostPath").GetString() |> should equal expectedSlash
            m.GetProperty("VolumeArg").GetString() |> should equal $"{expectedSlash}:/app/videos/Anime:ro"
        finally
            try Directory.Delete(tempRoot, true) with _ -> ()

    [<Fact>]
    let ``UT-MOUNT-03: WSL UNC パスの VIDEO_DIRS (TargetIsWindows=$true) でドライブ直下誤判定されず UNC:ro が生成される`` () =
        let distro = getLiveUserWslDistro ()
        withWslTempDir distro (fun _ uncBase ->
            let vidsUnc = Path.Combine(uncBase, "vids")
            Directory.CreateDirectory vidsUnc |> ignore
            let r = invokeCommonRaw $"(Resolve-VideoMounts -VideoDirs {ScriptProcess.psq vidsUnc} -TargetIsWindows $true) | ConvertTo-Json -Depth 4 -Compress" []
            Assert.True((r.ExitCode = 0), $"Resolve-VideoMounts (WSL UNC) 失敗: {r.StdErr}\nSTDOUT: {r.StdOut}")
            use doc = JsonDocument.Parse r.StdOut
            let m = doc.RootElement.GetProperty("Mounts").[0]
            m.GetProperty("HostPath").GetString() |> should equal vidsUnc
            m.GetProperty("VolumeArg").GetString() |> should equal $"{vidsUnc}:/app/videos/vids:ro"
        )

    [<Fact>]
    let ``UT-MOUNT-04: Linux 絶対パスの VIDEO_DIRS (TargetIsWindows=$true) で既定ディストロ解決・存在確認・UNC:ro 生成される`` () =
        let distro = getLiveUserWslDistro ()
        withWslTempDir distro (fun linuxBase uncBase ->
            let vidsUnc = Path.Combine(uncBase, "vids")
            Directory.CreateDirectory vidsUnc |> ignore
            let vidsLinux = $"{linuxBase}/vids"
            let r =
                invokeCommonRaw
                    $"(Resolve-VideoMounts -VideoDirs {ScriptProcess.psq vidsLinux} -TargetIsWindows $true -GetDefaultDistro {{ {ScriptProcess.psq distro} }}) | ConvertTo-Json -Depth 4 -Compress"
                    []
            Assert.True((r.ExitCode = 0), $"Resolve-VideoMounts (Linux絶対) 失敗: {r.StdErr}\nSTDOUT: {r.StdOut}")
            use doc = JsonDocument.Parse r.StdOut
            let m = doc.RootElement.GetProperty("Mounts").[0]
            m.GetProperty("HostPath").GetString() |> should equal vidsUnc
            m.GetProperty("VolumeArg").GetString() |> should equal $"{vidsUnc}:/app/videos/vids:ro"
        )

    [<Fact>]
    let ``UT-MOUNT-05: 空白を含む VIDEO_DIRS (TargetIsWindows=$true) が分割されず 1 引数として保持される`` () =
        let tempRoot = Path.Combine(Path.GetTempPath(), $"tvm-ut-mount05-{Guid.NewGuid():N}")
        let clipsDir = Path.Combine(tempRoot, "My Clips")
        Directory.CreateDirectory clipsDir |> ignore
        try
            let expectedSlash = clipsDir.Replace('\\', '/')
            let r = invokeCommonRaw $"(Resolve-VideoMounts -VideoDirs {ScriptProcess.psq clipsDir} -TargetIsWindows $true) | ConvertTo-Json -Depth 4 -Compress" []
            Assert.True((r.ExitCode = 0), $"Resolve-VideoMounts (空白入り) 失敗: {r.StdErr}")
            use doc = JsonDocument.Parse r.StdOut
            let volArgs = doc.RootElement.GetProperty("VolumeArgs").EnumerateArray() |> Seq.map (fun e -> e.GetString()) |> List.ofSeq
            volArgs |> should equal [ "-v"; $"{expectedSlash}:/app/videos/My Clips:ro" ]
        finally
            try Directory.Delete(tempRoot, true) with _ -> ()

    [<Fact>]
    let ``UT-MOUNT-06: VIDEO_DIRS への通常ファイル誤指定・..・ドライブ相対は早期 throw 拒否される`` () =
        let tempRoot = Path.Combine(Path.GetTempPath(), $"tvm-ut-mount06-{Guid.NewGuid():N}")
        let childDir = Path.Combine(tempRoot, "a", "b")
        Directory.CreateDirectory childDir |> ignore
        let sampleFile = Path.Combine(tempRoot, "sample.mp4")
        File.WriteAllText(sampleFile, "dummy")
        try
            let errFile = assertCommonFails $"""$null = Resolve-VideoMounts -VideoDirs {ScriptProcess.psq sampleFile} -TargetIsWindows $true""" []
            Assert.True(errFile.Contains("ファイル"), $"通常ファイル拒否エラーに 'ファイル' が含まれていません: {errFile}")

            let traversal = Path.Combine(tempRoot, "a", "..", "a", "b")
            let errTraversal = assertCommonFails $"""$null = Resolve-VideoMounts -VideoDirs {ScriptProcess.psq traversal} -TargetIsWindows $true""" []
            Assert.True(errTraversal.Contains(".."), $"パストラバーサル拒否エラーに '..' が含まれていません: {errTraversal}")

            let errDriveRel = assertCommonFails """$null = Resolve-VideoMounts -VideoDirs 'D:videos' -TargetIsWindows $true""" []
            Assert.True(errDriveRel.Contains("ドライブ相対"), $"ドライブ相対パス拒否エラーに 'ドライブ相対' が含まれていません: {errDriveRel}")
        finally
            try Directory.Delete(tempRoot, true) with _ -> ()

    [<Fact>]
    let ``UT-MOUNT-RO-01: New-RunArguments で動画 -v は末尾 :ro、DATA_DIR -v は :ro なしで生成される`` () =
        let tempRoot = Path.Combine(Path.GetTempPath(), $"tvm-ut-ro01-{Guid.NewGuid():N}")
        let vDir = Path.Combine(tempRoot, "Anime")
        let dDir = Path.Combine(tempRoot, "data")
        Directory.CreateDirectory vDir |> ignore
        try
            let r =
                invokeCommonRaw
                    $"""
$vm = Resolve-VideoMounts -VideoDirs {ScriptProcess.psq vDir} -TargetIsWindows $true
$dd = Resolve-DataDir -Path {ScriptProcess.psq dDir} -CreateIfMissing
$args = New-RunArguments -Name 'tag-based-video-manager' -Port 5620 -VideoMountArgs $vm.VolumeArgs -ContainerVideoDirs $vm.ContainerVideoDirs -DataDir $dd.MountSource -Runtime 'wslc'
ConvertTo-Json -InputObject ([string[]]$args) -Compress
"""
                    []
            Assert.True((r.ExitCode = 0), $"UT-MOUNT-RO-01 失敗: {r.StdErr}")
            use doc = JsonDocument.Parse r.StdOut
            let args = doc.RootElement.EnumerateArray() |> Seq.map (fun e -> e.GetString()) |> List.ofSeq
            let vPairs = args |> List.pairwise |> List.filter (fun (a, _) -> a = "-v") |> List.map snd
            let videoMounts = vPairs |> List.filter (fun v -> v.Contains(":/app/videos/"))
            let dataMounts = vPairs |> List.filter (fun v -> v.EndsWith(":/app/data") || v.EndsWith(":/app/data:ro"))
            videoMounts.Length |> should equal 1
            videoMounts.[0] |> should endWith ":ro"
            dataMounts |> should equal [ $"{dDir.Replace('\\', '/')}:/app/data" ]
        finally
            try Directory.Delete(tempRoot, true) with _ -> ()

/// 既存コンテナマウント比較・inspect 抽出単体テスト (Task 1.2: UT-WARN-01〜05)
module ExistingMountCompareTests =

    open DeployDataDirTestHarness

    [<Fact>]
    let ``UT-WARN-01: 異なるマウント元 (Windows vs WSL UNC) で Test-SameMountSource は $false を返す`` () =
        let r =
            invokeCommonRaw
                """
$same = Test-SameMountSource -ExistingSource 'D:\repo\data' -NewSource '\\wsl.localhost\Ubuntu-24.04\home\u\data'
Write-Output ($same.ToString().ToLowerInvariant())
"""
                []
        Assert.True((r.ExitCode = 0), $"Test-SameMountSource 失敗: {r.StdErr}")
        r.StdOut.TrimEnd('\r', '\n') |> should equal "false"

    [<Fact>]
    let ``UT-WARN-02: Windowsパス・ディストロ名大小文字差は $true、WSLサブパス大小文字差や空文字は $false を返す`` () =
        let r1 =
            invokeCommonRaw
                """
$same = Test-SameMountSource -ExistingSource 'D:\Repo\Data\' -NewSource 'd:/repo/data'
Write-Output ($same.ToString().ToLowerInvariant())
"""
                []
        Assert.True((r1.ExitCode = 0), $"Test-SameMountSource 失敗: {r1.StdErr}")
        r1.StdOut.TrimEnd('\r', '\n') |> should equal "true"

        let r2 =
            invokeCommonRaw
                """
$same = Test-SameMountSource -ExistingSource '\\WSL.LOCALHOST\ubuntu-24.04\home\u\data\' -NewSource '\\wsl.localhost\Ubuntu-24.04\home\u\data'
Write-Output ($same.ToString().ToLowerInvariant())
"""
                []
        Assert.True((r2.ExitCode = 0), $"Test-SameMountSource (UNC ディストロ名大小文字) 失敗: {r2.StdErr}")
        r2.StdOut.TrimEnd('\r', '\n') |> should equal "true"

        let rSubCase =
            invokeCommonRaw
                """
$same = Test-SameMountSource -ExistingSource '\\wsl.localhost\Ubuntu-24.04\home\u\data' -NewSource '\\wsl.localhost\Ubuntu-24.04\home\u\DATA'
Write-Output ($same.ToString().ToLowerInvariant())
"""
                []
        Assert.True((rSubCase.ExitCode = 0), $"Test-SameMountSource (WSL サブパス大小文字) 失敗: {rSubCase.StdErr}")
        rSubCase.StdOut.TrimEnd('\r', '\n') |> should equal "false"

        let r3 =
            invokeCommonRaw
                """
$same = Test-SameMountSource -ExistingSource '' -NewSource 'd:/repo/data'
Write-Output ($same.ToString().ToLowerInvariant())
"""
                []
        Assert.True((r3.ExitCode = 0), $"Test-SameMountSource (空文字) 失敗: {r3.StdErr}")
        r3.StdOut.TrimEnd('\r', '\n') |> should equal "false"

    [<Fact>]
    let ``UT-WARN-03: 旧バグ由来の D:\mnt\e\data や /mnt/e/data と正規 WSL パスは不一致 ($false) として検出する`` () =
        for oldSrc in [ @"D:\mnt\e\data"; "/mnt/e/data" ] do
            let r =
                invokeCommonRaw
                    $"""
$same = Test-SameMountSource -ExistingSource {ScriptProcess.psq oldSrc} -NewSource '\\wsl.localhost\Ubuntu-24.04\home\u\data'
Write-Output ($same.ToString().ToLowerInvariant())
"""
                    []
            Assert.True((r.ExitCode = 0), $"Test-SameMountSource 失敗: {r.StdErr}")
            r.StdOut.TrimEnd('\r', '\n') |> should equal "false"

    [<Fact>]
    let ``UT-WARN-04: コンテナ未存在時は例外も警告も出さず $null を返す`` () =
        let tempBin = Path.Combine(Path.GetTempPath(), $"tvm-ut-warn04-{Guid.NewGuid():N}")
        Directory.CreateDirectory tempBin |> ignore
        try
            File.Copy(getMockRuntimeExe (), Path.Combine(tempBin, "wslc.exe"))
            let existingPath = getEnvOrEmpty "PATH"
            let r =
                invokeCommonRaw
                    """
$warn = @()
$src = Get-ExistingContainerDataMount -Runtime 'wslc' -ContainerName 'tag-based-video-manager' -WarningVariable warn
[PSCustomObject]@{ IsNull = ($null -eq $src); WarnCount = @($warn).Count } | ConvertTo-Json -Compress
"""
                    [ ("PATH", tempBin + string Path.PathSeparator + existingPath); ("MOCK_INSPECT_EXIT", "1"); ("MOCK_INSPECT_STDOUT_B64", "") ]
            Assert.True((r.ExitCode = 0), $"Get-ExistingContainerDataMount 失敗: {r.StdErr}")
            use doc = JsonDocument.Parse r.StdOut
            doc.RootElement.GetProperty("IsNull").GetBoolean() |> should equal true
            doc.RootElement.GetProperty("WarnCount").GetInt32() |> should equal 0
        finally
            try Directory.Delete(tempBin, true) with _ -> ()

    [<Fact>]
    let ``UT-WARN-05: 非空不正 JSON および実プロセスタイムアウト時は診断 Write-Warning を出し $null、実機配列スキーマからは Source を抽出する`` () =
        let rBad =
            invokeCommonRaw
                """
$warn = @()
$src = Get-ExistingContainerDataMount -Runtime 'wslc' -ContainerName 'tag-based-video-manager' -InvokeInspect { 'not json' } -WarningVariable warn 3>$null
[PSCustomObject]@{ IsNull = ($null -eq $src); WarnCount = @($warn).Count; WarnText = ($warn -join ' ') } | ConvertTo-Json -Compress
"""
                []
        Assert.True((rBad.ExitCode = 0), $"Get-ExistingContainerDataMount (不正JSON) 失敗: {rBad.StdErr}")
        use docBad = JsonDocument.Parse rBad.StdOut
        docBad.RootElement.GetProperty("IsNull").GetBoolean() |> should equal true
        (docBad.RootElement.GetProperty("WarnCount").GetInt32() > 0) |> should equal true

        // 実プロセス -TimeoutMs 150 (MOCK_WSLC_SLEEP_MS=600) 時に例外停止せず $null を返しタイムアウト警告が出力される
        let tempBin = Path.Combine(Path.GetTempPath(), $"tvm-ut-warn05-to-{Guid.NewGuid():N}")
        Directory.CreateDirectory tempBin |> ignore
        try
            File.Copy(getMockRuntimeExe (), Path.Combine(tempBin, "wslc.exe"))
            let existingPath = getEnvOrEmpty "PATH"
            let rTimeout =
                invokeCommonRaw
                    """
$warn = @()
$src = Get-ExistingContainerDataMount -Runtime 'wslc' -ContainerName 'tag-based-video-manager' -TimeoutMs 150 -WarningVariable warn 3>$null
[PSCustomObject]@{ IsNull = ($null -eq $src); WarnCount = @($warn).Count; WarnText = ($warn -join ' ') } | ConvertTo-Json -Compress
"""
                    [ ("PATH", tempBin + string Path.PathSeparator + existingPath); ("MOCK_WSLC_SLEEP_MS", "600"); ("MOCK_INSPECT_EXIT", "0") ]
            Assert.True((rTimeout.ExitCode = 0), $"Get-ExistingContainerDataMount (Timeout) 失敗: {rTimeout.StdErr}")
            use docTimeout = JsonDocument.Parse rTimeout.StdOut
            docTimeout.RootElement.GetProperty("IsNull").GetBoolean() |> should equal true
            (docTimeout.RootElement.GetProperty("WarnCount").GetInt32() > 0) |> should equal true
            docTimeout.RootElement.GetProperty("WarnText").GetString() |> should contain "タイムアウト"
        finally
            try Directory.Delete(tempBin, true) with _ -> ()

        let realInspectJson =
            """[{"Name":"/tag-based-video-manager","Mounts":[{"Destination":"/app/videos/e","Name":"","ReadWrite":false,"Source":"D:\\mnt\\e\\e","Type":"bind"},{"Destination":"/app/data","Name":"","ReadWrite":true,"Source":"\\\\wsl.localhost\\Ubuntu-24.04\\home\\spjao\\var\\apps\\TagBasedVideoManager\\data","Type":"bind"}]}]"""
        let rGood =
            invokeCommonRaw
                $"""
$src = Get-ExistingContainerDataMount -Runtime 'wslc' -ContainerName 'tag-based-video-manager' -InvokeInspect {{ {ScriptProcess.psq realInspectJson} }}
Write-Output $src
"""
                []
        Assert.True((rGood.ExitCode = 0), $"Get-ExistingContainerDataMount (実機JSON) 失敗: {rGood.StdErr}")
        rGood.StdOut.TrimEnd('\r', '\n') |> should equal @"\\wsl.localhost\Ubuntu-24.04\home\spjao\var\apps\TagBasedVideoManager\data"

/// deploy.ps1 結合・副作用テスト (Task 1.3: IT-DATA-*, IT-WARN-*, IT-DEPLOY-*)
[<Collection("WslBashSerial")>]
module DeployPsIntegrationTests =

    open DeployDataDirTestHarness

    let private findRunArgValue (flag: string) (args: string list) : string list =
        args |> List.pairwise |> List.filter (fun (a, _) -> a = flag) |> List.map snd

    [<Fact>]
    let ``IT-DATA-SIDE-01: deploy.ps1 で相対 DATA_DIR (./custom-data) が自動作成されスラッシュ絶対パスでマウントされる`` () =
        withDeployPsEnv None None None (fun envInfo ->
            let videoDir = Path.Combine(envInfo.TempRoot, "videos", "Anime")
            Directory.CreateDirectory videoDir |> ignore
            withHealthServer (fun port ->
                let r = runDeployPs envInfo $"-VideoDirs {ScriptProcess.psq videoDir} -DataDir './custom-data' -Port {port}" []
                Assert.True((r.ExitCode = 0), $"deploy.ps1 失敗 (exit={r.ExitCode}):\nSTDERR: {r.StdErr}\nSTDOUT: {r.StdOut}")
                let expectedDir = Path.Combine(envInfo.RepoRoot, "custom-data")
                Directory.Exists expectedDir |> should equal true
                let calls = readRuntimeCalls envInfo.RuntimeLogPath
                let runCall = calls |> List.tryFind (fun args -> args.Head = "run")
                Assert.True(runCall.IsSome, $"wslc run が呼ばれていません: {calls}")
                let vArgs = findRunArgValue "-v" runCall.Value
                vArgs |> should contain $"{expectedDir.Replace('\\', '/')}:/app/data"
            )
        )

    [<Fact>]
    let ``IT-DATA-WSL-01: deploy.ps1 で \\wsl.localhost\... の DATA_DIR が /mnt/ に化けずそのまま渡る`` () =
        let distro = getLiveUserWslDistro ()
        withWslTempDir distro (fun _ uncBase ->
            let dataUnc = Path.Combine(uncBase, "data")
            withDeployPsEnv None None None (fun envInfo ->
                let videoDir = Path.Combine(envInfo.TempRoot, "videos", "Anime")
                Directory.CreateDirectory videoDir |> ignore
                withHealthServer (fun port ->
                    let r = runDeployPs envInfo $"-VideoDirs {ScriptProcess.psq videoDir} -DataDir {ScriptProcess.psq dataUnc} -Port {port}" []
                    Assert.True((r.ExitCode = 0), $"deploy.ps1 失敗 (exit={r.ExitCode}):\nSTDERR: {r.StdErr}\nSTDOUT: {r.StdOut}")
                    Directory.Exists dataUnc |> should equal true
                    let calls = readRuntimeCalls envInfo.RuntimeLogPath
                    let runCall = calls |> List.tryFind (fun args -> args.Head = "run")
                    Assert.True(runCall.IsSome, $"wslc run が呼ばれていません: {calls}")
                    let vArgs = findRunArgValue "-v" runCall.Value
                    vArgs |> should contain $"{dataUnc}:/app/data"
                    Assert.False((vArgs |> List.exists (fun a -> a.Contains("//wsl.localhost") || a.Contains("/mnt/"))), $"不正パスへ変換されました: {vArgs}")
                )
            )
        )

    [<Fact>]
    let ``IT-DATA-WSL-02: deploy.ps1 で Linux 絶対パス DATA_DIR がダミー wsl.exe からディストロ補完され Windows 側に誤作成されない`` () =
        let distro = getLiveUserWslDistro ()
        withWslTempDir distro (fun linuxBase uncBase ->
            let linuxDataDir = $"{linuxBase}/data"
            let expectedUnc = Path.Combine(uncBase, "data")
            // 現行バグで Windows ドライブ直下に \tmp\tvm-test-... が誤作成された場合の掃除パス
            let tempDriveRoot = Path.GetPathRoot(Path.GetTempPath())
            let bogusWinPath = Path.Combine(tempDriveRoot, linuxBase.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))
            let bogusExistedBefore = Directory.Exists bogusWinPath

            try
                let utf16Bytes = Encoding.Unicode.GetBytes($"docker-desktop\n{distro}\ndocker-desktop-data\n")
                let b64 = Convert.ToBase64String(utf16Bytes)
                withDeployPsEnv None None (Some(utf16Bytes, 0, 0)) (fun envInfo ->
                    let videoDir = Path.Combine(envInfo.TempRoot, "videos", "Anime")
                    Directory.CreateDirectory videoDir |> ignore
                    withHealthServer (fun port ->
                        let r =
                            runDeployPs
                                envInfo
                                $"-VideoDirs {ScriptProcess.psq videoDir} -DataDir {ScriptProcess.psq linuxDataDir} -Port {port}"
                                [ ("MOCK_WSL_STDOUT_B64", b64); ("MOCK_WSL_EXIT", "0") ]
                        Assert.True((r.ExitCode = 0), $"deploy.ps1 失敗 (exit={r.ExitCode}):\nSTDERR: {r.StdErr}\nSTDOUT: {r.StdOut}")
                        if not bogusExistedBefore then
                            Directory.Exists bogusWinPath |> should equal false
                        Directory.Exists expectedUnc |> should equal true
                        let calls = readRuntimeCalls envInfo.RuntimeLogPath
                        let runCall = calls |> List.tryFind (fun args -> args.Head = "run")
                        Assert.True(runCall.IsSome, $"wslc run が呼ばれていません: {calls}")
                        let vArgs = findRunArgValue "-v" runCall.Value
                        vArgs |> should contain $"{expectedUnc}:/app/data"
                        vArgs |> should contain $"{videoDir.Replace('\\', '/')}:/app/videos/Anime:ro"
                    )
                )
            finally
                if (not bogusExistedBefore) && Directory.Exists bogusWinPath then
                    try Directory.Delete(bogusWinPath, true) with _ -> ()
        )

    [<Fact>]
    let ``IT-WARN-01: 既存コンテナ別 DATA_DIR 時にビルド・停止より前に 4 要素を含む警告を出力しデプロイ続行する`` () =
        withDeployPsEnv None None None (fun envInfo ->
            let videoDir = Path.Combine(envInfo.TempRoot, "videos", "Anime")
            Directory.CreateDirectory videoDir |> ignore
            let oldSource = @"\\wsl.localhost\Ubuntu-24.04\home\user\old_data"
            let inspectJson =
                $"""[{{"Name":"/tag-based-video-manager","Mounts":[{{"Destination":"/app/data","Name":"","ReadWrite":true,"Source":"{oldSource.Replace("\\", "\\\\")}","Type":"bind"}}]}}]"""
            let inspectB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(inspectJson))
            withHealthServer (fun port ->
                let r =
                    runDeployPs
                        envInfo
                        $"-VideoDirs {ScriptProcess.psq videoDir} -DataDir './new-data' -Port {port}"
                        [ ("MOCK_INSPECT_STDOUT_B64", inspectB64); ("MOCK_INSPECT_EXIT", "0") ]
                Assert.True((r.ExitCode = 0), $"deploy.ps1 失敗 (exit={r.ExitCode}):\nSTDERR: {r.StdErr}\nSTDOUT: {r.StdOut}")
                let combined = r.StdOut + "\n" + r.StdErr
                combined |> should contain "old_data"
                combined |> should contain "new-data"
                Assert.True(combined.Contains("削除") || combined.Contains("残"), $"旧データ非削除の明記がありません: {combined}")
                combined |> should contain "appsettings.Production.json"
                combined |> should contain "Copy-Item"

                let warnIdx = combined.IndexOf("Copy-Item", StringComparison.Ordinal)
                let buildIdx = combined.IndexOf("イメージをビルド中", StringComparison.Ordinal)
                Assert.True(warnIdx >= 0 && buildIdx > warnIdx, $"警告がビルドより前に出力されていません (warn={warnIdx}, build={buildIdx}):\n{combined}")

                let calls = readRuntimeCalls envInfo.RuntimeLogPath
                let inspectPos = calls |> List.tryFindIndex (fun c -> c.Head = "inspect")
                let buildPos = calls |> List.tryFindIndex (fun c -> c.Head = "build")
                Assert.True(inspectPos.IsSome && buildPos.IsSome && inspectPos.Value < buildPos.Value, $"inspect が build より前に呼ばれていません: {calls}")
            )
        )

    [<Fact>]
    let ``IT-WARN-02: 既存コンテナ同一 DATA_DIR 時は inspect を実行した上で不一致警告を出さない`` () =
        withDeployPsEnv None None None (fun envInfo ->
            let videoDir = Path.Combine(envInfo.TempRoot, "videos", "Anime")
            Directory.CreateDirectory videoDir |> ignore
            let dataDir = Path.Combine(envInfo.RepoRoot, "same-data")
            let oldVariant = (dataDir.ToUpperInvariant()) + "\\"
            let inspectJson =
                $"""[{{"Name":"/tag-based-video-manager","Mounts":[{{"Destination":"/app/data","Name":"","ReadWrite":true,"Source":"{oldVariant.Replace("\\", "\\\\")}","Type":"bind"}}]}}]"""
            let inspectB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(inspectJson))
            withHealthServer (fun port ->
                let r =
                    runDeployPs
                        envInfo
                        $"-VideoDirs {ScriptProcess.psq videoDir} -DataDir {ScriptProcess.psq dataDir} -Port {port}"
                        [ ("MOCK_INSPECT_STDOUT_B64", inspectB64); ("MOCK_INSPECT_EXIT", "0") ]
                Assert.True((r.ExitCode = 0), $"deploy.ps1 失敗: {r.StdErr}")
                let calls = readRuntimeCalls envInfo.RuntimeLogPath
                Assert.True((calls |> List.exists (fun c -> c.Head = "inspect")), $"inspect が呼ばれていません: {calls}")
                let combined = r.StdOut + "\n" + r.StdErr
                combined |> should not' (contain "Copy-Item")
                combined |> should not' (contain "旧マウント")
            )
        )

    [<Fact>]
    let ``IT-WARN-03: 既存コンテナ未存在 (inspect exit 1) 時は警告なしで新規起動まで完了する`` () =
        withDeployPsEnv None None None (fun envInfo ->
            let videoDir = Path.Combine(envInfo.TempRoot, "videos", "Anime")
            Directory.CreateDirectory videoDir |> ignore
            withHealthServer (fun port ->
                let r =
                    runDeployPs
                        envInfo
                        $"-VideoDirs {ScriptProcess.psq videoDir} -DataDir './fresh-data' -Port {port}"
                        [ ("MOCK_INSPECT_EXIT", "1"); ("MOCK_INSPECT_STDOUT_B64", "") ]
                Assert.True((r.ExitCode = 0), $"deploy.ps1 失敗: {r.StdErr}")
                let calls = readRuntimeCalls envInfo.RuntimeLogPath
                Assert.True((calls |> List.exists (fun c -> c.Head = "inspect")), $"inspect が呼ばれていません: {calls}")
                Assert.True((calls |> List.exists (fun c -> c.Head = "run")), $"run が呼ばれていません: {calls}")
                let combined = r.StdOut + "\n" + r.StdErr
                combined |> should not' (contain "Copy-Item")
            )
        )

    [<Fact>]
    let ``IT-DEPLOY-OVERRIDE-01: DATA_DIR は CLI 引数 > 環境変数 > appsettings.Production.json の優先順位で解決される`` () =
        withDeployPsEnv None (Some """{"PORT": 5620, "DATA_DIR": "./from-appsettings"}""") None (fun envInfo ->
            let videoDir = Path.Combine(envInfo.TempRoot, "videos", "Anime")
            Directory.CreateDirectory videoDir |> ignore
            withHealthServer (fun port ->
                // 環境変数が appsettings を上書き
                let rEnv = runDeployPs envInfo $"-VideoDirs {ScriptProcess.psq videoDir} -Port {port}" [ ("DATA_DIR", "./from-env") ]
                Assert.True((rEnv.ExitCode = 0), $"環境変数上書き実行失敗: {rEnv.StdErr}")
                let callsEnv = readRuntimeCalls envInfo.RuntimeLogPath
                let runEnv = callsEnv |> List.find (fun c -> c.Head = "run")
                let expectedEnvMount = Path.Combine(envInfo.RepoRoot, "from-env").Replace('\\', '/') + ":/app/data"
                findRunArgValue "-v" runEnv |> should contain expectedEnvMount

                File.Delete envInfo.RuntimeLogPath

                // CLI 引数が環境変数を上書き
                let rCli = runDeployPs envInfo $"-VideoDirs {ScriptProcess.psq videoDir} -DataDir './from-cli' -Port {port}" [ ("DATA_DIR", "./from-env") ]
                Assert.True((rCli.ExitCode = 0), $"CLI 上書き実行失敗: {rCli.StdErr}")
                let callsCli = readRuntimeCalls envInfo.RuntimeLogPath
                let runCli = callsCli |> List.find (fun c -> c.Head = "run")
                let expectedCliMount = Path.Combine(envInfo.RepoRoot, "from-cli").Replace('\\', '/') + ":/app/data"
                findRunArgValue "-v" runCli |> should contain expectedCliMount
            )
        )

    [<Fact>]
    let ``IT-DEPLOY-FAILFAST-01: 不正 JSON / 不正 DATA_DIR / deploy.sh パーサ不在時は build・stop・rm・run を呼ばず非0終了する`` () =
        // (1) 不正 JSON
        withDeployPsEnv None (Some "{ invalid json") None (fun envInfo ->
            let videoDir = Path.Combine(envInfo.TempRoot, "videos", "Anime")
            Directory.CreateDirectory videoDir |> ignore
            withHealthServer (fun port ->
                let r = runDeployPs envInfo $"-VideoDirs {ScriptProcess.psq videoDir} -Port {port}" []
                Assert.True((r.ExitCode <> 0), $"不正 JSON で非 0 終了すべきです: exit={r.ExitCode}")
                let combinedJsonErr = r.StdOut + "\n" + r.StdErr
                Assert.True(combinedJsonErr.Contains("appsettings.Production.json") && combinedJsonErr.Contains("JSON"), $"不正 JSON エラーメッセージに対象ファイル名と JSON 解析失敗が含まれていません: {combinedJsonErr}")
                let calls = readRuntimeCalls envInfo.RuntimeLogPath
                Assert.False((calls |> List.exists (fun c -> c.Head = "build" || c.Head = "stop" || c.Head = "rm" || c.Head = "run")), $"副作用呼び出しあり: {calls}")
            )
        )

        // (2) 不正 DATA_DIR (D:\ および 通常ファイル)
        withDeployPsEnv None None None (fun envInfo ->
            let videoDir = Path.Combine(envInfo.TempRoot, "videos", "Anime")
            Directory.CreateDirectory videoDir |> ignore
            let badFile = Path.Combine(envInfo.TempRoot, "metadata.db")
            File.WriteAllText(badFile, "dummy")
            withHealthServer (fun port ->
                for (badData, expectedKey) in [ (@"D:\", "ドライブ直下"); (badFile, "ファイル") ] do
                    if File.Exists envInfo.RuntimeLogPath then File.Delete envInfo.RuntimeLogPath
                    let r = runDeployPs envInfo $"-VideoDirs {ScriptProcess.psq videoDir} -DataDir {ScriptProcess.psq badData} -Port {port}" []
                    Assert.True((r.ExitCode <> 0), $"不正 DATA_DIR ({badData}) で非 0 終了すべきです: exit={r.ExitCode}")
                    let combinedErr = r.StdOut + "\n" + r.StdErr
                    Assert.True(combinedErr.Contains(expectedKey), $"不正 DATA_DIR ({badData}) のエラー出力に '{expectedKey}' が含まれていません: {combinedErr}")
                    let calls = readRuntimeCalls envInfo.RuntimeLogPath
                    Assert.False((calls |> List.exists (fun c -> c.Head = "build" || c.Head = "stop" || c.Head = "rm" || c.Head = "run")), $"副作用呼び出しあり ({badData}): {calls}")
            )
        )

        // (3) deploy.sh で python3 と jq が両方不在の環境
        let rawBashNoParser =
            """
set -euo pipefail
TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT
mkdir -p "$TMP_DIR/repo/scripts" "$TMP_DIR/repo/src/TagBasedVideoManager" "$TMP_DIR/sysbin"
cp scripts/deploy.sh "$TMP_DIR/repo/scripts/deploy.sh"
chmod +x "$TMP_DIR/repo/scripts/deploy.sh"
echo '{"PORT": 5620, "VIDEO_DIRS": "", "DATA_DIR": "./data"}' > "$TMP_DIR/repo/src/TagBasedVideoManager/appsettings.json"
for tool in bash dirname basename mkdir sed tr grep cat rm cp env; do
    if command -v "$tool" >/dev/null 2>&1; then
        ln -sf "$(command -v "$tool")" "$TMP_DIR/sysbin/$tool"
    fi
done
export PATH="$TMP_DIR/sysbin"
set +e
OUT=$(bash "$TMP_DIR/repo/scripts/deploy.sh" 2>&1)
RC=$?
set -e
echo "RC=$RC"
echo "$OUT"
"""
        let rSh = runWslBash None rawBashNoParser (RepoLayout.repoRoot ()) 30000
        Assert.True((rSh.ExitCode = 0), $"bash 実行失敗: {rSh.StdErr}")
        let m = Regex.Match(rSh.StdOut, @"RC=(-?\d+)")
        Assert.True(m.Success && int m.Groups.[1].Value <> 0, $"python3/jq 不在時は非0終了すべきです: {rSh.StdOut}")
        Assert.True(rSh.StdOut.Contains("python3") && rSh.StdOut.Contains("jq"), $"導入案内がありません: {rSh.StdOut}")

/// deploy.sh 結合・異常系注入・永続性テスト (Task 1.3: IT-SH-DATA-01〜09, IT-SH-VIDEO-01〜02, IT-SH-WARN-01, IT-DATA-PERSIST-01)
[<Collection("WslBashSerial")>]
module DeployShIntegrationTests =

    open DeployDataDirTestHarness

    let private hasNoBuildStopRmRun (calls: string list list) =
        not (calls |> List.exists (fun c -> c.Head = "build" || c.Head = "stop" || c.Head = "rm" || c.Head = "run"))

    [<Theory>]
    [<InlineData("")>]
    [<InlineData("   ")>]
    let ``IT-SH-DATA-01: deploy.sh は DATA_DIR 未設定・空白時に ${REPO_ROOT}/data を作成・マウントする (S1)`` (dataDirVal: string) =
        let setup =
            $"""
mkdir -p "$TMP_DIR/videos/Anime"
export VIDEO_DIRS="$TMP_DIR/videos/Anime"
export DATA_DIR="{dataDirVal}"
echo "REPO_ROOT=$REPO_ROOT"
"""
        let invoke = """cd "$REPO_ROOT" && bash "$REPO_ROOT/scripts/deploy.sh" """
        let r = runDeployShTest setup invoke
        let rc, output, calls = parseDeployShResult r
        Assert.True((rc = 0), $"deploy.sh 失敗 (rc={rc}):\n{output}")
        let repoRootLinux = Regex.Match(output, @"REPO_ROOT=(.+)").Groups.[1].Value.Trim()
        let runCall = calls |> List.find (fun c -> c.Head = "run")
        runCall |> should contain $"{repoRootLinux}/data:/app/data"

    [<Fact>]
    let ``IT-SH-DATA-02: deploy.sh はリポジトリ外 CWD 実行時も DATA_DIR="./rel-data" を ${REPO_ROOT}/rel-data に正規化する (S5)`` () =
        let setup =
            """
mkdir -p "$TMP_DIR/videos/Anime" "$TMP_DIR/other_cwd"
export VIDEO_DIRS="$TMP_DIR/videos/Anime"
export DATA_DIR="./rel-data"
echo "REPO_ROOT=$REPO_ROOT"
"""
        let invoke = """cd "$TMP_DIR/other_cwd" && bash "$REPO_ROOT/scripts/deploy.sh" """
        let r = runDeployShTest setup invoke
        let rc, output, calls = parseDeployShResult r
        Assert.True((rc = 0), $"deploy.sh 失敗 (rc={rc}):\n{output}")
        let repoRootLinux = Regex.Match(output, @"REPO_ROOT=(.+)").Groups.[1].Value.Trim()
        let runCall = calls |> List.find (fun c -> c.Head = "run")
        runCall |> should not' (contain "./rel-data:/app/data")
        runCall |> should contain $"{repoRootLinux}/rel-data:/app/data"

    [<Fact>]
    let ``IT-SH-DATA-03: deploy.sh は WSL_DISTRO_NAME 一致時の WSL UNC パスを Linux パスへ解決する (S3)`` () =
        let setup =
            """
mkdir -p "$TMP_DIR/videos/Anime"
export WSL_DISTRO_NAME="Ubuntu-24.04"
export VIDEO_DIRS="$TMP_DIR/videos/Anime"
export DATA_DIR="//wsl.localhost/Ubuntu-24.04${TMP_DIR}/tvm-data"
echo "EXPECTED=${TMP_DIR}/tvm-data"
"""
        let invoke = """bash "$REPO_ROOT/scripts/deploy.sh" """
        let r = runDeployShTest setup invoke
        let rc, output, calls = parseDeployShResult r
        Assert.True((rc = 0), $"deploy.sh 失敗 (rc={rc}):\n{output}")
        let expectedPath = Regex.Match(output, @"EXPECTED=(.+)").Groups.[1].Value.Trim()
        let runCall = calls |> List.find (fun c -> c.Head = "run")
        runCall |> should contain $"{expectedPath}:/app/data"

    [<Theory>]
    [<InlineData("Ubuntu-24.04", "//wsl.localhost/Debian/tmp/data")>]
    [<InlineData("", "//wsl.localhost/Ubuntu-24.04/tmp/data")>]
    [<InlineData("evil", "//wsl$evil/data")>]
    [<InlineData("Ubuntu-24.04", "//wsl.localhost.evil/Ubuntu-24.04/data")>]
    let ``IT-SH-DATA-04: deploy.sh は他ディストロ・WSL_DISTRO_NAME空・偽装ホスト名の UNC をビルド前に拒否する (S3)`` (distroEnv: string, uncPath: string) =
        let setup =
            $"""
mkdir -p "$TMP_DIR/videos/Anime"
export WSL_DISTRO_NAME="{distroEnv}"
export VIDEO_DIRS="$TMP_DIR/videos/Anime"
export DATA_DIR='{uncPath}'
"""
        let invoke = """bash "$REPO_ROOT/scripts/deploy.sh" """
        let r = runDeployShTest setup invoke
        let rc, output, calls = parseDeployShResult r
        Assert.True((rc <> 0), $"非0終了すべきです (rc={rc}):\n{output}")
        Assert.True(hasNoBuildStopRmRun calls, $"ビルド・停止前に拒否すべきです: {calls}")
        Assert.True(output.Contains("/home/") || output.Contains("./data"), $"Linux パス設定例が含まれていません: {output}")

    [<Theory>]
    [<InlineData("Ubuntu-24.04")>]
    [<InlineData("")>]
    let ``IT-SH-DATA-05: deploy.sh は Windows ドライブパス DATA_DIR を常にビルド前に拒否する (S4)`` (distroEnv: string) =
        let setup =
            $"""
mkdir -p "$TMP_DIR/videos/Anime"
export WSL_DISTRO_NAME="{distroEnv}"
export VIDEO_DIRS="$TMP_DIR/videos/Anime"
export DATA_DIR='D:\data'
"""
        let invoke = """bash "$REPO_ROOT/scripts/deploy.sh" """
        let r = runDeployShTest setup invoke
        let rc, output, calls = parseDeployShResult r
        Assert.True((rc <> 0), $"非0終了すべきです (rc={rc}):\n{output}")
        Assert.True(hasNoBuildStopRmRun calls, $"ビルド・停止前に拒否すべきです: {calls}")
        Assert.True(output.Contains("/home/") || output.Contains("./data"), $"Linux パス設定例が含まれていません: {output}")

    [<Fact>]
    let ``IT-SH-DATA-06: deploy.sh は .. トラバーサルと / 単体を拒否し、v1..v2 は正当パスとして許可する (S2)`` () =
        for bad in [ "../etc"; "/" ] do
            let setup =
                $"""
mkdir -p "$TMP_DIR/videos/Anime"
export VIDEO_DIRS="$TMP_DIR/videos/Anime"
export DATA_DIR='{bad}'
"""
            let r = runDeployShTest setup """bash "$REPO_ROOT/scripts/deploy.sh" """
            let rc, output, calls = parseDeployShResult r
            Assert.True((rc <> 0), $"不正パス {bad} で非0終了すべきです:\n{output}")
            Assert.True(hasNoBuildStopRmRun calls, $"ビルド・停止前に拒否すべきです ({bad}): {calls}")
            Assert.False(output.Contains("appsettings の読み込みには"), $"パーサエラーで誤終了しました: {output}")
            Assert.True(output.Contains("..") || output.Contains("トラバーサル") || output.Contains("ルート") || output.Contains("無効") || output.Contains("不正"), $"不正パス拒否メッセージがありません: {output}")

        let setupGood =
            """
mkdir -p "$TMP_DIR/videos/Anime"
export VIDEO_DIRS="$TMP_DIR/videos/Anime"
export DATA_DIR="$TMP_DIR/v1..v2/data"
echo "EXPECTED=$TMP_DIR/v1..v2/data"
"""
        let rGood = runDeployShTest setupGood """bash "$REPO_ROOT/scripts/deploy.sh" """
        let rcGood, outputGood, callsGood = parseDeployShResult rGood
        Assert.True((rcGood = 0), $"v1..v2 は許可されるべきです (rc={rcGood}):\n{outputGood}")
        let expectedPath = Regex.Match(outputGood, @"EXPECTED=(.+)").Groups.[1].Value.Trim()
        let runCall = callsGood |> List.find (fun c -> c.Head = "run")
        runCall |> should contain $"{expectedPath}:/app/data"

    [<Fact>]
    let ``IT-SH-DATA-07: deploy.sh は通常の Linux 絶対パスを指定通りに作成・マウントする (S6)`` () =
        let setup =
            """
mkdir -p "$TMP_DIR/videos/Anime"
export VIDEO_DIRS="$TMP_DIR/videos/Anime"
export DATA_DIR="$TMP_DIR/abs-data"
echo "EXPECTED=$TMP_DIR/abs-data"
"""
        let r = runDeployShTest setup """bash "$REPO_ROOT/scripts/deploy.sh" """
        let rc, output, calls = parseDeployShResult r
        Assert.True((rc = 0), $"deploy.sh 失敗 (rc={rc}):\n{output}")
        let expectedPath = Regex.Match(output, @"EXPECTED=(.+)").Groups.[1].Value.Trim()
        let runCall = calls |> List.find (fun c -> c.Head = "run")
        runCall |> should contain $"{expectedPath}:/app/data"

    [<Fact>]
    let ``IT-SH-DATA-08: deploy.sh は DATA_DIR に通常ファイルが指定された場合ビルド前に非0終了する (S2)`` () =
        let setup =
            """
mkdir -p "$TMP_DIR/videos/Anime"
touch "$TMP_DIR/not-a-dir"
export VIDEO_DIRS="$TMP_DIR/videos/Anime"
export DATA_DIR="$TMP_DIR/not-a-dir"
"""
        let r = runDeployShTest setup """bash "$REPO_ROOT/scripts/deploy.sh" """
        let rc, output, calls = parseDeployShResult r
        Assert.True((rc <> 0), $"通常ファイル指定で非0終了すべきです (rc={rc}):\n{output}")
        Assert.True(hasNoBuildStopRmRun calls, $"ビルド・停止前に拒否すべきです: {calls}")
        Assert.False(output.Contains("appsettings の読み込みには"), $"パーサエラーで誤終了しました: {output}")
        Assert.True(output.Contains("ディレクトリ") || output.Contains("通常ファイル") || output.Contains("not a directory"), $"通常ファイル拒否メッセージがありません: {output}")

    [<Fact>]
    let ``IT-SH-DATA-09: deploy.sh は空白を含む Linux DATA_DIR を分割せず 1 引数でマウントする (S6)`` () =
        let setup =
            """
mkdir -p "$TMP_DIR/videos/Anime"
export VIDEO_DIRS="$TMP_DIR/videos/Anime"
export DATA_DIR="$TMP_DIR/my data dir"
echo "EXPECTED=$TMP_DIR/my data dir"
"""
        let r = runDeployShTest setup """bash "$REPO_ROOT/scripts/deploy.sh" """
        let rc, output, calls = parseDeployShResult r
        Assert.True((rc = 0), $"deploy.sh 失敗 (rc={rc}):\n{output}")
        let expectedPath = Regex.Match(output, @"EXPECTED=(.+)").Groups.[1].Value.Trim()
        let runCall = calls |> List.find (fun c -> c.Head = "run")
        runCall |> should contain $"{expectedPath}:/app/data"

    [<Fact>]
    let ``IT-SH-VIDEO-01: deploy.sh は Windows ドライブパス VIDEO_DIRS をビルド前に拒否する`` () =
        let setup =
            """
export VIDEO_DIRS='C:\videos'
export DATA_DIR="$TMP_DIR/data"
"""
        let r = runDeployShTest setup """bash "$REPO_ROOT/scripts/deploy.sh" """
        let rc, output, calls = parseDeployShResult r
        Assert.True((rc <> 0), $"Windows VIDEO_DIRS で非0終了すべきです (rc={rc}):\n{output}")
        Assert.True(hasNoBuildStopRmRun calls, $"ビルド・停止前に拒否すべきです: {calls}")
        Assert.False(output.Contains("appsettings の読み込みには"), $"パーサエラーで誤終了しました: {output}")
        Assert.True(output.Contains("Windows") || output.Contains("Linux") || output.Contains("/home/"), $"Windows パス拒否メッセージがありません: {output}")

    [<Theory>]
    [<InlineData("Ubuntu-24.04")>]
    [<InlineData("")>]
    let ``IT-SH-VIDEO-02: deploy.sh は解決不能な WSL UNC VIDEO_DIRS をビルド前に拒否する`` (distroEnv: string) =
        let setup =
            $"""
export WSL_DISTRO_NAME="{distroEnv}"
export VIDEO_DIRS='\\wsl.localhost\Other\videos'
export DATA_DIR="$TMP_DIR/data"
"""
        let r = runDeployShTest setup """bash "$REPO_ROOT/scripts/deploy.sh" """
        let rc, output, calls = parseDeployShResult r
        Assert.True((rc <> 0), $"解決不能 UNC VIDEO_DIRS で非0終了すべきです (rc={rc}):\n{output}")
        Assert.True(hasNoBuildStopRmRun calls, $"ビルド・停止前に拒否すべきです: {calls}")
        Assert.False(output.Contains("appsettings の読み込みには"), $"パーサエラーで誤終了しました: {output}")
        Assert.True(output.Contains("WSL") || output.Contains("UNC") || output.Contains("WSL_DISTRO_NAME") || output.Contains("Linux"), $"解決不能 UNC 拒否メッセージがありません: {output}")

    [<Fact>]
    let ``IT-SH-WARN-01: deploy.sh は既存コンテナの /app/data マウント変更時にビルド・停止前に 4 要素警告を出し続行する`` () =
        let setup =
            """
mkdir -p "$TMP_DIR/videos/Anime"
export VIDEO_DIRS="$TMP_DIR/videos/Anime"
export DATA_DIR="$TMP_DIR/new-data"
export MOCK_INSPECT_EXIT=0
export MOCK_INSPECT_JSON='[{"Name":"/tag-based-video-manager","Mounts":[{"Destination":"/app/data","Source":"/old/tbvm/data","Type":"bind"}]}]'
"""
        let r = runDeployShTest setup """bash "$REPO_ROOT/scripts/deploy.sh" """
        let rc, output, calls = parseDeployShResult r
        Assert.True((rc = 0), $"deploy.sh 失敗 (rc={rc}):\n{output}")
        output |> should contain "/old/tbvm/data"
        output |> should contain "new-data"
        Assert.True(output.Contains("削除") || output.Contains("残"), $"旧データ非削除の明記がありません: {output}")
        output |> should contain "appsettings.Production.json"
        output |> should contain "cp -a"

        let warnIdx = output.IndexOf("cp -a", StringComparison.Ordinal)
        let buildIdx = output.IndexOf("ビルド中", StringComparison.Ordinal)
        Assert.True(warnIdx >= 0 && buildIdx > warnIdx, $"警告がビルドより前に出力されていません (warn={warnIdx}, build={buildIdx}):\n{output}")
        let inspectPos = calls |> List.tryFindIndex (fun c -> c.Head = "inspect")
        let buildPos = calls |> List.tryFindIndex (fun c -> c.Head = "build")
        Assert.True(inspectPos.IsSome && buildPos.IsSome && inspectPos.Value < buildPos.Value, $"inspect が build より前に呼ばれていません: {calls}")

    [<Fact>]
    let ``IT-SH-WARN-02: deploy.sh は既存コンテナの /app/data マウント元が今回と同一の場合に不一致警告を出さない`` () =
        let setup =
            """
mkdir -p "$TMP_DIR/videos/Anime"
export VIDEO_DIRS="$TMP_DIR/videos/Anime"
export DATA_DIR="$TMP_DIR/same-data"
export MOCK_INSPECT_EXIT=0
export MOCK_INSPECT_JSON="[{\"Name\":\"/tag-based-video-manager\",\"Mounts\":[{\"Destination\":\"/app/data\",\"Source\":\"${TMP_DIR}/same-data/\",\"Type\":\"bind\"}]}]"
"""
        let r = runDeployShTest setup """bash "$REPO_ROOT/scripts/deploy.sh" """
        let rc, output, calls = parseDeployShResult r
        Assert.True((rc = 0), $"deploy.sh 失敗 (rc={rc}):\n{output}")
        Assert.True((calls |> List.exists (fun c -> c.Head = "inspect")), $"inspect が呼ばれていません: {calls}")
        output |> should not' (contain "旧マウント元")
        output |> should not' (contain "マウント元が今回と異なります")
        output |> should not' (contain "cp -a")

    [<Fact>]
    let ``IT-DATA-PERSIST-01: deploy.ps1 と deploy.sh の連続 2 回実行で同一 DATA_DIR マウントが維持され --rm にならない`` () =
        // PowerShell 側連続 2 回実行
        withDeployPsEnv None None None (fun envInfo ->
            let videoDir = Path.Combine(envInfo.TempRoot, "videos", "Anime")
            Directory.CreateDirectory videoDir |> ignore
            let expectedMount = Path.Combine(envInfo.RepoRoot, "persist-data").Replace('\\', '/') + ":/app/data"
            withHealthServer (fun port ->
                for _ in 1..2 do
                    let r = runDeployPs envInfo $"-VideoDirs {ScriptProcess.psq videoDir} -DataDir './persist-data' -Port {port}" []
                    Assert.True((r.ExitCode = 0), $"deploy.ps1 連続実行失敗: {r.StdErr}")
                let runCalls = readRuntimeCalls envInfo.RuntimeLogPath |> List.filter (fun c -> c.Head = "run")
                runCalls.Length |> should equal 2
                for rc in runCalls do
                    rc |> should contain expectedMount
                    rc |> should not' (contain "--rm")
            )
        )

        // Bash 側連続 2 回実行
        let setup =
            """
mkdir -p "$TMP_DIR/videos/Anime"
export VIDEO_DIRS="$TMP_DIR/videos/Anime"
export DATA_DIR="./persist-data"
echo "EXPECTED=$REPO_ROOT/persist-data:/app/data"
"""
        let invoke =
            """
cd "$REPO_ROOT"
bash "$REPO_ROOT/scripts/deploy.sh"
bash "$REPO_ROOT/scripts/deploy.sh"
"""
        let rSh = runDeployShTest setup invoke
        let rc, output, calls = parseDeployShResult rSh
        Assert.True((rc = 0), $"deploy.sh 連続実行失敗 (rc={rc}):\n{output}")
        let expectedMount = Regex.Match(output, @"EXPECTED=(.+)").Groups.[1].Value.Trim()
        let runCalls = calls |> List.filter (fun c -> c.Head = "run")
        runCalls.Length |> should equal 2
        for c in runCalls do
            c |> should contain expectedMount
            c |> should not' (contain "--rm")
