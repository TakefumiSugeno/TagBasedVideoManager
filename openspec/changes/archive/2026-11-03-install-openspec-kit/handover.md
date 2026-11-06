# 申し送り事項 (Handover): install-openspec-kit

- **変更名**: `install-openspec-kit`
- **日付**: 2026-11-03
- **ブランチ**: `chore/install-openspec-kit`
- **関連 Issue / PR**: なし

---

## 1. 変更サマリ

AIUtils で実証された OpenSpec 汎用拡張キット（`openspec-kit`）および最新の挑戦的レビューフレームワーク（Adversarial QA / 悪魔の代弁者 / ブラインド突合 / 特化ペルソナ）を本リポジトリへ導入した。
これにより、従来の形骸化したチェックリスト埋めを排除し、6ロール分業（TDD 3権分立）と機械的フェーズゲート検証（Gate 0〜3）による客観的で高規律な SDD × TDD 開発基盤を確立した。

---

## 2. 品質・テストエビデンス

- **フェーズゲート検証**:
  - `verify-gate.ps1 -Gate pre-propose`: PASS（トピックブランチ確認）
  - `verify-gate.ps1 -Gate propose`: PASS（成果物存在・レビューLGTM確認）
  - `verify-gate.ps1 -Gate apply`: PASS（全タスク完了・レビューLGTM・skip放置ゼロ確認）
- **構文・スキーマ検証**:
  - `openspec validate install-openspec-kit`: PASS（valid）
- **静的解析・フォーマット**:
  - `npx prettier --write`: 差分 0 件確認

---

## 3. 保留指摘・技術的負債

| 指摘元（フェーズ/タスク/役割） | 内容 | 保留理由 | 推奨対応時期 |
| ------------------------------ | ---- | -------- | ------------ |
| なし                           | -    | -        | -            |

※ 全タスク・全観点 LGTM。保留指摘なし。

---

## 4. 将来の改善・リファクタ候補

- 今後 F# 実装タスクで Apply を行う際、`qa-reviewer` による破壊テスト（Adversarial QA）を `dotnet test` スクラッチで実践し、実運用でのフィードバックをキット側に還元する。
- Avalonia GUI のビジュアル E2E テストにおいて `RenderTargetBitmap` 出力を活用した自動画像差分テストの導入検討。

---

## 5. 運用上の注意事項

- 今後の変更開始時は、必ず `develop` からブランチを作成し、Gate 0（`pwsh ./openspec/scripts/verify-gate.ps1 -Gate pre-propose`）を通してから `openspec new change` を実行すること。
- UI/UX変更時は必ず `mockup.html`（Tailwind CSS + Alpine.js の単一ファイル）を作成すること。
