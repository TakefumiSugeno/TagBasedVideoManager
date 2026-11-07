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

/// scripts/deploy.sh の .env 安全読み込み (IT-15)
module DeployScriptEnvParsingTests =

    [<Fact>]
    let ``IT-15: deploy.sh は source .env による危険な読み込みを行っていない`` () =
        let text = RepoLayout.readTextOrFail "scripts/deploy.sh"
        Regex.IsMatch(text, @"(?m)^\s*(source|\.)\s+.*\.env") |> should equal false

    [<Fact>]
    let ``IT-15: deploy.sh はセミコロンを含む VIDEO_DIRS を .env から安全に読み込める`` () =
        let bashCommand =
            """
set -euo pipefail
# deploy.sh が source .env を行っている場合はエラー終了させる
if grep -qE '^\s*(source|\.)\s+.*\.env' scripts/deploy.sh; then
    echo "ERROR: deploy.sh still uses source .env" >&2
    exit 1
fi

TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT

cat << 'EOF' > "$TMP_DIR/.env"
# テスト用コメント行
PORT=5620
VIDEO_DIRS="D:\Anime;F:\Movie"
DATA_DIR="/tmp/data"
CUSTOM_EMPTY=""
SINGLE_QUOTE='literal;value'
EOF

# deploy.sh から parse_env 関数を抽出して実行・検証
export REPO_ROOT="$TMP_DIR"

eval "$(sed -n '/^parse_env()/,/^}/p' scripts/deploy.sh)"

if ! declare -f parse_env >/dev/null 2>&1; then
    echo "ERROR: parse_env could not be extracted from scripts/deploy.sh" >&2
    exit 1
fi

# 環境変数をクリアして parse_env を実行
unset PORT VIDEO_DIRS DATA_DIR CUSTOM_EMPTY SINGLE_QUOTE
parse_env "$TMP_DIR/.env"

# 実質的な値の一致とセミコロン非分割をアサート
if [ "${VIDEO_DIRS:-}" != 'D:\Anime;F:\Movie' ]; then
    echo "ERROR: VIDEO_DIRS mismatch: got '${VIDEO_DIRS:-}', expected 'D:\Anime;F:\Movie'" >&2
    exit 1
fi
if [ "${PORT:-}" != "5620" ]; then
    echo "ERROR: PORT mismatch: got '${PORT:-}', expected '5620'" >&2
    exit 1
fi
if [ "${DATA_DIR:-}" != "/tmp/data" ]; then
    echo "ERROR: DATA_DIR mismatch: got '${DATA_DIR:-}', expected '/tmp/data'" >&2
    exit 1
fi
if [ "${SINGLE_QUOTE:-}" != 'literal;value' ]; then
    echo "ERROR: SINGLE_QUOTE mismatch: got '${SINGLE_QUOTE:-}', expected 'literal;value'" >&2
    exit 1
fi

echo "ENV_PARSING_OK"
"""
        let r = run "bash" [ "-c"; bashCommand ] (RepoLayout.repoRoot ()) 30000
        Assert.True((r.ExitCode = 0), $"deploy.sh の .env 安全読み込み検証が失敗しました (exit={r.ExitCode}):\nSTDERR: {r.StdErr}\nSTDOUT: {r.StdOut}")
        r.StdOut |> should contain "ENV_PARSING_OK"

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
