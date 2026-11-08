namespace TagBasedVideoManager.Tests.Unit

open System
open System.IO
open System.Text.RegularExpressions
open Xunit
open FsUnit
open TagBasedVideoManager.Domain

/// <summary>
/// Domain.FileIdentity モジュールにおける決定論的UUID（UUID v5）算出ロジックの境界値・異常系単体テスト（UT-01〜UT-06）。
/// RFC 9562 準拠の UUID v5 形式、ファイル更新日時不変、サンプリング境界、異常系エラーハンドリングを網羅的に検証する。
/// </summary>
module DomainTests =

    /// テスト用の一時ディレクトリを作成し、テスト終了時に削除するヘルパー
    let private withTempDir (action: string -> unit) =
        let tempDir = Path.Combine(Path.GetTempPath(), $"TBVM_DomainTest_{Guid.NewGuid():N}")
        Directory.CreateDirectory(tempDir) |> ignore
        try
            action tempDir
        finally
            try
                if Directory.Exists(tempDir) then
                    Directory.Delete(tempDir, true)
            with _ -> ()

    /// 指定サイズのダミーファイルを一時ディレクトリ内に生成するヘルパー
    let private createDummyFile (dir: string) (fileName: string) (size: int64) (fillPattern: byte -> byte) =
        let filePath = Path.Combine(dir, fileName)
        use fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None)
        let buffer = Array.zeroCreate<byte> 4096
        let mutable remaining = size
        let mutable byteVal = 0uy
        while remaining > 0L do
            let toWrite = int (min remaining (int64 buffer.Length))
            for i in 0 .. toWrite - 1 do
                buffer.[i] <- fillPattern byteVal
                byteVal <- byteVal + 1uy
            fs.Write(buffer, 0, toWrite)
            remaining <- remaining - int64 toWrite
        fs.Flush()
        filePath

    /// UUID v5 の形式検証（36文字、小文字ハイフン区切り、version 5、variant 1）
    let private assertValidUuidV5 (uuid: string) =
        uuid.Length |> should equal 36
        // 小文字英数字とハイフンのみ
        let regex = Regex(@"^[0-9a-f]{8}-[0-9a-f]{4}-5[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")
        regex.IsMatch(uuid) |> should be True
        let success, _ = Guid.TryParse(uuid)
        success |> should be True

    /// <summary>
    /// UT-01: 空・0バイト境界（最小値）
    /// 0バイトの空ファイルに対して Ok で有効な UUID v5 が返り、ファイル更新日時が変更されないこと。
    /// </summary>
    [<Fact>]
    let ``UT-01: computeFileUuid should return valid UUID v5 for 0-byte file without modifying timestamp`` () =
        withTempDir (fun tempDir ->
            let filePath = createDummyFile tempDir "empty.mp4" 0L id
            let pastTime = DateTime.UtcNow.AddDays(-1.0)
            File.SetLastWriteTimeUtc(filePath, pastTime)
            let originalModifiedAt = File.GetLastWriteTimeUtc(filePath)

            let result = FileIdentity.computeFileUuid filePath

            match result with
            | Ok uuid ->
                assertValidUuidV5 uuid
                // 決定論的アルゴリズムの固定リテラル値アサーション（Anti-Tautology / ミューテーション保護）
                uuid |> should equal "d71218b8-3967-5b5b-8b18-3f18311d5056"
                let afterModifiedAt = File.GetLastWriteTimeUtc(filePath)
                afterModifiedAt |> should equal originalModifiedAt
            | Error err ->
                failwithf "Expected Ok UUID for 0-byte file, but got Error: %s" err
        )

    /// <summary>
    /// UT-02: 1バイト境界（最小正数値）
    /// 1バイトのファイルに対して Ok で有効な UUID v5 が返り、0バイトファイルとは異なる UUID になること。
    /// </summary>
    [<Fact>]
    let ``UT-02: computeFileUuid should return distinct UUID v5 for 1-byte file compared to 0-byte file`` () =
        withTempDir (fun tempDir ->
            let path0 = createDummyFile tempDir "empty.mp4" 0L id
            let path1 = createDummyFile tempDir "single_byte.mp4" 1L (fun _ -> 0xABuy)

            let res0 = FileIdentity.computeFileUuid path0
            let res1 = FileIdentity.computeFileUuid path1

            match res0, res1 with
            | Ok uuid0, Ok uuid1 ->
                assertValidUuidV5 uuid0
                assertValidUuidV5 uuid1
                uuid1 |> should not' (equal uuid0)
            | Error e, _ | _, Error e ->
                failwithf "computeFileUuid failed unexpectedly: %s" e
        )

    /// <summary>
    /// UT-03: チャンク境界（最大全読込 - 1: 196,607 バイト = 192KB - 1B）
    /// 全バイト読込経路で UUID v5 が算出され、リネームや別ディレクトリ移動後も完全一致すること。
    /// </summary>
    [<Fact>]
    let ``UT-03: computeFileUuid should return identical UUID for 192KB - 1B file across rename and move`` () =
        withTempDir (fun tempDir ->
            let subDir = Path.Combine(tempDir, "SubFolder")
            Directory.CreateDirectory(subDir) |> ignore

            let size = 196607L // 192KB - 1B
            let pathOrig = createDummyFile tempDir "original.mp4" size (fun b -> b + 3uy)

            let resOrig = FileIdentity.computeFileUuid pathOrig

            // 別名・別フォルダに同一内容のファイルを配置
            let pathMoved = Path.Combine(subDir, "renamed_and_moved.mp4")
            File.Copy(pathOrig, pathMoved)

            let resMoved = FileIdentity.computeFileUuid pathMoved

            match resOrig, resMoved with
            | Ok uuidOrig, Ok uuidMoved ->
                assertValidUuidV5 uuidOrig
                uuidOrig |> should equal uuidMoved
            | Error e, _ | _, Error e ->
                failwithf "computeFileUuid failed unexpectedly: %s" e
        )

    /// <summary>
    /// UT-04: チャンク境界（最大全読込ちょうど: 196,608 バイト = 192KB ちょうど）
    /// 全バイト読込経路で UUID v5 が算出され、末尾1バイト変更時に別 UUID となること。
    /// </summary>
    [<Fact>]
    let ``UT-04: computeFileUuid should return distinct UUID when last byte is modified at 192KB boundary`` () =
        withTempDir (fun tempDir ->
            let size = 196608L // 192KB exactly
            let pathA = createDummyFile tempDir "full_192kb_a.mp4" size (fun b -> b * 7uy)
            let pathB = createDummyFile tempDir "full_192kb_b.mp4" size (fun b -> b * 7uy)

            // pathB の末尾1バイトのみ書き換える
            use fs = new FileStream(pathB, FileMode.Open, FileAccess.ReadWrite)
            fs.Seek(size - 1L, SeekOrigin.Begin) |> ignore
            let currentLastByte = fs.ReadByte()
            let newByte = if currentLastByte = 0xFF then 0x00 else currentLastByte + 1
            fs.Seek(size - 1L, SeekOrigin.Begin) |> ignore
            fs.WriteByte(byte newByte)
            fs.Flush()
            fs.Close()

            let resA = FileIdentity.computeFileUuid pathA
            let resB = FileIdentity.computeFileUuid pathB

            match resA, resB with
            | Ok uuidA, Ok uuidB ->
                assertValidUuidV5 uuidA
                assertValidUuidV5 uuidB
                uuidA |> should not' (equal uuidB)
            | Error e, _ | _, Error e ->
                failwithf "computeFileUuid failed unexpectedly: %s" e
        )

    /// <summary>
    /// UT-05: チャンク境界（3点サンプリング開始 +1: 196,609 バイト = 192KB + 1B、および 1,000,000 バイト）
    /// 先頭・中間・末尾 64KB サンプリング経路で算出され、先頭/中間/末尾の変更で別 UUID となること。
    /// また、サンプリング範囲外（オフセット 70,000 バイト等）の変更では UUID が一致すること（サンプリング動作の検証）。
    /// </summary>
    [<Fact>]
    let ``UT-05: computeFileUuid should sample head mid tail for files > 192KB`` () =
        withTempDir (fun tempDir ->
            let size = 1000000L // 1MB (> 192KB)
            let baseFile = createDummyFile tempDir "base_1mb.mp4" size (fun b -> b + 11uy)
            let resBase = FileIdentity.computeFileUuid baseFile

            // 1. 先頭 64KB 内の 1 バイト変更 (オフセット 100)
            let fileHeadModified = Path.Combine(tempDir, "head_mod.mp4")
            File.Copy(baseFile, fileHeadModified)
            use fsHead = new FileStream(fileHeadModified, FileMode.Open, FileAccess.ReadWrite)
            fsHead.Seek(100L, SeekOrigin.Begin) |> ignore
            fsHead.WriteByte(0xFFuy)
            fsHead.Close()
            let resHead = FileIdentity.computeFileUuid fileHeadModified

            // 2. 中間 64KB 内の 1 バイト変更 (オフセット size / 2L = 500,000)
            let fileMidModified = Path.Combine(tempDir, "mid_mod.mp4")
            File.Copy(baseFile, fileMidModified)
            use fsMid = new FileStream(fileMidModified, FileMode.Open, FileAccess.ReadWrite)
            fsMid.Seek(size / 2L, SeekOrigin.Begin) |> ignore
            fsMid.WriteByte(0xEEuy)
            fsMid.Close()
            let resMid = FileIdentity.computeFileUuid fileMidModified

            // 3. 末尾 64KB 内の 1 バイト変更 (オフセット size - 100L)
            let fileTailModified = Path.Combine(tempDir, "tail_mod.mp4")
            File.Copy(baseFile, fileTailModified)
            use fsTail = new FileStream(fileTailModified, FileMode.Open, FileAccess.ReadWrite)
            fsTail.Seek(size - 100L, SeekOrigin.Begin) |> ignore
            fsTail.WriteByte(0xDDuy)
            fsTail.Close()
            let resTail = FileIdentity.computeFileUuid fileTailModified

            // 4. サンプリング範囲外の 1 バイト変更 (オフセット 70,000: 先頭64KB直後かつ中間64KB手前)
            let fileUnsampledModified = Path.Combine(tempDir, "unsampled_mod.mp4")
            File.Copy(baseFile, fileUnsampledModified)
            use fsUnsampled = new FileStream(fileUnsampledModified, FileMode.Open, FileAccess.ReadWrite)
            fsUnsampled.Seek(70000L, SeekOrigin.Begin) |> ignore
            fsUnsampled.WriteByte(0xCCuy)
            fsUnsampled.Close()
            let resUnsampled = FileIdentity.computeFileUuid fileUnsampledModified

            match resBase, resHead, resMid, resTail, resUnsampled with
            | Ok uuidBase, Ok uuidHead, Ok uuidMid, Ok uuidTail, Ok uuidUnsampled ->
                assertValidUuidV5 uuidBase
                assertValidUuidV5 uuidHead
                assertValidUuidV5 uuidMid
                assertValidUuidV5 uuidTail
                assertValidUuidV5 uuidUnsampled

                // 先頭・中間・末尾の変更は別 UUID となること
                uuidHead |> should not' (equal uuidBase)
                uuidMid |> should not' (equal uuidBase)
                uuidTail |> should not' (equal uuidBase)

                // サンプリング対象外の変更では同一 UUID となること（サンプリング高速化の仕様）
                uuidUnsampled |> should equal uuidBase
            | _ ->
                failwithf "computeFileUuid failed unexpectedly during UT-05 sampling verification"
        )

    /// <summary>
    /// UT-06: 異常系・ファイル不在境界
    /// 存在しないファイルパスや空文字列に対して例外クラッシュせず Error が返却されること。
    /// </summary>
    [<Theory>]
    [<InlineData("")>]
    [<InlineData("   ")>]
    [<InlineData("non_existent_video_file_98765.mp4")>]
    [<InlineData("Z:\\invalid_path\\non_existent_folder\\video.mp4")>]
    let ``UT-06: computeFileUuid should return Error on non-existent or invalid file path without crashing`` (invalidPath: string) =
        let result = FileIdentity.computeFileUuid invalidPath
        match result with
        | Ok uuid -> failwithf "Expected Error for invalid path '%s', but got Ok: %s" invalidPath uuid
        | Error errMsg ->
            errMsg |> should not' (be EmptyString)
