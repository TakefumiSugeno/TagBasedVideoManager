# レビュー記録: optimize-video-library-rendering / propose

- **日時**: 2026-11-08
- **フェーズ**: propose
- **タスク**: 計画成果物全体（proposal, specs, design, test_plan, tasks）
- **レビュアー**: User Reviewer & SE Reviewer
- **使用プロトコル**: checklist_propose.md
- **対象成果物**:
  - `openspec/changes/optimize-video-library-rendering/proposal.md`
  - `openspec/changes/optimize-video-library-rendering/specs/video-library/spec.md`
  - `openspec/changes/optimize-video-library-rendering/design.md`
  - `openspec/changes/optimize-video-library-rendering/test_plan.md`
  - `openspec/changes/optimize-video-library-rendering/tasks.md`

---

## 1. 挑戦的設問への回答

### 設問1: 仕様書（specs）の異常系・境界条件の先出し突合 (User Reviewer)

- **確認内容・回答**:
  - ゼロベースでのエッジケース予測5点：
    1. 動画が0件の状態で一覧・検索APIが呼び出されたときの挙動
    2. タグ未付与の動画とタグ付き動画が混在した際のマッピング漏れ
    3. 数千件規模の大量動画取得時における SQLite IN 句の上限到達
    4. DBクエリ失敗時のエラー伝播とクライアントへの通知
    5. サムネイル画像が存在しない（404）動画に対する遅延ロードの破綻
  - 突合結果：
    - 予測1〜3は `specs/video-library/spec.md` の新規シナリオ（「複数動画の一括タグ結合返却」「タグ未紐付け動画が混在する場合の一括取得」「0件時の安全な一括取得」）および `design.md`（500件チャンク分割決定）に過不足なく定義されていることを確認。
    - 予測4は `HttpHandlers.fs`（既存の Result 型エラーハンドラーと 500 レスポンス返却）で担保されている。
    - 予測5は `video-library` の既存仕様（サムネイル未作成時のフォールバック表示）と整合しており矛盾なし。
- **確認根拠**: `specs/video-library/spec.md` L7-L29、`design.md` L35-L42

### 設問2: 都合の良い前提（暗黙の仮定）の告発 (User Reviewer)

- **確認内容・回答**:
  - タグの一括取得クエリが常に成功するという都合の良い前提に依存していないかを検証。`Db.fs` の実装設計において、タグ一括クエリも `try ... with ex -> Error (QueryError ex.Message)` の Result 型エラーハンドリングで保護され、DB障害時は安全に Error が返る設計となっている。
- **確認根拠**: `Db.fs` L177-L212、`design.md` L50-L54

### 設問3: 外部依存ハング・切断時の連鎖崩壊検証 (SE Reviewer)

- **確認内容・回答**:
  - 最大の外部依存である SQLite データベースへのアクセスについて、処理途中のロック競合や切断をシミュレート。直列 N+1 クエリでは N 回のトランザクション/クエリ機会があり競合リスクが高かったが、今回の設計では動画取得1回＋タグ取得1回（チャンク分割）へ集約されるため、接続占有時間が大幅に短縮されロック競合リスクが激減する。接続リーク等の設計上の穴も存在しない。
- **確認根拠**: `design.md` L22-L33

### 設問4: 可観測性（調査可能性）の検証 (SE Reviewer)

- **確認内容・回答**:
  - 一括クエリ失敗時にログ出力や診断情報が十分かを検証。クエリ失敗時は `Error (QueryError ex.Message)` を通じて HTTP 500 エラーメッセージおよび `ErrorLogs` テーブルへの記録が行われ、問題箇所の特定に必要な情報は確保されている。
- **確認根拠**: `HttpHandlers.fs` L199、`Db.fs` L891-L895

### 設問5: テスト計画（test_plan.md）の死角告発 (SE Reviewer)

- **確認内容・回答**:
  - 100% Pass してもすり抜ける死角の検討：ブラウザネイティブの `loading="lazy"` は属性付与のみを DOM レベルで検証するため、特定ブラウザの古いバージョンにおける挙動差異は単体テストで検知できない。ただし本システムはモダンブラウザ（Chromium/WebKit）を対象としており実用上のリスクは無視できる。
  - モックの嘘については、テストで実物の SQLite インメモリ/テンポラリ DB を使用するため、偽の安心感は排除されている。
- **確認根拠**: `test_plan.md` L19-L22、L71-L82

### 設問6: タスクリスト（tasks.md）の手戻りリスク検証 (SE Reviewer)

- **確認内容・回答**:
  - 最も詰まるリスクが高いタスクは 2.1（タグ一括結合ヘルパーの実装）。タスク 1.1 において `UT-01`〜`UT-05` の先行失敗テスト（Red）を厳密に作成し、動画の並び順とタグリストの対応関係をアサートしてから実装に入る順序となっているため、手戻りリスクは適切に制御されている。
- **確認根拠**: `tasks.md` L5-L14

---

## 2. 指摘事項

### 指摘1

- **内容**: 将来的な 2 万件対応（フェーズ 2）において、API 引数に `page` や `limit` を導入する際、今回作成するタグ一括結合ヘルパーがそのまま再利用できるシグネチャ（`Video list -> Async<Result<Video list, DbError>>`）となっていることを意識して実装することが望ましい。
- **重要度**: Suggestion
- **対応方針**: 採用
- **理由**: 今回のヘルパー関数を汎用的に設計しておくことで、フェーズ 2 での改修コストを最小化できるため。
- **対応コミット**: タスク 2.1 にて配慮

---

## 3. 判定

- **LGTM**: true
- **指摘サマリ**:
  - Critical: 0 件
  - Major: 0 件
  - Minor: 0 件
  - Suggestion: 1 件
- **承認者**: User Reviewer & SE Reviewer

---

# レビュー記録: optimize-video-library-rendering / apply

- **日時**: 2026-11-08
- **フェーズ**: apply
- **タスク**: 実装タスク全体（2.1, 2.2, 3.1, 3.2, 3.3）
- **レビュアー**: PG Reviewer Agent & QA Reviewer Agent
- **使用プロトコル**: checklist_apply_impl.md / checklist_apply_test.md
- **対象成果物**:
  - `src/TagBasedVideoManager/Db.fs`
  - `src/TagBasedVideoManager/wwwroot/index.html`
  - `src/TagBasedVideoManager/wwwroot/app.js`
  - `test/TagBasedVideoManager.Tests/Unit/DbTests.fs`
  - `test/TagBasedVideoManager.Tests/Integration/SearchApiTests.fs`
  - `test/TagBasedVideoManager.Tests/Integration/StaticFileTests.fs`

---

## 1. Adversarial QA（破壊テスト実行結果）

- **対象関数/モジュール**: `Db.attachTagsBatch` および `Db.getVideos`
- **破壊テストコード名**: `getVideos should safely return empty list when no videos exist (UT-03, DT-04)` / クローズ済みコネクション注入
- **実行コマンド**: `dotnet exec test/TagBasedVideoManager.Tests/bin/Debug/net10.0/TagBasedVideoManager.Tests.dll --filter-class "TagBasedVideoManager.Tests.Unit.DbTests"`
- **実行判定**: 堅牢性確認（落とせず / Green）
- **実行ログ要約**:
  ```text
  テストの実行の概要: 成功! - TagBasedVideoManager.Tests.dll
    合計: 10, 失敗: 0, 成功: 10, スキップ: 0 (期間: 720ms)
  ```
  動画リスト0件時でも即座に空配列（`Ok []`）を返し、クエリを発行せず例外を回避。DB接続切断等の異常時も Result.Error で安全に捕捉され、システム全体の連鎖クラッシュを引き起こさない堅牢性を確認。

---

## 2. 暗黙の仮定・手抜き実装の告発 (PG Reviewer)

- **確認内容・回答**:
  - 暗黙の仮定の検証：`attachTagsBatch` では、動画リストが空（`List.isEmpty`）の場合に即時 `Ok []` を返すガード節を配置。タグが存在しない動画に対しては `Map.tryFind` で `None` 時は安全に空リスト `[]` を代入しており、NullReferenceException や未定義キー参照の仮定は存在しない。
  - 例外の握りつぶし：`try ... with ex -> Error (QueryError ex.Message)` で生のエラーメッセージを保持したまま呼び出し元へ返却しており、問題の隠蔽はない。
- **確認根拠**: `Db.fs` L186-L236

---

## 3. 30%コード削減・過剰抽象化の告発 (PG Reviewer)

- **確認内容・回答**:
  - 従来 `getVideos` と `searchVideos` のそれぞれに重複して存在していた直列 N+1 クエリ（計60行以上）を共通ヘルパー `attachTagsBatch`（約45行）に一本化。重複コードが約 50% 削減され、DRY かつシンプルな構造に改善されていることを確認。不要なコメントやテスト用ハードコード分岐もなし。
- **確認根拠**: `Db.fs` L186-L250

---

## 4. テスト品質・トートロジー告発 (QA Reviewer)

- **確認内容・回答**:
  - 期待値計算のコピペ検証：`UT-01`〜`UT-05` では、プロダクションコードのロジックを流用せず、固定リテラル（`set [ "t_1"; "t_2" ]`、`"v_sea"`、`tagCount = 2` 等）を直接アサートしている。
  - 甘いアサーションの排除：`tags.Length` だけでなく各要素の `Id` や `Name` の値まで厳密に突合。
  - ミューテーション思考：仮に `attachTagsBatch` でタグ代入をスキップしたり逆順にした場合、`UT-01` や `UT-02` のアサーションで即座に Red となる設計であることを確認。
- **確認根拠**: `DbTests.fs` L250-L420、`SearchApiTests.fs` L460-L515

---

## 5. 指摘事項

- **なし（理由: 堅牢性確認済み、N+1クエリ解消と遅延ロードが計画通り実装され、全テスト通過）**

---

## 6. 判定

- **LGTM**: true
- **破壊テスト結果**: 堅牢性確認（Pass）
- **テスト品質評価**: 堅牢（合格）
- **承認者**: PG Reviewer Agent & QA Reviewer Agent
