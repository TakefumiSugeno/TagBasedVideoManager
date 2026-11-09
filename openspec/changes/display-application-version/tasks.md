# Tasks: 稼働アプリケーションバージョンの画面表示とセマンティックバージョニング規則の制定

## 1. プロジェクト設定とバージョン管理基盤（SemVer初期化）

- [x] 1.1 Web プロジェクト（`src/TagBasedVideoManager/TagBasedVideoManager.fsproj`）に `<Version>0.1.0</Version>` を設定し、`dotnet build src/TagBasedVideoManager` でビルドが成功することを検証
- [x] 1.2 デスクトッププロジェクト（`src/TagBasedVideoManager.Renamer/TagBasedVideoManager.Renamer.fsproj`）に `<Version>0.1.0</Version>` を設定し、`dotnet build src/TagBasedVideoManager.Renamer` でビルドが成功することを検証

## 2. Webバックエンド: バージョンAPIの実装とキャッシュ制御 (TDD)

- [x] 2.1 [qa-tester] 純粋関数 `formatAssemblyVersion` の単体テスト（`UT-01`, `UT-02`, `UT-03`）および `GET /api/version` の結合テスト（`IT-01` Cache-Control ヘッダー検証含む, `IT-02`, `IT-03`）を `test/TagBasedVideoManager.Tests/HttpHandlersTests.fs` に作成し、Red（テスト失敗）を確認
- [x] 2.2 [pg-implementer] `src/TagBasedVideoManager/HttpHandlers.fs` に純粋関数と `GET /api/version`（`Cache-Control: no-cache, no-store, must-revalidate` 付与）を実装し、全テストが Green（成功）することおよび Refactor を検証
- [x] 2.3 [pg-implementer] `src/TagBasedVideoManager/Program.fs` にて起動時に解決されたバージョンを `Log.Information` で出力するよう実装し、起動ログ出力を検証

## 3. デスクトップアプリ: ウィンドウタイトルへのバージョン表示と起動ログ (TDD)

- [x] 3.1 [qa-tester] タイトル文字列フォーマット純粋関数 `formatTitle` の先行単体テスト（`UT-04`, `UT-05`）を `test/TagBasedVideoManager.Renamer.Tests/` に作成し、Red（テスト失敗）を確認
- [x] 3.2 [pg-implementer] `src/TagBasedVideoManager.Renamer/Program.fs` にて純粋関数を用いて `base.Title` に設定し、起動ログ出力（`printfn`）を実装して Green（成功）および起動確認（`E2E-04`）を検証

## 4. Webフロントエンド: 画面ヘッダーへのバージョンバッジ追加と E2E 検証

- [x] 4.1 [pg-implementer] `src/TagBasedVideoManager/wwwroot/index.html` および `app.js` を更新し、2秒タイムアウト・Bulkhead非同期取得（コア初期化ブロック防止）、タイトル横の兄弟要素インラインバッジ（モバイル折り返し追従）、および `<title>` へのバージョン反映を実装
- [x] 4.2 [qa-tester] Playwright E2E テスト（`E2E-01` デスクトップ表示、`E2E-02` モバイル折り返し、`E2E-03` 3秒ハング・500障害時のコア機能維持とフォールバック）を実装・実行し、`test/TestResults/` に PNG 画面キャプチャを出力して検証

## 5. OpenSpec 運用ルール反映と全体検証 (DoD)

- [x] 5.1 `AGENTS.md` および `openspec/config.yaml` に L1/L2/L3 規模連動およびユーザー指示に基づく SemVer 採番・インクリメント運用ルールを追記
- [x] 5.2 全体テストを実行して TRX レポートおよび HTML カバレッジレポートを出力し、C0 90%+/C1 85%+ の達成と Gate 2 検証（`pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply -ChangeName display-application-version`）が PASS することを確認
