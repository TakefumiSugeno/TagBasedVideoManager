<!--
  PR 本文テンプレート（Spec駆動）
  - タイトル: <type>(<change-name>): <subject>
  - 作成例: gh pr create --base <BASE_BRANCH> --head feature/<change-name> --title "<title>" --body-file <この内容を埋めた一時ファイル>
  - 各セクションは OpenSpec 成果物を正本として転記・要約する（このコメントは削除して投稿）
-->

## 概要 (What & Why)

<!-- proposal.md から目的・課題・価値を要約 -->

## 仕様変更・追加要件 (Spec Delta)

<!-- specs/<capability>/spec.md の ADDED / MODIFIED / REMOVED 要件一覧 -->

## 設計・実装内容 (Design & Tasks)

<!-- design.md / tasks.md から主要設計方針・変更コンポーネント -->

## テスト結果・品質エビデンス (Verification & Evidence)

<!-- reviews.md の Archive 総括レビューから転記 -->

- 単体/結合/E2E テスト: 〇件 Pass（失敗 0 件, スキップ 0 件）
- カバレッジ: 〇%
- エビデンス: `<出力先パス>`（Git 管理外）

## レビュー記録 (Reviews)

<!-- Propose / Apply / Archive の各レビュー結果（LGTM）と主要指摘の対応状況 -->

## 申し送り事項・残課題 (Handover)

<!-- handover.md の保留指摘・将来の改善候補（ない場合は「特になし」） -->

## 関連

- OpenSpec change: `openspec/changes/archive/<YYYY-MM-DD>-<change-name>/`
- Issue: <URL または なし>
