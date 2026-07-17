namespace TagBasedVideoManager.Domain

open System

/// 検索DSLのトークン
type SearchToken =
    | TagName of string
    | FavoriteOnly of bool
    | Keyword of string
    | AndOp
    | OrOp

type SearchQuery = SearchToken list

module SearchDsl =
    /// 検索文字列を解析して AST に変換する関数
    let parse (input: string) : SearchQuery =
        if String.IsNullOrWhiteSpace(input) then []
        else
            input.Split([|' '; '　'|], StringSplitOptions.RemoveEmptyEntries)
            |> Array.map (fun term ->
                let lowerTerm = term.ToLower()
                if lowerTerm = "or" then OrOp
                elif lowerTerm = "and" then AndOp
                elif term.StartsWith("tag:") && term.Length > 4 then
                    TagName (term.Substring(4))
                elif term = "is:favorite" || term = "favorite:true" then
                    FavoriteOnly true
                else
                    Keyword term
            )
            |> Array.toList
