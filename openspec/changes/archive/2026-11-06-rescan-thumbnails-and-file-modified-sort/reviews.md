# レビュー記録: rescan-thumbnails-and-file-modified-sort / propose

- **日時**: 2026-11-06
- **フェーズ**: propose
- **レビュアー**: user-reviewer & se-reviewer
- **使用プロトコル**: openspec/templates/checklist_propose.md
- **対象成果物**: proposal.md, specs/**/*.md, design.md, test_plan.md, tasks.md, mockup.html

---

## 1. 挑戦的設問への回答（悪魔の代弁者・ブラインド突合・故障シミュレーション）

### 設問1: 異常系・境界条件の先出し突合 (User Reviewer)

- **確認内容・回答**:
  1. ファイルシステムの `LastWriteTimeUtc` が破損・取得不能な場合: `spec.md` および `design.md` にて `CreatedAt` への安全なフォールバックを定義済み。
  2. サムネイルディレクトリのパスが存在しない、または親階層が存在しない場合: `Directory.CreateDirectory` を起動時・スキャン時・生成前の3重で実行する安全設計を確認。
  3. サムネイル未作成動画が破損している場合: セマフォ（最大並列数 1）と ffprobe 既定値フォールバック（120秒）により、無限ハングやシステムクラッシュを引き起こさないことを確認。
  4. 既存レコードが NULL または未設定の場合のソート破綻: クライアント側（`app.js`）およびサーバー側（`Db.fs`）でフォールバック比較を行う仕様を明記。
  5. スキャン時の大量データ更新による負荷: 更新日時に差分がない場合は DB UPDATE をスキップする最適化を確認。
- **確認根拠**: `openspec/changes/rescan-thumbnails-and-file-modified-sort/specs/video-scanner/spec.md`, `design.md` セクション「Decisions 1, 2, 4, 5」

### 設問2: 外部依存ハング・切断時の連鎖崩壊検証 (SE Reviewer)

- **確認内容・回答**:
  FFmpeg 外部プロセスが 5 秒以上ハングした場合でも、既存の `POST /api/media/stop` エンドポイントによる安全中断、およびワーカーセマフォによる逐次処理が担保されており、Web API の応答（HTTP サーバー）が連鎖的に崩壊することはない。また SQLite へのカラム追加（`ALTER TABLE`）は起動時の初期化トランザクション内で即座に完了し、デッドロックの危険性はない。
- **確認根拠**: `src/TagBasedVideoManager/Scanner.fs` (L23-L60), `openspec/changes/rescan-thumbnails-and-file-modified-sort/design.md` セクション「Risks / Trade-offs」

### 設問3: テスト計画書の死角とモック境界の検証 (SE / QA Reviewer)

- **確認内容・回答**:
  外部プロセス（FFmpeg/ffprobe）のみを `IMediaProcessor` としてモック化し、SQLite および実ファイルシステム（ダミーファイル）は本物の環境を使用してテストする方針であることを確認。C0 90%+, C1 85%+, C2 100%（再処理判定デシジョンテーブル 6パターン網羅）が明確に定義されており、モック過剰による偽の安心感は排除されている。
- **確認根拠**: `openspec/changes/rescan-thumbnails-and-file-modified-sort/test_plan.md` セクション 1, 3, 4

### 設問4: UIモックの初心者ペルソナ検証 (User Reviewer)

- **確認内容・回答**:
  `mockup.html` を検証した結果、サムネイル未生成動画に対する視覚的なバッジ表示、および「ディレクトリ同期」ボタンによる即時回復アクションが明示されており、ユーザーが戸惑うことなく状態を把握できるUIとなっている。また、デスクトップおよびモバイル両方のソート設定シートで「ファイル更新日時」への切り替えが滑らかに動作することを確認。
- **確認根拠**: `openspec/changes/rescan-thumbnails-and-file-modified-sort/mockup.html`

### 設問5: タスクリストの手戻りリスク検証 (SE Reviewer)

- **確認内容・回答**:
  TDD 3権分立に則り、不具合再現（Redテスト）→ DBマイグレーション → スキャナーロジック修正・バックフィル → UIソート実装 → 結合・E2E検証 の順に各タスクの検証方法が具体的に記載されており、手戻りリスクは極めて低い。
- **確認根拠**: `openspec/changes/rescan-thumbnails-and-file-modified-sort/tasks.md`

---

## 2. Adversarial QA（破壊テスト実行結果）

- **フェーズ対象外**: Propose フェーズのため対象外（Apply 実装時に実行予定）

---

## 3. 指摘事項

### 指摘1 (Suggestion)

- **内容**: 将来的な拡張として、動画ファイルにメタデータタグ（Exif / QuickTime creation_date 等）が含まれている場合、OS更新日時より優先して撮影日時を採用するディープメタデータ解析も検討の余地あり。
- **重要度**: Suggestion
- **対応方針**: 保留（今回はスコープ外とし、将来のバックグラウンド拡張候補とする）
- **理由**: ffprobe 解析のオーバーヘッドを避け、スキャン速度を優先するため。

---

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし
- **保留指摘（申し送り）**: 指摘1（将来のExif/メタデータ拡張検討）
- **次のアクション**: Gate 1 を検証し、ユーザー合意後に Apply フェーズへ移行

---

# レビュー記録: rescan-thumbnails-and-file-modified-sort / apply

- **日時**: 2026-11-06
- **フェーズ**: apply
- **レビュアー**: qa-reviewer & pg-reviewer
- **使用プロトコル**: openspec/templates/checklist_apply_impl.md, checklist_apply_test.md
- **対象成果物**: Db.fs, Scanner.fs, Media.fs, HttpHandlers.fs, app.js, index.html, ScannerTests.fs, MediaTests.fs, DbInitTests.fs, E2ETests.fs

---

## 1. 第1サイクル批判的レビューと検出事項（LGTM: false）

### Adversarial QA（破壊テスト実行結果）- QA Reviewer

1. **破壊テスト1 (Red / バグ検出)**:
   - テスト名: `Adversarial_QA1_同一ファイルを再スキャンした際にタイムゾーン不整合により無限バックフィルが発生しないこと`
   - 検出内容: ファイルシステムから取得した `fi.LastWriteTimeUtc`（`Utc`）と、DB から `DateTime.Parse` で復元した `v.FileModifiedAt`（`Local`）の減算差分が常に 32,400 秒（9時間）となり、毎回のスキャン実行時に全件無駄な `UPDATE Videos SET FileModifiedAt` が走り続ける「無限バックフィル」を検出。
2. **破壊テスト2 (Red / 例外漏出)**:
   - テスト名: `Adversarial_QA2_GenerateThumbnailにoutputPathがnullで渡された場合に未処理例外を投げずResult型でハンドリングすること`
   - 検出内容: `outputPath = null` の場合に `Path.GetDirectoryName` で未処理の `System.ArgumentNullException` がスローされる例外安全性の不備。
3. **甘いアサーションの告発 (Major)**:
   - `ScannerTests.fs` の再処理スキップ検証において、初期パスとモック生成パスが同一（`{videoId}.jpg`）であったため、再処理が走ってもスキップされてもテストが Pass してしまう死角を告発。

### 暗黙の仮定・コード重複・YAGNI告発 - PG Reviewer

1. **`DateTime.Parse` の例外脆弱性**: カルチャ未指定かつ TryParse を使わないパースにより、不正データ1件で `GET /api/videos` が単一障害点として即死するリスクを告発。
2. **O(N×M) リスト走査の手抜き**: `existingVideoMap`（Map）があるにもかかわらず、毎ファイルのループ内で `existingVideosResult` を `List.tryFind` していた手抜き実装を告発（6,000件環境で3,600万回走査）。
3. **5重コピペ重複**: `Db.fs` の 5 関数（`getVideos`, `getVideo`, `searchVideos`, `getVideoRanking`, `getStats`）で全く同一の `VideoDbDto -> Video` マッピングが重複していた点を告発（約60行の削減余地）。

---

## 2. 実装修正とリファクタリング内容（Green & Refactor）

1. **`Db.fs` の大幅リファクタリング**:
   - `module Db =` 内に `parseIsoDate`（`CultureInfo.InvariantCulture`, `DateTimeStyles.RoundtripKind` による UTC 正規化・フォールバック安全化）および `dtoToVideo` 共通関数を導入。
   - 5 箇所のマッピング重複を `dtos |> Seq.map dtoToVideo |> Seq.toList` の 1 行に集約し、約 80 行を削減。
   - `saveVideo` および `updateVideoFileModifiedAt` で `ToUniversalTime().ToString("o")` を徹底。
2. **`Scanner.fs` の O(N log M) 最適化とタイムゾーン統一**:
   - `existingVideoMap` を `Map<string, Video>` に変更し、ループ内の `existingOpt = Map.tryFind filePath existingVideoMap` に一本化。
   - 既存動画の更新日時バックフィル比較を `(v.FileModifiedAt.ToUniversalTime() - fileModifiedAt.ToUniversalTime()).TotalSeconds` で UTC 基準に統一し、無限バックフィルを根絶。
3. **`Media.fs` の引数ガード節追加**:
   - `outputPath` が null または空文字列の場合に `Error (InvalidFormat "Output path is null or empty")` を早期返却。
4. **テストスイートの厳密化と Adversarial QA テストの正式追加**:
   - `ScannerTests.fs` の再処理スキップテストで初期パスを `{videoId}_existing_custom.jpg` に変更し、再処理上書きが発生していないことを厳密にアサート。
   - 破壊テストとして指摘されたタイムゾーン整合テストおよび Media 引数ガードテストを正式な単体テストとして追加。

---

## 3. 第2サイクル検証結果

- **テスト実行結果**:
  ```text
  テストの実行の概要: 成功! - TagBasedVideoManager.Tests.dll (net10.0|x64)
    合計: 83
    失敗: 0
    成功: 83
    スキップされました: 0
    期間: 1m 27s 307ms
  ```
- **堅牢性確認**:
  - Adversarial QA テスト（タイムゾーン乖離・nullパスガード）を含め、全 83 件が 100% 成功。
  - 意地悪な入力に対しても例外が漏れず、堅牢性を確認。

---

## 4. 判定

- **LGTM**: true
- **条件付きLGTM**: なし
- **保留指摘（申し送り）**: なし（第1サイクルの全指摘を解消済み）
- **次のアクション**: Gate 2 (DoD) を機械的検証スクリプトで PASS させ、Archive フェーズへ進む

---

# レビュー記録: rescan-thumbnails-and-file-modified-sort / archive

- **日時**: 2026-11-06
- **フェーズ**: archive
- **レビュアー**: User / SE / PG / QA Reviewer Agent
- **使用プロトコル**: openspec/templates/checklist_archive.md
- **対象成果物**: 全コード差分、handover.md、specs/**/spec.md、design.md、test_plan.md、tasks.md

---

## 1. 隠された技術的負債・妥協点の自白（悪魔の代弁者 - SE & PG Reviewer）

1. **今回あえて残した技術的負債**:
   - 動画ファイル自体が削除された場合の物理サムネイルファイルの自動削除（ガベージコレクション）は今回のスコープ外とし、`handover.md` の「将来の改善候補」に明記した。
   - バックフィル処理は差分のある動画のみ個別 UPDATE としているが、初回起動で万単位の更新がある場合はバルクトランザクション化の余地がある。
2. **将来の機能追加で踏み抜く可能性が高い罠**:
   - `Db.fs` の `dtoToVideo` および `parseIsoDate` で UTC 正規化（`DateTimeKind.Utc`）を徹底しているため、今後動画の日時プロパティを追加する際は必ずカルチャ不変かつ Kind を指定して復元すること（Local 時の混入によるタイムゾーン乖離の再発防止）。

---

## 2. 初見エンジニア視点でのドキュメント検証（Whyの欠落告発 - User Reviewer）

1. **設計の背景（Why）の追跡可能性**:
   - `proposal.md` および `design.md` に「なぜ Duration > 0 かつ ThumbnailPath = None を除外していたのか」「なぜ今回の改修で無条件投入に切り替えたのか（環境トラブルで1,068件がゾンビ化していた実態）」が詳細に記録されており、新人エンジニアでも経緯が完全に理解可能。
   - UI 名称について、「ファイル更新日」と「TBVM登録日」に整理されたことで、ホストOSファイルシステムのタイムスタンプと本システム取り込み日時の区別が明確になっている。
2. **トラブルシューティング手順の十分性**:
   - 万が一サムネイル生成に失敗した場合でも、`Directory.CreateDirectory` による自動復旧、および `POST /api/media/stop` によるプロセス安全中断が可能であることがドキュメント化されている。

---

## 3. ロールバック・安全性の最終検証（SE & QA Reviewer）

1. **ロールバック（Revert）手順の検証**:
   - SQLite へのカラム追加（`ADD COLUMN FileModifiedAt TEXT`）は後方互換性があり、旧バージョンのアプリケーションでもそのまま動作する。`git revert` だけで安全に旧状態へ巻き戻し可能。
2. **保留指摘の全件引き継ぎ確認**:
   - Propose フェーズで保留とした「Exif/メタデータ解析によるディープな撮影日時取得」が `handover.md` に漏れなく転記されていることを確認。

---

## 4. 総合判定

- **LGTM**: true
- **条件付きLGTM**: なし
- **引き継ぎ完了確認**: handover.md に全ての負債・申し送りが記録済み
- **リリース可否**: 即時リリース可
- **承認者**: User / SE / PG / QA Reviewer Agent
