# 申し送り事項 (Handover): migrate-web-specs

- **変更名**: `migrate-web-specs`
- **日付**: 2026-11-03
- **ブランチ**: `feature/migrate-web-specs`
- **関連 Issue / PR**: なし

---

## 1. 変更サマリ

Webアプリケーション本体の仕様が旧ドキュメント（`doc/spec.md`）に留まり、デスクトップコンパニオン側との仕様管理の非対称性およびコード実態との乖離が存在していた問題を解消しました。
ユーザーの機能価値を軸にした5大モジュール（`video-library`, `video-scanner`, `tag-management`, `search-and-folders`, `viewing-analytics`）に分割・体系化し、観測可能な振る舞い（Behavior-Driven）に基づく要件とシナリオをOpenSpec仕様書として確立しました。

---

## 2. 品質・テストエビデンス

- **全テストスイート**: `dotnet exec test/TagBasedVideoManager.Tests/bin/Debug/net10.0/TagBasedVideoManager.Tests.dll`
  - 合計: 66 件 Pass（失敗 0 / スキップ 0）
  - 単体・結合テスト: 56 件 Pass
  - Playwright E2Eテスト: 10 件 Pass
- **OpenSpecバリデーション**: `openspec validate "migrate-web-specs"` → Valid
- **フェーズゲート検証**: Gate 0, Gate 1, Gate 2 PASSED

---

## 3. 保留指摘・技術的負債

| 指摘元（フェーズ/タスク/役割）  | 内容                                                                            | 保留理由                                                                                   | 推奨対応時期                   |
| :------------------------------ | :------------------------------------------------------------------------------ | :----------------------------------------------------------------------------------------- | :----------------------------- |
| Propose / 設問1 / User Reviewer | 破損ファイル・未対応ストリームに対する再スキャン時の無限再投入リスク            | 現行の仕様移行スコープを超えるため、コード変更を伴う機能改修として分離                     | 次期スキャナ改善時             |
| Propose / 設問2 / SE Reviewer   | FFmpeg外部プロセスハング時のタイムアウト未設定によるキュー凍結リスク            | 現行は `POST /api/media/stop` で手動回復可能であり、自動タイムアウトの導入は別Changeとする | 次期メディアパイプライン改修時 |
| Propose / 設問2 / SE Reviewer   | アプリ起動時 `ErrorLogs` 全件削除（証拠隠滅）およびログへの操作コンテキスト欠落 | 現行コード実態の反映であり、テーブル設計・移行計画を伴う別Changeとして実施                 | 次期可観測性向上時             |

---

## 4. 将来の改善・リファクタ候補

1. **外部プロセス実行の耐障害性向上**:
   `MediaProcessor.runProcess` に CancellationToken / タイムアウト（30秒等）を導入し、ハングした FFmpeg プロセスを自動で Kill して後続キューを保護する。
2. **可観測性（ErrorLogs）の改善**:
   起動時全クリアを廃止し、件数上限付きローテーション保持へ移行する。また `ErrorLogs` に `VideoId` や `FilePath` カラムを追加する。
3. **フロントエンド（Web）の保守性向上**:
   単一ファイルに集約されている `index.html` (109KB) および `app.js` (40KB) について、No-build の原則を維持しながらパーツ分割やストア分離を検討する。

---

## 5. 運用上の注意事項

- システムの仕様・要件・振る舞いを確認・更新する際は、旧 `doc/spec.md` ではなく、正本である `openspec/specs/` 配下の各仕様書を参照してください。
