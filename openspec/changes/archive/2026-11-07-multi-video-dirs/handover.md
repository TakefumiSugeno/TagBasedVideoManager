# 申し送り事項 (Handover): multi-video-dirs

- **変更名**: `multi-video-dirs`
- **日付**: 2026-11-07
- **ブランチ**: `feature/multi-video-dirs`
- **関連 Issue / PR**: [Issue #22](https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/22)

---

## 1. 変更サマリ

Docker 環境においてホスト OS のシンボリックリンク／ジャンクションが解決できない問題（Issue #22）を解消するため、スキャン対象として複数の物理動画フォルダを直接指定可能にしました。

- **環境変数一本化**: 本体側は `VIDEO_DIRS`（セミコロン `;` 区切り）に一本化し、旧 `VIDEO_DIR` を完全廃止（未設定・旧変数残存時は設定例付きエラー）。デスクトップアプリ（AI File Renamer）はホスト専用のため `VIDEO_DIR` を維持。
- **タグ自動導出**: 指定された各ルートのフォルダ名（Leaf 名）およびサブフォルダ名を動画タグとして自動付与（同名は統合、入れ子ルートは和集合）。
- **ドライブ直下・重複の拒否**: `D:\` や `/` などのルートパスおよびマウント先フォルダ名衝突を早期バリデーションで拒絶。
- **デプロイスクリプト早期検証**: `scripts/common.ps1`, `scripts/deploy.ps1`, `scripts/deploy.sh` において、イメージビルドやコンテナ停止より前に入力検証を行い副作用を抑止。`deploy.sh` の `.env` 読み込みを安全パーサ（`parse_env`）に置換。
- **UI エラー詳細通知**: スキャン失敗時、API から返却された設定例・移行案内メッセージを Web UI トースト内にモノスペース領域で 7 秒間展開表示。

---

## 2. 品質・テストエビデンス

- **単体・結合・E2Eテスト**:
  - `TagBasedVideoManager.Tests` (Web / E2E): `129` 件 Pass（失敗 0 / スキップ 0）
  - `TagBasedVideoManager.Renamer.Tests`: `333` 件 Pass（失敗 0 / スキップ 0）
  - 合計: `462` 件 100% Pass
- **カバレッジ**:
  - `TagBasedVideoManager.Renamer`: Line 84.61% / Branch 64.42% / Method 62.65%
  - `TagBasedVideoManager`: Line 66.44%（新規 `VideoDirs.fs` は単体テスト C0/C1 100% 網羅）
- **エビデンス出力先**:
  - TRX: `test/TagBasedVideoManager.Tests/TestResults/` および `test/TagBasedVideoManager.Renamer.Tests/TestResults/`
  - HTML カバレッジレポート: `CoverageReport/index.html`
  - E2E 画面キャプチャ:
    - `test/TestResults/e2e-01-happy-path.png` (Happy Path 複数フォルダ検出・タグ表示)
    - `test/TestResults/e2e-02-zero-state-error.png` (Zero-State エラー詳細トースト表示)
    - `test/TestResults/e2e-03-recovery-path.png` (Recovery Path 設定反映後スキャン成功)
    - `test/TestResults/e2e-04-mockup-fidelity.png` (UI モック忠実性検証)

---

## 3. 保留指摘・技術的負債

Apply フェーズのレビュー（`reviews.md`）において保留とした指摘全件の引き継ぎ記録です。

| 指摘元（フェーズ/タスク/役割）          | 内容                                                                                                                            | 保留理由                                                                                 | 推奨対応時期           |
| :-------------------------------------- | :------------------------------------------------------------------------------------------------------------------------------ | :--------------------------------------------------------------------------------------- | :--------------------- |
| **Apply / Task 1.3 / PG** (Min-1)       | `VideoDirs.fs` における内部型のスコープ限定（`[<AutoOpen>]` 配慮）                                                              | 単一目的のモジュールであり他モジュールとの名前衝突リスクが低いため                       | 次期リファクタリング時 |
| **Apply / Task 1.3 / PG** (Sugg-1)      | `VideoDirs.fs` の `validateRootNotDrive` での複数スラッシュ早期置換                                                             | 現状の正規表現 `^/+$` で正常に検知できているため                                         | 次期リファクタリング時 |
| **Apply / Task 2.3 / SE** (Min-2)       | `Scanner.fs` におけるスキャン中の動的ファイル削除（`FileNotFoundException`）への防護                                            | スキャン実行中の外部ファイル削除は運用上極めて稀であり、次回スキャンで自動同期されるため | 次期機能改修時         |
| **Apply / Task 2.3 / SE** (Min-3)       | `Scanner.fs` における親ディレクトリを指す循環シンボリックリンク（ジャンクション）による無限再帰対策（`visitedDirs` の導入）     | 通常構成では極めて稀であり、Docker コンテナ内ではシンボリックリンクを跨がないため        | 次期機能改修時         |
| **Apply / Task 2.3 / SE** (Min-4)       | `Scanner.fs` におけるファイル走査中（`enumerateFiles`）のアクセス権限エラー発生時の詳細ログ出力                                 | 一般的な動画フォルダ走査ではアクセス拒否は発生しにくいため                               | 次期ログ強化時         |
| **Apply / Task 3.4 / PG** (Sugg-2)      | `HttpHandlers.fs` における `getEnv` や 500 レスポンス生成の共通 Giraffe 関数活用                                                | 現状の記述で十分に簡潔であり動作に支障がないため                                         | 次期リファクタリング時 |
| **Apply / Task 3.4 / PG** (Sugg-3)      | `app.js` における `triggerScan()` 冒頭への `isScanning` 二重実行ガード節追加                                                    | UI 上でスキャンボタンが disabled になるため実害は軽微                                    | 次期 UI 改善時         |
| **Apply / Task 3.4 / SE** (Sugg-4)      | `index.html` におけるトースト詳細領域への `max-h-48 overflow-y-auto` スクロール制御追加                                         | 現状のエラーメッセージ長では画面内に収まるため                                           | 次期 UI 改善時         |
| **Apply / Task 4.5 / PG** (Sugg-5)      | `scripts/common.ps1` の `Resolve-VideoMounts` における `$VideoDirs` 空チェックと `$rawPaths.Count -eq 0` の重複エラー分岐の統合 | 動作および早期バリデーションに支障がないため                                             | 次期スクリプト整理時   |
| **Apply / Task 4.5 / PG** (Sugg-6)      | `scripts/deploy.ps1` の `nvidia-smi` 判定における不要な try-catch の削除および冗長代入の整理                                    | コマンド有無判定の動作に支障がないため                                                   | 次期スクリプト整理時   |
| **Archive / 総括 / User** (Sugg-User-1) | `README.md` へのトラブルシューティング独立見出しの新設                                                                          | 現状は注意書きに集約されており動作・品質に影響しないため                                 | 次期ドキュメント改善時 |

---

## 4. 将来の改善・リファクタ候補

1. **NAS / SMB ネットワークドライブ対応のドキュメント拡充**:
   - Docker Desktop の WSL2 上では Windows 側のネットワークドライブレター（`X:`, `Y:`）のバインドマウントが制限されるため、Windows ホスト直接実行（`dotnet run`）または CIFS ボリューム指定の手順ガイドを開発者向け Wiki 等に整備する。
2. **循環シンボリックリンク防御 (`visitedDirs`)**:
   - `Scanner.fs` の再帰ディレクトリ走査において、実体パスを `HashSet` で追跡し、意図しない循環ジャンクションによるスタックオーバーフローを物理的に防ぐ仕組みを導入する。
3. **トースト UI の拡張**:
   - 長文エラー発生時に備え、トースト詳細領域にスクロール制御（`max-h-48 overflow-y-auto`）とクリップボードコピー用ボタンを追加する。

---

## 5. 運用上の注意事項

- **環境変数移行**:
  - Web アプリ本体および Docker コンテナでは旧 `VIDEO_DIR` が廃止され、`VIDEO_DIRS` のみが認識されます。`.env` またはデプロイ環境で `VIDEO_DIRS="パス1;パス2"` を設定してください。
- **Docker 環境での `FilePath` 変更**:
  - Docker 起動時、マウント先は `/app/videos/<フォルダ名>/...` となります。以前の単一マウント（`/app/videos/...`）で登録されていたメタデータ DB を流用するとパス不一致が発生するため、初回スキャン時に新規動画として再登録されます（DB の再作成を推奨）。
- **ロールバック手順**:
  - 本変更をロールバックする場合は、`git revert` でコミットを巻き戻し、`.env` の `VIDEO_DIRS` を旧 `VIDEO_DIR` に戻すだけで完全に復元可能です。DB スキーマへの破壊的変更はありません。
