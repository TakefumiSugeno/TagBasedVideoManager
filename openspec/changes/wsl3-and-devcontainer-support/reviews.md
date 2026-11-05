# レビュー記録: wsl3-and-devcontainer-support / propose

- **日時**: 2026-11-06
- **フェーズ**: propose
- **対象成果物**:
  - `proposal.md`
  - `specs/**/spec.md`
  - `design.md`
  - `test_plan.md`
  - `tasks.md`

---

## 1. User Reviewer Agent レビュー結果（Propose フェーズ）

- **レビュアー**: user (User Reviewer Agent)
- **使用プロトコル**: `openspec/templates/checklist_propose.md`（ブラインド仕様比較）
- **判定**: **LGTM: true**（Cycle 2 にて全指摘解消）

### 挑戦的設問回答・検証サマリ

1. **データ永続化ボリュームマウント仕様の保護**:
   - `docker-infrastructure/spec.md` に SQLite データベースおよびサムネイルのボリューム永続化（`./data:/app/data`）の Requirement & Scenario を新設し、コンテナ破棄・再作成時もデータが喪失しないことを保証。
2. **ランタイム未導入状態およびエラー回復導線**:
   - `companion-docker-control/spec.md` に「✕ NO RUNTIME」表示シナリオ、コマンド実行失敗時エラー通知、連動再起動失敗時の手動再起動案内シナリオを追加。
3. **WSL 3.0 AMD GPU 記述の仕様整合**:
   - WSL 3.0 では AMD GPU は CPU ソフトウェアデコードへフォールバックし、`/dev/dri` パススルーは Linux Docker 限定であることを明記。
4. **ポート事前衝突チェック**:
   - `container-deployment-scripts/spec.md` にデプロイ前のポート 5620 使用中検知および安全な即時中断仕様を追加。
5. **未導入時ガイダンス**:
   - ランタイム未検出時に `wsl --update` を案内する要件を明記。

---

## 2. SE Reviewer Agent レビュー結果（Propose フェーズ）

- **レビュアー**: se (SE Reviewer Agent)
- **使用プロトコル**: `openspec/templates/checklist_propose.md`（故障シミュレーション・死角告発・手戻り検証）
- **判定**: **LGTM: true**（Cycle 2 にて全指摘解消）

### 挑戦的設問回答・検証サマリ

1. **プロセス実行タイムアウト制御および可観測性の確保**:
   - `design.md` に外部プロセス実行の 10 秒タイムアウト＆キャンセル（強制 Kill）機構を明記。非ゼロ終了時・パース失敗時の stderr / 生テキストログ出力を追加し、Silent Discard を廃止。
2. **パス変換ユーティリティのテストケース網羅**:
   - `test_plan.md` に Windows ↔ WSL パス変換の単体テスト（`UT-PATH-01`〜`UT-PATH-04`）を追加。
3. **タスク・計画・プロジェクトファイル整合**:
   - `tasks.md` に `IT-SCRIPT-02`（Bash スクリプト検証）およびパス変換テストタスクを追加。
   - `TagBasedVideoManager.Renamer.fsproj` / `TagBasedVideoManager.Renamer.Tests.fsproj` のファイル参照更新およびテスト移行タスクを明記。
4. **デプロイスクリプト障害診断情報**:
   - ヘルスチェック失敗時に `logs --tail 50` を出力する仕様を具体化。
5. **タスク完了基準明確化**:
   - タスク 1.3 および 2.3 の検証アサーションを具体化。

---

## 3. 総合判定

- **LGTM**: true
- **条件付きLGTM**: なし
- **指摘サマリ**:
  - `Critical`: 0 件
  - `Major`: 0 件（当初 5 件検出、全件修正済み）
  - `Minor`: 0 件（当初 4 件検出、全件修正済み）
  - `Suggestion`: 0 件（当初 1 件検出、全件修正済み）
- **保留指摘（申し送り）**: なし
- **承認者**: User Reviewer Agent, SE Reviewer Agent
