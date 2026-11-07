# 申し送り事項 (Handover): fix-wslc-unsupported-restart-option

- **変更名**: `fix-wslc-unsupported-restart-option`
- **日付**: 2026-11-08
- **ブランチ**: `fix/wslc-unsupported-restart-option`
- **関連 Issue / PR**: #26

---

## 1. 変更サマリ

Windows 11 の WSL 3.0 環境で `scripts/deploy.ps1` を実行した際、自動検出されたコンテナランタイム `wslc` が `--restart` オプションをサポートしていないことによる起動失敗、および `wslc` の IPv4 単一バインドと PowerShell の IPv6 優先名前解決によるヘルスチェックタイムアウト誤判定の 2 点の不具合を解消しました。

### 原因と対応（不具合修正の場合）

- **原因**:
  1. `scripts/common.ps1` の `New-RunArguments` 関数が無条件で `--restart unless-stopped` を付与していたが、`wslc run` は `--restart` オプションに対応していなかった。また `deploy.ps1` から `New-RunArguments` に検出されたランタイムが渡されていなかった。
  2. `wslc` はポートフォワーディングを IPv4（`127.0.0.1`）のみにバインドするが、`scripts/deploy.ps1` のヘルスチェック先が `http://localhost:${resolvedPort}/` となっていたため、Windows PowerShell の `Invoke-WebRequest` が IPv6（`[::1]`）優先で接続試行して毎回 2 秒タイムアウト（10回 / 計30秒）し、コンテナ正常起動中にもかかわらず失敗判定されていた。
- **対応**:
  1. `scripts/common.ps1` の `New-RunArguments` に `[string]$Runtime = ""` 引数を追加し、`$Runtime -ne "wslc"` の場合のみ `--restart unless-stopped` を付与するよう修正。
  2. `scripts/deploy.ps1` から `New-RunArguments` 呼出時に `-Runtime $runtime` を渡すよう更新。
  3. `scripts/deploy.ps1` のヘルスチェック URL（`$healthUrl`）を `http://127.0.0.1:${resolvedPort}/` に更新。
  4. `test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs` に `wslc` 指定時に `--restart` が含まれない単体テスト（`UT-RUN-ARGS-WSLC`）、AST による `-Runtime` 伝達検証テスト（`IT-SCRIPT-03`）、およびヘルスチェック URL が `127.0.0.1` であることを検証するテスト（`IT-SCRIPT-04`）を追加。

---

## 2. 品質・テストエビデンス

- **単体テスト**: `DeployScriptTests` 関連テスト全件 Pass（新規テスト 3 件含む）
- **ランタイム結合・引数検証**: `pwsh -NoProfile` 実機スクリプト検証 → `wslc` ランタイムで `--restart` が確実に除外され、`http://127.0.0.1:5620/` に対して即座に `200 OK` が返ることを確認済み
- **カバレッジ**: スクリプト引数生成およびヘルスチェック URL 設定ロジック 100% 網羅
- **エビデンス出力先**: `test/TestResults/`

---

## 3. 保留指摘・技術的負債

| 指摘元（フェーズ/タスク/役割） | 内容                                                                                    | 保留理由                                                                                                                          | 推奨対応時期       |
| ------------------------------ | --------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- | ------------------ |
| Propose / SE Reviewer 指摘1    | `wslc` では `--restart unless-stopped` が除外されるため OS/WSL 再起動時に自動起動しない | ローカル開発・軽量実行ランタイムとしての位置づけであり、現時点で常時稼働機構をスクリプト側に過剰実装する必要がないため            | 常時稼働要求発生時 |
| Propose / SE Reviewer 指摘2    | `$Runtime -ne "wslc"` の否定条件によるランタイム差分分岐                                | 現行の Windows サポートランタイムは `wslc` と `docker` のみであり、現時点での抽象レイヤー導入はオーバーエンジニアリングとなるため | ランタイム追加時   |

---

## 4. 将来の改善・リファクタ候補

- 将来的に Podman 等の追加ランタイムを Windows でサポートする場合、ランタイム機能マトリクス（Capabilities）による動的オプション構成へのリファクタリング。

---

## 5. 運用上の注意事項

- `wslc`（WSL 3.0 コンテナ）を利用している環境では、WSL インスタンスのシャットダウンやホスト再起動後にコンテナは自動再起動されません。再稼働させたい場合は `scripts/deploy.ps1` を再度実行してください。
- ブラウザや外部クライアントからアクセスする際は、`http://localhost:5620/` または `http://127.0.0.1:5620/` のどちらからでも接続可能です。
