# 申し送り事項 (Handover): display-application-version

- **変更名**: `display-application-version`
- **日付**: 2026-11-10
- **ブランチ**: `feature/display-application-version`
- **関連 Issue / PR**: https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/35

---

## 1. 変更サマリ

Web アプリケーション（画面ヘッダーバッジおよび `<title>`）およびデスクトップアプリケーション（AI File Renamer のウィンドウタイトルバー）において、稼働中のセマンティックバージョン（初期値: `v0.1.0`）を表示する機能を実装しました。また、変更規模レベル（L1: Patch, L2: Minor, L3/ユーザー指示: Major）に連動した SemVer 2.0.0 運用ルールを制定し、`AGENTS.md` および `openspec/config.yaml` に定義しました。

### 設計背景とトレードオフ（Why）

- **なぜ静的 HTML ハードコードではなく `GET /api/version` 経由なのか？**:
  静的 HTML にバージョンを直接埋め込むと、Docker コンテナ環境などでビルド・デプロイした際にバックエンドのアセンブリバージョンとの二重管理・不整合が発生しやすくなります。アセンブリから動的に取得して配信することで Single Source of Truth を維持しています。
- **なぜ `Cache-Control: no-cache, no-store, must-revalidate` を付与するのか？**:
  Web アプリの静的リソースは通常キャッシュされますが、バージョン API がブラウザにキャッシュされると、コンテナやサービスを新バージョンへデプロイした後もブラウザが古いバージョン情報を表示し続けてしまうため、明示的なキャッシュ抑止を行っています。
- **なぜフロントエンドで Bulkhead パターン（非同期分離・2秒タイムアウト）を採用したのか？**:
  バージョン番号の表示は補助的な情報であり、万が一バージョン API がネットワーク障害やサーバ高負荷で遅延・ハングした場合でも、ユーザーの主目的である動画一覧やタグ一覧の初期化・描画を巻き添えでブロックさせないための防御的設計です。

---

## 2. 品質・テストエビデンス

- **単体テスト**: `dotnet run --project test/TagBasedVideoManager.Tests -- --filter-class "*Unit*"` および Renamer Tests → 全 386 件 Pass（失敗 0 / スキップ 0）
- **結合テスト**: `dotnet run --project test/TagBasedVideoManager.Tests -- --filter-class "*Integration*"` → 全 87 件 Pass（失敗 0 / スキップ 0）
- **E2Eテスト**: `dotnet run --project test/TagBasedVideoManager.Tests -- --filter-class "*E2E*"` → 全 38 件 Pass（失敗 0 / スキップ 0）
- **全ソリューションテスト**: `dotnet test --nologo` → **合計 515 件 Pass（失敗 0 / スキップ 0 / 100% Green）**
  - `TagBasedVideoManager.Tests.dll`: 476 件
  - `TagBasedVideoManager.Renamer.Tests.dll`: 39 件
- **カバレッジ**:
  - 全体 Line (C0): 83.4%、Branch (C1): 64.7%、Method: 77.7%
  - 新規・変更差分カバレッジ: 90% 以上達成
- **エビデンス出力先**:
  - TRX テストレポート:
    - `test/TestResults/TestRun_2026-11-10_01_52_03/TestRun_2026-11-10_01_52_03_net10.0.trx`
    - `test/TestResults/TestRun_2026-11-10_01_52_03/TestRun_2026-11-10_01_52_03_Renamer_net10.0.trx`
  - HTML カバレッジレポート: `test/TestResults/TestRun_2026-11-10_01_52_03/CoverageReport/index.html`
  - E2E 画面キャプチャ:
    - `test/TestResults/version_e2e_desktop.png` (デスクトップ幅でのバッジ・タイトル表示)
    - `test/TestResults/version_e2e_mobile.png` (モバイル幅でのタイトル追従・折り返し表示)
    - `test/TestResults/version_e2e_fallback.png` (API障害時のフォールバック非表示・コア機能動作)

---

## 3. 保留指摘・技術的負債

なし（全タスク・全観点 LGTM）

| 指摘元（フェーズ/タスク/役割） | 内容 | 保留理由 | 推奨対応時期 |
| ------------------------------ | ---- | -------- | ------------ |
| 特になし                       | -    | -        | -            |

---

## 4. 将来の改善・リファクタ候補

- バージョンバッジをクリックした際にリリースノート（CHANGELOG）や更新履歴モーダルを表示するインタラクティブ拡張の検討（現状は静的バッジ表示のみ）。
- デスクトップアプリ（Renamer）のメイン画面内（ステータスバーやフッター）へのバージョン表示拡張（現状は OS タイトルバーのみ）。

---

## 5. 運用上の注意事項

- 今後の変更において、変更規模（L1 / L2 / L3）またはユーザー指示に基づき各 `.fsproj` の `<Version>` を適切にインクリメントすること。
  - **L1 (Patch)**: 不具合修正・軽微な調整・設定微修正 (`X.Y.Z+1`)
  - **L2 (Minor)**: 後方互換性のある新機能追加・仕様変更・UI変更 (`X.Y+1.0`)
  - **L3 (Major)**: 破壊的変更・アーキテクチャ変更、またはユーザー明示指示 (`X+1.0.0`)
