# Proposal: Linux (NAME_MAX 255B) 及び Windows (MAX_PATH 260字) のクロスプラットフォーム動画ファイル名制限対応

- **変更名**: `cross-platform-filename-limit`
- **適用規模レベル**: `L2` (通常変更: 機能追加・仕様変更・UI変更・通常バグ修正)
- **変更種別**: `fix` / `feat` (クロスプラットフォーム互換性対応・不具合修正および機能拡張)
- **関連Issue**: GitHub Issue [#27](https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/27)

## Why

Windows (NTFS) はファイル名単体の長さを 255 文字（UTF-16）まで許容するのに対し、Linux (Docker / WSL / ext4) は 255 バイト（UTF-8, `NAME_MAX`）に制限されており、日本語ファイル名では約85文字で上限に達します。この仕様差により、Windows ホスト上で正常に保存・管理されている動画を Docker / WSL コンテナにバインドマウントしてストリーミング再生しようとした際、Linux カーネルが `ENAMETOOLONG` を返し、.NET の `PathTooLongException` で 500 クラッシュする問題が発生しています。
ホストOS (Windows) に存在する動画を Docker コンテナ (Linux) 上で確実に認識・再生可能とするため、Renamer での両制約の自動検知・可視化・AI短縮、Web バックエンドでの例外安全ハンドリング、Web UI でのエラー原因通知、およびスキャナーでの事前警告記録を包括的に導入します。

## What Changes

- **AI File Renamer (`companion-file-rename`)**:
  - **自動抽出の包括化**: Windows パス長（フルパス文字数 ≧ 240文字）**または** Linux ファイル名（UTF-8 バイト数 ≧ 240バイト）のどちらか一方にでも違反するファイルを全件自動抽出。
  - **制約種別の可視化**: 各候補カードの BEFORE 行に、違反理由バッジ（`[🪟 Windows: パス長超過]`、`[🐧 Linux: ファイル名超過]`、`[⚠️ 両方超過]`）および文字数・UTF-8バイト数を明示。
  - **表示フィルタの提供**: ツールバーに違反種別ごとの絞り込みコンボボックス（「すべて」「🐧 Linuxファイル名超過のみ」「🪟 Windowsパス長超過のみ」「⚠️ 両方超過」）を追加。
  - **AI プロンプトとリアルタイム判定の両立**: AI提案時に「新ファイル名は UTF-8 で 240 バイト以内、かつフルパス 240 文字以内」の指示を自動注入し、提案結果および手動編集時の AFTER バッジで両制約が安全かをリアルタイム判定（`[✓ 両方安全]` / 超過時は危険警告）。
- **Web バックエンド (`video-library`)**:
  - `HttpHandlers.fs` の `streamVideo` において、`PhysicalFileResultExecutor.ExecuteAsync` 実行時の `PathTooLongException` を捕捉し、HTTP 422 (Unprocessable Entity) と JSON エラー（`code: "FILE_NAME_TOO_LONG"`）を返却。
- **Web UI (`video-library`)**:
  - `<video>` タグの再生エラー（`@error`）発生時、API からエラー理由を取得して赤色トースト通知を表示するとともに、プレイヤー領域中央に「Linux の 255 バイト制限（NAME_MAX）超過のため再生不可。Renamer で短縮してください」というオーバーレイ案内を表示。
- **Web スキャナー (`video-scanner`)**:
  - ディレクトリ同期走査時、ファイル名単体の UTF-8 バイト数が 255 バイトを超えるファイルを検知した場合、DB へは登録しつつ、例外エラーログ（`Db.saveErrorLog`）に警告を出力。

## Non-goals (やらないこと)

- Linux カーネル自体の `NAME_MAX` (255バイト) 制限を回避するためのファイル名ハッシュ化マッピングや仮想ファイルシステム層の導入（実物理ファイルのリネームとエラー検知で解決する）。
- Windows ホスト上での動画ファイルの自動サイレント強制リネーム（ユーザーの承認・対比確認を経ずに勝手にファイルを書き換えない）。
- 動画トランスコーディングによるファイル名のインメモリ置換配信。

## Acceptance Criteria (検証可能な受け入れ基準)

- **AC-01 (Renamer 包括抽出)**:
  - Given: フルパスが 100 文字だが日本語ファイル名が 90 文字（UTF-8: 270バイト）の動画、およびフルパスが 250 文字で日本語ファイル名が 30 文字（UTF-8: 90バイト）の動画が存在する
  - When: Renamer でスキャンを実行する
  - Then: 両方の動画が漏れなく抽出され、前者は `[🐧 Linux: ファイル名超過]`、後者は `[🪟 Windows: パス長超過]` のバッジが表示されること
- **AC-02 (Renamer 表示フィルタ)**:
  - Given: 抽出結果に Linux 超過と Windows 超過のファイルが混在している
  - When: 表示フィルタで「Linuxファイル名超過のみ」を選択する
  - Then: Linux ファイル名が 240 バイト以上のファイルのみが絞り込み表示されること
- **AC-03 (Renamer 両立 AI 提案 & AFTER 判定)**:
  - Given: 抽出されたファイルに対して AI 提案を実行する
  - When: 提案された新ファイル名を受信する
  - Then: 新ファイル名が拡張子を含め UTF-8 で 240 バイト以内かつフルパス 240 文字以内となり、`[✓ 両方安全]` バッジが表示されること
- **AC-04 (Web ストリーミング例外捕捉)**:
  - Given: Linux 環境（WSL/Docker/DevContainer）で 255 バイト超のファイル名を持つ動画に対して `GET /api/videos/{id}/stream` をリクエストする
  - When: `PathTooLongException` が発生する
  - Then: サーバーが 500 クラッシュせず、HTTP 422 と `code: "FILE_NAME_TOO_LONG"` の JSON を返却すること
- **AC-05 (Web UI エラー通知)**:
  - Given: 上記の再生不可動画をブラウザでクリックして再生を開始する
  - When: ストリーミングエラーが発生する
  - Then: 画面上にトースト通知とプレイヤー中央オーバーレイで「Linux 255バイト制限超過」のメッセージが表示されること
- **AC-06 (Web スキャナー警告ログ)**:
  - Given: 255 バイト超のファイル名を持つ動画を含むディレクトリを同期スキャンする
  - When: スキャンが完了する
  - Then: DB に動画レコードが登録され、かつ `ErrorLogs` テーブルに警告ログが記録されること

## Capabilities

### New Capabilities

なし（既存の 3 つの Capability を拡張・改修）

### Modified Capabilities

- `companion-file-rename`: Windows パス長（文字数）および Linux ファイル名長（UTF-8 バイト数）の二重制約に対応した自動抽出、制約種別バッジ表示、表示フィルタ、AI プロンプト最適化、および AFTER リアルタイム検証要件の追加・改定。
- `video-library`: ストリーミングエンドポイントにおける `PathTooLongException` の安全な捕捉と HTTP 422 JSON 返却、およびフロントエンドプレイヤーでのエラーオーバーレイ・トースト通知要件の追加。
- `video-scanner`: ディレクトリ走査時における UTF-8 255 バイト超過ファイルの検知と、DB 登録を維持した上での例外警告ログ記録要件の追加。

## Impact

- **デスクトップアプリ (`TagBasedVideoManager.Renamer`)**:
  - `Domain.fs`: 候補データ型にバイト数および制約種別フラグを追加。
  - `FileScanner.fs`: フルパス文字数または UTF-8 バイト数の OR 判定による抽出。
  - `OpenRouterClient.fs`: UTF-8 240 バイト制限・フルパス 240 文字制限のプロンプト追加。
  - `Views.fs`: ツールバーの表示フィルタ、BEFORE 行の制約種別バッジ、AFTER 行の両方安全判定バッジの描画。
- **Web バックエンド (`TagBasedVideoManager`)**:
  - `HttpHandlers.fs`: `streamVideo` の `try-with` で `PathTooLongException` を捕捉し、HTTP 422 JSON を返却。
  - `Scanner.fs`: `syncVideoDirectories` でファイル名 UTF-8 バイト数を検査し、超過時にログ記録。
- **Web フロントエンド (`src/TagBasedVideoManager/wwwroot/`)**:
  - `index.html`, `app.js`: `<video>` の `@error` ハンドラによるエラー理由取得・プレイヤーオーバーレイおよびトースト表示。
