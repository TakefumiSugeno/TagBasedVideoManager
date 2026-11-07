# レビュー記録: multi-video-dirs / propose

- **日時**: 2026-11-06
- **フェーズ**: propose
- **対象成果物**:
  - `openspec/changes/multi-video-dirs/proposal.md`
  - `openspec/changes/multi-video-dirs/mockup.html`
  - `openspec/changes/multi-video-dirs/specs/**/spec.md`
  - `openspec/changes/multi-video-dirs/design.md`
  - `openspec/changes/multi-video-dirs/test_plan.md`
  - `openspec/changes/multi-video-dirs/tasks.md`

---

## サイクル 1 レビュー結果

- **日時**: 2026-11-06
- **レビュアー**: `user-reviewer`, `se-reviewer`
- **判定**: **LGTM: false**（User: Major 2 / SE: Major 5）
- **主な指摘事項**:
  - **M1 / A1**: UI がスキャン失敗時に固定文しか出さず、設定不備（`VIDEO_DIRS` 未設定等）のエラー詳細が把握できない（`mockup.html` 必須）。
  - **M2**: デプロイスクリプトの旧引数（`-VideoDir`）や既定値（`/mnt/videos`）の扱いが未定義。
  - **M-1**: `deploy.sh` での `source .env` がセミコロンを誤解釈するリスク。
  - **M-2**: `FilePath` の正規化（大文字小文字・末尾区切り・物理パス解決）未定義による二重登録リスク。
  - **M-3**: タグ導出の基準ルートとリンク・入れ子ルート時のタグ付与方針が未確定。
  - **M-4**: デプロイの検証順序と、`run` 失敗時のサービス断リスク。
  - **M-5**: スペックの `-v` 期待値と実コードの WSL パス変換の矛盾。
- **対応方針**:
  - ユーザーヒアリングを実施し、A1（UI エラー詳細表示のスコープ追加・`mockup.html` 作成）および A2（デプロイスクリプトは検証を stop 前に行う構成を維持し、run 失敗時のサービス断リスクを design に明記）の合意を取得。
  - 全指摘（M1〜M5、m1〜m8）を反映し、成果物一式（`mockup.html`, `proposal.md`, `specs/`, `design.md`, `test_plan.md`, `tasks.md`）を改訂。

---

## サイクル 2 レビュー結果（再レビュー）

- **日時**: 2026-11-06
- **レビュアー**: `user-reviewer`, `se-reviewer`
- **判定**: **LGTM: true**（User: Critical 0, Major 0 / SE: Critical 0, Major 0）

### 1. 設問への回答サマリ

#### 1.1 User Reviewer（ブラインド仕様突合・初心者検証）

- **異常系・境界条件の先出し突合**: 未設定・旧変数残存、一部/全不在、重複・入れ子、ドライブ直下拒否、既存動画へのタグ追記、大文字小文字表記揺れ防止のすべてがシナリオとして過不足なく定義されていることを確認。
- **暗黙の前提の排除**: スキャン失敗時のトースト通知拡張、スクリプトでの既定値廃止と事前入力検証、行単位の安全な `.env` パース仕様が明文化されたことを確認。
- **UI モック検証**: `mockup.html` において、設定不備時にトースト内に詳細エラーと設定例がコード風ボックスで表示され、表示時間 7 秒＋閉じるボタンにより初心者が直感的に復旧できることを確認。

#### 1.2 SE Reviewer（故障シミュレーション・可観測性・手戻り検証）

- **故障シミュレーション**: 走査時の低速・切断ドライブ発生時も例外トラップによりプロセス全体のクラッシュやDB不整合は防がれ、直列走査により SQLite のロック競合も防止されることを確認。
- **可観測性**: 不備パス・設定例を含む詳細エラーが API・UI トースト・ログの全層に通知され、MTTR が最小化されていることを確認。
- **テスト計画・タスク検証**: 外部依存のモック境界が明瞭であり、タスク 2.2（入れ子ルート対応）についても `IT-05b` のテストファースト推進により安全に実装可能であることを確認。

### 2. 指摘事項（保留・申し送り）

| ID         | 種別       | 対象         | 内容                                                                                            | 対応方針                                              |
| :--------- | :--------- | :----------- | :---------------------------------------------------------------------------------------------- | :---------------------------------------------------- |
| **Min-1**  | Minor      | `Scanner.fs` | ファイル走査時にアクセス拒否（`UnauthorizedAccessException`）等が発生した場合の警告ログ出力考慮 | 実装時に考慮し、Archive 時に `handover.md` へ申し送り |
| **Sugg-1** | Suggestion | `app.js`     | トースト内エラーメッセージのクリックコピー補助機能                                              | 次期 UI 改善候補として `handover.md` へ申し送り       |

### 3. 総合判定

- **User Reviewer**: LGTM: true
- **SE Reviewer**: LGTM: true
- **LGTM**: true
- **合意状況**: 全ロール承認済み

---

## Apply タスクレビュー結果: Task 1.3 (VideoDirs モジュール)

- **日時**: 2026-11-07
- **フェーズ**: apply
- **タスク**: Task 1.3: VideoDirs モジュールのレビュー・破壊テスト
- **レビュアー**: `qa-reviewer`, `pg-reviewer`
- **使用プロトコル**: `openspec/templates/checklist_apply_test.md` / `checklist_apply_impl.md`
- **対象成果物**:
  - `src/TagBasedVideoManager/VideoDirs.fs`
  - `test/TagBasedVideoManager.Tests/Unit/VideoDirsTests.fs`

### 1. 挑戦的設問への回答サマリ

#### 1.1 qa-reviewer（Adversarial QA / トートロジー検証）

- **トートロジー・甘いアサーションの検証**:
  - 期待値は固定リテラル値でアサートされており、トートロジーは皆無。
  - UT-12 のアサーションに含まれていた甘い条件（`|| msg.Contains("VIDEO_DIRS")`）を告発し、ドライブ直下専用メッセージのみを判定するよう厳格化修正済み。
- **Adversarial QA（破壊テスト実行結果）**:
  - スクラッチスクリプトにて `null`、1万件の連続セミコロン、DoS 入力、多種多様なルートパス（`C:\`, `C:`, `c:/`, `/`, `\\` 等）と通常パスの混在、Unicode 絵文字パス等、全 42 パターンの破壊テストを作成・実行。
  - 例外終了せず全て適切な `Result.Ok` / `Result.Error` を返却し、**堅牢性を確認（Green / 落とせず）**。

#### 1.2 pg-reviewer（コードシンプルさ / 暗黙の仮定検証）

- **暗黙の仮定・例外握りつぶし**:
  - 純粋関数かつ ROP（`Result` 型）で例外握りつぶしは皆無。健全なエラーハンドリングを確認。
  - 末尾スラッシュ付きパスの正規化差異は後続タスク（Scanner の `Path.GetFullPath`）で吸収することを合意。
- **30% コード削減リファクタ**:
  - `deduplicate` を `List.distinctBy` に簡素化、`StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries` の活用、ブール論理式の単純化により、83 行から 56 行へ**約 32.5% のコード削減**を達成・反映済み。

### 2. 指摘事項（保留・申し送り）

| ID        | 種別  | 対象           | 内容                                                                                     | 対応方針                 |
| :-------- | :---- | :------------- | :--------------------------------------------------------------------------------------- | :----------------------- |
| **Min-2** | Minor | `VideoDirs.fs` | 末尾区切り付きパス（`D:\A\` と `D:\A`）の同一視は Scanner 側の `Path.GetFullPath` で担保 | 後続 Task 2.1/2.2 で検証 |

### 3. 総合判定

- **qa-reviewer**: LGTM: true
- **pg-reviewer**: LGTM: true
- **LGTM**: true

---

## Apply タスクレビュー結果: Task 2.3 (Scanner 複数ルート走査・パス正規化・タグ導出)

- **日時**: 2026-11-07
- **フェーズ**: apply
- **タスク**: Task 2.3: Scanner 複数ルート走査・パス正規化・タグ導出のレビュー
- **レビュアー**: `qa-reviewer`, `pg-reviewer`, `se-reviewer`
- **使用プロトコル**: `openspec/templates/checklist_apply_test.md` / `checklist_apply_impl.md`
- **対象成果物**:
  - `src/TagBasedVideoManager/Scanner.fs`
  - `test/TagBasedVideoManager.Tests/Unit/ScannerTests.fs`

### 1. 挑戦的設問への回答サマリ

#### 1.1 qa-reviewer（Adversarial QA / トートロジー検証）

- **トートロジー・甘いアサーションの検証**:
  - `UT-11`, `UT-12b`, `DT-01`, `DT-02`, `IT-01`〜`IT-10`, `ST-01/02/04` の全テストにおいて、期待値は具体的な固定リテラル値でアサートされておりトートロジーは皆無。
- **Adversarial QA（破壊テスト再実行結果）**:
  - `deriveFolderTags`, `deriveAllFolderTags`, `syncVideoDirectories` に対し、パストラバーサル、別ドライブ、特殊文字、不正Null文字、真の DB Fault Injection（接続無効化、テーブルDROP）など全 28 パターンの破壊テストを実行。
  - すべての意地悪な入力に対してクラッシュせず仕様通りの安全な戻り値または `Error(QueryError)` を即座に返却し、**堅牢性を確認（Green / 落とせず）**。

#### 1.2 pg-reviewer（コードシンプルさ / 暗黙の仮定検証）

- **暗黙の仮定・例外握りつぶし**:
  - DB初期データ取得失敗時のサイレントフォールバックを排除し、即座に `Error err` を返却するよう是正完了。
  - `thumbDir` 作成失敗時もサイレントに捨てず警告ログを出力するよう改善。
- **コード削減・リファクタ**:
  - フォルダタグ付与と自動タグ付けルールにおける動画へのタグ紐付け・辞書同期コードを共通関数 `attachTag` に集約し、約 20 行のコード削減および辞書ルックアップの効率化を達成。

#### 1.3 se-reviewer（障害耐性 / 可観測性 / 状態遷移検証）

- **障害耐性・耐障害性**:
  - 不在ルートは警告ログを出力してスキップされ、全滅時も `Ok 0` として安全に終了。
  - 走査中の権限不足や切断時も例外トラップによりプロセスダウンを防止。
- **可観測性・状態遷移**:
  - 不在ルートの警告ログ（`[Scanner Warning]`）により運用者の特定が迅速に行える。
  - `ST-01`（正常遷移）、`ST-02`（再スキャン冪等性）、`ST-04`（不在ルート時の既存レコード保護）のすべてが成立することを確認。

### 2. 指摘事項（保留・申し送り）

| ID        | 種別  | 対象         | 内容                                                                                                   | 対応方針                                                    |
| :-------- | :---- | :----------- | :----------------------------------------------------------------------------------------------------- | :---------------------------------------------------------- |
| **Min-3** | Minor | `Scanner.fs` | 親ディレクトリを指す循環シンボリックリンク（ジャンクション）による無限再帰対策（`visitedDirs` の導入） | 通常構成では極めて稀。Archive 時に `handover.md` へ申し送り |
| **Min-4** | Minor | `Scanner.fs` | ファイル走査中（`enumerateFiles`）のアクセス権限エラー発生時の詳細ログ出力                             | Archive 時に `handover.md` へ申し送り                       |

### 3. 総合判定

- **qa-reviewer**: LGTM: true
- **pg-reviewer**: LGTM: true
- **se-reviewer**: LGTM: true
- **LGTM**: true

---

## Apply タスクレビュー結果: Task 3.4 (スキャン API エラー詳細化と UI エラートースト改修)

- **日時**: 2026-11-07
- **フェーズ**: apply
- **タスク**: Task 3.4: スキャン API エラー詳細化と UI エラートースト改修のレビュー (Task 3.1〜3.3)
- **レビュアー**: `qa-reviewer`, `pg-reviewer`, `se-reviewer`
- **使用プロトコル**: `openspec/templates/checklist_apply_test.md` / `checklist_apply_impl.md`
- **対象成果物**:
  - `src/TagBasedVideoManager/HttpHandlers.fs` (`triggerScan`)
  - `src/TagBasedVideoManager/wwwroot/app.js` (`showToast`, `triggerScan`)
  - `src/TagBasedVideoManager/wwwroot/index.html` (トースト詳細表示領域)
  - `test/TagBasedVideoManager.Tests/Unit/HttpHandlersTests.fs` (IT-07, IT-07b, IT-08, ST-03)

### 1. 挑戦的設問への回答サマリ

#### 1.1 qa-reviewer（Adversarial QA / トートロジー検証）

- **トートロジー・甘いアサーションの検証**:
  - `IT-07`, `IT-07b`, `IT-08` ではステータスコード 500 だけでなく、設定例（`VIDEO_DIRS=D:\Anime;F:\Movie`）や旧変数廃止案内キーワードを固定リテラル値で厳格に検証。
  - `ST-03` ではスキャン失敗時の既存動画・タグレコードの不変性（ID・パス・属性・タグ紐付けの全項目）を検証し、異常遷移拒否を完全に実証。
- **Adversarial QA（破壊テスト実行結果）**:
  - 不正記号、空白、ドライブ直下混在、セミコロンのみ、不在パスなど多角的な極端入力を与えて検証。未処理例外によるクラッシュはなく、制御された適切なエラーレスポンスまたは安全なスキップ（200 OK 0件）を返却することを確認（**Green / 落とせず**）。

#### 1.2 pg-reviewer（コードシンプルさ / 暗黙の仮定検証）

- **暗黙の仮定・例外握りつぶし**:
  - `VideoDirs.fromEnvironment` のエラー時にデフォルトへのサイレントフォールバックを排除し、即座に 500 レスポンスを返却。
  - フロントエンドでは `toastTimeoutId` の導入により連続通知時のタイマー競合（早期消滅）を防止。
  - `x-text` バインドにより XSS リスクも完全に排除。

#### 1.3 se-reviewer（障害耐性 / 可観測性 / UIモック忠実性）

- **可観測性・MTTR短縮**:
  - 設定不備時にサーバーログを追うことなく、UI のトースト（7秒間表示）上に設定例が表示され、画面上ですぐに対処可能なため MTTR が大幅に削減。
- **UIモック忠実性**:
  - `mockup.html` の仕様（表示時間 7秒、モノスペース、暗色背景、`break-all leading-relaxed whitespace-pre-wrap`、閉じるボタン）が `index.html` および `app.js` に忠実に反映されていることを確認。

### 2. 指摘事項（保留・申し送り）

| ID         | 種別       | 対象              | 内容                                                               | 対応方針                       |
| :--------- | :--------- | :---------------- | :----------------------------------------------------------------- | :----------------------------- |
| **Sugg-2** | Suggestion | `HttpHandlers.fs` | `getEnv` や 500 レスポンス生成の共通 Giraffe 関数活用              | 保留（次期リファクタ時に検討） |
| **Sugg-3** | Suggestion | `app.js`          | `triggerScan()` 冒頭への `isScanning` 二重実行ガード節追加         | 保留（次期UI改善時に検討）     |
| **Sugg-4** | Suggestion | `index.html`      | トースト詳細領域への `max-h-48 overflow-y-auto` スクロール制御追加 | 保留（Archive 時に申し送り）   |

### 3. 総合判定

- **qa-reviewer**: LGTM: true
- **pg-reviewer**: LGTM: true
- **se-reviewer**: LGTM: true
- **LGTM**: true

---

## レビュー記録: multi-video-dirs / apply / Task 4 (Task 4.1〜4.5)

- **日時**: 2026-11-07
- **フェーズ**: apply
- **タスク**: Task 4.5: デプロイスクリプトと Docker 構成のレビュー (Task 4.1〜4.4)
- **レビュアー**: `qa-reviewer`, `pg-reviewer`, `se-reviewer`
- **使用プロトコル**: `openspec/templates/checklist_apply_test.md` / `checklist_apply_impl.md`
- **対象成果物**:
  - `Dockerfile`
  - `scripts/common.ps1` (`Resolve-VideoMounts`, `New-RunArguments`)
  - `scripts/deploy.ps1`
  - `scripts/deploy.sh` (`parse_env`, 早期検証, マウント引数構築)
  - `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs` (UT-13〜UT-19, IT-13〜IT-17)

### 1. 挑戦的設問への回答サマリ

#### 1.1 qa-reviewer（Adversarial QA / トートロジー検証 / 実機コンテナ検証）

- **Cycle 1 指摘と Adversarial QA 破壊テスト**:
  - 敵対的 QA として意地悪な入力（空要素混在、末尾スラッシュ連続、特殊記号、パストラバーサル、ルート複数スラッシュ等）を与えて検証を実施。
  - 指摘 1 (Critical): `scripts/deploy.sh` で `dir/..` や `..` が指定された場合、`leaf=".."` となり、マウント先が `/app/videos/..`（すなわちコンテナ内 `/app` 全体）を上書きマウントしてしまう致命的な脆弱性を検出。
  - 指摘 2 (Major): `scripts/deploy.sh` でルート複数スラッシュ（`///`）が末尾トリムをすり抜けてルート直下マウントされてしまう不具合を検出。
  - 指摘 3 (Major): `DeployScriptTests.fs` の `IT-15` が単に `grep -qE "load_env|parse_env"` しているだけの偽装テスト（形骸化テスト）であることを告発。
  - Cycle 1 判定: `LGTM: false`。
- **Cycle 2 是正確認と堅牢性実証**:
  - `qa-tester` により `IT-15` を `parse_env` の実動作検証へ是正し、パストラバーサル・複数スラッシュルートの Red テスト 16 件（UT-18: 10件, IT-16: 2件, IT-17: 4件）を追加。
  - `pg-implementer` により `scripts/deploy.sh` および `scripts/common.ps1` にガード節を追加し、全 16 件が Green に転換。
  - 独立スクラッチテストによる 47 件の破壊テスト（意地悪入力・インジェクション記号・特殊記号・未定義変数等）を再実行し、全不正入力が例外発生で安全に拒絶され正常系のみ通過することを確認（**Green / 落とせず堅牢**）。
- **IT-11 / IT-12 実コンテナ検証 (Task 4.4)**:
  - ホスト（Windows 11）上の Docker Desktop 環境にて `alpine:latest` を用いた実コンテナ検証を実施。
  - `IT-11`: 複数一時ディレクトリが独立してマウントされ両方のファイルが読み取れること、および `:ro` マウントによりコンテナ内からの書き込み試行が `Read-only file system` エラーで即座に拒否されることを確認（PASS）。
  - `IT-12`: コンテナ内環境変数 `VIDEO_DIRS`（`/app/videos/tvm-test-A;/app/videos/tvm-test-B`）と実マウント先パスが完全一致していることを確認（PASS）。

#### 1.2 pg-reviewer（コードシンプルさ / 暗黙の仮定検証）

- **暗黙の仮定・例外握りつぶし**:
  - `scripts/deploy.ps1` および `deploy.sh` において、未設定・空文字環境変数に対する早期バリデーションと設定例付きエラー出力を確認。
  - `Test-PortInUse` は非ブロッキング警告用として適切に実装。
- **コード品質・シェルイディオム**:
  - PowerShell: `[CmdletBinding()]`、明示的な型指定、`[PSCustomObject]` による構造化返却が徹底。
  - Bash: `set -euo pipefail` 厳格モードの維持、行単位パーサ `parse_env` の実装、改行コード LF の維持を確認。
- **コード削減提案 (YAGNI)**:
  - Suggestion 2件（重複エラー分岐の統合、不要な try-catch 削減）を提示。動作に影響しないため保留として記録。

#### 1.3 se-reviewer（障害耐性 / インフラ運用 / セキュリティ検証）

- **外部依存・シェルパースの安全性**:
  - `deploy.sh` において `source .env` および `eval` が完全に排除され、安全な行パーサ `parse_env` によりコマンドインジェクション・パラメータインジェクションのリスクを根絶。
- **副作用抑止と冪等性**:
  - 早期入力検証がイメージビルド（`build`）やコンテナ停止・削除（`stop`, `rm`）より前に確実に実行され、AST 解析（`resolveLine < buildLine < stopLine`）およびモックランタイム監査ログにより不正入力時の副作用ゼロを実証（IT-14）。
- **運用・保守・エラーガイダンス**:
  - 未設定・旧変数残存・ルート指定・不在・重複時の設定例付きエラーメッセージによる MTTR 短縮。
  - `:ro` マウントによるホスト動画ファイル破損リスクの物理的排除。
  - ヘルスチェック失敗時のコンテナログ自動出力機能の維持。

### 2. 指摘事項（保留・申し送り）

| ID         | 種別       | 対象                 | 内容                                                                                                    | 対応方針                       |
| :--------- | :--------- | :------------------- | :------------------------------------------------------------------------------------------------------ | :----------------------------- |
| **Sugg-5** | Suggestion | `scripts/common.ps1` | `Resolve-VideoMounts` における `$VideoDirs` 空チェックと `$rawPaths.Count -eq 0` の重複エラー分岐の統合 | 保留（次期リファクタ時に検討） |
| **Sugg-6** | Suggestion | `scripts/deploy.ps1` | `nvidia-smi` 判定における不要な try-catch の削除および冗長な代入の整理                                  | 保留（次期リファクタ時に検討） |

### 3. 総合判定

- **qa-reviewer**: LGTM: true
- **pg-reviewer**: LGTM: true
- **se-reviewer**: LGTM: true
- **LGTM**: true
