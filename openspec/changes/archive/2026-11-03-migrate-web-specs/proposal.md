# Proposal

## 変更メタデータ

- 適用規模レベル: `L2`
- 変更種別: `chore` / `docs`

## Why

現在、デスクトップコンパニオン側の仕様は `openspec/specs/` 配下に厳密に管理されているのに対し、Webアプリケーション本体の仕様は旧ドキュメント（`doc/spec.md`）に留まっており、仕様の二重管理および非対称性が生じています。
また、コードベースとの突合によりサムネイル配信やタグ解除APIなどでドキュメントとコード実態の乖離が確認されたため、コードの実態（真実）をベースとしてWebアプリの仕様をOpenSpecのメイン仕様へ移行・体系化し、リポジトリ全体のSingle Source of Truthを確立します。

## What Changes

- Webアプリケーションの仕様をユーザーの機能価値を軸にした5大モジュール（`video-library`, `video-scanner`, `tag-management`, `search-and-folders`, `viewing-analytics`）に分割し、OpenSpecデルタ仕様として新規定義
- 旧ドキュメント（`doc/spec.md`）とコード実態の乖離を解消し、真実に基づく仕様（`/thumbnails/{id}.jpg` 静的配信、`DELETE /api/videos/{id}/tags` のJSONボディ受付等）を反映
- 観測可能な振る舞い（Behavior-Driven）に基づき、各要件（Requirement）および受け入れシナリオ（Scenario）を明文化

## Non-goals (やらないこと)

- 本変更におけるプロダクションコード（F#バックエンド、フロントエンドJS/HTML）の機能追加・破壊的変更
- Webアプリとデスクトップアプリの連携プロトコルの変更（既存のDocker再起動・ファイル共有連携を維持）
- UIデザインの大幅改変（UI変更は伴わないためHTMLモックアップは対象外）

## Capabilities

### New Capabilities

- `video-library`: 動画一覧の閲覧（グリッド/リスト表示、等速marquee）、HTTP 206 Rangeストリーミング配信、シーク多重カウントを防止した再生回数記録、お気に入り登録・一括操作
- `video-scanner`: ホスト動画ディレクトリの再帰走査（シンボリックリンク追跡）、即時DB登録、FFmpeg/ffprobe非同期メディア処理パイプライン（2段階シーク・GPU支援・同時実行制御）、メディア処理手動中断
- `tag-management`: 自己参照階層型タグのCRUD、動的動画件数集計、動画へのタグ紐付け・解除、自動タグ付けルールの作成・編集・遡及適用、TSV/JSON一括インポート・エクスポート
- `search-and-folders`: 検索DSL（AND/OR論理演算、`tag:`, `is:favorite`、親子階層タグ再帰ヒット）、スマートフォルダの保存・適用・削除
- `viewing-analytics`: 再生数ランキング（上位100件）、登録タグ辞書優先＋文字種境界によるハイブリッド抽出を用いたおすすめ動画（20件）、例外エラーログ履歴表示

### Modified Capabilities

（なし）

## Acceptance Criteria (受け入れ基準)

- **GIVEN** Webアプリケーション本体の機能群が実装されている状態において
- **WHEN** `migrate-web-specs` により5つの仕様書が `specs/` 配下に作成されたとき
- **THEN** 各仕様書がOpenSpecのRequirement / Scenario形式を満たし、コード実態（API契約・DB挙動・UI振る舞い）と100%整合していること
- **THEN** `openspec validate "migrate-web-specs"` およびフェーズゲート検証（Gate 1）がPASSすること

## Impact

- **仕様ドキュメント**: `openspec/specs/` に Web アプリのメイン仕様が追加され、システム仕様の正本が一本化される
- **既存コード**: プロダクションコードへの影響なし
- **テスト**: 既存の単体・結合・E2Eテストはすべて維持され、仕様書シナリオとテストケースが直接対応付けられる
