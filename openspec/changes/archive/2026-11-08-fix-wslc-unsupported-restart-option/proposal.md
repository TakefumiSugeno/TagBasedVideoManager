# Proposal: WSL 3.0 (wslc) における未サポートの --restart オプション除外および IPv4 ヘルスチェック対応

- **Level**: L1（軽微: スクリプトの局所修正・未サポートオプション除外・ヘルスチェックURL修正）
- **Type**: fix

## Why

Windows 11 の WSL 3.0 環境で `scripts/deploy.ps1` を実行した際、以下の 2 点の問題が発生します。

1. **`wslc run` の `--restart` オプション未サポート**:
   自動検出されたコンテナランタイム `wslc` に対して、`scripts/common.ps1` の `New-RunArguments` が `--restart unless-stopped` を付与してコンテナ起動（`wslc run`）を呼び出します。しかし、`wslc run` コマンドは `--restart` オプションをサポートしていないため、「`オプション名が現在のコマンドに対して認識されませんでした: '--restart'`」というエラーが発生してコンテナ起動が失敗します。
2. **ヘルスチェック時の IPv6 優先接続によるタイムアウト**:
   コンテナ起動後、`scripts/deploy.ps1` は `http://localhost:${resolvedPort}/` に対する HTTP GET で起動確認を行いますが、`wslc` はポートフォワーディングを IPv4（`127.0.0.1`）のみにバインドします。一方、Windows PowerShell の `Invoke-WebRequest` は `localhost` を IPv6（`[::1]`）優先で解決・接続しようとするため、ポートが開いていない IPv6 側で毎回 2 秒間のタイムアウトが発生し、規定回数（10 回 / 30 秒）すべてでタイムアウトとなり、コンテナが正常稼働しているにもかかわらずデプロイ失敗と誤判定されます。

これらを解決するため、コンテナ起動引数構築時にランタイム種別を判定して `wslc` の場合に `--restart` オプションを除外するとともに、ヘルスチェック接続先を明示的に IPv4 ループバック（`http://127.0.0.1:${resolvedPort}/`）へ更新します。

## What Changes

- **`scripts/common.ps1`**:
  - `New-RunArguments` に `[string]$Runtime = ""` パラメータを追加。
  - `$Runtime -ne "wslc"` の場合のみ `--restart unless-stopped` を追加するよう修正。
- **`scripts/deploy.ps1`**:
  - `New-RunArguments` 呼び出し時に `-Runtime $runtime` を渡すように更新。
  - ヘルスチェック URL（`$healthUrl`）を `http://localhost:${resolvedPort}/` から `http://127.0.0.1:${resolvedPort}/` に変更。
- **`test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs`**:
  - `UT-RUN-ARGS-WSLC: wslc 指定時は --restart オプションを含めない` テストを追加。
  - `IT-SCRIPT-03: deploy.ps1 は New-RunArguments に -Runtime $runtime を渡している (AST)` テストを追加。
  - `IT-SCRIPT-04: deploy.ps1 は IPv4 ループバック 127.0.0.1 を用いてヘルスチェックを行っている (AST)` テストを追加。
  - 既存の `UT-RUN-ARGS: --restart unless-stopped を指定する`（未指定 / docker 時）が引き続き PASS することを確認。

## Capabilities

### New Capabilities

なし

### Modified Capabilities

なし（既存仕様のバグ修正）

## Non-goals

- `docker` や `podman` 環境における `--restart unless-stopped` の廃止（これらは従来通り維持）。
- `scripts/deploy.sh`（Linux 向け）の変更（Linux 環境では `docker` または `podman` のみが対象であり `--restart` をサポートしているため）。

## Acceptance Criteria

- **Scenario: wslc ランタイムでの起動引数**
  - **GIVEN** ランタイムとして `wslc` が指定されていること
  - **WHEN** `New-RunArguments` を呼び出したとき
  - **THEN** 生成された引数配列に `--restart` が含まれないこと
- **Scenario: docker / 既定ランタイムでの起動引数**
  - **GIVEN** ランタイムとして `docker` が指定されている（または省略された）こと
  - **WHEN** `New-RunArguments` を呼び出したとき
  - **THEN** 生成された引数配列に `--restart unless-stopped` が含まれること
- **Scenario: deploy.ps1 からのランタイム引数伝達**
  - **GIVEN** Windows ホストで `deploy.ps1` が実行されたとき
  - **WHEN** `New-RunArguments` が呼び出されるとき
  - **THEN** 検出された `$runtime`（例: `wslc`）が確実に `New-RunArguments` に渡されること
- **Scenario: deploy.ps1 の IPv4 ループバックヘルスチェック**
  - **GIVEN** `deploy.ps1` のヘルスチェック処理において
  - **WHEN** ヘルスチェック URL を解決したとき
  - **THEN** IPv6 優先によるタイムアウトを回避するため `http://127.0.0.1:<ポート>/` を指定していること
