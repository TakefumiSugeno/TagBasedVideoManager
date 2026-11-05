namespace TagBasedVideoManager.Renamer.Tests

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Xunit
open FsUnit

/// リポジトリ内ファイルの解決ヘルパー（実行ファイル位置から上方探索）
module RepoLayout =

    /// ソリューションファイル名（リポジトリルートの目印）
    let private solutionMarker = "TagBasedVideoManager.slnx"

    /// リポジトリルートを解決する。環境変数 TBVM_REPO_ROOT があれば優先する
    let private findRepoRoot () : string =
        match Environment.GetEnvironmentVariable "TBVM_REPO_ROOT" with
        | null
        | "" ->
            let rec up (dir: DirectoryInfo) =
                if isNull dir then
                    failwith $"リポジトリルート ({solutionMarker}) を {AppContext.BaseDirectory} から上方探索しましたが見つかりません"
                elif File.Exists(Path.Combine(dir.FullName, solutionMarker)) then
                    dir.FullName
                else
                    up dir.Parent

            up (DirectoryInfo AppContext.BaseDirectory)
        | root -> root

    let private root = lazy (findRepoRoot ())

    /// リポジトリルートの絶対パス
    let repoRoot () : string = root.Value

    /// リポジトリルート相対パスを絶対パスへ変換する
    let pathOf (relative: string) : string = Path.Combine(repoRoot (), relative)

    /// 指定ファイルが存在することを明示メッセージ付きで検証し、全文を返す
    let readTextOrFail (relative: string) : string =
        let full = pathOf relative
        Assert.True(File.Exists full, $"必須ファイルが存在しません: {relative} (解決先: {full})")
        File.ReadAllText full

/// DevContainer 設定の検証 (IT-DEV-01)
module DevContainerConfigTests =

    /// コメント・末尾カンマを許容する JSONC 読み込み
    let private parseJsonc (text: string) : JsonDocument =
        let options = JsonDocumentOptions(CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)
        JsonDocument.Parse(text, options)

    let private devcontainerJsonPath = ".devcontainer/devcontainer.json"
    let private dockerfilePath = ".devcontainer/Dockerfile"

    /// ネストしたプロパティを辿る（存在しなければ None）
    let private tryPath (root: JsonElement) (path: string list) : JsonElement option =
        path
        |> List.fold
            (fun acc key ->
                acc
                |> Option.bind (fun (el: JsonElement) ->
                    if el.ValueKind = JsonValueKind.Object then
                        match el.TryGetProperty key with
                        | true, v -> Some v
                        | _ -> None
                    else
                        None))
            (Some root)

    let private stringsOf (el: JsonElement) : string list =
        el.EnumerateArray() |> Seq.map (fun e -> e.GetRawText().Trim('"')) |> List.ofSeq

    [<Fact>]
    let ``IT-DEV-01: devcontainer.json が JSONC として解析でき、ルートがオブジェクトである`` () =
        use doc = parseJsonc (RepoLayout.readTextOrFail devcontainerJsonPath)
        doc.RootElement.ValueKind |> should equal JsonValueKind.Object

    [<Fact>]
    let ``IT-DEV-01: forwardPorts に 5621 を含む`` () =
        use doc = parseJsonc (RepoLayout.readTextOrFail devcontainerJsonPath)

        match tryPath doc.RootElement [ "forwardPorts" ] with
        | Some ports when ports.ValueKind = JsonValueKind.Array -> stringsOf ports |> should contain "5621"
        | _ -> failwith "forwardPorts が配列として定義されていません"

    [<Fact>]
    let ``IT-DEV-01: forwardPorts に 本番ポート 5620 を含まない（競合防止）`` () =
        use doc = parseJsonc (RepoLayout.readTextOrFail devcontainerJsonPath)

        match tryPath doc.RootElement [ "forwardPorts" ] with
        | Some ports when ports.ValueKind = JsonValueKind.Array ->
            stringsOf ports |> List.exists (fun p -> p = "5620" || p.EndsWith ":5620") |> should equal false
        | _ -> failwith "forwardPorts が配列として定義されていません"

    [<Fact>]
    let ``IT-DEV-01: containerEnv.PORT は文字列 "5621"`` () =
        use doc = parseJsonc (RepoLayout.readTextOrFail devcontainerJsonPath)

        match tryPath doc.RootElement [ "containerEnv"; "PORT" ] with
        | Some port ->
            port.ValueKind |> should equal JsonValueKind.String
            port.GetString() |> should equal "5621"
        | None -> failwith "containerEnv.PORT が定義されていません"

    [<Fact>]
    let ``IT-DEV-01: customizations.vscode.extensions に Ionide と C# DevKit を含む`` () =
        use doc = parseJsonc (RepoLayout.readTextOrFail devcontainerJsonPath)

        match tryPath doc.RootElement [ "customizations"; "vscode"; "extensions" ] with
        | Some exts when exts.ValueKind = JsonValueKind.Array ->
            let ids = stringsOf exts
            ids |> should contain "Ionide.Ionide-fsharp"
            ids |> should contain "ms-dotnettools.csdevkit"
        | _ -> failwith "customizations.vscode.extensions が配列として定義されていません"

    [<Fact>]
    let ``IT-DEV-01: 破損した JSONC は解析に失敗する（検証ロジックが緩すぎないことの対照）`` () =
        Assert.ThrowsAny<JsonException>(Action(fun () -> (parseJsonc "{ \"forwardPorts\": [5621, }").Dispose()))
        |> ignore

    [<Fact>]
    let ``IT-DEV-01: JSONC のコメントと末尾カンマを許容する読み込みで値を取得できる（対照）`` () =
        use doc = parseJsonc "{ // comment\n \"forwardPorts\": [5621,], /* block */ }"
        stringsOf (doc.RootElement.GetProperty "forwardPorts") |> should equal [ "5621" ]

    /// Dockerfile の行継続（バックスラッシュ改行）を 1 行に連結する
    let private joinContinuations (text: string) : string =
        Regex.Replace(text, @"\\\r?\n", " ")

    /// apt-get install を含む RUN 行をすべて返す
    let private aptInstallLines (text: string) : string list =
        joinContinuations text
        |> fun t -> t.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
        |> Seq.map (fun l -> l.Trim())
        |> Seq.filter (fun l ->
            l.StartsWith("RUN", StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(l, @"apt(-get)?\s+install"))
        |> List.ofSeq

    [<Fact>]
    let ``IT-DEV-01: Dockerfile のベースイメージは dotnet 10 系`` () =
        let text = RepoLayout.readTextOrFail dockerfilePath
        Regex.IsMatch(text, @"(?im)^\s*FROM\s+\S*dotnet\S*10\.0") |> should equal true

    [<Theory>]
    [<InlineData("ffmpeg")>]
    [<InlineData("sqlite3")>]
    [<InlineData("curl")>]
    let ``IT-DEV-01: Dockerfile の apt install に必須パッケージを含む`` (package: string) =
        let lines = aptInstallLines (RepoLayout.readTextOrFail dockerfilePath)
        Assert.NotEmpty lines

        lines
        |> List.exists (fun l -> Regex.IsMatch(l, $@"(?<![\w.-]){Regex.Escape package}(?![\w.-])"))
        |> should equal true
