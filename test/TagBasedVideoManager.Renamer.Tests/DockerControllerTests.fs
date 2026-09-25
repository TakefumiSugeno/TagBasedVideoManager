namespace TagBasedVideoManager.Renamer.Tests

open System
open Xunit
open FsUnit
open TagBasedVideoManager.Renamer

module DockerControllerTests =

    let dummyWorkDir = "C:\\TagBasedVideoManager"

    [<Fact>]
    let ``checkStatus は単一JSON配列形式の docker compose ps 出力を正常にパースし Running を判定する`` () =
        async {
            let jsonArray = """
            [
              {
                "Service": "video-manager",
                "State": "running",
                "ID": "cont-12345"
              }
            ]
            """
            let mockRunner _dir _cmd =
                async { return 0, jsonArray, "" }

            let mockHealth _url =
                async { return true }

            let! status = DockerController.checkStatus (Some mockRunner) (Some mockHealth) dummyWorkDir
            status.State |> should equal Running
            status.IsPortAccessible |> should equal true
            status.ContainerId |> should equal (Some "cont-12345")
        }

    [<Fact>]
    let ``checkStatus は改行区切りNDJSON形式の docker compose ps 出力にフォールバックしてパースできる`` () =
        async {
            let ndjson = """
            {"Service":"other-service","State":"running","ID":"other-1"}
            {"Service":"video-manager","State":"running","ID":"cont-ndjson-999"}
            """
            let mockRunner _dir _cmd =
                async { return 0, ndjson, "" }

            let mockHealth _url =
                async { return false } // ポート疎通は未通

            let! status = DockerController.checkStatus (Some mockRunner) (Some mockHealth) dummyWorkDir
            status.State |> should equal Running
            status.IsPortAccessible |> should equal false
            status.ContainerId |> should equal (Some "cont-ndjson-999")
        }

    [<Fact>]
    let ``checkStatus はコンテナが停止中 (exited) の場合に Stopped を判定する`` () =
        async {
            let json = """[{"Service":"video-manager","State":"exited","ID":"stopped-1"}]"""
            let mockRunner _dir _cmd =
                async { return 0, json, "" }

            let mockHealth _url =
                async { return false }

            let! status = DockerController.checkStatus (Some mockRunner) (Some mockHealth) dummyWorkDir
            status.State |> should equal Stopped
            status.IsPortAccessible |> should equal false
            status.ContainerId |> should equal (Some "stopped-1")
        }

    [<Fact>]
    let ``checkStatus は対象サービスが見つからない場合に NotFound を判定する`` () =
        async {
            let json = "[]"
            let mockRunner _dir _cmd =
                async { return 0, json, "" }

            let mockHealth _url =
                async { return false }

            let! status = DockerController.checkStatus (Some mockRunner) (Some mockHealth) dummyWorkDir
            status.State |> should equal NotFound
            status.IsPortAccessible |> should equal false
            status.ContainerId |> should equal None
        }

    [<Fact>]
    let ``executeAction は docker compose コマンドが成功した際 Ok を返す`` () =
        async {
            let mockRunner _dir cmd =
                async {
                    cmd |> should contain "restart"
                    return 0, "Restarting tag-based-video-manager ... done", ""
                }

            let! result = DockerController.executeAction (Some mockRunner) dummyWorkDir "restart tag-based-video-manager"
            match result with
            | Error err -> failwith $"Failed: {err}"
            | Ok stdout -> stdout |> should contain "done"
        }

    [<Fact>]
    let ``executeAction はコマンドが非0で終了した際 DockerError を返す`` () =
        async {
            let mockRunner _dir _cmd =
                async { return 1, "", "Cannot connect to the Docker daemon" }

            let! result = DockerController.executeAction (Some mockRunner) dummyWorkDir "up -d"
            match result with
            | Ok _ -> failwith "Expected failure"
            | Error (DockerError (cmd, code, stderr)) ->
                code |> should equal 1
                stderr |> should contain "Cannot connect to the Docker daemon"
            | Error other -> failwith $"Unexpected error: {other}"
        }
