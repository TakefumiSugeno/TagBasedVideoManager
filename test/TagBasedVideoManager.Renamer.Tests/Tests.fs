namespace TagBasedVideoManager.Renamer.Tests

open System
open Xunit
open FsUnit
open TagBasedVideoManager.Renamer

module InitialSmokeTests =

    [<Fact>]
    let ``Renamer test project initializes successfully`` () =
        true |> should equal true

    // ==========================================
    // Application Versioning 単体テスト (UT-04, UT-05)
    // ==========================================

    [<Fact>]
    let ``formatTitle は アセンブリバージョンからタイトルバー文字列を正常に生成する (UT-04)`` () =
        let ver = Some (Version(0, 1, 0, 0))
        let title = Program.formatTitle ver
        title |> should equal "TagBasedVideoManager - AI File Renamer v0.1.0"

    [<Fact>]
    let ``formatTitle は バージョン不在 (None) 時に安全なデフォルト 0.0.0 でタイトルを生成する (UT-05)`` () =
        let ver = None
        let title = Program.formatTitle ver
        title |> should equal "TagBasedVideoManager - AI File Renamer v0.0.0"
