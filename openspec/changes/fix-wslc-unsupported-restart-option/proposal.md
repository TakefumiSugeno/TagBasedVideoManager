# Proposal: WSL 3.0 (wslc) における未サポートの --restart オプション除外

- **Level**: L1（軽微: スクリプトの局所修正・未サポートオプション除外）
- **Type**: fix

## Why

Windows 11 の WSL 3.0 環境で `scripts/deploy.ps1` を実行した際、自動検出されたコンテナランタイム `wslc` に対して、`scripts/common.ps1` の `New-RunArguments` が `--restart unless-stopped` を付与してコンテナ起動（`wslc run`）を呼び出します。
しかし、`wslc run` コマンドは `--restart` オプションをサポートしていないため、「`オプション名が現在のコマンドに対して認識されませんでした: '--restart'`」というエラーが発生してコンテナ起動が失敗します。

この問題を解決するため、コンテナ起動引数構築時にランタイム種別を判定し、`wslc` の場合には `--restart` オプションを除外して起動できるように修正します。

## What Changes

- **`scripts/common.ps1`**:
  - `New-RunArguments` に `[string]$Runtime = ""` パラメータを追加。
  - `$Runtime -ne "wslc"` の場合のみ `--restart unless-stopped` を追加するよう修正。
- **`scripts/deploy.ps1`**:
  - `New-RunArguments` 呼び出し時に `-Runtime $runtime` を渡すように更新。
- **`test/TagBasedVideoManager.Renamer.Tests/DeployScriptTests.fs`**:
  - `UT-RUN-ARGS-WSLC: wslc 指定時は --restart オプションを含めない` テストを追加。
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
