namespace TagBasedVideoManager.Tests

open Xunit

[<assembly: CollectionBehavior(DisableTestParallelization = true)>]
do ()

module Tests =
    open Xunit

    [<Fact>]
    let ``My test`` () =
        Assert.True(true)
