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
