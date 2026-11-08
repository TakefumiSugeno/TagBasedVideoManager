# Technical Design: Linux (NAME_MAX 255B) 及び Windows (MAX_PATH 260字) のクロスプラットフォーム動画ファイル名制限対応

- **変更名**: `cross-platform-filename-limit`
- **適用規模レベル**: `L2`
- **対象コンポーネント**: `TagBasedVideoManager.Renamer` (Avalonia.FuncUI), `TagBasedVideoManager` (Web バックエンド / Giraffe), `wwwroot` (Web フロントエンド / Alpine.js)

---

## 1. Context (背景とシステム構成)

Windows ホスト上で動画を管理し、Docker コンテナ (Linux) にバインドマウントして Web サーバーを実行する構成において、OS・ファイルシステム間で以下のファイル名仕様の不一致が存在する（詳細は `proposal.md` 参照）。

```
+---------------------------------------------------------------------------------------------------+
|                        Windows (ホスト) × Docker Linux (コンテナ) の 2大制約                      |
+---------------------------------------------------------------------------------------------------+

    【制約 1: Windows ホスト側の門番】                      【制約 2: Linux コンテナ側の門番】
    - 対象: フルパス全体 (D:\Videos\...\file.mp4)           - 対象: ファイル名単体 (file.mp4)
    - 上限: MAX_PATH (260文字)                              - 上限: NAME_MAX (255バイト UTF-8)
    - 安全閾値: 240文字                                     - 安全閾値: 240バイト (日本語約75文字)
    - 超過時: バインドマウント失敗・パス参照不可            - 超過時: ENAMETOOLONG / PathTooLongException
                                                                     (ストリーミング再生クラッシュ)
                                    \                       /
                                     \                     /
                                      v                   v
                    +-----------------------------------------------+
                    |  両方を同時にクリアしないと Docker で動かない！ |
                    +-----------------------------------------------+
```

---

## 2. Goals / Non-Goals

### Goals

1. **Renamer 包括自動抽出**: Windows パス長（文字数 ≧ 240）**または** Linux ファイル名（UTF-8 バイト数 ≧ 240）のどちらかに抵触するファイルを 1 回のスキャンで網羅抽出。
2. **制約種別の可視化**: 抽出されたファイルがどの制約に違反しているか（`[Windows: パス長超過]`、`[Linux: ファイル名超過]`、`[両方超過]`）を BEFORE カードに明示。0件時は安心できる Empty State を表示。
3. **表示フィルタの提供**: 違反種別ごとの絞り込みコンボボックス（「すべて」「Linuxファイル名超過のみ」「Windowsパス長超過のみ」「両方超過」）を提供。
4. **AI プロンプトと AFTER リアルタイム検証の両立**: AI提案時に両制約（UTF-8 240B以内 かつ フルパス240字以内）を指示し、AFTER バッジでリアルタイムに両立安全（`[✓ 安全]`、注意、危険）を保証。同名衝突時の連番付与時も再検証。
5. **Web サーバー Fail-Fast & 例外安全化**: `streamVideo` で `ExecuteAsync` 呼出前にファイル名 UTF-8 バイト数を検査（> 255B なら即時 HTTP 422 返却）。実行時の `PathTooLongException` および `FileNotFoundException` を安全捕捉し、500 クラッシュを防止。診断ログに VideoId, FilePath, Bytes を明記。クライアント切断（`OperationCanceledException`）は静かに終了。
6. **Web UI エラー通知 & オーバーレイ**: `<video>` の `@error` 検知時に `fetch` で HTTP 422 を確認し、赤色トースト通知およびプレイヤー中央に「ファイルパスをコピー」を含むオーバーレイ案内を表示。
7. **Web スキャナー過負荷防止 & 警告記録**: ディレクトリ同期スキャン時、255 バイト超のファイルは DB に登録しつつ `MediaQueue` への投入をスキップし、外部プロセスの無限再投入・滞留を防止。`Db.saveErrorLog` に診断情報付き警告を出力。

### Non-Goals

- カーネルやファイルシステム層での仮想マッピングやハッシュ化による誤魔化し。
- ユーザーの確認を経ないサイレント強制自動リネーム。

---

## 3. Architecture & Technical Decisions

### Decision 1: Renamer における二重制約のモデル化と自動抽出

- **データ構造 (`Domain.fs`)**:
  ```fsharp
  /// 制約違反の種別
  type ConstraintViolation =
      | WindowsPathOnly     // Windows フルパス文字数のみ超過 (>= 240字)
      | LinuxFileNameOnly   // Linux ファイル名 UTF-8 バイト数のみ超過 (>= 240B)
      | BothViolated        // 両方超過

  /// 表示フィルタの選択肢
  type ConstraintFilter =
      | FilterAll           // すべて
      | FilterLinuxOnly     // Linuxファイル名超過のみ
      | FilterWindowsOnly   // Windowsパス長超過のみ
      | FilterBothOnly      // 両方超過
  ```
  `ScanCandidate` および `RenameProposal` に `FileNameBytes: int` と `Violation: ConstraintViolation` を追加保持する。
- **抽出ロジック (`FileScanner.fs`)**:
  ```fsharp
  let fileNameBytes = Encoding.UTF8.GetByteCount(fi.Name)
  let isPathLong = file.Length >= threshold       // デフォルト 240字
  let isNameLong = fileNameBytes >= byteThreshold // デフォルト 240B
  let isTarget = isVideoFile file && (isPathLong || isNameLong) && ...
  ```
- **閾値階層**:
  - `UTF-8 Bytes <= 240`: 安全（Green `[✓ 安全]`）
  - `240 < UTF-8 Bytes <= 255`: 注意（Yellow `[⚠️ Linux注意 (上限間近)]`）
  - `UTF-8 Bytes >= 256`: 危険（Red `[⚠️ Linux危険 (NAME_MAX超過)]`）

### Decision 2: ツールバー表示フィルタと Empty State 設計

- **State (`State.fs`)**:
  - `Model` に `FilterConstraint: ConstraintFilter` を追加（既定値: `FilterAll`）。
  - `Msg` に `SetConstraintFilter of ConstraintFilter` を追加。
- **View (`Views.fs`)**:
  - ツールバーに ComboBox を配置し、選択状態に応じてリストを射影フィルタリング。
  - 候補が 0 件の時、またはフィルタ適用で該当ファイルが 0 件になった際、リスト中央に Empty State（「対象ファイルはありません。すべてのファイルが安全です 🎉」）を表示し、初心者の不安を解消。
  - **BEFORE / AFTER のファイル名開始位置完全一致**: BEFORE行（旧ファイル名）とAFTER行（新ファイル名入力欄）の左側ラベル領域の幅をコンパクトに固定（195px等）し、余分な空白を詰めてファイル名の開始X座標を左に寄せつつ垂直に完全一致させて新旧比較性と視認領域を最大化。

### Decision 3: AI プロンプトへの制約同時注入 & 重複連番サフィックス時の再検証

- **`OpenRouterClient.fs`**:
  `buildUserPrompt` 内に以下を明示:
  ```text
  【制約要件】:
  1. 新ファイル名は拡張子を含めて UTF-8 エンコーディングで 240 バイト以内（全角日本語約75文字以内）に収めること。
  2. 新ファイル名に変更した後のフルパス長が 240 文字以内に収まること。
  3. 作品名・話数等の主要キーワードを優先して残し、余分な記号や冗長な説明文を要約すること。
  ```
- **`FileRenamer.fs` (同名衝突時)**:
  移動先に同名ファイルが存在して `_1.mp4` 等のサフィックスを付加する際、サフィックス付加後のバイト数が 240B を超えないよう、必要に応じてベース名をトリミングして安全マージンを確保。

### Decision 4: Web サーバー (`HttpHandlers.fs`) のストリーミング例外ハンドリングと Fail-Fast

- **処理フロー**:
  ```fsharp
  let streamVideo (id: string) : HttpHandler =
      fun (next : HttpFunc) (ctx : HttpContext) ->
          task {
              let conn = ctx.GetService<IDbConnection>()
              let! videoResult = Db.getVideo conn id
              // ... パス解決 ...
              let resolvedFinalPath = TagBasedVideoManager.Domain.PathHelper.resolvePhysicalPath filePath
              let fileName = Path.GetFileName(resolvedFinalPath)
              let fileNameBytes = Encoding.UTF8.GetByteCount(fileName)

              // 1. Fail-Fast 事前検査 (Linux NAME_MAX 255バイト制限)
              if fileNameBytes > 255 then
                  do! Db.saveErrorLog conn $"[ストリーミング拒否: NAME_MAX超過] VideoId={id}, FilePath={resolvedFinalPath}, Bytes={fileNameBytes}" None |> Async.StartAsTask :> Task
                  ctx.SetStatusCode 422
                  return! json {|
                      code = "FILE_NAME_TOO_LONG"
                      error = "ファイル名が Linux の 255 バイト制限 (NAME_MAX) を超過しているため再生できません。Windows の AI File Renamer 等でファイル名を短縮してください。"
                  |} next ctx
              else
                  try
                      let result = PhysicalFileResult(resolvedFinalPath, "video/mp4")
                      result.EnableRangeProcessing <- true
                      let executor = ctx.RequestServices.GetRequiredService<IActionResultExecutor<PhysicalFileResult>>()
                      let actionContext = ActionContext(ctx, ctx.GetRouteData(), Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor())
                      do! executor.ExecuteAsync(actionContext, result)
                      return! next ctx
                  with
                  | :? System.OperationCanceledException ->
                      // クライアント切断 (シーク・タブ閉じ) は正常終了扱い
                      return! next ctx
                  | :? System.IO.PathTooLongException as ex ->
                      do! Db.saveErrorLog conn $"[ストリーミング失敗: PathTooLongException] VideoId={id}, FilePath={resolvedFinalPath}, Bytes={fileNameBytes}, Msg={ex.Message}" (Some (ex.ToString())) |> Async.StartAsTask :> Task
                      ctx.SetStatusCode 422
                      return! json {| code = "FILE_NAME_TOO_LONG"; error = "ファイル名が長すぎるため再生できません。" |} next ctx
                  | :? System.IO.FileNotFoundException as ex when fileNameBytes > 255 ->
                      // Linux 上で stat() が ENAMETOOLONG により FileInfo.Exists が false となりスローされるケースの安全捕捉
                      do! Db.saveErrorLog conn $"[ストリーミング失敗: NAME_MAX起因FileNotFound] VideoId={id}, FilePath={resolvedFinalPath}, Bytes={fileNameBytes}" (Some (ex.ToString())) |> Async.StartAsTask :> Task
                      ctx.SetStatusCode 422
                      return! json {| code = "FILE_NAME_TOO_LONG"; error = "ファイル名が Linux の 255 バイト制限を超過しているため再生できません。" |} next ctx
          }
  ```

### Decision 5: Web UI (`app.js`, `index.html`) のエラー通知 & オーバーレイ

- **フロントエンドの挙動**:
  - `<video id="mainPlayer" @error="handleVideoError($event)">` を配置。
  - 再生エラー発火時、`fetch(videoStreamUrl, { method: 'GET', headers: { Range: 'bytes=0-0' } })` を発行して HTTP ステータスとレスポンス JSON を取得。
  - ステータスが 422 かつ `FILE_NAME_TOO_LONG` の場合:
    1. 赤色トースト通知: `this.showToast("⚠️ 再生エラー: ファイル名が Linux の 255 バイト制限を超過しています。", "error")`
    2. プレイヤー中央オーバーレイ:
       - 「動画を再生できません」
       - 「ファイル名が Linux の 255 バイト制限を超過しています。(UTF-8: 〇〇B / 上限: 255B)」
       - 「ファイルパスをコピー」ボタン（クリックでクリップボードに `video.filePath` をコピーしトーストで「コピーしました」を表示）

### Decision 6: Web スキャナー (`Scanner.fs`) の事前検知 & MediaQueue 投入スキップ

- **処理フロー**:
  - `syncVideoDirectories` 内で各ファイルを DB 登録する際、ファイル名 UTF-8 バイト数を検査。
  - `bytes > 255` の場合:
    - DB 登録は正常に実行（一覧表示を維持）。
    - `ErrorLogs` に `VideoId`, `FilePath`, `Bytes` を明記した警告ログを 1 回記録。
    - **重要: `MediaQueue.enqueue` への投入をスキップ**。これにより、Linux 上で確実に失敗する FFmpeg プロセスの無限起動・滞留を防ぎ、正常な動画のメタデータ処理キューをブロックしない。

---

## 4. Risks & Mitigations

| リスク                                                                                            | 影響度 | 対策 / 緩和策                                                                                                                                               |
| :------------------------------------------------------------------------------------------------ | :----: | :---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Linux カーネルの `stat()` が `PathTooLongException` ではなく `FileNotFoundException` を投げる** |   高   | ストリーミング実行前にファイル名バイト数（> 255B）を検査して直ちに 422 を返す Fail-Fast 設計とし、例外ハンドラでも `FileNotFoundException` を安全捕捉する。 |
| **定期スキャン実行時に失敗確定の FFmpeg プロセスが滞留する**                                      |   高   | 255B 超のファイルは `MediaQueue` への投入をスキップし、他の正常な動画のバックグラウンド処理への悪影響を完全に遮断する。                                     |
| **同名衝突時に連番サフィックス付与で上限を再超過する**                                            |   中   | `FileRenamer.fs` において、連番サフィックス付加後のバイト数が 240B / 240字を超えないよう、ベース名を事前にトリミングして安全マージンを確保する。            |
