namespace TagBasedVideoManager.Tests.Unit

open Xunit
open FsUnit
open System
open TagBasedVideoManager.Domain

/// <summary>
/// TaggingEngine の動作を検証する単体テストモジュール。
/// </summary>
module TaggingEngineTests =

    /// <summary>
    /// matchesPattern によるパターンマッチ判定（前方一致、後方一致、部分一致、正規表現）の検証。
    /// </summary>
    [<Fact>]
    let ``matchesPattern should support various match types`` () =
        // matchesPattern はプライベート関数だが、isRuleMatch を介して間接的に評価可能。
        let createRule pattern matchType target =
            { Id = "r1"; Pattern = pattern; TagId = "t1"; MatchType = matchType; TargetField = target; MinSize = None; MaxSize = None; CreatedAt = DateTime.Now }

        // prefix
        let rulePrefix = createRule "target" "prefix" "fileName"
        TaggingEngine.isRuleMatch rulePrefix "target_video.mp4" "path" 100L |> should be True
        TaggingEngine.isRuleMatch rulePrefix "video_target.mp4" "path" 100L |> should be False

        // suffix
        let ruleSuffix = createRule "target" "suffix" "fileName"
        TaggingEngine.isRuleMatch ruleSuffix "video_target" "path" 100L |> should be True
        TaggingEngine.isRuleMatch ruleSuffix "target_video" "path" 100L |> should be False

        // regex
        let ruleRegex = createRule @"^\d{3}_video" "regex" "fileName"
        TaggingEngine.isRuleMatch ruleRegex "123_video.mp4" "path" 100L |> should be True
        TaggingEngine.isRuleMatch ruleRegex "a123_video.mp4" "path" 100L |> should be False

        // partial
        let rulePartial = createRule "mid" "partial" "fileName"
        TaggingEngine.isRuleMatch rulePartial "video_mid_test.mp4" "path" 100L |> should be True
        TaggingEngine.isRuleMatch rulePartial "video.mp4" "path" 100L |> should be False

    /// <summary>
    /// TargetField (fileName, filePath, folderName) に応じたマッチング対象の切り替え検証。
    /// </summary>
    [<Fact>]
    let ``isRuleMatch should target correct field`` () =
        let createRule pattern target =
            { Id = "r1"; Pattern = pattern; TagId = "t1"; MatchType = "partial"; TargetField = target; MinSize = None; MaxSize = None; CreatedAt = DateTime.Now }

        let fileName = "video.mp4"
        let filePath = @"C:\Videos\Family\video.mp4"

        // fileName
        let ruleFile = createRule "video" "fileName"
        TaggingEngine.isRuleMatch ruleFile fileName filePath 100L |> should be True

        // filePath
        let rulePath = createRule "Family" "filePath"
        TaggingEngine.isRuleMatch rulePath fileName filePath 100L |> should be True

        // folderName
        let ruleFolder = createRule "Family" "folderName"
        TaggingEngine.isRuleMatch ruleFolder fileName filePath 100L |> should be True
        // 親の親（Videos）は folderName の対象外（folderName は直接の親フォルダ名のみ）
        let ruleGrandFolder = createRule "Videos" "folderName"
        TaggingEngine.isRuleMatch ruleGrandFolder fileName filePath 100L |> should be False

    /// <summary>
    /// MinSize および MaxSize によるファイルサイズ制限の検証。
    /// </summary>
    [<Fact>]
    let ``isRuleMatch should constrain by file size in MB`` () =
        let createRule minSize maxSize =
            { Id = "r1"; Pattern = ""; TagId = "t1"; MatchType = "partial"; TargetField = "fileName"; MinSize = minSize; MaxSize = maxSize; CreatedAt = DateTime.Now }

        let mb = 1024L * 1024L
        let size50MB = 50L * mb
        let size100MB = 100L * mb
        let size150MB = 150L * mb

        // 50MB〜100MB
        let ruleRange = createRule (Some 50L) (Some 100L)
        TaggingEngine.isRuleMatch ruleRange "video.mp4" "path" size50MB |> should be True
        TaggingEngine.isRuleMatch ruleRange "video.mp4" "path" size100MB |> should be True
        TaggingEngine.isRuleMatch ruleRange "video.mp4" "path" size150MB |> should be False
        TaggingEngine.isRuleMatch ruleRange "video.mp4" "path" (size50MB - 1L) |> should be False

        // 50MB以上（上限なし）
        let ruleMinOnly = createRule (Some 50L) None
        TaggingEngine.isRuleMatch ruleMinOnly "video.mp4" "path" size150MB |> should be True

        // 100MB以下（下限なし）
        let ruleMaxOnly = createRule None (Some 100L)
        TaggingEngine.isRuleMatch ruleMaxOnly "video.mp4" "path" size50MB |> should be True

    /// <summary>
    /// 旧バージョン互換用 isMatch メソッドの検証。
    /// </summary>
    [<Fact>]
    let ``isMatch should perform case-insensitive partial match on filename`` () =
        TaggingEngine.isMatch "test" "my_test_video.mp4" |> should be True
        TaggingEngine.isMatch "TEST" "my_test_video.mp4" |> should be True
        TaggingEngine.isMatch "invalid" "my_test_video.mp4" |> should be False
        TaggingEngine.isMatch "" "video.mp4" |> should be False

    /// <summary>
    /// evaluateRulesFull による複数ルール一括評価とマッチ結果（タグID）の抽出・重複排除の検証。
    /// </summary>
    [<Fact>]
    let ``evaluateRulesFull should evaluate rules list and return unique tag ids`` () =
        let rules = [
            { Id = "r1"; Pattern = "action"; TagId = "t_action"; MatchType = "partial"; TargetField = "fileName"; MinSize = None; MaxSize = None; CreatedAt = DateTime.Now }
            { Id = "r2"; Pattern = "comedy"; TagId = "t_comedy"; MatchType = "partial"; TargetField = "fileName"; MinSize = None; MaxSize = None; CreatedAt = DateTime.Now }
            { Id = "r3"; Pattern = "movie"; TagId = "t_action"; MatchType = "partial"; TargetField = "fileName"; MinSize = None; MaxSize = None; CreatedAt = DateTime.Now } // 同じ tagId
        ]

        let result = TaggingEngine.evaluateRulesFull rules "action_movie.mp4" "path" 100L
        result |> should contain "t_action"
        result.Length |> should equal 1 // 重複排除されること
