# container-deployment-scripts Delta Specification

## MODIFIED Requirements

### Requirement: VIDEO_DIRS のマウント変換と入力検証

デプロイスクリプト（`scripts/deploy.ps1`, `scripts/deploy.sh`）は、`.env` ファイルを読み込んではならず（SHALL NOT）、設定値の解決に `src/TagBasedVideoManager/appsettings.json` および環境別ファイル（`src/TagBasedVideoManager/appsettings.{Environment}.json`、既定環境: `Production`、`-Environment` パラメータまたは `ASPNETCORE_ENVIRONMENT` で切替可能）を使用しなければならない（SHALL）。解決優先順位は、**CLI引数 ＞ シェル環境変数 ＞ `appsettings.{Environment}.json` ＞ `appsettings.json` ＞ 既定値**としなければならない（SHALL）。
スクリプトは、解決されたホストの `VIDEO_DIRS`（`;` 区切り）を解釈し、各物理フォルダを `/app/videos/<フォルダ名>` へ読み取り専用でマウントする引数を生成し、コンテナには対応する `VIDEO_DIRS` を注入しなければならない（SHALL）。スクリプトは旧引数（`-VideoDir`）および暗黙の既定動画パス（`C:\Users\Public\Videos`, `/mnt/videos`）を廃止し、有効な `VIDEO_DIRS` の指定を必須としなければならない（SHALL）。また、検証処理はコンテナイメージのビルドおよび既存コンテナの停止・削除処理よりも前に実行し、不備がある場合は既存環境を変更せず非 0 終了しなければならない（SHALL）。

#### Scenario: 複数フォルダの変換

- **WHEN** `VIDEO_DIRS=D:\X;F:\Y`（または `appsettings.Production.json` に同様の設定）でデプロイスクリプトが実行されたとき
- **THEN** WSL/Linux パス変換を考慮した上で各フォルダが `/app/videos/X:ro` および `/app/videos/Y:ro` にマウントされ、`-e VIDEO_DIRS=/app/videos/X;/app/videos/Y` が注入されること

#### Scenario: 単一フォルダ指定

- **WHEN** `VIDEO_DIRS=D:\X` の 1 件のみでデプロイスクリプトが実行されたとき
- **THEN** `/app/videos/X` への 1 件のマウントとなり、正常に起動すること

#### Scenario: 未設定時の早期エラー

- **WHEN** CLI引数・環境変数・`appsettings*.json` のいずれにも `VIDEO_DIRS` が有効設定されていない状態でデプロイスクリプトが実行されたとき
- **THEN** イメージビルドやコンテナの停止・削除は実行されず、`appsettings.Production.json` への設定例等を伴うエラーメッセージを表示して即座に非 0 終了すること

#### Scenario: 旧 VIDEO_DIR 残存時の移行案内エラー

- **WHEN** 旧 `VIDEO_DIR` のみが指定され `VIDEO_DIRS` が未設定の状態でデプロイスクリプトが実行されたとき
- **THEN** 既存コンテナを停止せず、旧パラメータが廃止された旨と `VIDEO_DIRS` への移行案内を表示して非 0 終了すること

#### Scenario: フォルダ名重複の拒否

- **WHEN** `VIDEO_DIRS=D:\a\Clips;F:\b\Clips` のように最終フォルダ名が重複するとき（大文字小文字の違い含む）
- **THEN** マウント先衝突を示すエラーで非 0 終了し、ビルドや既存コンテナの停止を行わないこと

#### Scenario: 存在しないパスの拒否

- **WHEN** `VIDEO_DIRS` にホスト上に存在しないパスが含まれるとき
- **THEN** 該当パスを示すエラーで非 0 終了し、ビルドやコンテナの変更を行わないこと

#### Scenario: ドライブ直下の拒否

- **WHEN** `VIDEO_DIRS=D:\` のようにフォルダ名が得られないパスが指定されたとき
- **THEN** マウント名を決定できない旨のエラーで非 0 終了すること

#### Scenario: appsettings.json 系からの設定読み込みと .env 非依存の検証

- **WHEN** `.env` が存在しない状態で `src/TagBasedVideoManager/appsettings.Production.json` にセミコロンを含む `"VIDEO_DIRS": "/mnt/a;/mnt/b"` および `"PORT": 5630` を定義して `deploy.ps1` / `deploy.sh` を実行したとき
- **THEN** `.env` に依存せず `appsettings.json` に `appsettings.Production.json` がマージされた設定値（`/mnt/a;/mnt/b`・ポート 5630）がシェルコード展開等の誤動作なく安全に取得されること

#### Scenario: CLI引数・環境変数による appsettings 設定の上書き

- **WHEN** `appsettings.Production.json` に値が定義された状態で CLI引数（`-Port 5640` 等）または環境変数（`PORT=5640`）を指定して実行したとき
- **THEN** `appsettings.Production.json` より CLI引数・環境変数が優先され、ポート 5640 が使用されること

#### Scenario: 不正な JSON 構文またはツール不在時の安全 Fail-Fast 終了

- **WHEN** `appsettings.json` / `appsettings.Production.json` に構文エラーがある状態、または `deploy.sh` 実行時に信頼できる JSON パーサ（`python3` および `jq` の双方）が存在しない状態で実行されたとき
- **THEN** 不完全な文字列推測やビルド・旧コンテナ停止を行わず、問題のあるファイル名（または `python3` / `jq` の導入案内）とエラー内容を出力して直ちに非 0 終了すること
