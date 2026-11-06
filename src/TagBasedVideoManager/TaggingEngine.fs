namespace TagBasedVideoManager.Domain

open System
open System.Text.RegularExpressions

module TaggingEngine =
    
    /// パターンとマッチタイプに従って、对象のテキストと一致するか判定する
    let private matchesPattern (pattern: string) (matchType: string) (text: string) : bool =
        if String.IsNullOrWhiteSpace(pattern) || text = null then false
        else
            match matchType with
            | "prefix" -> text.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)
            | "suffix" -> text.EndsWith(pattern, StringComparison.OrdinalIgnoreCase)
            | "regex"  -> 
                try Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase)
                with _ -> false
            | _        -> // "partial" もしくは未知の場合は部分一致
                text.Contains(pattern, StringComparison.OrdinalIgnoreCase)

    /// 容量単位のバイト数に変換（MBを内部単位とする）
    let private mbToBytes (mb: int64) : int64 = mb * 1024L * 1024L

    /// 1つのルールが、小数点以下のフィールドの値に一致するか判定する
    let isRuleMatch (rule: TaggingRule) (fileName: string) (filePath: string) (fileSize: int64) : bool =
        // targetFieldに応じたテキストマッチング
        let textMatch =
            if String.IsNullOrWhiteSpace(rule.Pattern) then true
            else
                match rule.TargetField with
                | "filePath"   -> matchesPattern rule.Pattern rule.MatchType filePath
                | "folderName" ->
                    let folder = System.IO.Path.GetDirectoryName(filePath)
                    let folderName = System.IO.Path.GetFileName(folder)
                    matchesPattern rule.Pattern rule.MatchType folderName
                | _            -> // "fileName" デフォルト
                    matchesPattern rule.Pattern rule.MatchType fileName

        // ファイルサイズ条件（内部単位: MB。Noneの場合はチェックしない）
        let sizeMatch =
            let minOk = match rule.MinSize with Some mb -> fileSize >= mbToBytes mb | None -> true
            let maxOk = match rule.MaxSize with Some mb -> fileSize <= mbToBytes mb | None -> true
            minOk && maxOk

        textMatch && sizeMatch

    /// 下位互換: ファイル名のみで判定する旧版 isMatch
    let isMatch (pattern: string) (fileName: string) : bool =
        if String.IsNullOrWhiteSpace(pattern) || String.IsNullOrWhiteSpace(fileName) then
            false
        else
            fileName.Contains(pattern, StringComparison.OrdinalIgnoreCase)

    /// 登録済みのルール一覧から、小数点以下のフィールドの値に一致するタグIDの一覧を抽出する
    let evaluateRules (rules: TaggingRule list) (videoName: string) : string list =
        rules
        |> List.filter (fun rule -> isMatch rule.Pattern videoName)
        |> List.map (fun rule -> rule.TagId)
        |> List.distinct

    /// 拡張版: ファイル名・パス・サイズを包含したルール評価
    let evaluateRulesFull (rules: TaggingRule list) (fileName: string) (filePath: string) (fileSize: int64) : string list =
        rules
        |> List.filter (fun rule -> isRuleMatch rule fileName filePath fileSize)
        |> List.map (fun rule -> rule.TagId)
        |> List.distinct
