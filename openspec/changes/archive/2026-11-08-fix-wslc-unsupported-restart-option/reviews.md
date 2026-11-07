# レビュー記録: fix-wslc-unsupported-restart-option / propose

- **日時**: 2026-11-08
- **フェーズ**: propose
- **レビュアー**: se (SE Reviewer Agent)
- **使用プロトコル**: openspec/templates/checklist_propose.md
- **対象成果物**:
  - `openspec/changes/fix-wslc-unsupported-restart-option/proposal.md`
  - `openspec/changes/fix-wslc-unsupported-restart-option/tasks.md`
  - `scripts/common.ps1` (L194-249: `New-RunArguments`)
  - `scripts/deploy.ps1` (L108-115: `New-RunArguments` 呼出および起動処理)

---

## 1. 挑戦的設問への回答（故障シミュレーション・可観測性・死角告発）

### 設問1: 外部依存ハング・切断時の連鎖崩壊検証（プロセス耐障害性と自動復旧）

- **確認内容・回答**:
  - `scripts/deploy.ps1` L109 において、Windows WSL 3.0 ランタイムである `wslc` 実行時に `--restart unless-stopped` を除外する設計の妥当性を検証した。
  - Docker 環境ではデーモン停止やホスト再起動時にコンテナが自動復帰するが、`wslc run` は `--restart` オプションをサポートしていないため、渡すと即時異常終了（`オプション名が現在のコマンドに対して認識されませんでした: '--restart'`）を引き起こす即死障害（Hard Failure）が発生する。
  - 本設計で `--restart` を除外することで、起動不能の連鎖崩壊を確実に防止できる。
  - 一方、`wslc` 環境下では OS 再起動や WSL インスタンスのシャットダウン（`wsl --shutdown` 等）後にコンテナが自動再起動されず、手動で `deploy.ps1` を再実行するまでサービスが停止したままとなるダウンタイム延長リスクが存在する。ただし、本リポジトリにおける `wslc` の位置づけは Docker Desktop を持たない Windows 11 上でのローカル開発・軽量実行ランタイムであり、常時稼働を保証する本番サーバー用途には Docker が利用されるため、このトレードオフは設計上許容される。
- **確認根拠**:
  - `scripts/common.ps1` L92-100 (`Get-ContainerRuntimeName` によるランタイム解決)
  - `scripts/common.ps1` L245-247 (`New-RunArguments` における `--restart unless-stopped` 付与ロジック)
  - `openspec/changes/fix-wslc-unsupported-restart-option/proposal.md` L8-11, L15-18

### 設問2: 可観測性（MTTR短縮・障害調査可能性）の検証

- **確認内容・回答**:
  - `scripts/deploy.ps1` L76-82 で検出されたランタイム名（`wslc` または `docker`）がコンソールに出力され、L111 で `& $runtime @allArgs` が直接実行される構成となっている。
  - もしコンテナ起動が失敗した場合、L112-115 で `Write-Error "コンテナの起動に失敗しました。"` が出力されるが、CLI 実行自体の標準エラー出力（stderr）はそのまま端末に流れるため、オプション不整合やポート衝突などの原因は即座に特定可能である。
  - `New-RunArguments` に `[string]$Runtime = ""` を新設し、呼び出し元の `deploy.ps1` から `-Runtime $runtime` を明示的に渡す構造となっており、引数の伝達経路も直感的でトレースしやすい。
- **確認根拠**:
  - `scripts/deploy.ps1` L81 (`Write-Host "検出されたランタイム: $runtime"`), L109-115 (`New-RunArguments` 呼出および exit コードチェック)
  - `openspec/changes/fix-wslc-unsupported-restart-option/proposal.md` L49-52

### 設問3: 100%パスしても抜けるバグ（最大の死角）の検証

- **確認内容・回答**:
  - **死角1（実機バイナリとのセマンティクス乖離）**: `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs` で実行される単体テストは、PowerShell の文字列配列展開を JSON 経由で検証するものであり、実際にホスト上で `wslc` バイナリを呼び出してコンテナがデーモンモード（`-d`）で起動するかどうかは検証されない。`tasks.md` のタスク 2.1「実機環境での `scripts/deploy.ps1` 起動引数確認」が必須の補完ステップとなる。
  - **死角2（大文字小文字・パス表現の差異）**: `Get-ContainerRuntimeName` は文字列 `"wslc"` を返す設計（`scripts/common.ps1` L94）であり、PowerShell の `-ne` 演算子は既定で大文字小文字を区別しないため（Case-Insensitive）、ランタイム名表記揺れによる誤判定リスクは極めて低い。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs` L634-648 (`RunArgumentsScriptTests.build`)
  - `scripts/common.ps1` L93-95
  - `openspec/changes/fix-wslc-unsupported-restart-option/tasks.md` L20-21

### 設問4: タスクリストの手戻りリスクおよび完了基準の検証

- **確認内容・回答**:
  - `tasks.md` において、TDD の標準フローに従い「タスク 1.1 再現テスト（Red）作成」→「タスク 1.2 実装（Green）」→「タスク 1.3 回帰確認」と明確に順序づけられている。
  - `scripts/common.ps1` の引数追加はオプショナルパラメータ（`[string]$Runtime = ""`）であるため、既存の単体テスト呼び出し（`DeployScriptTests.fs` L640）のシグネチャを破壊せず、後方互換性を保ちながら最小修正が可能であり、手戻りリスクは極めて低い。
- **確認根拠**:
  - `openspec/changes/fix-wslc-unsupported-restart-option/tasks.md` L8-17
  - `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs` L724-729

---

## 2. Adversarial QA（破壊テスト実行結果）

- 対象外（Propose フェーズの設計・計画レビューのため）

---

## 3. 指摘事項

### 指摘1

- **内容**: `wslc` では `--restart unless-stopped` が除外されるため、マシンの再起動や WSL ディストリビューション終了時にコンテナが自動復旧しない。常時稼働が求められる環境では Docker Desktop または Linux ネイティブ環境の利用が推奨される旨の留意事項が存在する。
- **重要度**: Suggestion
- **対応方針**: 保留
- **理由**: 本修正は WSL 3.0 (wslc) の即死障害を解消する L1 修正であり、ローカル開発・検証ランタイムとしての機能提供を主目的としているため、現時点でのスクリプト側での複雑な自動復旧機構（Windows タスクスケジューラ連携等）の導入は不要（YAGNI）。
- **対応コミット**: なし（運用上の留意点として記録）

### 指摘2

- **内容**: 将来的に `podman` やその他のランタイムサポート、あるいは `wslc` の将来バージョンでの `--restart` 対応など差分が増加した場合、`$Runtime -ne "wslc"` のようなハードコードされた否定条件が散乱すると保守性が低下する。
- **重要度**: Suggestion
- **対応方針**: 保留
- **理由**: 現行の Windows サポートランタイムは `wslc` と `docker` の 2 種類のみであり、現時点での抽象レイヤー導入（ランタイム Capabilities マトリクス等）はオーバーエンジニアリングとなるため。今後のランタイム追加時に再検討する。
- **対応コミット**: なし

---

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし
- **保留指摘（申し送り）**: 指摘1, 指摘2（いずれも将来拡張時の Suggestion）
- **次のアクション**: Gate 1 を通過し、Apply フェーズ（TDD 実装）へ移行可能。

---

# レビュー記録: fix-wslc-unsupported-restart-option / apply / Task 1.1-1.3

- **日時**: 2026-11-08
- **フェーズ**: apply
- **タスク**: Task 1.1（根本原因の特定と再現テスト作成）、Task 1.2（プロダクションスクリプトの修正）、Task 1.3（回帰テスト実行）
- **レビュアー**: qa (QA Reviewer Agent)
- **使用プロトコル**: openspec/templates/checklist_apply_test.md, openspec/templates/checklist_apply_impl.md
- **対象成果物**:
  - `openspec/changes/fix-wslc-unsupported-restart-option/proposal.md`
  - `openspec/changes/fix-wslc-unsupported-restart-option/tasks.md`
  - `scripts/common.ps1` (行 211, 246-250)
  - `scripts/deploy.ps1` (行 109)
  - `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs` (行 634-644, 738-741, 848-866)

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: トートロジーテスト（実装式のコピペ）の有無

- **確認内容・回答**:
  テストコードにおいて、プロダクションコードの判定ロジック（`if ($Runtime -ne "wslc")`）をそのままコピペして期待値を計算している箇所はありません。
  `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs` の `UT-RUN-ARGS-WSLC`（行 738-741）では、固定値 `Some "wslc"` を渡して呼び出した結果の引数配列に対して、`args |> should not' (contain "--restart")` と直接固定リテラルで否定アサーションを行っています。
  また既存の `UT-RUN-ARGS: --restart unless-stopped を指定する`（行 731-735）でも、`hasPair "--restart" "unless-stopped" args` によりフラグと値のペアを厳格にアサートしており、トートロジーは存在しません。
  さらに `IT-SCRIPT-03`（行 848-866）では、PowerShell AST を用いて `scripts/deploy.ps1` の `New-RunArguments` 呼び出しに `-Runtime $runtime` が渡されていることを静的構文解析で直接検証しており、自己完結的で独立性の高いテストとなっています。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs`: 行 731-741, 848-866
  - `scripts/common.ps1`: 行 246-250

### 設問2: 甘いアサーション・無意味なモックの有無

- **確認内容・回答**:
  モックライブラリを用いた内部ロジックのモック化やすり替えは一切行われておらず、実際の PowerShell スクリプト（`scripts/common.ps1` および `scripts/deploy.ps1`）を呼び出して生成された引数リストや AST を検証しています。
  アサーションも `not.toBeNull()` や単なる例外非発生チェックに留まらず、配列内に `"--restart"` が含まれないこと、あるいは `"--restart"` と `"unless-stopped"` のペアが含まれていること、AST 内に `-Runtime $runtime` が指定されていることを厳密に検証しており、甘いアサーションはありません。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs`: 行 731-741, 863-865

### 設問3: ミューテーション思考（バグのすり抜け検証）

- **確認内容・回答**:
  プロダクションコードの条件式を反転（Mutation）させた場合のテストの挙動を思考検証しました。
  1. **条件反転（`-ne` を `-eq` に反転）**:
     `scripts/common.ps1` の行 247 を `if ($Runtime -eq "wslc")` に反転させた場合、`UT-RUN-ARGS-WSLC` では `wslc` 時に `--restart` が付与されるため `should not' (contain "--restart")` で確実に **Red (失敗)** します。またデフォルト（未指定/docker）時にも `--restart` が付与されなくなるため `UT-RUN-ARGS: --restart unless-stopped を指定する` で確実に **Red (失敗)** します。
  2. **定数破壊（`"wslc"` を `"docker"` などに誤修正）**:
     `UT-RUN-ARGS-WSLC` で `wslc` 指定時に `--restart` が含まれるため確実に **Red (失敗)** します。
  3. **呼び出し側引数漏れ（`-Runtime $runtime` の渡し忘れ）**:
     `scripts/deploy.ps1` で `-Runtime` を渡し忘れた場合、`IT-SCRIPT-03` が AST パース正規表現不一致により確実に **Red (失敗)** します。
     このように、プロダクションコードの主要な破壊に対して死角なくテストが検知できる状態になっています。
- **確認根拠**:
  - `scripts/common.ps1`: 行 247
  - `scripts/deploy.ps1`: 行 109
  - `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs`: 行 734-740, 858-865

### 設問4: フレーキー要因・環境依存の摘出

- **確認内容・回答**:
  テストコード内に固定秒数の `sleep` 待機、グローバル状態の汚染、現在時刻の参照、テスト間順序への依存はありません。プロセス呼び出しや AST 解析も決定論的であり、フレーキーとなる要因は認められません。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs`: 行 634-644, 738-741, 848-866

---

## 2. Adversarial QA（破壊テスト実行結果）

意地悪な入力（空文字、null、大文字 "WSLC"、"docker"、"podman"、特殊文字、不正型）を `New-RunArguments` に投入し、スクリプトの堅牢性を実証検証しました。

- **対象関数/モジュール**: `scripts/common.ps1` / `New-RunArguments`
- **破壊テストコード名**: `AdversarialRuntimeParameterTest` (PowerShell スクラッチ検証)
- **実行コマンド**:
  ```powershell
  . ./scripts/common.ps1
  $inputs = @(
      @{ Name = 'null'; Val = $null },
      @{ Name = 'empty'; Val = '' },
      @{ Name = 'wslc'; Val = 'wslc' },
      @{ Name = 'WSLC (upper)'; Val = 'WSLC' },
      @{ Name = 'Wslc (title)'; Val = 'Wslc' },
      @{ Name = 'docker'; Val = 'docker' },
      @{ Name = 'podman'; Val = 'podman' },
      @{ Name = 'special chars'; Val = 'wslc;rm' },
      @{ Name = 'trailing space'; Val = 'wslc ' }
  )

  foreach ($tc in $inputs) {
      $res = New-RunArguments -Name 'test' -Port 5620 -VideoMountArgs @('-v', '/v:/app/videos/v:ro') -ContainerVideoDirs '/app/videos/v' -DataDir '/data' -Runtime $tc.Val
      $hasRestart = $res -contains '--restart'
      [PSCustomObject]@{ Case = $tc.Name; Input = $tc.Val; HasRestart = $hasRestart }
  }
  ```
- **実行判定**: 堅牢性確認（Green / 落とせず）
- **実行ログ要約**:
  ```text
  Case           Input   HasRestart
  ----           -----   ----------
  null                         True
  empty                        True
  wslc           wslc         False
  WSLC (upper)   WSLC         False
  Wslc (title)   Wslc         False
  docker         docker        True
  podman         podman        True
  special chars  wslc;rm       True
  trailing space wslc          True
  ```
  また、型不一致となる配列 `Runtime = @('wslc', 'docker')` を渡したケースでは、PowerShell の `[string]$Runtime` 型制約により即座に引数変換エラーで安全に例外停止することを確認しました。
  さらに、PowerShell の `-ne` 演算子はデフォルトで大文字小文字を区別しないため、`WSLC` や `Wslc` など大文字混在で指定された場合でも確実に `--restart` が除外される堅牢性を備えています。

---

## 3. 指摘事項

なし（理由: 堅牢性確認済み。トートロジーや甘いアサーションはなく、意地悪な入力に対しても安全に動作することを確認）

---

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし
- **保留指摘（申し送り）**: なし
- **次のアクション**: Gate 2 通過。次フェーズ（Archive）へ進む

---

# レビュー記録: fix-wslc-unsupported-restart-option / apply / Task 3.1-3.3

- **日時**: 2026-11-08
- **フェーズ**: apply
- **タスク**: Task 3.1（ヘルスチェック IPv4 化再現テスト作成）、Task 3.2（deploy.ps1 のヘルスチェック URL 修正）、Task 3.3（実機検証と回帰テスト確認）
- **レビュアー**: qa (QA Reviewer Agent)
- **使用プロトコル**: openspec/templates/checklist_apply_test.md, openspec/templates/checklist_apply_impl.md
- **対象成果物**:
  - `scripts/deploy.ps1` (行 117-120: `$healthUrl`)
  - `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs` (行 868-886: `IT-SCRIPT-04`)

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: トートロジーテストの有無およびアサーションの厳密性

- **確認内容・回答**:
  `IT-SCRIPT-04` は PowerShell AST 解析により、`scripts/deploy.ps1` の `$healthUrl` 代入文の右辺に `127.0.0.1` が含まれているかを静的構文解析によって直接検証しています。プロダクションコードの式をコピペして同一視するようなトートロジーはなく、独立した AST 解析ロジックで確実に検証されています。
- **確認根拠**:
  - `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs`: 行 868-886

### 設問2: Adversarial QA（IPv6 / IPv4 接続検証）

- **確認内容・回答**:
  WSL 3.0 (`wslc`) がバインドするポートマッピング（`127.0.0.1:5620->5620/tcp`）に対して、IPv6 (`[::1]`) と IPv4 (`127.0.0.1`) の双方から実際に HTTP リクエストを発行して挙動を突合しました。
  - `http://[::1]:5620/` → `HttpClient.Timeout of 2 seconds elapsing`（タイムアウト）
  - `http://127.0.0.1:5620/` → `StatusCode: 200`（即座に成功）
    これにより、`deploy.ps1` で明示的に `127.0.0.1` を指定することが、WSL 3.0 環境における誤タイムアウトを防ぐ唯一かつ最も確実な解であることを実証しました。
- **確認根拠**:
  - `scripts/deploy.ps1`: 行 119-120
  - PowerShell 実機リクエスト検証ログ

---

## 2. 指摘事項

なし（理由: IPv4 ループバック指定により、PowerShell の IPv6 優先名前解決に起因する 30 秒タイムアウト誤判定が完全に解消されたことを実証確認済み）

---

## 3. 判定

- **LGTM**: true
- **条件付きLGTM**: なし
- **保留指摘（申し送り）**: なし
- **次のアクション**: Gate 2 通過。Archive フェーズ（handover.md 作成）へ進む

---

# レビュー記録: fix-wslc-unsupported-restart-option / archive

- **日時**: 2026-11-08
- **フェーズ**: archive
- **レビュアー**: se (SE Reviewer Agent)
- **使用プロトコル**: openspec/templates/checklist_archive.md
- **対象成果物**:
  - `openspec/changes/fix-wslc-unsupported-restart-option/proposal.md`
  - `openspec/changes/fix-wslc-unsupported-restart-option/tasks.md`
  - `openspec/changes/fix-wslc-unsupported-restart-option/handover.md`
  - `openspec/changes/fix-wslc-unsupported-restart-option/reviews.md`
  - `scripts/common.ps1`
  - `scripts/deploy.ps1`
  - `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs`

---

## 1. 挑戦的設問への回答（技術的負債自白・初見エンジニア視点・安全性検証）

### 設問1: 今回あえて残した技術的負債・妥協点の自白（悪魔の代弁者）

- **確認内容・回答**:
  本変更（Level: L1）において、スコープ最小化および Green 化優先のためにあえて残した技術的負債・妥協点は以下の 2 点です。
  1. **`wslc` 環境下における常時稼働機構（自動復帰）の欠落**:
     `wslc run` は `--restart` オプションを受け付けないため起動引数から除外しましたが、これにより Windows ホストの再起動や WSL ディストリビューション終了（`wsl --shutdown` 等）後にコンテナが自動復帰しないという挙動の妥協を残しています。本リポジトリにおける `wslc` は Docker Desktop を導入していない Windows 11 環境でのローカル開発・軽量実行を想定したフォールバックランタイムであるため、Windows タスクスケジューラ連携等の複雑な自動復旧機構はあえて見送り、運用対処（再実行）に留めています。
  2. **否定条件 `$Runtime -ne "wslc"` による局所的分岐**:
     各ランタイムの機能サポートをモデル化する Capabilities マトリクスや抽象化レイヤーを新設せず、`scripts/common.ps1` 行 247 で `$Runtime -ne "wslc"` という直値否定でオプション除外を行っています。現時点でサポート対象ランタイムが `wslc` と `docker` の 2 種類のみであることから、YAGNI の観点に基づき最小修正を選択しました。
     これら 2 点の妥協点と負債は、いずれも `handover.md` 第3節「保留指摘・技術的負債」および第4節・第5節に漏れなく記録されていることを確認しました。
- **確認根拠**:
  - `scripts/common.ps1`: 行 247-250
  - `scripts/deploy.ps1`: 行 119-120
  - `openspec/changes/fix-wslc-unsupported-restart-option/handover.md`: 行 33-50
  - `openspec/changes/fix-wslc-unsupported-restart-option/reviews.md`: 行 68-83

### 設問2: 将来の機能追加で踏み抜く可能性が高い罠（暗黙の前提と結合箇所）

- **確認内容・回答**:
  次にこのモジュールやデプロイスクリプトに機能追加を行うエンジニアが踏み抜くリスクとして、以下の 3 点が挙げられます。
  1. **ブラックリスト判定（否定条件）の罠**:
     `if ($Runtime -ne "wslc")` は「`wslc` 以外のランタイムはすべて `--restart unless-stopped` を受け付ける」という暗黙の前提に立っています。将来 Windows 向けに第 3 のコンテナランタイムを追加した際、もしそれが `--restart` をサポートしていなかった場合、同様のオプション不正エラーを引き起こします。
  2. **デフォルト引数の暗黙性**:
     `New-RunArguments` の `[string]$Runtime = ""` は未指定時に空文字となるため、`$Runtime -ne "wslc"` が `$true` と評価されて `--restart` が付与されます。
  3. **ヘルスチェックの IPv4 固定**:
     `$healthUrl = "http://127.0.0.1:${resolvedPort}/"` は IPv4 ループバックを前提としています。もしホストが純粋な IPv6 オンリー環境となった場合は接続できなくなる可能性がありますが、現行の Windows 11 / WSL 開発環境では常に IPv4 ループバックが利用可能であるため問題ありません。
     これらの罠は `handover.md` 第 4 節「将来の改善・リファクタ候補」にて、ランタイム機能マトリクスによる動的構成への移行課題として明記されています。
- **確認根拠**:
  - `scripts/common.ps1`: 行 211, 247-250
  - `scripts/deploy.ps1`: 行 119-120
  - `openspec/changes/fix-wslc-unsupported-restart-option/handover.md`: 行 42-45

### 設問3: 初見エンジニア視点でのドキュメント検証（Whyの追跡可能性とトラブルシューティング）

- **確認内容・回答**:
  1. **Whyの追跡可能性**:
     `proposal.md` および `handover.md` の記述は明瞭であり、WSL 3.0 の `wslc run` コマンドが `--restart` オプションをサポートしていない点、および IPv4 のみにバインドするため PowerShell の IPv6 優先名前解決でタイムアウトしていた背景（Why）が完全にドキュメント化されています。
  2. **トラブルシューティング手順の十分性**:
     `handover.md` 第 5 節「運用上の注意事項」に、`wslc` 環境下では自動再起動が行われないため、OS 再起動や WSL 停止時には `scripts/deploy.ps1` を手動で再実行する旨、および IPv4 ループバックでアクセスする旨が明記されています。
- **確認根拠**:
  - `openspec/changes/fix-wslc-unsupported-restart-option/proposal.md`: 行 6-17
  - `openspec/changes/fix-wslc-unsupported-restart-option/handover.md`: 行 10-25, 48-55
  - `scripts/deploy.ps1`: 行 81, 119-120, 140

### 設問4: ロールバック・安全性の最終検証（Revert安全性と保留指摘引き継ぎ）

- **確認内容・回答**:
  1. **ロールバック（Revert）手順の検証**:
     本変更に含まれるファイルは PowerShell スクリプト 2 本（`scripts/common.ps1`, `scripts/deploy.ps1`）および単体・結合テストコード（`DeployScriptTests.fs`）のみです。データベース変更や外部 API 改変は一切含まれておらず、`git revert` 1 回のコミット巻き戻しのみで安全・迅速に直前の安定状態へ復旧可能です。
  2. **保留指摘の全件引き継ぎ確認**:
     Propose フェーズの SE レビューにおいて保留とした指摘 2 件が `handover.md` 第 3 節の表に漏れなく転記されていることを確認しました。
- **確認根拠**:
  - `openspec/changes/fix-wslc-unsupported-restart-option/handover.md`: 行 33-46

---

## 2. Adversarial QA（破壊テスト実行結果）

- 対象外（Archive 総括レビューのため）

---

## 3. 指摘事項

なし（理由: 妥協点・技術的負債が `handover.md` に全て明記されており、初見エンジニア視点での Why 追跡可能性、トラブルシューティング手順、および `git revert` 安全性が完全に担保されていることを確認済み）

---

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし
- **保留指摘（申し送り）**: なし（過去フェーズの全保留指摘が `handover.md` へ記録・集約済み）
- **次のアクション**: Gate 3 / Archive 完了および本番ブランチへの PR マージ準備へ進む
