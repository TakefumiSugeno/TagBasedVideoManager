# container-deployment-scripts Specification

## Purpose

ソースコード変更後に、ユーザーまたは開発者が単一コマンドでコンテナイメージの再ビルド、旧コンテナの安全な停止・削除、新コンテナの起動、およびヘルスチェック疎通確認を一気通貫で実行できるクロスプラットフォーム自動デプロイスクリプトを提供する。

## Requirements

### Requirement: クロスプラットフォームデプロイスクリプトの提供

システムは、Windows 環境向けに PowerShell スクリプト（`scripts/deploy.ps1`）、Linux 環境向けに Bash スクリプト（`scripts/deploy.sh`）を提供し、ソースコード変更後のビルドおよびコンテナ更新を一貫した手順で実行できなければならない（SHALL）。

#### Scenario: Windows PowerShell によるデプロイ実行

- **WHEN** ユーザーが Windows PowerShell から `scripts/deploy.ps1` を実行したとき
- **THEN** コンテナイメージがビルドされ、既存コンテナが停止・再作成され、Web アプリが起動すること

#### Scenario: Linux Bash によるデプロイ実行

- **WHEN** ユーザーが Linux 環境から `scripts/deploy.sh` を実行したとき
- **THEN** Linux 上のコンテナランタイムを用いてコンテナイメージがビルドされ、正常に起動すること

### Requirement: コンテナランタイムの自動検出とガイダンス

デプロイスクリプトは、実行ホスト環境上の利用可能なコンテナ CLI を自動判別（Windows では `wslc` を優先し、なければ `docker`。Linux では `docker` または `podman`）して処理を実行しなければならない（SHALL）。いずれも検出できない場合は、解決策（Windows では `wsl --update` または Docker Desktop 導入）を含む明確なガイダンスメッセージを表示して終了しなければならない。

#### Scenario: WSL 3.0 環境での wslc 自動選択

- **WHEN** Windows ホストに `wslc.exe` がインストールされている環境でデプロイスクリプトが実行されたとき
- **THEN** `wslc` が優先検出され、`wslc build` および `wslc run` が発行されること

#### Scenario: ランタイム未導入時の具体的ガイダンス提示

- **WHEN** システム上に `wslc`, `docker`, `podman` のいずれも検出できない環境でスクリプトが実行されたとき
- **THEN** コンテナランタイムが未導入である旨と、WSL 3.0 のアップデートコマンド（`wsl --update`）または Docker 導入を促す案内を表示し、非ゼロの終了コードで中断すること

### Requirement: ポート事前衝突チェック

デプロイスクリプトは、コンテナビルドまたは起動を行う前に、ターゲットポート（既定: 5620）が別プロセスにより既に使用されていないかを事前検証しなければならない（SHALL）。使用中の場合はコンテナ停止・再作成を中止し、即座にエラー通知しなければならない。

#### Scenario: ポート使用中による事前検知と中断

- **WHEN** ホスト上の別プロセスがポート 5620 をリッスンしている状態でスクリプトが実行されたとき
- **THEN** ポート 5620 が使用中である旨のエラーメッセージを表示し、コンテナ破棄を行わずに即時終了すること

### Requirement: デプロイ後の自動ヘルスチェックと障害ログ出力

デプロイスクリプトは、新コンテナ起動後にポート 5620 への HTTP GET リクエストをポーリング（タイムアウト付き）し、Web サーバーの起動完了を検証した上で成功を通知しなければならない（SHALL）。規定秒数内に応答が得られない場合は、直近のコンテナログを出力して終了しなければならない。

#### Scenario: 正常起動完了の検証

- **WHEN** コンテナが起動し、ポート 5620 で HTTP 200/成功応答が返されたとき
- **THEN** スクリプトが成功メッセージを出力して終了コード 0 で終了すること

#### Scenario: 起動失敗時のタイムアウト検出とログ出力

- **WHEN** コンテナ内部で例外が発生してポート 5620 が応答せず規定秒数が経過したとき
- **THEN** ヘルスチェック失敗のエラーメッセージと共に直近のコンテナログ（例: `wslc logs --tail 50`）を出力して終了コード 1 で終了すること

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
