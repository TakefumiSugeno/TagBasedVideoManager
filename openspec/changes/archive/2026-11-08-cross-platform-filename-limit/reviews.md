# レビュー記録: cross-platform-filename-limit / Propose フェーズ

- **対象成果物**: `proposal.md`, `specs/**/spec.md`, `design.md`, `test_plan.md`, `tasks.md`, `mockup.html`
- **適用規模レベル**: `L2`
- **使用プロトコル**: `openspec/templates/checklist_propose.md`
- **レビュー実施サイクル**: 最大3サイクル（本記録: Cycle 1 レビュー ＆ Cycle 2 再レビュー・承認）

---

# Cycle 1 レビュー記録

- **日時**: 2026-11-08
- **レビュアー**: User Reviewer Agent & SE Reviewer Agent
- **判定**: `LGTM: false` (Major 6件, Minor 4件)

### 1. 指摘事項（Cycle 1）

1. **【User Reviewer / Major 1】同名衝突（Collision）時のサフィックス付加とバイト数再超過**:
   - リネーム先でファイル名が重複した際に `_1.mp4` 等の連番サフィックスが付加されると、240B / 240字の上限を再超過する懸念がある。
2. **【User Reviewer / Major 2】Web UI (`<video>`) におけるストリーミングエラー理由判定手順の具体化不足**:
   - ブラウザの `<video>` の `error` イベント単体では HTTP 422 や JSON を取得できないため、能動的な `fetch` 調査フローが必要。
3. **【User Reviewer / Major 3】Renamer 候補リストにおける Zero-State（データ0件時・フィルタ結果0件時）表示の欠落**:
   - 超過ファイルが 0 件の時や走査前にリスト領域が真っ白になり、初心者ユーザーが困惑する。
4. **【SE Reviewer / Major 4】実稼働 Linux 環境での `PhysicalFileResultExecutor` 例外型不一致による 500 クラッシュ**:
   - Linux 上で `stat()` が `ENAMETOOLONG` を返すと、.NET の `FileInfo.Exists` は false となり、Executor は `PathTooLongException` ではなく `FileNotFoundException` をスローするため、Fail-Fast 事前検査と両方の例外捕捉が必須。
5. **【SE Reviewer / Major 5】エラーログにおける診断情報（VideoId / FilePath / バイト数）の欠落**:
   - ログにファイル名やバイト数が含まれず、深夜の障害時に即座に特定できない。
6. **【SE Reviewer / Major 6】スキャン再実行時の `MediaQueue` 無限再投入と FFmpeg 外部プロセス過負荷**:
   - 255B 超の動画は Linux 上で FFmpeg が開けないため、再スキャンのたびに失敗確定のプロセスが起動されてキューが詰まる。
7. **【SE Reviewer / Minor 1】Task 3.1（先行テスト）と Task 4.1（Domain型定義）の依存関係逆転**:
   - 型定義スタブを先行させないと F# コンパイルエラー（FS0039）が発生する。
8. **【SE Reviewer / Minor 2】クライアント切断（`OperationCanceledException`）の漏洩**:
   - シーク時の切断例外が 500 エラーとして記録されるのを防止する。
9. **【User Reviewer / Minor 3】Web プレイヤーエラーオーバーレイの「無意味な再試行ボタン」**:
   - リネーム前には絶対に成功しない再試行ボタンを廃止し、「ファイルパスをコピー」ボタンを設置すべき。
10. **【SE Reviewer / Minor 4】閾値（240B vs 255B）の判定階層の明文化**:
    - 240B以下（安全）、241〜255B（注意）、256B以上（危険）の判定基準を統一する。

---

# Cycle 2 再レビュー記録（修正反映後の再検証）

- **日時**: 2026-11-08
- **レビュアー**: User Reviewer Agent & SE Reviewer Agent
- **対象修正成果物**:
  - `specs/companion-file-rename/spec.md` (閾値階層・連番再検証・Empty State仕様追加)
  - `specs/video-library/spec.md` (Fail-Fast検査・例外安全捕捉・切断正常終了・fetchエラー調査仕様追加)
  - `specs/video-scanner/spec.md` (MediaQueue投入スキップ・診断情報ログ仕様追加)
  - `design.md` (Decision 1〜6 における全設計反映・リスク緩和策更新)
  - `test_plan.md` (Fail-Fast・キュースキップ・連番トリミング・Empty Stateテストケース追加)
  - `tasks.md` (Domain型先行・クライアント切断・キュースキップタスク再編)
  - `mockup.html` (Renamer Empty State表示・ファイルパスクリップボードコピーボタン追加)

---

## 1. 挑戦的設問への回答（Cycle 2 確認）

### 設問1: 仕様書（`specs/**/spec.md`）のブラインド比較・異常系網羅検証

- **確認内容・回答**:
  - `specs/companion-file-rename/spec.md` (L59-71, L82-84) において、同名衝突時の連番サフィックス付与による再超過防止仕様、および候補0件時の安心 Empty State 案内仕様が漏れなく規定されたことを確認。
  - `specs/video-library/spec.md` (L7-28) において、ストリーミング実行前の Fail-Fast バイト数検査（> 255B で即時 422 返却）、`PathTooLongException` / `FileNotFoundException` の安全捕捉、クライアント切断（`OperationCanceledException`）の静かな終了、およびフロントエンドによる `fetch` エラー詳細調査フローが明確に規定されたことを確認。
- **確認根拠**: `specs/companion-file-rename/spec.md`, `specs/video-library/spec.md`

### 設問2: 設計書（`design.md`）の故障シミュレーションと可観測性検証

- **確認内容・回答**:
  - Linux 環境での `PhysicalFileResultExecutor` 実行前に `Encoding.UTF8.GetByteCount(fileName) > 255` を検査する Fail-Fast 設計により、カーネル例外や `FileNotFoundException` の不整合リスクが構造的に遮断されたことを確認（Decision 4）。
  - `Db.saveErrorLog` の出力フォーマットに `VideoId={id}, FilePath={path}, Bytes={bytes}` が明記され、運用者が 1 分で根本原因を特定可能となったことを確認。
  - `Scanner.fs` において 255B 超のファイルを `MediaQueue` から除外（スキップ）する設計（Decision 6）により、定期スキャン時の FFmpeg プロセス無限滞留とキュー閉塞が完全に防止されたことを確認。
- **確認根拠**: `design.md` Decision 4, Decision 6, Section 4 Risks & Mitigations

### 設問3: テスト計画書（`test_plan.md`）の死角検証

- **確認内容・回答**:
  - `UT-WEB-01`（Fail-Fast 検査）、`UT-WEB-04`（切断正常終了）、`UT-WEB-05`（`FileNotFoundException` 捕捉）、`UT-SCN-02`（`MediaQueue` 投入スキップ）、`UT-REN-16`（連番サフィックス付加時トリミング）、`UT-REN-17`（Empty State）が網羅され、テストの死角が解消されたことを確認。
  - C2 デシジョンテーブルが 100% 網羅され、トレーサビリティマトリクスに全シナリオが対応付けられていることを確認。
- **確認根拠**: `test_plan.md` Section 2, 3, 4, 5

### 設問4: UIモック（`mockup.html`）の初心者ペルソナ検証

- **確認内容・回答**:
  - Renamer において、走査前およびフィルタ適用で該当ファイルが 0 件の際に「対象ファイルはありません。すべてのファイルが安全です 🎉」と表示される Empty State が実装され、初心者が途方に暮れる問題が解消されたことを確認。
  - Web プレイヤーのエラーオーバーレイから無意味な再試行ボタンが撤去され、ファイルパスをクリップボードにコピーして Renamer やエクスプローラーで即座に特定できる「ファイルパスをコピー」ボタン（トースト連動）が提供されたことを確認。
- **確認根拠**: `mockup.html` L250-275, L560-580

### 設問5: タスクリスト（`tasks.md`）の手戻り・依存関係検証

- **確認内容・回答**:
  - Task 1.1 に `Domain.fs` の型スタブ先行整備が配置され、Task 1.2 以降のテストコード作成時における F# コンパイルエラー依存逆転が解消されたことを確認。
  - 全タスクに明確な検証コマンド（`dotnet test`、Playwright、`verify-gate.ps1`）が付与されていることを確認。
- **確認根拠**: `tasks.md` Task 1.1〜6.3

---

## 2. Adversarial QA（破壊テスト実行結果）

- **対象外**: Propose フェーズの計画成果物レビューのため（Apply 実装フェーズにて実施予定）

---

## 3. 指摘事項

- **なし（理由: Cycle 1 で検出された Major 6 件、Minor 4 件の指摘が全件適切に修正・反映され、堅牢性および初心者体験が大幅に向上したことを確認したため）**

---

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし
- **保留指摘（申し送り）**: なし
- **次のアクション**: Gate 1 検証（`verify-gate.ps1 -Gate propose`）を実行し、ユーザー合意を得て Apply フェーズへ移行する。

---

# Apply フェーズ レビュー記録

- **日時**: 2026-11-08
- **対象成果物**:
  - `src/TagBasedVideoManager/HttpHandlers.fs`
  - `src/TagBasedVideoManager/Scanner.fs`
  - `src/TagBasedVideoManager/wwwroot/app.js`
  - `src/TagBasedVideoManager/wwwroot/index.html`
  - `src/TagBasedVideoManager.Renamer/Domain.fs`
  - `src/TagBasedVideoManager.Renamer/FileScanner.fs`
  - `src/TagBasedVideoManager.Renamer/FileRenamer.fs`
  - `src/TagBasedVideoManager.Renamer/OpenRouterClient.fs`
  - `src/TagBasedVideoManager.Renamer/State.fs`
  - `src/TagBasedVideoManager.Renamer/Views.fs`
- **使用プロトコル**: `openspec/templates/checklist_apply_impl.md`, `openspec/templates/checklist_apply_test.md`
- **レビュアー**: QA Reviewer Agent & PG Reviewer Agent
- **レビュー実施サイクル**: Cycle 1 指摘・改善要求 → Cycle 2 修正反映・再検証・承認

---

## 1. Cycle 1 指摘事項（QA & PG Reviewer）

1. **【QA Reviewer / Critical】`trimBaseNameToFit` によるサロゲートペア（絵文字）の泣き別れ切断・文字化け**:
   - `for ch in baseName do` で 16ビット `char` 単位で走査しているため、4バイト絵文字が240バイト境界を跨いだ際に上位サロゲートのみが追加され下位サロゲートが切り捨てられ、孤立サロゲートによる文字化け・破損が発生する（Adversarial QA 実機テストで再現）。
2. **【QA & PG Reviewer / Major】`HttpHandlers.fs` の到達不能デッドコードと `UT-WEB-05` の空洞化**:
   - `streamVideo` 先頭の Fail-Fast チェックにより、後続の `when fnBytes > 255` ガード付き `FileNotFoundException` ハンドラは到達不能。テスト `UT-WEB-05` も Fail-Fast で遮断され例外ハンドラを通っていなかった。
3. **【PG Reviewer / Major】422 JSON レスポンス契約不一致による「0 バイト」表示バグ**:
   - バックエンドが `bytes` および `filePath` を JSON に含めておらず、フロントエンドで「0 バイト」と誤表示される。
4. **【QA Reviewer / Major】`StateTests.fs` の `UT-STATE-FILTER-01` 空洞化**:
   - `model.FilterConstraint` の値変化しかテストしておらず、`State.getFilteredCandidates` による実際の候補リスト絞り込み動作が未テスト。
5. **【PG Reviewer / Minor】`FileScanner.fs` の長パス例外時における `isVideoFile` 検査漏れ**:
   - `PathTooLongException` 捕捉時に `isVideoFile` の判定が抜けており非動画ファイルが混入するリスク。
6. **【QA Reviewer / Minor】`FileScannerTests.fs` のトートロジーアサーション**:
   - `Encoding.UTF8.GetByteCount` 式のコピペによる検証。固定リテラル `244` にすべき。

---

## 2. Cycle 2 修正反映・再検証結果

### 設問1: サロゲートペア切断防止（Adversarial QA 再検証）

- **確認内容・回答**:
  - `FileRenamer.fs` の `trimBaseNameToFit` を `.NET 10` 標準の `baseName.EnumerateRunes()` による Unicode スカラー値（Rune）走査へ刷新。`rune.Utf8SequenceLength` および `rune.Utf16SequenceLength` を用いて、サロゲートペアを不可分単位として安全に判定・追加するよう改修されたことを確認。
  - `FileRenamerTests.fs` に新規追加された `UT-REN-16-EMOJI` において、4バイト絵文字 `🐧`（`\uD83D\uDC27`）が240バイト境界に位置する場合でも、孤立サロゲートが発生せず、UTF-8 ラウンドトリップが完全に一致することを実機検証（PASS）。
- **確認根拠**: `src/TagBasedVideoManager.Renamer/FileRenamer.fs` Line 11-21, `test/TagBasedVideoManager.Renamer.Tests/FileRenamerTests.fs` `UT-REN-16-EMOJI`

### 設問2: HTTP 422 契約とフェイルファスト検証（デッドコード解消）

- **確認内容・回答**:
  - `HttpHandlers.fs` の 422 返却 JSON に `filePath = filePath; bytes = fnBytes; message = ...` を追加。フロントエンド `app.js` でも `data.bytes` および `TextEncoder` によるフォールバック計算を配備し、「0 バイト」表示バグを根絶。
  - 到達不能だった `FileNotFoundException when fnBytes > 255` 節を削除し、`UT-WEB-05` を「Fail-Fast により OS コール（PhysicalFileResultExecutor）を呼び出さずに安全遮断すること」を明示的にアサートするテストへ改修して PASS を確認。
- **確認根拠**: `src/TagBasedVideoManager/HttpHandlers.fs`, `src/TagBasedVideoManager/wwwroot/app.js`, `test/TagBasedVideoManager.Tests/Unit/HttpHandlersTests.fs`

### 設問3: 状態遷移と表示フィルタ実動検証

- **確認内容・回答**:
  - `StateTests.fs` の `UT-STATE-FILTER-01` において、`FilterAll`, `FilterLinuxOnly`, `FilterWindowsOnly`, `FilterBothOnly` の各フィルタ適用時に、`State.getFilteredCandidates` が各制約違反種別を正確に絞り込むことをアサートするテストを追加し、PASS を確認。
  - `FileScanner.fs` の長パス例外処理に `if isVideoFile file then` ガードを追加し、非動画ファイルの混入を防止。
  - `FileScannerTests.fs` の `FileNameBytes` アサーションを固定リテラル値 `244`, `255`, `256` に修正。
- **確認根拠**: `test/TagBasedVideoManager.Renamer.Tests/StateTests.fs`, `src/TagBasedVideoManager.Renamer/FileScanner.fs`, `test/TagBasedVideoManager.Renamer.Tests/FileScannerTests.fs`

---

## 3. Adversarial QA（破壊テスト実行結果）

- **対象関数/モジュール**: `TagBasedVideoManager.Renamer.FileRenamer.trimBaseNameToFit` / `executeRename`
- **破壊テストコード名**: `UT-REN-16-EMOJI: 4バイト絵文字（サロゲートペア）が240バイト境界にある場合でもサロゲート泣き別れ切断を起こさず安全にトリミングする`
- **実行コマンド**: `dotnet test test/TagBasedVideoManager.Renamer.Tests/TagBasedVideoManager.Renamer.Tests.fsproj`
- **実行判定**: **堅牢性確認（Pass / 落とせなくなった）**
- **実行ログ要約**:
  341 件のテスト全件合格。サロゲートペア絵文字が境界を跨ぐ場合でも、孤立サロゲートのない完全な UTF-16 文字列として生成され、UTF-8 変換およびファイル作成が正常に完了することを確認。

---

## 4. 総合判定

- **LGTM**: **true**
- **条件付きLGTM**: なし（全指摘事項が適切に解消されたことを確認）
- **保留指摘（申し送り）**: なし
- **承認者**: QA Reviewer Agent & PG Reviewer Agent
- **次のアクション**: Gate 2 検証（`verify-gate.ps1 -Gate apply`）を実行する。

---

# Archive フェーズ 総括レビュー記録

- **日時**: 2026-11-08
- **使用プロトコル**: `openspec/templates/checklist_archive.md`
- **レビュアー**: User Reviewer Agent / SE Reviewer Agent / PG Reviewer Agent / QA Reviewer Agent（全4ロール）
- **対象成果物**: 実装コミット `845eddb`, `handover.md`, `reviews.md`, `design.md`, `specs/**/spec.md`, `tasks.md`

---

## 1. 隠された技術的負債・妥協点の自白（SE & PG Reviewer 回答）

1. **今回あえて残した技術的負債と引き継ぎ状況**:
   - `Views.fs` における `candidateCardVertical` と `candidateCardHorizontal` のメトリクス・バッジ計算ロジック重複、および `RenameProposal` の手動初期化ボイラープレートについては、回帰防止を優先して現状維持とし、`handover.md` Section 4 に将来のリファクタリング候補として明記済み。
   - `FileScanner.fs` と `FileRenamer.fs` における Windows 超長パス（`\\?\`）処理の分散についても `handover.md` Section 4 に記録済み。
2. **将来の機能追加で踏み抜く可能性が高い罠**:
   - Renamer の目標閾値（240B：連番サフィックス用マージン込み）と Web サーバーの Fail-Fast 境界（255B：Linux `NAME_MAX` 上限）の 15 バイト安全マージンを安易に 255B に統一すると、衝突サフィックス付与時に 255B を再超過する罠があるため、`handover.md` Section 5 に設計意図を明記して保護。
   - `<video>` タグの `@error` と `fetch` 調査の二段構え設計の意図を `design.md` Decision 5 に記録済み。

---

## 2. 初見エンジニア視点でのドキュメント検証（User Reviewer 回答）

1. **設計の背景（Why）の追跡可能性**:
   - Windows (UTF-16 260字) と Linux (UTF-8 255B) の非対称性、ボツ案（仮想マッピングやサイレント強制リネームの不採用理由）、255B 超ファイルを DB 登録維持しつつ `MediaQueue` だけスキップする理由が `design.md` および `handover.md` に図解・根拠付きで追跡可能であることを確認。
2. **トラブルシューティング手順の十分性**:
   - `handover.md` Section 5 に「Web プレイヤーでパスコピー → Windows 側 Renamer で 240B 以内短縮 → Web 側で再 Sync」の 3 ステップ運用手順、および `ErrorLogs` の `VideoId / FilePath / Bytes` による特定手順が具体的に文書化されていることを確認。

---

## 3. ロールバック・安全性の最終検証（SE & QA Reviewer 回答）

1. **ロールバック（Revert）手順の検証**:
   - SQLite DB のスキーマ変更や破壊的設定変更は一切含まれておらず、`git revert 845eddb` のみで直前の安定状態へ 100% 安全に巻き戻し可能であることを確認。
2. **保留指摘の全件引き継ぎ確認 & テストエビデンス確認**:
   - Propose / Apply フェーズの Cycle 1 で検出された全指摘は Cycle 2 で完全解消済み。
   - 全 485 件のテスト（Renamer 341件、Web 144件）が 100% PASS（Skip 0件）し、カバレッジ基準（C0/C1/C2）および仕様 Scenario 100% を満たしていることを確認。

---

## 4. Archive 総括判定

- **LGTM**: true
- **条件付きLGTM**: なし
- **引き継ぎ完了確認**: `handover.md` に全ての負債・改善候補・運用手順が記録済み
- **リリース可否**: 即時リリース可
- **承認者**: User / SE / PG / QA Reviewer Agent
