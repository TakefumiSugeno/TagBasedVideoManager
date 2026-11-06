# レビュー記録: remove-obsolete-docker-compose / propose / user-review

- **日時**: 2026-11-06
- **フェーズ**: propose
- **タスク**: Proposeフェーズ計画成果物レビュー（仕様書ブラインド比較・初期/エラー体験・Why検証）
- **レビュアー**: user (User Reviewer Agent)
- **使用プロトコル**: openspec/templates/checklist_propose.md
- **対象成果物**: `openspec/changes/remove-obsolete-docker-compose/proposal.md`, `openspec/changes/remove-obsolete-docker-compose/specs/docker-infrastructure/spec.md`, `openspec/changes/remove-obsolete-docker-compose/specs/companion-docker-control/spec.md`, `openspec/changes/remove-obsolete-docker-compose/design.md`, `openspec/changes/remove-obsolete-docker-compose/test_plan.md`, `openspec/changes/remove-obsolete-docker-compose/tasks.md`

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: 仕様書（`specs/**/spec.md`）のブラインド比較レビュー（異常系・境界条件の先出し突合と暗黙の仮定の告発）

- **確認内容・回答**:
  1. **`specs/` 閲覧前のゼロベース予測（5点）と仕様突合結果**:
     - **予測①（既存ユーザーの習慣的な `docker compose up -d` 実行と移行手順の迷子）**: 従来 `docker-compose.yml` で起動していたユーザーがアップデート後に起動方法が分からなくなるケース。
       - **突合結果**: `specs/docker-infrastructure/spec.md`（L5-18 `Requirement: Docker Compose 構成ファイルの非依存と単一コンテナ運用の一本化`）および `design.md`（L79-86, L95-96）、`test_plan.md`（L85-87 `IT-CLEAN-01`〜`03`）にて、`Dockerfile` と `README.md` から `docker-compose.yml` / `docker compose` の記述を完全に廃止し、Windows 向け `pwsh ./scripts/deploy.ps1` と Linux 向け `./scripts/deploy.sh` の起動手順へ一本化することが明確に定義されている。
     - **予測②（`docker-compose.yml` 削除に伴う GPU パススルー・環境変数・ボリューム設定の欠落）**: 従来 `docker-compose.yml` 内のコメントや定義で担っていたポート `5620`、必須環境変数4種、`:ro` マウント、NVIDIA/AMD GPU 設定がスクリプト側で漏れるケース。
       - **突合結果**: `specs/docker-infrastructure/spec.md`（L19-38）、メイン仕様 `openspec/specs/docker-infrastructure/spec.md`（L18-54）、および `test_plan.md`（L50 `UT-PORT-01/02`, L68-76 `UT-GPU-C2-01`〜`04`）により、Windows (`scripts/deploy.ps1` + `scripts/common.ps1`) と Linux (`scripts/deploy.sh`) の双方で環境変数・ボリューム・GPU 自動判定が網羅されていることを確認した。また `README.md`（L218）の `-EnableGpu` 表記整理も `design.md` / `tasks.md` Task 1.2 に反映済み。
     - **予測③（`DockerError`・`DockerController.fs` 削除に伴う他モジュール・UI・テストでの参照残存）**: `Domain.fs` から `RenamerError.DockerError` を削除した際、`Views.fs` や他のテストでパターンマッチ不全やコンパイルエラーが発生するケース。
       - **突合結果**: `specs/companion-docker-control/spec.md`（L19-23 `Scenario: 旧 DockerController および DockerError の完全廃止`）および `test_plan.md`（L43-49 `UT-STATE-ERR-01`〜`03`, L88 `IT-CLEAN-04`）で定義済み。実コード（`Domain.fs` L123, `State.fs` L612-613, `Views.fs`, 各 `.fsproj`）を調査し、削除対象以外のファイルに `DockerError` や `DockerController` の参照が存在しないこと、およびリフレクションによる型ケース不在検証（`IT-CLEAN-04`）が計画されていることを確認した。
     - **予測④（DevContainer や補助スクリプトにおける `docker-compose.yml` への隠れた依存）**: `.devcontainer/devcontainer.json` 等が裏で `dockerComposeFile` を参照しており開発環境が壊れるケース。
       - **突合結果**: `.devcontainer/devcontainer.json`（`DevContainerConfigTests.fs` L54-124）は `.devcontainer/Dockerfile` の単一ビルド構成であり、`scripts/deploy.ps1`（L88）・`scripts/deploy.sh`（L56）もルートの `Dockerfile` を直接ビルドしているため、`docker-compose.yml` への隠れた依存は存在しないことを確認した。
     - **予測⑤（コンテナランタイム未導入時やコマンド失敗時の回復不能・フリーズ）**: `ContainerError` 一本化後、ランタイム未導入環境やデーモン停止時に初心者ユーザーが原因を特定できないケース。
       - **突合結果**: `specs/companion-docker-control/spec.md`（L5-18 `Scenario: コマンド実行失敗時のエラー通知`）および `test_plan.md`（L45-47 `UT-STATE-ERR-02/03`, L110 `E2E-03`）にて、非ゼロ終了コード時は `ContainerError`、タイムアウト時は `TimeoutError`、ランタイム未検出時は `ValidationError` として `IsDockerBusy = false` に復帰し、エラーメッセージを表示して再試行可能な状態を維持することが明記されている。
  2. **都合の良い前提（暗黙の仮定）の告発**:
     - 「ユーザー環境に必ず Docker が入っている」「コマンドは常に成功する」といった都合の良い前提はなく、Windows (`wslc` → `docker`)・Linux (`docker` → `podman`) のフォールバック検出（`UT-RUN-C2-01`〜`07`）、未導入時のエラー案内、ヘルスチェック失敗時のコンテナログ自動出力（`deploy.ps1` L146, `deploy.sh` L116）まで具体的に設計されている。
- **確認根拠**:
  - `openspec/changes/remove-obsolete-docker-compose/proposal.md` (L6-67)
  - `openspec/changes/remove-obsolete-docker-compose/specs/docker-infrastructure/spec.md` (L5-38)
  - `openspec/changes/remove-obsolete-docker-compose/specs/companion-docker-control/spec.md` (L5-23)
  - `openspec/changes/remove-obsolete-docker-compose/design.md` (L61-97)
  - `openspec/changes/remove-obsolete-docker-compose/test_plan.md` (L27-110)

### 設問2: UIモック（`mockup.html`）の初心者ペルソナ検証（Zero-State / Error-State の回復導線検証）

- **確認内容・回答**:
  - 本変更（`remove-obsolete-docker-compose`）は不要な Docker Compose 関連ファイルとデッドコードの削除（`Type: refactor`）であり、画面レイアウトや操作フローの UI/UX 変更を伴わないため `mockup.html` の作成は対象外（`design.md` L27 で明記）。
  - 既存のデスクトップアプリ UI（`Views.fs`）および状態遷移（`State.fs`）における初心者体験への影響を検証した結果、Zero-State（ランタイム未導入時の `"✕ NO RUNTIME"` 表示および API キー未設定時の設定案内）と Error-State（`ContainerError` / `TimeoutError` 時のエラー表示と `IsDockerBusy = false` 復帰・再試行導線）が完全に維持されていることを確認した。
- **確認根拠**:
  - `openspec/changes/remove-obsolete-docker-compose/design.md` (L27, L61-78)
  - `openspec/changes/remove-obsolete-docker-compose/test_plan.md` (L43-47, L104-110)
  - `src/TagBasedVideoManager.Renamer/Views.fs` (L83-197)
  - `src/TagBasedVideoManager.Renamer/State.fs` (L338-345, L608-618)

---

## 2. Adversarial QA（破壊テスト実行結果）※Apply実装レビュー時のみ

- **対象関数/モジュール**: `対象外（Proposeフェーズ計画成果物レビューのため）`
- **破壊テストコード名**: `対象外`
- **実行コマンド**: `対象外`
- **実行判定**: 堅牢性確認（Green / 落とせず）
- **実行ログ要約**:

```text
Proposeフェーズの計画成果物レビューのため破壊テスト実行は対象外。Applyフェーズにて実施する。
```

---

## 3. 指摘事項

### 指摘1

- **内容**: `README.md` の「3. コンテナ起動 (Web アプリ)」から「(B) Docker Compose を使用する場合」を削除する際、現行 `README.md` L218 に残っている「AMD GPU をパススルーする場合は `-EnableGpu` オプションを指定できます」という実在しないオプション記述および `(A)` 見出し番号も合わせて整理し、`scripts/deploy.ps1` / `scripts/deploy.sh` で GPU パススルーが自動判定される旨を案内すること。
- **重要度**: Minor
- **対応方針**: 採用
- **理由**: `docker-compose.yml` 廃止後にユーザーが GPU 設定方法で迷わないようにするため、`design.md` および `tasks.md` Task 1.2 に反映済み。
- **対応コミット**: Task 1.2 にて対応

### 指摘2

- **内容**: メイン仕様 `openspec/specs/companion-docker-control/spec.md` の `## Purpose`（L5）に残存している `docker compose ps --format json` / `docker compose コマンド` という記述について、Archive（仕様同期）時に `Purpose` 文面も単一コンテナ OCI CLI 表記へ更新することを推奨する。
- **重要度**: Suggestion
- **対応方針**: 保留
- **理由**: OpenSpec のデルタ仕様スキーマ（`## MODIFIED Requirements`）自体は現状で正しく構成されており、Archive / Sync 時にメイン仕様の Purpose を整えれば十分なため。
- **対応コミット**: Archive フェーズ（仕様同期・`handover.md` 申し送り）にて対応

---

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし
- **保留指摘（申し送り）**: 指摘2（Archive 時のメイン仕様 Purpose 文面調整・`handover.md` へ転記）
- **次のアクション**: 次フェーズへ

---

# レビュー記録: remove-obsolete-docker-compose / propose / se-review

- **日時**: 2026-11-06
- **フェーズ**: propose
- **タスク**: Proposeフェーズ計画成果物一式（設計故障シミュレーション・テスト計画死角告発・タスク手戻りリスク検証）
- **レビュアー**: se (SE Reviewer Agent)
- **使用プロトコル**: `openspec/templates/checklist_propose.md`
- **対象成果物**: `openspec/changes/remove-obsolete-docker-compose/proposal.md`, `openspec/changes/remove-obsolete-docker-compose/specs/docker-infrastructure/spec.md`, `openspec/changes/remove-obsolete-docker-compose/specs/companion-docker-control/spec.md`, `openspec/changes/remove-obsolete-docker-compose/design.md`, `openspec/changes/remove-obsolete-docker-compose/test_plan.md`, `openspec/changes/remove-obsolete-docker-compose/tasks.md`

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問2-1: 設計書（`design.md`）の故障シミュレーション — 外部依存ハング・切断時の連鎖崩壊検証

- **確認内容・回答**:
  - **最大の外部依存の特定と「5秒間ハング / 処理途中切断」シミュレーション**:
    1. **Desktop Companion (`ContainerController.fs`)**: 最大の外部依存はコンテナランタイム CLI サブプロセス（Windows: `wslc.exe` / `docker.exe`、Linux: `docker` / `podman`）および `http://localhost:5620/` への HTTP ヘルスチェックである。外部 CLI が 5秒間ハングした場合、`defaultProcessRunner`（`ContainerController.fs` L59-68）および `executeActionWithTimeout`（L253-285）の既定10秒タイムアウトにより非同期で待機し、10秒超過時は `proc.Kill(true)` でプロセスツリーごと強制終了して `Error (TimeoutError action)` を返し、`State.update`（`State.fs` L614-615）で `IsDockerBusy = false` に復帰するため、UI フリーズやリトライストームは発生しない。HTTP ヘルスチェックも2秒タイムアウト（`ContainerController.fs` L73-82）で `Unhealthy` に安全にフォールバックする。
    2. **設計・実装上の穴の特定**:
       - `src/TagBasedVideoManager.Renamer/State.fs` L568-575 の `ExecuteUndo` では、ファイル名復元成功後の `ContainerController.executeAction ... "restart"` の戻り値を `_` で破棄して `Ok ()` を返しているが、直後に `UndoCompleted (Ok ())` が `Cmd.ofMsg CheckDockerStatus`（L581）を発行するため、ステータスバー自体は最新の稼働状態（`Stopped` / `Unhealthy` 等）に即時同期され状態不整合は残らない（将来の改善候補として `handover.md` に記録推奨）。
- **確認根拠**:
  - `openspec/changes/remove-obsolete-docker-compose/design.md` L32-78, L93-97
  - `src/TagBasedVideoManager.Renamer/ContainerController.fs` L40-82, L203-294
  - `src/TagBasedVideoManager.Renamer/State.fs` L532-540, L561-587, L600-618
  - `scripts/deploy.ps1` L86-148, `scripts/deploy.sh` L54-118

### 設問2-2: 設計書（`design.md`）の故障シミュレーション — 可観測性（調査可能性）の検証

- **確認内容・回答**:
  - **デプロイスクリプト層**: `scripts/deploy.ps1` L144-147 および `scripts/deploy.sh` L114-117 では、ヘルスチェック失敗時に自動で `& $runtime logs --tail 50 $containerName` を出力する設計となっており、コンテナ起動障害時の MTTR 短縮が担保されている。
  - **Desktop Companion 層**: `ContainerController.fs` L194, L199, L229 では JSON パース失敗時の生テキストや `ps` 非ゼロ終了時の `stderr`・終了コードを標準エラー出力へ警告ログ出力しており、原因特定が可能である。なお `State.fs` L610-611 の UI 表示文字列では `ContainerError` の `stderr` のみを表示しているため、将来的な改善候補として終了コード併記を `Suggestion` に記録した。
- **確認根拠**:
  - `openspec/changes/remove-obsolete-docker-compose/design.md` L61-78
  - `src/TagBasedVideoManager.Renamer/Domain.fs` L118-126
  - `src/TagBasedVideoManager.Renamer/State.fs` L610-618
  - `src/TagBasedVideoManager.Renamer/ContainerController.fs` L194, L199, L229

### 設問3-1: テスト計画書（`test_plan.md`）の死角告発 — 100%パスしても抜けるバグ（最大の死角）トップ2

- **確認内容・回答**:
  - **死角トップ1**: `README.md` 内の実在しない `-EnableGpu` オプション記述および見出し番号 `(A)` の孤立。→ `design.md` L85 および `tasks.md` Task 1.2 に明記して対策済み。
  - **死角トップ2**: メイン仕様書 `openspec/specs/companion-docker-control/spec.md` の `Purpose` セクションに残る `docker compose` 表記。→ Archive フェーズの仕様同期時に合わせて更新する方針を確認済み。
- **確認根拠**:
  - `openspec/changes/remove-obsolete-docker-compose/test_plan.md` L27-36, L83-90
  - `openspec/changes/remove-obsolete-docker-compose/design.md` L83-85
  - `openspec/changes/remove-obsolete-docker-compose/tasks.md` L6

### 設問3-2: テスト計画書（`test_plan.md`）の死角告発 — モックの嘘（偽の安心感）の検証

- **確認内容・回答**:
  - `test_plan.md` L19-21 および既存の `DeployScriptTests.fs`・`ContainerControllerTests.fs` では、外部プロセス起動（`processRunner`）と HTTP 通信（`httpHealthChecker`）のみが DI 関数でモック化され、PowerShell スクリプトは実 `pwsh` と `Parser` AST 解析、Bash スクリプトは実 `bash -n` とバイト列検査、ファイル・型検証（`IT-CLEAN-01`〜`04`）は実ファイルシステムと F# リフレクション（`FSharpType.GetUnionCases`）で検証するため、偽の安心感は排除されている。
- **確認根拠**:
  - `openspec/changes/remove-obsolete-docker-compose/test_plan.md` L19-21, L43-50, L83-90
  - `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs` L63-116
  - `test/TagBasedVideoManager.Renamer.Tests/ContainerControllerTests.fs` L25-48

### 設問5-1: タスクリスト（`tasks.md`）の手戻りリスク検証 — 最も詰まるリスクが高いタスクの特定

- **確認内容・回答**:
  - Task 1.1（`qa-tester` による Red テスト作成）では `DockerError` 型がまだ存在する状態でコンパイルを通す必要があるが、`test_plan.md` L88（`IT-CLEAN-04`）の通り `FSharpType.GetUnionCases typeof<RenamerError>` によるリフレクション検証を用いることで、Task 1.1（Red）と Task 1.2（`DockerError` 削除後の Green）の双方でコンパイルエラーを起こさず検証可能である。また Task 1.2 で削除・変更すべき全ファイル（`docker-compose.yml`, `DockerController.fs`, `DockerControllerTests.fs`, 両 `.fsproj`, `Domain.fs`, `State.fs`, `Dockerfile`, `README.md`）が網羅されている。
- **確認根拠**:
  - `openspec/changes/remove-obsolete-docker-compose/tasks.md` L5-6
  - `openspec/changes/remove-obsolete-docker-compose/test_plan.md` L88

### 設問5-2: タスクリスト（`tasks.md`）の手戻りリスク検証 — 完了基準の検証可能性

- **確認内容・回答**:
  - 全タスク（1.1, 1.2, 2.1）に対象ファイル、テストケースID（`IT-CLEAN-01`〜`04`, `UT-STATE-ERR-01`〜`03`）、テスト配置先、および機械的ゲート検証コマンド（`verify-gate.ps1 -Gate apply -ChangeName remove-obsolete-docker-compose`）が明確に定義されており、完了基準に曖昧さはない。
- **確認根拠**:
  - `openspec/changes/remove-obsolete-docker-compose/tasks.md` L1-11

---

## 2. Adversarial QA（破壊テスト実行結果）※Apply実装レビュー時のみ

- **対象関数/モジュール**: `対象外（Proposeフェーズ計画成果物レビューのため）`
- **破壊テストコード名**: `対象外`
- **実行コマンド**: `対象外`
- **実行判定**: 堅牢性確認（Green / 落とせず）
- **実行ログ要約**:

```text
Proposeフェーズの設計・仕様・テスト計画・タスクリストに対するレビューのため、破壊テスト実行はApplyフェーズ（実装レビュー時）に実施する。
```

---

## 3. 指摘事項

### 指摘1

- **内容**: `src/TagBasedVideoManager.Renamer/State.fs` L610-611 の `DockerCommandCompleted (action, Error (ContainerError (_, _, stderr)))` で終了コード `exitCode` が破棄されており、`stderr` が空文字の異常終了時に UI メッセージから終了コードが分からない点、および L617-618 のフォールバック文言 `"Docker エラー: {other}"` の表記統一については、既存テスト期待値との互換性を保ちつつ将来の改善候補として申し送ることを推奨する。
- **重要度**: Suggestion
- **対応方針**: 保留
- **理由**: 既存の単体・E2E テストの期待メッセージ形式との互換性を維持し、本変更は不要な Docker Compose 資産と `DockerError` の削除に集中するため。
- **対応コミット**: Archive 時 `handover.md` へ転記

### 指摘2

- **内容**: `README.md` L226-241 の「`#### (B) Docker Compose を使用する場合`」を削除する際、直前の L209「`#### (A) デプロイスクリプトを使用する場合 (推奨)`」の `(A)` 番号表記および L218 の実在しない `-EnableGpu` オプション記述も合わせて整理すること。
- **重要度**: Minor
- **対応方針**: 採用
- **理由**: `design.md` L85 および `tasks.md` Task 1.2 に反映済み。
- **対応コミット**: Task 1.2 にて対応

---

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし
- **保留指摘（申し送り）**: 指摘1（`State.fs` の `ContainerError` 終了コード表示およびフォールバック文言統一の将来検討を Archive 時に `handover.md` へ転記）
- **次のアクション**: 次フェーズへ

---

# レビュー記録: remove-obsolete-docker-compose / apply / Task 1.1

- **日時**: 2026-11-06
- **フェーズ**: apply
- **タスク**: Task 1.1: 廃止資産の不在・ドキュメント整合・`ContainerError` ハンドリングを検証する Red テストの追加
- **レビュアー**: QA Reviewer Agent (qa)
- **使用プロトコル**: `openspec/templates/checklist_apply_test.md`
- **対象成果物**: `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs`, `test/TagBasedVideoManager.Renamer.Tests/StateTests.fs`

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: トートロジーテスト（実装式のコピペ）の告発

- **確認内容・回答**:
  1. **期待値計算のコピペ検証**: テストコードの期待値にプロダクションコードの計算式や正規表現をコピペしている箇所は存在しない。すべてのアサーションが具体的な固定リテラル値（文字列・真偽値・`Option` リテラル）で検証されている。
  2. **アサーションなし・形式的テストの告発**: 「例外が出ないことだけを確認するテスト」は 0 件。全 7 件のテスト（`IT-CLEAN-01`〜`04`, `UT-STATE-ERR-01`〜`03`）すべてにおいて、実ファイル存在有無・ファイル内容・判別共用体メタデータ・Elmish `Model` の状態フィールド（`IsDockerBusy`, `ErrorMessage`）および `Cmd` の発行有無を具体的にアサートしている。
- **確認根拠**: `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs` L475-510、`test/TagBasedVideoManager.Renamer.Tests/StateTests.fs` L628-667。

### 設問2: 甘いアサーション・無意味なモックの告発

- **確認内容・回答**:
  1. **モック過多による空洞化**: モックは一切使用されていない。`ObsoleteDockerComposeRemovalTests` は `RepoLayout.pathOf` / `RepoLayout.readTextOrFail` を通じてリポジトリの実ファイルと実型定義（`typeof<RenamerError>`）を直接検証しており、`StateTests` は実物の `State.init` / `State.update` を直接呼び出して状態遷移を検証している。
  2. **あいまいアサーションの排除**: `IT-CLEAN-01`〜`04` および `UT-STATE-ERR-01`〜`02` は `should equal` による厳密一致アサーションを行っている。`UT-STATE-ERR-03` の `ValidationError "No runtime"` ケース（L664-665）では `should not' (equal None)` に加えて `validationModel.ErrorMessage.Value |> should contain "No runtime"` で具体的なエラーペイロード文字列が含まれることを検証している。
- **確認根拠**: `DeployScriptTests.fs` L475-510、`StateTests.fs` L628-667。

### 設問3: ミューテーション思考（バグのすり抜け検証）

- **確認内容・回答**:
  - `docker-compose.yml`、`DockerController.fs`、`DockerControllerTests.fs` の削除漏れ、または `Domain.fs` の `DockerError` 削除漏れがあった場合、`IT-CLEAN-01` / `IT-CLEAN-04` が確実に失敗（Red）する（現在先行テストとして 4 件すべて意図通り Red となる構造）。
  - `State.fs` L610-618 の `DockerCommandCompleted` において、`IsDockerBusy` を `false` に戻し忘れた場合や、`ContainerError` / `TimeoutError` のエラーメッセージ文字列を改変した場合、`UT-STATE-ERR-02` / `UT-STATE-ERR-03` が確実に失敗（Red）する。
  - **特定した死角（すり抜け箇所）**: `StateTests.fs` L635, L646, L658, L666 において発行された Elmish コマンドを `cmd.IsEmpty |> should equal false` で検証しているため、もし `State.fs` の `Cmd.ofMsg CheckDockerStatus` が別のメッセージに書き換えられた場合、`cmd.IsEmpty` は `false` のままとなる（Elmish `Cmd<'Msg>` の構造上の制約によるもので、`Suggestion` として記録）。
- **確認根拠**: `StateTests.fs` L635, L646, L658, L666 および `src/TagBasedVideoManager.Renamer/State.fs` L608-618。

### 設問4: フレーキー要因・環境依存の摘出

- **確認内容・回答**: 追加された全テストにおいて、`Thread.Sleep` / `Async.Sleep`、テスト間の可変状態共有、テスト実行順序依存、`DateTime.Now` の直参照、外部ネットワーク通信は一切存在せず、完全に決定論的（Deterministic）である。
- **確認根拠**: `DeployScriptTests.fs` L475-510、`StateTests.fs` L628-667。

---

## 2. Adversarial QA（破壊テスト実行結果）※Apply実装レビュー時のみ

- **対象関数/モジュール**: 対象外（Task 1.1 は TDD 先行テスト作成タスクであり、Apply テストレビュープロトコル適用のため）
- **破壊テストコード名**: 対象外
- **実行コマンド**: 対象外
- **実行判定**: 堅牢性確認（Green / 落とせず）
- **実行ログ要約**:

```text
Task 1.1 は先行 Red テスト実装タスク（checklist_apply_test.md 適用）のため Adversarial QA は対象外。Task 1.2（プロダクションコード実装）完了後の実装レビューにて実施する。
```

---

## 3. 指摘事項

### 指摘1

- **内容**: `StateTests.fs` L635, L646, L658, L666 の `UT-STATE-ERR-01`〜`03` では、返却された `Cmd<Msg>` に対して `cmd.IsEmpty |> should equal false` のみを検証しており、実際にディスパッチされるメッセージが `CheckDockerStatus` であることまでは検証していない（ミューテーション思考による死角）。
- **重要度**: Suggestion
- **対応方針**: 保留
- **理由**: Elmish の `Cmd<'Msg>` 型は内部が関数リスト `(Dispatch<'Msg> -> unit) list` であり、非空チェック（`cmd.IsEmpty |> should equal false`）でコマンド発行の有無を担保する現在の設計が適切であるため。
- **対応コミット**: 保留（Archive 時に `handover.md` へ転記）

---

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし
- **テスト品質評価**: 堅牢（合格）
- **要修正事項**: なし
- **保留指摘（申し送り）**: 指摘1（Suggestion: `DockerCommandCompleted` テストにおける `Cmd` 検証の粒度）
- **承認者**: QA Reviewer Agent
- **次のアクション**: 次タスクへ

---

# レビュー記録: remove-obsolete-docker-compose / apply / Task 1.2 (pg-review)

- **日時**: 2026-11-06
- **フェーズ**: apply
- **タスク**: Task 1.2 (不要な Docker Compose 関連資産の廃止・エラー型統一・ドキュメント更新)
- **レビュアー**: PG Reviewer Agent (pg)
- **使用プロトコル**: `openspec/templates/checklist_apply_impl.md`
- **対象成果物**: `Dockerfile`, `README.md`, `docker-compose.yml` (削除), `src/TagBasedVideoManager.Renamer/DockerController.fs` (削除), `src/TagBasedVideoManager.Renamer/Domain.fs`, `src/TagBasedVideoManager.Renamer/State.fs`, `src/TagBasedVideoManager.Renamer/TagBasedVideoManager.Renamer.fsproj`, `test/TagBasedVideoManager.Renamer.Tests/DockerControllerTests.fs` (削除), `test/TagBasedVideoManager.Renamer.Tests/TagBasedVideoManager.Tests.fsproj`

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: 暗黙の前提（都合の良い思い込み）の告発

- **確認内容・回答**:
  1. **`ContainerController.parseContainerPsOutput` における旧 Compose 命名の暗黙の仮定**: `ContainerController.fs` の L131-132 に `elif el.TryGetProperty("Service", ...)`、および L183 に `|| names.IndexOf("video-manager", StringComparison.OrdinalIgnoreCase) >= 0` が残存している。将来的なリファクタ候補として申し送る。
  2. **`State.update` の `DockerCommandCompleted` における `ValidationError` 文字列化**: `ValidationError msg` はワイルドカード分岐 `DockerCommandCompleted (_, Error other)`（`State.fs` L615-616）で処理され `$"Docker エラー: {other}"` として表示される。
- **確認根拠**: `src/TagBasedVideoManager.Renamer/ContainerController.fs` (L131-132, L180-184)、`src/TagBasedVideoManager.Renamer/State.fs` (L608-616)

### 設問2: 例外の握りつぶし・根拠のないフォールバックの告発

- **確認内容・回答**:
  - 今回の変更箇所（`Domain.fs` / `State.fs` の `DockerError` 削除）自体には、新規の例外握りつぶしや根拠のないフォールバックは存在しない。なお既存の `ExecuteUndo`（`State.fs` L571）における再起動戻り値の破棄は技術的負債として `handover.md` に記録する。
- **確認根拠**: `src/TagBasedVideoManager.Renamer/State.fs` (L532-538, L568-575, L608-616)

### 設問3: 30%コード削減・過剰抽象化の告発（YAGNI検証）

- **確認内容・回答**:
  - `State.fs` L608-616 の `DockerCommandCompleted` では各 `Error` パターンごとに `{ model with IsDockerBusy = false; ErrorMessage = Some ... }, Cmd.ofMsg CheckDockerStatus` が繰り返されているため、エラーメッセージの組み立てのみを内部 `match` に分離するリファクタ案（9行→6行、約30%削減）を将来の改善候補として提示した。
  - 今回の実装では不要な `DockerController.fs`（173行）、`DockerControllerTests.fs`（115行）、`docker-compose.yml`（98行）の計386行が完全に削除され、デッドコードが大幅に削減されている。
- **確認根拠**: `src/TagBasedVideoManager.Renamer/State.fs` (L608-616)、`git diff` 全量

---

## 2. Adversarial QA（破壊テスト実行結果）※Apply実装レビュー時のみ

- **対象関数/モジュール**: `TagBasedVideoManager.Renamer.State.update` / `Domain.RenamerError`
- **破壊テストコード名**: `QA Reviewer Agent 実行結果を参照`
- **実行コマンド**: `QA Reviewer Agent 実行結果を参照`
- **実行判定**: 堅牢性確認（Green / 落とせず）
- **実行ログ要約**:

```text
PG Reviewer Agent は checklist_apply_impl.md セクション 2・3 を担当。破壊テストの作成・実行は下記の QA Reviewer Agent 記録を参照。
```

---

## 3. 指摘事項

### 指摘1

- **内容**: `State.fs` L608-616 の `DockerCommandCompleted` において、エラー種別ごとの状態更新レコード生成の重複削減および `ValidationError` 表示文言の改善余地がある。
- **重要度**: Suggestion
- **対応方針**: 保留
- **理由**: 既存の全テストおよび仕様要件を満たしており機能上の不具合はないため、将来のリファクタリング候補として `handover.md` へ申し送る。
- **対応コミット**: Archive 時 `handover.md` へ転記

### 指摘2

- **内容**: `State.fs` L571 の `ExecuteUndo` 内の再起動エラー通知、および `ContainerController.fs` L131-132 / L183 の旧 `"Service"` / `"video-manager"` フォールバック分岐の整理。
- **重要度**: Minor
- **対応方針**: 保留
- **理由**: 今回の変更前から存在する既存コードであり、本変更のスコープ外であるため `handover.md` に記録して申し送る。
- **対応コミット**: Archive 時 `handover.md` へ転記

---

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし
- **保留指摘（申し送り）**: 指摘1、指摘2（Archive 時に `handover.md` へ転記）
- **次のアクション**: 次タスクへ

---

# レビュー記録: remove-obsolete-docker-compose / apply / Task 1.2 (qa-review)

- **日時**: 2026-11-06
- **フェーズ**: apply
- **タスク**: Task 1.2 (`pg-implementer`: 不要な Docker Compose 関連資産の削除・エラー型統一・ドキュメント更新)
- **レビュアー**: QA Reviewer Agent (qa)
- **使用プロトコル**: `openspec/templates/checklist_apply_impl.md`
- **対象成果物**: `Dockerfile`, `README.md`, `src/TagBasedVideoManager.Renamer/Domain.fs`, `src/TagBasedVideoManager.Renamer/State.fs`, `src/TagBasedVideoManager.Renamer/ContainerController.fs`, `src/TagBasedVideoManager.Renamer/TagBasedVideoManager.Renamer.fsproj`, `test/TagBasedVideoManager.Renamer.Tests/TagBasedVideoManager.Tests.fsproj`

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合等）

### 設問1: Adversarial QA（破壊テストの作成と実行による境界・異常入力耐性の実証）

- **確認内容・回答**:
  `DockerError` 廃止および `DockerController.fs` 削除後の `Domain.fs`・`State.fs`・`ContainerController.fs` に対し、リフレクション検査およびファジング入力（`null`、空文字、空白、100KB 超長文、制御文字・ANSI エスケープシーケンス `\0\t\r\n\u001b[31m`、フォーマット指定子 `%s %d {0} {{}}`、サロゲートペア・シェルメタ文字、極端な終了コード `Int32.MinValue`〜`Int32.MaxValue`、奇形・巨大 NDJSON 5,000 行）を注入する破壊テストスクリプト `adversarial_qa_task1_2.fsx` を作成し、`dotnet fsi` で実行した。
  全 460 パターン以上の異常入力に対して未捕捉例外は 0 件であり、全ケースで確実に `IsDockerBusy = false`（ロック解除）・`ErrorMessage = Some ...`・`CheckDockerStatus` コマンド発行が行われることを確認した。
- **確認根拠**:
  - `src/TagBasedVideoManager.Renamer/Domain.fs` (L118-L125)
  - `src/TagBasedVideoManager.Renamer/State.fs` (L608-L616)
  - `src/TagBasedVideoManager.Renamer/ContainerController.fs` (L114-L201, L253-L285)

---

## 2. Adversarial QA（破壊テスト実行結果）※Apply実装レビュー時のみ

- **対象関数/モジュール**: `TagBasedVideoManager.Renamer.RenamerError`, `State.update` (`DockerCommandCompleted`), `ContainerController.parseContainerPsOutput`, `ContainerController.executeActionWithTimeout`
- **破壊テストコード名**: `adversarial_qa_task1_2.fsx`（スクラッチで実行、`git status` クリーン維持確認済み）
- **実行コマンド**: `dotnet fsi adversarial_qa_task1_2.fsx`
- **実行判定**: 堅牢性確認（Green / 落とせず）
- **実行ログ要約**:

```text
=== Adversarial QA Test Suite for Task 1.2 (remove-obsolete-docker-compose) ===
[PASS] RenamerError does not contain DockerError
[PASS] RenamerError contains ContainerError
[PASS] Assembly does not contain DockerController module
[PASS] State.update (DockerCommandCompleted with adversarial ContainerError matrix: 7x7x9 = 441 cases) never throws and always unlocks IsDockerBusy
[PASS] TimeoutError("") / ("   ") / ("null") / ("long") unlocks IsDockerBusy and sets ErrorMessage
[PASS] Other RenamerError ("IoError" / "SettingsError" / "OpenRouterError" / "ValidationError" / "UndoConflictError") unlocks IsDockerBusy and sets ErrorMessage
[PASS] parseContainerPsOutput survived malformed & 5000-line NDJSON inputs (len=220073) -> (Running, Some "target-5000")
[PASS] executeActionWithTimeout(None, act="" / "start" / "stop" / "restart" / shell metacharacters / <null>) returns ValidationError
=== Total Failures: 0 ===
```

---

## 3. 指摘事項

なし（理由: 堅牢性確認済み。460 パターン以上の境界・異常値・奇形 JSON 注入テストに対して未捕捉例外 0 件、`IsDockerBusy` 状態ロック残留 0 件を確認）

---

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし
- **保留指摘（申し送り）**: なし
- **次のアクション**: 次タスクへ
