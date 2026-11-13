# Design: 設定ファイルの一本化（.env 廃止・appsettings.{env}.json 統一）および AI File Renamer ローディングスピナー UI

## Context

現在、本リポジトリは以下の 2 点で構造的な不整合と UX 課題を抱えています。

1. **設定管理の二重化と Git 追跡漏れ**:
   - Web アプリ（`TagBasedVideoManager`）は `Program.fs` 独自の `loadDotEnv ()` と `Environment.GetEnvironmentVariable` に依存しており、デプロイスクリプト（`scripts/deploy.ps1`, `scripts/deploy.sh`）も `.env` を前提としています。一方、`src/TagBasedVideoManager/appsettings.Development.json` は中身が空同然のまま Git 追跡されています。
   - デスクトップアプリ（`TagBasedVideoManager.Renamer`）は `.env`、`appsettings.json`、`appsettings.{Environment}.json`、さらに既定ディレクトリでの `VIDEO_DIR` OS 環境変数を混在参照しており、`Settings.resolveSavePath` は常に `appsettings.json` へ保存するため、環境別ファイル（`appsettings.Development.json` / `appsettings.Production.json`）への書き戻しができません。また、`src/TagBasedVideoManager.Renamer/appsettings.Production.json` が Git 追跡されたまま `.gitignore` に未登録となっています。
2. **AI File Renamer の非同期ローディング視覚フィードバック不足（Step 2）**:
   - `State.fs` は `IsScanning`, `IsRequestingAi`, `IsRenaming`, `IsDockerBusy` と `Candidate.IsAiProcessing` をすでに状態として保持していますが、`Views.fs` ではスキャン中に未実行プレースホルダーが表示されたままになり、AI 提案中・リネーム中・コンテナ操作中にも回転スピナーが表示されないため、処理進行が直感的に分かりません。

---

## Goals / Non-Goals

**Goals:**

- Web / Renamer / デプロイスクリプト全体から `.env`（`.env.sample`・`loadDotEnv`・`parseDotEnv`）および Renamer の汎用 OS 環境変数（`VIDEO_DIR` 等）参照を完全に撤廃し、`appsettings.json` ＋ Git 管理外の環境別ファイル（`appsettings.Development.json` / `appsettings.Production.json`）へ一本化する。
- Renamer の設定保存先（`Settings.resolveSavePath`）を現在アクティブな環境名に基づく `appsettings.{Environment}.json` とし、書込権限不足時の `%APPDATA%` フォールバックと次回起動時の最優先再読込を整合させる。
- Web アプリを ASP.NET Core 標準 `IConfiguration` ベースの参照へ移行し、`deploy.ps1` / `deploy.sh` が Web 側の `appsettings.json` ＋ `appsettings.{Environment}.json`（既定 `Production`）を読み取り、CLI引数・シェル環境変数による上書きとコンテナ `-e` 注入を行えるようにする。
- AI File Renamer において、`IsScanning`, `IsRequestingAi` (`IsAiProcessing`), `IsRenaming`, `IsDockerBusy` の全 4 非同期処理に対し、MVU 差分更新に負荷を掛けない Avalonia ネイティブアニメーションの回転スピナーを表示し、異常終了・キャンセル時の確実な解除と回復導線を保証する。
- L2 変更として Web を `0.2.0` → `0.3.0`、Renamer を `0.1.0` → `0.2.0` にインクリメントする。

**Non-Goals:**

- Web UI（`src/TagBasedVideoManager/wwwroot/`）の追加スピナー改修（Step 1 `web-loading-spinners` で完了済み）
- 新規 NuGet パッケージの導入（標準 `System.Text.Json`, `Microsoft.Extensions.Configuration`, Avalonia 11 標準アニメーションのみ使用）

---

## Decisions

### 1. 設定ファイルの Git 管理境界とクリーンテンプレート化

| ファイルパス                                                    | Git 管理                      | 役割・ポリシー                                                                                                                                                              |
| :-------------------------------------------------------------- | :---------------------------- | :-------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `src/TagBasedVideoManager/appsettings.json`                     | **追跡（維持）**              | Web 共通デフォルト値（`PORT: 5620`, `VIDEO_DIRS: ""`, `THUMBNAIL_DIR: "thumbnails"`, `DATABASE_PATH: "metadata.db"`, `DATA_DIR: "./data"`）。秘密値・個人パス禁止           |
| `src/TagBasedVideoManager/appsettings.Development.json`         | **除外（`git rm --cached`）** | 開発用ローカル上書き実体（`.gitignore` 登録）                                                                                                                               |
| `src/TagBasedVideoManager/appsettings.Production.json`          | **除外（新規ルール）**        | 本番デプロイ用ローカル上書き実体（`.gitignore` 登録）                                                                                                                       |
| `src/TagBasedVideoManager.Renamer/appsettings.json`             | **追跡（維持）**              | Renamer 共通テンプレート（`targetDirectory: ""`, `pathLengthThreshold: 240`, `selectedModel`, `apiKey: null`, `rules`）。`E:\e` 等のローカル固有パス・実 API キーを含めない |
| `src/TagBasedVideoManager.Renamer/appsettings.Development.json` | **除外（維持）**              | Renamer 開発環境用実ファイル（`.gitignore` 登録）                                                                                                                           |
| `src/TagBasedVideoManager.Renamer/appsettings.Production.json`  | **除外（`git rm --cached`）** | Renamer 本番環境用実ファイル（`.gitignore` 登録）                                                                                                                           |
| `.env.sample` / `.env`                                          | **廃止・削除**                | `.env.sample` は削除。`.env` 読み込みコードも削除（誤残存 `.env` があっても読まない）                                                                                       |

- **`.gitignore` 追加ルール**:
  - 既存の `**/appsettings.Development.json` に加え、`**/appsettings.Production.json` を追加する。
  - 既に Git インデックスへ登録されている `src/TagBasedVideoManager/appsettings.Development.json` と `src/TagBasedVideoManager.Renamer/appsettings.Production.json` は `git rm --cached` で追跡解除し、開発環境のローカル実ファイルは削除せず維持する。
- **`.fsproj` 条件付きビルド配置**:
  - `src/TagBasedVideoManager.Renamer/TagBasedVideoManager.Renamer.fsproj` において `appsettings.Development.json` と `appsettings.Production.json` の双方に `Condition="Exists('...')" ` を付与し、クローン直後・CI 環境等で環境別ファイルが存在しない場合でもビルドが失敗しないことを保証する。

---

### 2. Web アプリケーション & デプロイスクリプトの設定統合 (`IConfiguration`)

#### Web 設定スキーマ (`src/TagBasedVideoManager/appsettings.json`)

ASP.NET Core `IConfiguration` は JSON ルートのフラットキーとコンテナ環境変数名を完全一致（大文字小文字非区別、ただしアンダースコア有無は一致が必要）させることで、無変換でオーバーライド可能になります。

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "PORT": 5620,
  "VIDEO_DIRS": "",
  "THUMBNAIL_DIR": "thumbnails",
  "DATABASE_PATH": "metadata.db",
  "DATA_DIR": "./data"
}
```

#### Web ランタイム参照優先順位（.NET 標準 `WebApplication.CreateBuilder`）

```
(低) appsettings.json
  └─▶ appsettings.{ASPNETCORE_ENVIRONMENT}.json
        └─▶ コンテナ / OS 環境変数 (PORT, VIDEO_DIRS, THUMBNAIL_DIR, DATABASE_PATH)
              └─▶ コマンドライン引数 (高)
```

- `Program.fs`: `loadDotEnv ()` を削除。`WebApplication.CreateBuilder(args)` 構築後、`builder.Configuration` から `"DATABASE_PATH"`, `"THUMBNAIL_DIR"`, `"PORT"` を読み取るヘルパーを定義。`THUMBNAIL_DIR` と同様に `DATABASE_PATH` の親ディレクトリ（`Path.GetDirectoryName`）が存在しない場合は `Directory.CreateDirectory` で自動作成し、`SQLite Error 14` 起動失敗を防止する。起動ログには構成環境名・`PORT`・`THUMBNAIL_DIR`・`DATABASE_PATH` に加え、解決済み **`VIDEO_DIRS`（有効件数およびパス一覧）** を標準出力へ出力する。
- `HttpHandlers.fs`: `triggerScan` は `ctx.GetService<IConfiguration>()` を優先参照（テストの `TestHost` / 手動 DI 環境で `IConfiguration` 未登録時のみ安全フォールバック）し、スキャン時も解決済み `VIDEO_DIRS` をログ出力する。
- `VideoDirs.fs`: 既存の `VideoDirs.fromEnvironment (getConfig: string -> string option)` はそのまま汎用ルックアップ関数として活用し、`IConfiguration` 用ラッパー（例: `fun key -> config.[key] |> Option.ofObj`）を渡せるようにする。

#### デプロイスクリプト (`scripts/deploy.ps1`, `scripts/deploy.sh`, `scripts/common.ps1`)

- `.env` 読み込みブロック（`Get-Content $envFile` / `parse_env`）を削除。
- `Resolve-WebDeploySettings`（`common.ps1` / Windows）は標準の `ConvertFrom-Json` を用いて解析する。
- **Linux (`deploy.sh`) JSON 解析方式**: `python3`（最優先）または `jq` の信頼できる JSON パーサのみを使用し、**シェルの grep/sed/awk による曖昧な軽量フォールバックおよび `eval`/`source` は一切行わない**。`python3` と `jq` のいずれも存在しない環境、または JSON 構文エラー（不正フォーマット）検出時は、ビルドや既存コンテナ停止を一切行わず、「`python3` または `jq` をインストールしてください」または構文エラーファイル名を明示して即座に非 0 終了（Fail-Fast）させる。
- **デプロイスクリプト設定解決優先順位**:
  1. CLI 引数（例: `-VideoDirs`, `-Port`, `-DataDir`, `-Environment`）
  2. シェル環境変数（`VIDEO_DIRS`, `PORT`, `DATA_DIR`）
  3. `src/TagBasedVideoManager/appsettings.{Environment}.json`（既定 `Environment = "Production"`、未指定時は `ASPNETCORE_ENVIRONMENT` → `"Production"`）
  4. `src/TagBasedVideoManager/appsettings.json`
  5. スクリプト組み込み既定値（`PORT=5620`, `DATA_DIR=$repoRoot/data`。※ `VIDEO_DIRS` は既定値なし、空のままならビルド前エラー終了）
- **コンテナ起動時の注入**: 従来通りホストパス検証＋`/app/videos/<leaf>:ro` マウント組み立て後、`-e PORT=... -e VIDEO_DIRS=... -e THUMBNAIL_DIR=... -e DATABASE_PATH=...` を注入するため、コンテナ内部では `IConfiguration` の環境変数層が確実に適用される。

---

### 3. Renamer 設定の完全一本化とアクティブ環境別保存 (`Settings.fs`)

#### 環境名の決定とサニタイズ (`resolveEnvironmentName`)

1. `DOTNET_ENVIRONMENT`（空白除去後、有効文字列なら採用）
2. `ASPNETCORE_ENVIRONMENT`
3. `#if DEBUG` → `"Development"` / `#else` → `"Production"`

- **セキュリティ/不正値ガード**: 指定値にパス区切り（`/`, `\`, `..`, 不正ファイル名文字）や空白のみが含まれる場合は無効とみなし、警告ログを出力してコンパイル時既定環境名（`Development` / `Production`）へ安全フォールバックする。

#### 読み込みレイヤー優先順位（修正前との重要な差分）

```
1. 組み込み既定値 (defaultSettingsWithoutEnv: TargetDirectory = カレントではなく未設定 "" または test/videos 存在時のみ、OS環境変数 VIDEO_DIR は非参照)
2. 探索ベース階層（優先順: プロジェクトソース直下 -> カレント -> 実行ディレクトリ）で見つかった先頭ディレクトリの:
   a. appsettings.json
   b. appsettings.{Environment}.json
3. ユーザー個別領域 (%APPDATA%\TagBasedVideoManager\) のオーバーライド（常に上位層として存在するキーのみ上書き）:
   a. appsettings.json
   b. appsettings.{Environment}.json
```

> [!NOTE]
> **設計上の改善ポイント**: 従来実装では `BaseDir` に `appsettings.json` があると `%APPDATA%` を探索せず終了していたため、実行ディレクトリが書込不可（Read-Only）で `%APPDATA%` にフォールバック保存した設定が次回起動時に読めない構造的欠陥がありました。%APPDATA% を「常に上位オーバーライド層」として重ねることで、通常ポータブル運用・開発運用と Read-Only 環境フォールバックの双方で保存と再読込が 100% 一貫します。

※ `loadConfigurationWithConfig` における旧 `.env` パース処理 (`parseDotEnv`) は廃止。起動時に `.env` が存在する場合でも読み込まず無視し、廃止・移行を促す警告ログを記録する。また、`Settings.fs` の `Rules` マージにおいて上位層（`envOpt` 等）で `"rules"` プロパティが「未定義 (`None`)」の場合のみ下位層を継承し、明示的に配列が定義された場合（空配列含む、ただしベース初期テンプレート欠損時を除く）は意図しない下位ルールのゾンビ復元を防ぐ設計とする。テスト互換性・単一ファイル明示読み込みのために引数インターフェースを整理（`envNameOpt` / `baseDirOpt` / `explicitJsonPathOpt` 等）し、テストでは `%APPDATA%` への副作用なしに一時ディレクトリ指定のみで決定論的に検証可能にする。さらに `Settings.loadWithDiagnostics` により JSON 構文エラー時はクラッシュせず下位層へフォールバックし、画面エラーバナー用の診断メッセージを保持する。

#### 環境別保存先決定 (`resolveSavePath`) と GUID 一意一時ファイルによるアトミック保存 (`Settings.save`)

1. **保存先ディレクトリ候補の決定**:
   - まずベース探索階層（プロジェクト設定ディレクトリ / 実行ディレクトリ `BaseDirectory`）のディレクトリに `appsettings.json` または `appsettings.{Environment}.json` が存在し、かつ書込可能かを検査。
   - 書込不可（`UnauthorizedAccessException` / 権限不足）または上記ベースディレクトリに構成ファイルが存在しない場合は、`%APPDATA%\TagBasedVideoManager\` を保存先ディレクトリとする。
   - 出力ファイル名は常に `appsettings.{Environment}.json`（例: `appsettings.Development.json` / `appsettings.Production.json`）とする。ベース `appsettings.json` は上書きしない。
2. **部分オーバーライド JSON の安全なマージ保存 & GUID 一意一時ファイルによるアトミック書き込み**:
   - 既に保存先 `appsettings.{Environment}.json` に設定項目が存在する場合、UI から一部設定変更を保存する際、既存環境ファイルおよび有効設定を壊さず更新する。
   - 連続操作・並行書き込み競合による `IOException` を防止するため、固定 `.tmp` 名ではなく **同一ディレクトリ内の一意名一時ファイル（`appsettings.{env}.json.{Guid.NewGuid():N}.tmp`）** へ書き込み後に `File.Move(tmpPath, targetPath, overwrite = true)` でアトミック置換する（例外時は一時ファイルを `try File.Delete tmpPath with _ -> ()` で確実にクリーンアップ）。
   - 保存失敗時（I/O エラー・権限エラー）は `Error (IoError ...)` を返し、`State.fs` で握りつぶさず `ErrorMessage` とログへ失敗理由＋解決策（書込権限・保存先パス）を通知し、メモリ上の編集状態は保持する。

---

### 4. AI File Renamer ローディングスピナー UI & 状態管理 (`State.fs`, `Views.fs`, `Domain.fs`)

#### 確定実装方式：Avalonia 11 カスタム `SpinnerControl`（ネイティブ回転アニメーション一本化）

- Avalonia.FuncUI で `Model` にタイマー角度を持たせて毎フレーム再描画すると、多数の候補カードと TextBox 全体へ不要な Virtual DOM 差分計算が走り入力遅延の原因になります。
- 本設計では、**`Views.fs` 内に `SpinnerControl : Control`（円弧 `Path` 描画 ＋ `Avalonia.Animation.Animation` による `RotateTransform.AngleProperty` 0°→360°・`IterationCount.Infinite` を適用した単一カスタムコントロール）を実装し、`ViewBuilder.Create<SpinnerControl>` で呼び出す方式に一本化**します（`DispatcherTimer` も不要）。
- UI ツリーから該当条件が `false` になってノードが除去されると Avalonia アニメーションエンジン側で自動停止・解放されるため、**CPU 負荷・MVU 再描画・タイマーリークがゼロ**となります。

#### 状態遷移・入力排他・個別カード失敗＆回復設計 (`Domain.fs` / `State.fs`)

- **スキャン結果 0 件の区別 (`HasScanned: bool`, `ScannedVideoCount: int`)**:
  - `TargetDirectory` が空のクリーン状態では一覧にフォルダ未設定 Zero-State ガイダンスを表示し、そのまま誤スキャンを実行しない。
  - `FileScanner` の走査結果で動画総本数 `ScannedVideoCount` を保持し、「動画 0 本」と「N 本検査し制限超過 0 本（安全）」を出し分ける。
- **スキャン・リネーム中の UI 入力排他ガード (`State.update`)**:
  - `model.IsScanning || model.IsRenaming` の間は `TargetDirectoryChanged`, `ThresholdChanged`, `RuleSelected`, `ModelSelected`, `ToggleFilterNonJapaneseOnly` を `State.update` 側で無視（`model, Cmd.none`）し、UI コントロールも非活性化する。
- **個別カード単位の AI 失敗 Error-State と単独再試行**:
  - `RenameProposal` に `AiError: string option` を追加し、個別ファイルの通信失敗（429 / タイムアウト / 5xx）時は他カードを継続しつつ該当カードのみ赤枠エラー（失敗理由＋「↻ 再試行」ボタン）を表示し、`RetryCandidateAiProposal of string` メッセージで該当ファイルのみ単独再提案を実行できる。

#### 各非同期状態における UI 挙動・排他・異常回復マトリクス

| フラグ / 状態                                                  | スピナー表示箇所 & 文言                                                                                                                                                                      | UI ガード・誤表示防止                                                                                 | 正常完了 / キャンセル / 異常終了時                                                                                                                                                |
| :------------------------------------------------------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | :---------------------------------------------------------------------------------------------------- | :-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **`TargetDirectory = ""`**                                     | 一覧中央にフォルダ選択ガイダンス (`📁 参照...`)                                                                                                                                              | 未設定のまま即エラーになる導線を防止                                                                  | フォルダ設定後にスキャン実行可能                                                                                                                                                  |
| **`IsScanning = true`**                                        | 候補リスト中央パネル（`64px` リングスピナー + `動画ファイルをスキャン中...`）                                                                                                                | 未実行案内・Empty State 完全非表示。フォルダ/閾値/ルール/モデル変更を `State.update`・UI 双方でロック | `ScanCompleted (Ok ([], 0))` → 「動画0本」案内。<br>`ScanCompleted (Ok ([], N>0))` → 「N本検査・全件安全」。<br>`ScanCompleted (Error _)` → スピナー解除 + 回復導線付きエラー表示 |
| **`IsRequestingAi = true`** (`c.IsAiProcessing` / `c.AiError`) | 1) 処理中カードに `14px` スピナー + `🌐 検索・AI提案中...`<br>2) 個別失敗カードに赤枠 `⚠️ 提案失敗` + `↻ 再試行`<br>3) フッター進捗ピル内に `14px` スピナー + `🤖 AI提案中... (完了/総件数)` | 実行中ボタン文字 `⟳ 新条件で再実行`、`⏹ 中止` ボタン Enabled、リネーム実行ボタン群 Disabled           | 1件毎完了で反映。個別失敗は該当カードのみ `IsAiProcessing = false` + `AiError = Some msg`。<br>`⏹ 中止` / 全体失敗 → 全残行 `IsAiProcessing = false`, `IsRequestingAi = false`    |
| **`IsRenaming = true`**                                        | 候補リスト全体の上に半透明暗転オーバーレイ (`#cc0f172a`) + 中央カード＋`48px` スピナー (`ファイル名を変更中...` / `変更を元に戻しています...`)                                               | 背後カード編集・チェック操作・入力系・ボタンを完全遮断                                                | `RenameCompleted` / `UndoCompleted`（成功・部分失敗・衝突エラーすべて）→ 必ず `IsRenaming = false` に戻してオーバーレイ解除＋結果/エラー表示                                      |
| **`IsDockerBusy = true`**                                      | 上部コンテナバーのステータスバッジ横に `14px` 小型スピナー + `コンテナ操作中...`                                                                                                             | Start / Stop / Restart ボタン Disabled                                                                | `DockerActionCompleted`（成功・`TimeoutError`・`ContainerError` すべて）→ `IsDockerBusy = false` に戻し、最新ステータス再取得・エラー表示                                         |

#### アクティブ構成環境バッジ (`Views.fs` ヘッダー右側 / ステータスバー)

- 画面上部に環境バッジ（`DEV` = 青系ピル / `PROD` = 緑系ピル 等）と設定保存先パス表示（`appsettings.Development.json` / `appsettings.Production.json`、ホバーツールチップでフルパスを表示）を配置する。

---

## 故障シミュレーション・異常系対策 & 可観測性 (Observability)

1. **保存先ディレクトリ書込不可・途中失敗への耐性**:
   - `resolveSavePath` 時に一時書き込みテストまたは `trySave` の `UnauthorizedAccessException` 捕捉により `%APPDATA%\TagBasedVideoManager\appsettings.{Environment}.json` へ自動フォールバック。
   - アトミック置換 (`tmp` -> `File.Move`) により、保存中のプロセス強制終了やディスク容量不足時でも既存の設定ファイルが壊れない。
   - 従来の `State.fs` L285, L746, L772 等にあった `Settings.save ... |> ignore`（保存失敗の握りつぶし）を廃止し、`Error` 時は `ErrorMessage` とログへ失敗理由＋解決策（書込権限・保存先パス）を通知する。
2. **外部依存ハング（OpenRouter API / `ddgs` / Docker CLI）時のスピナー残留防止**:
   - `OpenRouterClient`（HTTP タイムアウト + `CancellationToken`）・`WebSearchClient`（プロセスタイムアウト）・`ContainerController`（コマンドタイムアウト）がどの例外/キャンセル経路で終了しても、Elmish の完了メッセージハンドラで**例外なく全ビジーフラグを `false` へ戻す**。
   - `ExecuteScanAndPropose` 中の再実行時は旧 `CancellationTokenSource` を即時 `Cancel()` し、遅れて到着した旧リクエスト結果によって新スキャン/新スピナー状態が上書き破壊されないよう、リクエスト世代番号（またはキャンセル済み判定）でガードする。
3. **可観測性 (ログ出力)**:
   - Web アプリ起動時: `[Config] Environment={Env}, Port={Port}, ThumbnailDir={Dir}, DatabasePath={Db}, VideoDirs(count={N})=[...]`（資格情報は含まない）およびスキャン時の有効 `VIDEO_DIRS` 解決ログを標準出力へ記録し、1分で設定起因障害を切り分け可能とする。
   - Renamer 起動時: `[Config] Environment={Env}, LoadedFrom=[...], SaveTarget={Path}`（JSON 構文エラーや `.env` 残存検出時は `[WARN]` ログ＋画面バナー通知）および未接続・保存失敗時の詳細例外メッセージを出力する。

---

## Risks / Trade-offs

| リスク                                                                       | 緩和策                                                                                                                                                                                                                                          |
| :--------------------------------------------------------------------------- | :---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 既存ユーザーの `.env` 設定が読まれなくなる                                   | `proposal.md` / `README.md` / `PRODUCTION_ENVIRONMENT.md` に `appsettings.{Environment}.json` への明確な移行ガイドを記載。デプロイスクリプトは `VIDEO_DIRS` 未設定時に旧 `.env` ではなく `appsettings.Production.json` 設定例を提示して早期終了 |
| `git rm --cached` 後に既存環境ファイルが見つからずデフォルト動作になるケース | `.fsproj` に `Condition="Exists(...)"` を設定し、`appsettings.json` 単体で安全起動・ビルドできることを単体テスト・結合テストで保証                                                                                                              |
| Avalonia `DispatcherTimer` や無限アニメーションのヘッドレス/テスト環境互換   | スピナー描画は純粋なビジュアル構成とし、ロジック・状態遷移テスト (`StateTests.fs`) は MVU モデルのフラグとメッセージ遷移を独立して 100% 検証可能に分離                                                                                          |
