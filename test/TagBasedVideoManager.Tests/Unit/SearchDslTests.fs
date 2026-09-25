namespace TagBasedVideoManager.Tests

open Xunit
open FsUnit
open TagBasedVideoManager.Domain

type SearchDslTests () =

    [<Fact>]
    member _.``空文字またはスペースのみの入力は空のリストを返す`` () =
        SearchDsl.parse "" |> should be Empty
        SearchDsl.parse "   " |> should be Empty

    [<Fact>]
    member _.``通常のキーワードはKeywordトークンに分解される`` () =
        let result = SearchDsl.parse "test video"
        result |> should equal [ Keyword "test"; Keyword "video" ]

    [<Fact>]
    member _.``tagプレフィックスはTagNameトークンになる`` () =
        let result = SearchDsl.parse "tag:ツーリング"
        result |> should equal [ TagName "ツーリング" ]

    [<Fact>]
    member _.``favorite指定はFavoriteOnlyトークンになる`` () =
        SearchDsl.parse "is:favorite" |> should equal [ FavoriteOnly true ]
        SearchDsl.parse "favorite:true" |> should equal [ FavoriteOnly true ]

    [<Fact>]
    member _.``複合クエリが正しくトークンリストに分解される`` () =
        let result = SearchDsl.parse "tag:ツーリング is:favorite キャンプ"
        result |> should equal [ TagName "ツーリング"; FavoriteOnly true; Keyword "キャンプ" ]

    [<Fact>]
    member _.``tagプレフィックスの後ろが空の場合はKeywordトークンとして扱う`` () =
        // 境界値: "tag:" のみの場合
        SearchDsl.parse "tag:" |> should equal [ Keyword "tag:" ]
        SearchDsl.parse "tag: " |> should equal [ Keyword "tag:" ]

    [<Fact>]
    member _.``全角スペースや連続する空白文字が正しくトリミングされて分割される`` () =
        // 境界値: 連続スペース、全角スペース混在
        let result = SearchDsl.parse "tag:ツーリング　　is:favorite   F#"
        result |> should equal [ TagName "ツーリング"; FavoriteOnly true; Keyword "F#" ]

    [<Fact>]
    member _.``無効なお気に入り表現はKeywordトークンとして扱う`` () =
        // 異常系/境界値: favorite:false や is:unfavorite など
        SearchDsl.parse "favorite:false" |> should equal [ Keyword "favorite:false" ]
        SearchDsl.parse "is:unfavorite" |> should equal [ Keyword "is:unfavorite" ]
