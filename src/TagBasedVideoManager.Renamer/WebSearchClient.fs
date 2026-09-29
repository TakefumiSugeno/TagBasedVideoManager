namespace TagBasedVideoManager.Renamer

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open System.Diagnostics

/// DuckDuckGo Web検索クライアントモジュール
module WebSearchClient =

    /// JSON デシリアライズ用 DTO
    type SearchResultDto = {
        [<JsonPropertyName("title")>]
        Title: string
        [<JsonPropertyName("snippet")>]
        Snippet: string
        [<JsonPropertyName("url")>]
        Url: string
    }

    /// 一般的な動画タグ・コーデック・解像度パターンの正規表現
    let private videoNoiseRegex =
        Regex(@"(?i)\b(1080p|720p|2160p|4k|480p|webrip|web-dl|bluray|bdrip|dvdrip|x264|x265|h264|h265|hevc|aac|dts|ac3|flac|10bit|remux|repack|proper)\b", RegexOptions.Compiled)

    /// リリースグループやブラケット表記の正規表現
    let private bracketNoiseRegex =
        Regex(@"\[[^\]]*\]|\([^\)]*\)|-[A-Za-z0-9_]+$", RegexOptions.Compiled)

    /// 連続空白の正規表現
    let private whitespaceRegex =
        Regex(@"\s+", RegexOptions.Compiled)

    /// ファイル名から検索クエリを抽出・整形する
    let extractSearchQuery (fileName: string) : string =
        if String.IsNullOrWhiteSpace(fileName) then ""
        else
            let nameWithoutExt = Path.GetFileNameWithoutExtension(fileName)
            // ドットやアンダースコアを空白に置換
            let replaced = nameWithoutExt.Replace('.', ' ').Replace('_', ' ')
            // ノイズタグやブラケット表記を除去
            let cleanedNoise = videoNoiseRegex.Replace(replaced, " ")
            let cleanedBracket = bracketNoiseRegex.Replace(cleanedNoise, " ")
            let normalized = whitespaceRegex.Replace(cleanedBracket, " ").Trim()
            if String.IsNullOrWhiteSpace(normalized) then
                // もし過剰に消去されて空になった場合は置換後の元の文字列をトリムして返す
                whitespaceRegex.Replace(replaced, " ").Trim()
            else
                normalized

    /// 検索結果アイテムのリストをプロンプト埋め込み用テキストにフォーマットする
    let formatSearchResultsForPrompt (results: SearchResultItem list) : string =
        if List.isEmpty results then
            "（Web検索結果なし）"
        else
            let sb = StringBuilder()
            sb.AppendLine("--- Web検索結果 (DuckDuckGo / ddgs) ---") |> ignore
            results
            |> List.iteri (fun idx item ->
                sb.AppendLine($"[{idx + 1}] タイトル: {item.Title}") |> ignore
                if not (String.IsNullOrWhiteSpace(item.Url)) then
                    sb.AppendLine($"    URL: {item.Url}") |> ignore
                sb.AppendLine($"    概要: {item.Snippet}") |> ignore
            )
            sb.AppendLine("----------------------------------------") |> ignore
            sb.ToString().TrimEnd()

    /// Pythonスクリプトが出力したJSON文字列をパースする
    let parseSearchResults (json: string) : Result<SearchResultItem list, string> =
        if String.IsNullOrWhiteSpace(json) then
            Ok []
        else
            try
                let options = JsonSerializerOptions()
                options.PropertyNameCaseInsensitive <- true
                let dtos = JsonSerializer.Deserialize<SearchResultDto list>(json, options)
                let items =
                    dtos
                    |> List.map (fun d -> {
                        SearchResultItem.Title = if isNull d.Title then "" else d.Title
                        SearchResultItem.Snippet = if isNull d.Snippet then "" else d.Snippet
                        SearchResultItem.Url = if isNull d.Url then "" else d.Url
                    })
                Ok items
            with ex ->
                Error $"JSONパース失敗: {ex.Message}"

    /// 指定されたPython実行可能ファイル・スクリプトパスを使用してWeb検索を実行する
    let searchCustomAsync
        (pythonExecutable: string)
        (scriptPath: string)
        (query: string)
        (maxResults: int)
        (cancellationToken: CancellationToken)
        : Task<Result<SearchResultItem list, string>> =
        task {
            if String.IsNullOrWhiteSpace(query) then
                return Ok []
            else
                try
                    let psi = ProcessStartInfo()
                    psi.FileName <- pythonExecutable
                    // クエリのエスケープ (ダブルクォーテーションをエスケープ)
                    let escapedQuery = query.Replace("\"", "\\\"")
                    psi.Arguments <- $"\"{scriptPath}\" \"{escapedQuery}\" -n {maxResults}"
                    psi.UseShellExecute <- false
                    psi.RedirectStandardOutput <- true
                    psi.RedirectStandardError <- true
                    psi.CreateNoWindow <- true
                    psi.StandardOutputEncoding <- Encoding.UTF8
                    psi.StandardErrorEncoding <- Encoding.UTF8

                    use proc = new Process()
                    proc.StartInfo <- psi

                    let started =
                        try proc.Start()
                        with ex -> false

                    if not started then
                        return Error $"Pythonプロセス '{pythonExecutable}' を起動できませんでした。"
                    else
                        // キャンセル時にプロセスを強制終了する登録
                        use registration = cancellationToken.Register(fun () ->
                            try
                                if not proc.HasExited then
                                    proc.Kill(true)
                            with _ -> ()
                        )

                        let! stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken)
                        let! stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken)
                        do! proc.WaitForExitAsync(cancellationToken)

                        if proc.ExitCode <> 0 then
                            return Error $"ddgsスクリプトが異常終了しました (code {proc.ExitCode}): {stderrTask}"
                        else
                            return parseSearchResults stdoutTask
                with
                | :? OperationCanceledException ->
                    return Error "Web検索がキャンセルされました。"
                | ex ->
                    return Error $"Web検索サブプロセス実行時エラー: {ex.Message}"
        }

    /// デフォルト設定でWeb検索を実行する
    let searchAsync (query: string) (maxResults: int) (cancellationToken: CancellationToken) : Task<Result<SearchResultItem list, string>> =
        task {
            let scriptPath =
                let candidates = [
                    Path.Combine(AppContext.BaseDirectory, "scripts", "ddgs_search.py")
                    Path.Combine(AppContext.BaseDirectory, "../../../../scripts/ddgs_search.py")
                    Path.Combine(AppContext.BaseDirectory, "../../../../../scripts/ddgs_search.py")
                    Path.Combine(Directory.GetCurrentDirectory(), "scripts", "ddgs_search.py")
                    "scripts/ddgs_search.py"
                ]
                candidates
                |> List.map Path.GetFullPath
                |> List.tryFind File.Exists
                |> Option.defaultValue (Path.GetFullPath("scripts/ddgs_search.py"))

            return! searchCustomAsync "python" scriptPath query maxResults cancellationToken
        }
