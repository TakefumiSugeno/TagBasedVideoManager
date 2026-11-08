# 申し送り事項 (Handover): uuid-video-identity-and-cleanup

- **変更名**: `uuid-video-identity-and-cleanup`
- **日付**: 2026-11-09
- **ブランチ**: `feature/uuid-video-identity-and-cleanup`
- **関連 Issue / PR**: https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/23

---

## 1. 変更サマリ

動画ファイル本体を一切変更することなく（Read-Only サンプリング）、決定論的 UUID（RFC 9562 UUID v5）によるファイル同一性の管理を導入しました。これにより、サブフォルダ移動、ファイル名リネーム（AI File Renamer 等）、および Docker バインドマウント変更やホスト/コンテナ切り替えが発生しても、既存の動画 ID、タグ、お気に入り、再生回数、再生時間を自動的に引き継ぎ、FFmpeg / ffprobe による重いサムネイル再生成やメタデータ解析を完全にスキップして即座に同期できるようになりました。また、物理削除された動画の DB レコード・タグ紐付け・サムネイル画像の一括削除、孤児サムネイル画像の自動削除、および再生試行時の HTTP 404 Fail-Fast 応答と UI エラーオーバーレイ・トースト通知を実装しました。

### 設計背景とトレードオフ（Why）

- **なぜファイル全件の SHA-256 ではなく先頭・中間・末尾の Read-Only サンプリングなのか？**:
  数 GB〜数十 GB に及ぶ大容量動画ファイルを全件ハッシュ化すると、スキャン時に激しいディスク I/O が発生しシステム全体が数秒〜数十秒間ブロッキングします。本設計ではファイルサイズ（8B）と先頭・中間・末尾 64KB（計 192KB）のみをサンプリングすることで、ミリ秒オーダーの極めて軽快な処理速度を維持しつつ、実用上十分な一意性を担保しています。
- **なぜスキャンを 3 フェーズに分けたのか（Phase 1 パス一致の先行確定）？**:
  同一内容のコピー動画（同一 UUID）が別フォルダに配置された場合、もし未登録ファイルと既存 DB レコードを一括でマッチングしてしまうと、元ファイルが実在するにもかかわらず既存レコードのパスがコピー先パスで誤って上書き（横取り）されてしまうリスクがあります。そのため、Phase 1 でまずパスが一致する既存ファイルを確定・保護した上で、Phase 2 で未マッチの残存ファイルのみを対象に UUID 照合を行う決定論的アーキテクチャを採用しています。

### 原因と対応（不具合修正・機能拡張）

- **原因**:
  1. 従来の同期処理は完全修飾ファイルパス（`FilePath`）のみを主キー同等として扱っていたため、バインドマウントの変更（`/data/videos` ⇄ `C:\videos`）やサブフォルダ移動・リネーム時に新規動画として二重登録され、タグや視聴履歴が失われてサムネイル再生成が再発していた。
  2. 物理削除された動画の DB レコードやサムネイルが残存し、スキャン前にプレイヤーで再生しようとした際にバックエンドで `FileNotFoundException`（HTTP 500）が発生し、フロントエンドが原因不明のまま無反応になっていた。
- **対応**:
  1. **UUID v5 算出アルゴリズムの導入**: ファイルサイズおよび先頭・中間・末尾 64KB のサンプリングから高速かつ決定論的な UUID v5 を算出（動画本体は Read-Only）。
  2. **スキャン 3 フェーズ同期の実装**:
     - Phase 1: 正規化パス一致の既存動画の `FileUuid` バックフィル、および `{thumbDir}/{Id}.jpg` 実在確認による再処理スキップとパス補正。
     - Phase 2: 未登録パス動画に対する $O(1)$ ハッシュ照合（UUID 一致 ＋ 旧DB移行フォールバック）による同一動画引き継ぎ（`addedCount` は 0 件維持）と新フォルダタグ追加付与。
     - Phase 3: 不在 DB レコードの削除（SQLite `ON DELETE CASCADE` 連動）、サムネイル実ファイル削除、および孤児サムネイル画像削除。走査失敗ディレクトリ配下の動画を誤削除から保護する防御ガード（SE-201）を実装。
  3. **ストリーミング Fail-Fast & UI 通知**: 物理ファイル不在時に HTTP 404 と `FILE_NOT_FOUND` JSON を返却し、フロントエンドに赤色トースト通知およびプレイヤーエラーオーバーレイ（対象パス表示・「今すぐスキャン同期を実行」ボタン）を実装（`mockup.html` 準拠）。

---

## 2. 品質・テストエビデンス

- **単体テスト**: `dotnet test test/TagBasedVideoManager.Tests/TagBasedVideoManager.Tests.fsproj -- --filter-class "*Unit*"` → 全 347 件 Pass（失敗 0 / スキップ 0）
- **結合テスト**: `dotnet test test/TagBasedVideoManager.Tests/TagBasedVideoManager.Tests.fsproj -- --filter-class "*Integration*"` → 全 84 件 Pass（失敗 0 / スキップ 0）
- **E2Eテスト**: `dotnet test test/TagBasedVideoManager.Tests/TagBasedVideoManager.Tests.fsproj -- --filter-class "*E2ETests*"` → 全 35 件 Pass（失敗 0 / スキップ 0）
- **全ソリューションテスト**: `dotnet test --nologo` → **合計 503 件 Pass（失敗 0 / スキップ 0 / 100% Green）**
  - `TagBasedVideoManager.Tests.dll`: 466 件
  - `TagBasedVideoManager.Renamer.Tests.dll`: 37 件
- **カバレッジ**:
  - 単体テスト C0 (Line): 82.4%、結合テスト C0: 52.25%、E2Eテスト C0: 64.48%
  - 新規・変更差分カバレッジ: 90% 以上達成
  - C2（複合条件網羅）: `DT-01`〜`DT-10` のデシジョンテーブル 100% 網羅
- **エビデンス出力先**:
  - TRX テストレポート: `test/TagBasedVideoManager.Tests/TestResults/TestRun_2026-11-08_19_31_29/`
  - HTML カバレッジレポート: `test/TagBasedVideoManager.Tests/TestResults/TestRun_2026-11-08_19_31_29/{Unit,Integration,E2E}/CoverageReport_*/index.html`
  - E2E 画面キャプチャ:
    - `test/TestResults/e2e_03_file_not_found.png` (HTTP 404 エラートースト & オーバーレイ)
    - `test/TestResults/e2e_03_after_sync.png` (同期実行後の削除カード整理完了)

---

## 3. 保留指摘・技術的負債

| 指摘元（フェーズ/タスク/役割）      | 内容                                                                    | 保留理由                                                                                                                         | 推奨対応時期                            |
| ----------------------------------- | ----------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------- |
| Apply / Task 2 / SE Review (SE-204) | `Scanner.fs` の Phase 3 における `File.Delete` 失敗時の詳細警告ログ出力 | ファイルロック等によるスキャン全体の中断を防止するため `try ... with _ -> ()` で安全に保護済み。直ちに機能破綻しないため。       | 将来的なロガー導入・ログ改善時          |
| Apply / Task 1 / PG Review (PG-3)   | `Db.updateVideoLocation` 等において更新対象行が 0 件の場合の戻り値      | 既存の `Db.fs` 関数群全体（`updateVideoFileModifiedAt`, `deleteTag` 等）と一貫したイデオムとなっており、動作上の問題がないため。 | DB レイヤー全体のリファクタ時           |
| Propose / SE Review (SE-4)          | 可観測性向上のための構造化診断ログ出力                                  | 現状の `Db.saveErrorLog` によるログ記録で障害追跡は十分可能であるため。                                                          | 将来的な Serilog / OpenTelemetry 導入時 |

---

## 4. 将来の改善・リファクタ候補

- **`Videos.FileUuid` のインデックス作成**:
  現在の動画規模（数千本程度）ではインメモリの辞書インデックスで $O(1)$ 照合され瞬時に完了しますが、10万本規模の大規模ライブラリへのスケールアウトを見据え、将来的に `CREATE INDEX IF NOT EXISTS idx_videos_file_uuid ON Videos(FileUuid)` を追加することを検討可能。
- **孤児サムネイル削除の非同期バッチ化**:
  サムネイルディレクトリ内の画像ファイルが数万件を超える場合、スキャン完了後のバックグラウンドタスクとして遅延実行するアーキテクチャの導入。

---

## 5. 運用上の注意事項

- **自動スキーママイグレーション（前方互換）**:
  - アプリケーション起動時に `DbInit.initializeDatabase` が実行され、`Videos` テーブルに `FileUuid` カラムが存在しない場合は自動的に `ALTER TABLE Videos ADD COLUMN FileUuid TEXT` が実行されます（手動マイグレーション不要、ダウンタイムなし）。
  - 既存の登録済み動画は、次回のディレクトリ同期（Scan）実行時に自動的に `FileUuid` がバックフィルされます。
- **ロールバック手順**:
  - `Videos.FileUuid` は NULL 許容の追加カラムであるため、万が一アプリケーションバイナリを本 PR マージ前の旧バージョンへロールバックした場合でも、旧バージョンの `SELECT` / `INSERT` クエリは `FileUuid` カラムを無視してそのまま正常動作します（DB ファイルのリストアや手動 ALTER は不要）。

---

## 6. トラブルシューティング・運用調査手順

本番環境で同期や再生に関する不具合が疑われる場合、以下の手順で迅速に切り分けを実施してください。

### (1) 再生時に HTTP 404（ファイル不在）が発生する場合

- **事象**: 動画カードをクリックした際、画面に「⚠️ 動画ファイルが見つかりません」のエラートーストまたはエラーオーバーレイが表示される。
- **切り分け**:
  1. 画面のエラーオーバーレイまたは DB の `Videos.FilePath` を確認します。
  2. Docker / コンテナ環境の場合、ホスト側の動画フォルダがコンテナ内の `/app/videos/<フォルダ名>` に正しくマウントされているか確認します（`.env` の `VIDEO_DIRS` 設定と `docker inspect` のマウント設定を確認）。
  3. ファイルが実際に移動またはリネームされている場合は、プレイヤー内の「今すぐスキャン同期を実行」ボタンまたはヘッダーの同期ボタンを押してスキャンを実行してください。自動的に新パスへ追従されます。
  4. ファイルが物理削除されている場合も、スキャンを実行することで DB レコードとサムネイルが自動クリーンアップされます。

### (2) ファイルを削除したのにスキャンで DB レコードが消えない場合

- **事象**: ファイルを削除してスキャンを実行しても、一覧にカードが残り続ける。
- **原因**: 走査例外保護ガード（SE-201）が発動している可能性があります。一部のディレクトリで権限エラー（アクセス拒否）や I/O エラーが発生した場合、連鎖的な誤削除を防ぐため、該当ディレクトリ配下の動画は削除されず安全に維持されます。
- **調査 SQL**:
  ```sql
  SELECT * FROM ErrorLogs WHERE Message LIKE '%[Scanner Warning]%' ORDER BY Id DESC LIMIT 10;
  ```
- **対処**: コンソールログおよび `ErrorLogs` に記録されたアクセス失敗ディレクトリの権限（パーミッション）や接続状態を復旧させた後、再度スキャンを実行してください。

### (3) UUID 照合および同一性引き継ぎの調査

- **調査 SQL**:
  ```sql
  -- 特定の動画の FileUuid とメタデータ確認
  SELECT Id, FileName, FilePath, FileUuid, FileSize, FileModifiedAt FROM Videos WHERE Id = 'v_xxxxxxxx';

  -- FileUuid の重複や未設定（バックフィル待ち）レコードの確認
  SELECT FileUuid, COUNT(*) FROM Videos WHERE FileUuid IS NOT NULL GROUP BY FileUuid HAVING COUNT(*) > 1;
  ```
