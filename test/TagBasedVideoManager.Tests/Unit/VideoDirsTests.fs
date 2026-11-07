namespace TagBasedVideoManager.Tests.Unit

open Xunit
open FsUnit
open TagBasedVideoManager

/// <summary>
/// VideoDirs モジュールの単体テスト（UT-01〜UT-10, UT-12, UT-08a/b）。
/// </summary>
module VideoDirsTests =

    /// <summary>
    /// UT-01: 空文字列および null の場合は Error となり、VIDEO_DIRS 設定案内と設定例が含まれること。
    /// </summary>
    [<Fact>]
    let ``UT-01: parse should return Error for null or empty string with example`` () =
        match VideoDirs.parse "" with
        | Ok _ -> Assert.Fail("Expected Error for empty string, but got Ok")
        | Error msg ->
            msg.Contains("VIDEO_DIRS") |> should be True
            msg.Contains("VIDEO_DIRS=") |> should be True

        match VideoDirs.parse null with
        | Ok _ -> Assert.Fail("Expected Error for null, but got Ok")
        | Error msg ->
            msg.Contains("VIDEO_DIRS") |> should be True

    /// <summary>
    /// UT-02: 空白のみの場合は Error となること。
    /// </summary>
    [<Fact>]
    let ``UT-02: parse should return Error for whitespace only`` () =
        match VideoDirs.parse "   " with
        | Ok _ -> Assert.Fail("Expected Error for whitespace, but got Ok")
        | Error msg ->
            msg.Contains("VIDEO_DIRS") |> should be True

    /// <summary>
    /// UT-03: 区切りのみ（";" / ";;"）の場合は Error となること。
    /// </summary>
    [<Theory>]
    [<InlineData(";")>]
    [<InlineData(";;")>]
    [<InlineData(" ; ; ")>]
    let ``UT-03: parse should return Error for semicolons only`` (input: string) =
        match VideoDirs.parse input with
        | Ok _ -> Assert.Fail($"Expected Error for '{input}', but got Ok")
        | Error msg ->
            msg.Contains("VIDEO_DIRS") |> should be True

    /// <summary>
    /// UT-04: 1件境界（正常系）のパスが正しくパースされること。
    /// </summary>
    [<Fact>]
    let ``UT-04: parse should return Ok for single directory`` () =
        VideoDirs.parse @"D:\A" |> should equal (Ok [ @"D:\A" ] : Result<string list, string>)

    /// <summary>
    /// UT-05: 余白および空要素が無視され、順序が維持されること。
    /// </summary>
    [<Fact>]
    let ``UT-05: parse should ignore whitespace and empty elements while preserving order`` () =
        VideoDirs.parse @" D:\A ;; D:\B ;" |> should equal (Ok [ @"D:\A"; @"D:\B" ] : Result<string list, string>)

    /// <summary>
    /// UT-06: 複数件のパスが指定順でパースされること。
    /// </summary>
    [<Fact>]
    let ``UT-06: parse should return multiple directories in specified order`` () =
        VideoDirs.parse @"D:\A;F:\B;E:\C" |> should equal (Ok [ @"D:\A"; @"F:\B"; @"E:\C" ] : Result<string list, string>)

    /// <summary>
    /// UT-07: 同一パス重複が除去されること。
    /// </summary>
    [<Fact>]
    let ``UT-07: parse should remove exact duplicate paths`` () =
        VideoDirs.parse @"D:\A;D:\A" |> should equal (Ok [ @"D:\A" ] : Result<string list, string>)

    /// <summary>
    /// UT-08a: Windows 判定時、大文字小文字違いの重複が除去されること。
    /// </summary>
    [<Fact>]
    let ``UT-08a: parseForPlatform Windows should ignore case when deduplicating`` () =
        VideoDirs.parseForPlatform true @"D:\A;d:\a" |> should equal (Ok [ @"D:\A" ] : Result<string list, string>)

    /// <summary>
    /// UT-08b: Linux 判定時、大文字小文字違いは別ディレクトリとして保持されること。
    /// </summary>
    [<Fact>]
    let ``UT-08b: parseForPlatform Linux should preserve case difference as distinct directories`` () =
        VideoDirs.parseForPlatform false "/mnt/a;/mnt/A" |> should equal (Ok [ "/mnt/a"; "/mnt/A" ] : Result<string list, string>)

    /// <summary>
    /// UT-09: 特殊文字や空白を含むパスが正常に保持されること。
    /// </summary>
    [<Fact>]
    let ``UT-09: parse should preserve special characters and spaces in paths`` () =
        VideoDirs.parse @"D:\動画 A;D:\B&C" |> should equal (Ok [ @"D:\動画 A"; @"D:\B&C" ] : Result<string list, string>)

    /// <summary>
    /// UT-10: VIDEO_DIRS 未設定で VIDEO_DIR のみ設定されている場合、旧変数廃止案内と移行例を含む Error を返すこと。
    /// </summary>
    [<Fact>]
    let ``UT-10: fromEnvironment should return Error with migration guidance when only VIDEO_DIR is set`` () =
        let getEnv = function
            | "VIDEO_DIRS" -> None
            | "VIDEO_DIR" -> Some @"D:\Videos"
            | _ -> None

        match VideoDirs.fromEnvironment getEnv with
        | Ok _ -> Assert.Fail("Expected Error for legacy VIDEO_DIR, but got Ok")
        | Error msg ->
            msg.Contains("VIDEO_DIR") |> should be True
            msg.Contains("VIDEO_DIRS") |> should be True
            msg.Contains("VIDEO_DIRS=") |> should be True

    /// <summary>
    /// UT-12: ドライブ直下やルートパスなどフォルダ名を持たないパスが含まれる場合は Error となること。
    /// </summary>
    [<Theory>]
    [<InlineData(@"D:\")>]
    [<InlineData(@"D:")>]
    [<InlineData("/")>]
    [<InlineData(@"D:\A;D:\")>]
    [<InlineData(@"/;D:\Anime")>]
    let ``UT-12: parse should return Error when path has no folder name`` (input: string) =
        match VideoDirs.parse input with
        | Ok _ -> Assert.Fail($"Expected Error for root path '{input}', but got Ok")
        | Error msg ->
            // ドライブ直下やルートパスが不可である旨が含まれること（未設定エラーとの誤判定を防ぐため厳格に検証）
            (msg.Contains("フォルダ名") || msg.Contains("ドライブ直下") || msg.Contains("ルート"))
            |> should be True

    /// <summary>
    /// fromEnvironment: VIDEO_DIRS が設定されている場合は正常にパース結果が返ること。
    /// </summary>
    [<Fact>]
    let ``fromEnvironment should parse VIDEO_DIRS when present`` () =
        let getEnv = function
            | "VIDEO_DIRS" -> Some @"D:\Anime;F:\Movie"
            | "VIDEO_DIR" -> Some @"D:\Ignored"
            | _ -> None

        VideoDirs.fromEnvironment getEnv
        |> should equal (Ok [ @"D:\Anime"; @"F:\Movie" ] : Result<string list, string>)

    /// <summary>
    /// fromEnvironment: VIDEO_DIRS も VIDEO_DIR も未設定の場合は未設定エラー（設定例付き）が返ること。
    /// </summary>
    [<Fact>]
    let ``fromEnvironment should return Error when neither VIDEO_DIRS nor VIDEO_DIR is set`` () =
        let getEnv _ = None

        match VideoDirs.fromEnvironment getEnv with
        | Ok _ -> Assert.Fail("Expected Error when no env var set, but got Ok")
        | Error msg ->
            msg.Contains("VIDEO_DIRS") |> should be True
            msg.Contains("VIDEO_DIRS=") |> should be True
