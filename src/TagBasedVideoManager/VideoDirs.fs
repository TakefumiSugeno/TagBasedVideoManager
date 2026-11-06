namespace TagBasedVideoManager

open System
open System.Runtime.InteropServices

/// <summary>
/// 動画ディレクトリ設定（VIDEO_DIRS）の解析および環境変数解決モジュール。
/// </summary>
module VideoDirs =

    /// <summary>
    /// パスがフォルダ名を持たない（ルートディレクトリやドライブ直下）かどうかを判定する。
    /// </summary>
    let private hasNoFolderName (path: string) : bool =
        let trimmed = path.Trim().TrimEnd('/', '\\')
        String.IsNullOrEmpty(trimmed) || (trimmed.Length = 2 && Char.IsAsciiLetter(trimmed[0]) && trimmed[1] = ':')

    /// <summary>
    /// プラットフォームに応じた大文字小文字の区別規則に従い、順序を維持して重複を除去する。
    /// </summary>
    let private deduplicate (isWindows: bool) (items: string list) : string list =
        items |> List.distinctBy (fun s -> if isWindows then s.ToUpperInvariant() else s)

    /// <summary>
    /// OS プラットフォーム（Windows判定）を指定して VIDEO_DIRS 設定文字列をパースする。
    /// </summary>
    let parseForPlatform (isWindows: bool) (input: string) : Result<string list, string> =
        if isNull input || String.IsNullOrWhiteSpace(input) then
            Error "環境変数 VIDEO_DIRS が設定されていません。例: VIDEO_DIRS=D:\\Anime;F:\\Movie"
        else
            let rawElements =
                input.Split(';', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                |> Array.toList

            if List.isEmpty rawElements then
                Error "環境変数 VIDEO_DIRS が設定されていません。例: VIDEO_DIRS=D:\\Anime;F:\\Movie"
            elif List.exists hasNoFolderName rawElements then
                Error "動画ディレクトリにはルートディレクトリやドライブ直下（例: D:\\ や /）は指定できません。必ずフォルダ名を指定してください。例: VIDEO_DIRS=D:\\Anime;F:\\Movie"
            else
                Ok(deduplicate isWindows rawElements)

    /// <summary>
    /// 実行中 OS の判定に基づき VIDEO_DIRS 設定文字列をパースする。
    /// </summary>
    let parse (input: string) : Result<string list, string> =
        let isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        parseForPlatform isWindows input

    /// <summary>
    /// 環境変数取得関数から VIDEO_DIRS を解決する。
    /// 未設定時に VIDEO_DIR が存在する場合は移行案内メッセージを返却する。
    /// </summary>
    let fromEnvironment (getEnv: string -> string option) : Result<string list, string> =
        match getEnv "VIDEO_DIRS" with
        | Some raw when not (String.IsNullOrWhiteSpace(raw)) -> parse raw
        | _ when (getEnv "VIDEO_DIR").IsSome ->
            Error "環境変数 VIDEO_DIR は廃止されました。VIDEO_DIRS に移行してください。例: VIDEO_DIRS=D:\\Anime;F:\\Movie"
        | _ -> Error "環境変数 VIDEO_DIRS が設定されていません。例: VIDEO_DIRS=D:\\Anime;F:\\Movie"
