# container-deployment-scripts Delta Specification

## MODIFIED Requirements

### Requirement: VIDEO_DIRS のマウント変換と入力検証

デプロイスクリプト（`scripts/deploy.ps1`, `scripts/deploy.sh`）は、`.env` ファイルを読み込んではならず（SHALL NOT）、設定値の解決に `src/TagBasedVideoManager/appsettings.json` および環境別ファイル（`src/TagBasedVideoManager/appsettings.{Environment}.json`、既定環境: `Production`、`-Environment` パラメータまたは `ASPNETCORE_ENVIRONMENT` で切替可能）を使用しなければならない（SHALL）。解決優先順位は、**CLI引数 ＞ シェル環境変数 ＞ `appsettings.{Environment}.json` ＞ `appsettings.json` ＞ 既定値**としなければならない（SHALL）。
スクリプトは、解決されたホストの `VIDEO_DIRS`（`;` 区切り）を解釈し、各物理フォルダを `/app/videos/<フォルダ名>` へ読み取り専用でマウントする引数を生成し、コンテナには対応する `VIDEO_DIRS` を注入しなければならない（SHALL）。Windows 環境（`scripts/deploy.ps1`）において `wslc` または Windows コンテナ CLI へ渡すバインドマウントのホスト側パスは、Windows ドライブパスについてはスラッシュ区切りの絶対パス（例: `D:/X`）、WSL3 内パスについては正規化済み WSL UNC パス（例: `\\wsl.localhost\<Distro>\<Path>`。Linux 絶対パス `/...` が入力された場合は既定 WSL ディストロの UNC パスへ解決）としなければならず（SHALL）、空白を含むパス（例: `D:\My Videos\Clips`）も分割や破壊をせず正しく保持し、`/mnt/<drive>/...` 変換によって Windows カレントドライブ配下に空フォルダを誤生成してはならない（SHALL NOT）。
スクリプトは旧引数（`-VideoDir`）および暗黙の既定動画パス（`C:\Users\Public\Videos`, `/mnt/videos`）を廃止し、有効な `VIDEO_DIRS` の指定を必須としなければならない（SHALL）。また、既存ディレクトリではなく通常ファイルを誤指定した場合やパスセグメントに `..` を含む場合、および Linux 環境（`scripts/deploy.sh`）で Windows ドライブパス（`D:\...` 等）や Linux から解決できない WSL UNC パスが指定された場合も含め、検証処理はすべてコンテナイメージのビルドおよび既存コンテナの停止・削除処理よりも前に実行し、不備がある場合は正しい設定例を案内した上で既存環境を変更せず非 0 終了しなければならない（SHALL）。

#### Scenario: 複数フォルダの変換

- **WHEN** `VIDEO_DIRS=D:\X;F:\Y`（または `appsettings.Production.json` に同様の設定）で `scripts/deploy.ps1` が実行されたとき
- **THEN** 各フォルダが `D:/X:/app/videos/X:ro` および `F:/Y:/app/videos/Y:ro` にマウントされ、`-e VIDEO_DIRS=/app/videos/X;/app/videos/Y` が注入されること

#### Scenario: 単一フォルダ指定

- **WHEN** `VIDEO_DIRS=D:\X` の 1 件のみでデプロイスクリプトが実行されたとき
- **THEN** `/app/videos/X` への 1 件のマウントとなり、正常に起動すること

#### Scenario: WSL UNC パス・Linux パスの VIDEO_DIRS 指定（Windows）

- **WHEN** Windows 環境の `scripts/deploy.ps1` に `VIDEO_DIRS=\\wsl.localhost\Ubuntu-24.04\home\user\videos` または `VIDEO_DIRS=/home/user/videos`（既定ディストロが解決可能な環境）が指定されたとき
- **THEN** WSL 側のディレクトリ存在確認が行われ、正規化された `\\wsl.localhost\Ubuntu-24.04\home\user\videos:/app/videos/videos:ro` がマウント引数として生成されること

#### Scenario: Linux 環境での Windows パス・変換不能 UNC パスの拒否（deploy.sh）

- **WHEN** Linux 環境の `scripts/deploy.sh` に `VIDEO_DIRS=D:\X` または解決不能な `\\wsl.localhost\OtherDistro\videos` が指定されたとき
- **THEN** ビルドや既存コンテナの停止を行わずに明確なエラーメッセージを表示して非 0 終了すること

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

#### Scenario: 通常ファイルなどディレクトリ以外の誤指定の拒否

- **WHEN** `VIDEO_DIRS` にディレクトリではなく既存の通常ファイル（例: `D:\Videos\sample.mp4`）が含まれるとき
- **THEN** 該当パスと正しいディレクトリ設定例を示すエラーで非 0 終了し、ビルドやコンテナの変更を行わないこと

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

---

## ADDED Requirements

### Requirement: DATA_DIR のクロスプラットフォームパス解決と WSL3 内永続化対応

デプロイスクリプト（`scripts/deploy.ps1`, `scripts/deploy.sh`）は、`DATA_DIR`（未設定・空白時は `./data`）を `/app/data` へ永続化マウントできる形式へ早期検証・解決・事前作成しなければならない（SHALL）。`deploy.ps1` は相対/Windows パスを `D:/...`、WSL UNC・Linux パスを正規化 `\\wsl.localhost\<Distro>\<subpath>` へ解決し（Windows 直下への作成禁止 SHALL NOT）、`deploy.sh` は相対パスを `${REPO_ROOT}` 基準絶対パス、`WSL_DISTRO_NAME` 一致時の UNC のみ Linux パスへ解決する。通常ファイル、ドライブ直下/相対、セグメント `..`、不正 UNC 等はビルド・停止前に拒否する（SHALL）。

#### Scenario: Windows 相対パス・絶対パス・空白入りパスの DATA_DIR 解決と事前作成

- **WHEN** `DATA_DIR="./data"`、`DATA_DIR=""`（空白含む・既定フォールバック）、`DATA_DIR="D:\tbvm\data"`、または `DATA_DIR="D:\My Data\tbvm"` で `scripts/deploy.ps1` を実行したとき
- **THEN** リポジトリルート基準の絶対パス上でディレクトリが存在確認・作成され、コンテナ起動引数にはスラッシュ区切りの `-v D:/.../data:/app/data`（空白を保持）が渡されること

#### Scenario: Windows 環境での WSL UNC パス DATA_DIR の指定とディレクトリ作成

- **WHEN** `DATA_DIR="\\wsl.localhost\Ubuntu-24.04\home\user\tbvm\data"` または `\\wsl$\Ubuntu-24.04\home\user\tbvm\data` で `scripts/deploy.ps1` を実行したとき
- **THEN** `//wsl.localhost/...` に破壊されず `\\wsl.localhost\Ubuntu-24.04\home\user\tbvm\data` へ正規化されて WSL 側にディレクトリ作成・存在確認が行われ、`-v \\wsl.localhost\Ubuntu-24.04\home\user\tbvm\data:/app/data` としてマウントされること

#### Scenario: Windows 環境での Linux 絶対パス DATA_DIR の既定 WSL ディストロ解決

- **WHEN** `DATA_DIR="/home/user/tbvm/data"` で `scripts/deploy.ps1` を実行したとき
- **THEN** `docker-desktop` 等のシステムディストロを除いた既定ユーザー WSL ディストロ（例: `Ubuntu-24.04`）が取得されて `\\wsl.localhost\Ubuntu-24.04\home\user\tbvm\data` へ解決され、Windows カレントドライブ直下（`D:\home\...`）は作成されず、WSL 側にディレクトリが用意されること。WSL 未導入・ディストロ 0 件・タイムアウト等で取得できない場合は `\\wsl.localhost\<Distro名>\...` 明示指定等の回復手順を案内してビルド・コンテナ停止前に非 0 終了すること

#### Scenario: 不正な DATA_DIR（不正 UNC・ドライブ直下・ドライブ相対・通常ファイル・パストラバーサル）の早期拒否

- **WHEN** `DATA_DIR` に `\\wsl.localhost\`（ディストロ名なし）、`\\wsl.localhost\Ubuntu-24.04`（サブパスなし）、`\\fileserver\share\data`、`D:\` / `D:`（ドライブ直下）、`D:data`（ドライブ相対）、`/` 単体、`..` セグメントを含むパス、または既存の通常ファイル（例: `./data/metadata.db`）が指定されたとき
- **THEN** ビルドや既存コンテナ停止を実行せず、代替の正しい指定例を含む明確なエラーメッセージで即座に非 0 終了すること。なお `D:\v1..v2\data` のようにディレクトリ名の一部に `..` 文字を含むだけの正当なパスは誤拒否されないこと

#### Scenario: Linux (deploy.sh) における DATA_DIR 相対パスのリポジトリルート基準絶対パス正規化

- **WHEN** `DATA_DIR="./data"` が設定された状態で、リポジトリルート以外のカレントディレクトリから `scripts/deploy.sh` を実行したとき
- **THEN** カレントディレクトリではなく `${REPO_ROOT}/data` の絶対パスとしてディレクトリ作成および `-v ${REPO_ROOT}/data:/app/data` マウントが行われること

#### Scenario: Linux (deploy.sh) における WSL UNC パス・Windows ドライブパスの扱い

- **WHEN** `scripts/deploy.sh` に `DATA_DIR="\\wsl.localhost\Ubuntu-24.04\home\user\data"` が指定されたとき（`WSL_DISTRO_NAME` が一致する WSL である場合 vs `WSL_DISTRO_NAME` 未設定・別ディストロである場合）、または `DATA_DIR="D:\data"` が指定されたとき
- **THEN** `WSL_DISTRO_NAME` が一致する WSL 環境上の `\\wsl.localhost` / `\\wsl$` パスに限り `/home/user/data` へ解決され、それ以外の環境（`$WSL_DISTRO_NAME` 未設定のネイティブ Linux・別ディストロ・Windows ドライブパス）ではビルド・停止前に、正しい Linux パス設定例を示して非 0 でエラー終了すること

---

### Requirement: 既存コンテナの /app/data マウント元変更時の警告通知

既存コンテナ `tag-based-video-manager` が存在し、ランタイムの `inspect` から取得した `/app/data` マウント元と今回の解決済み `DATA_DIR` が（環境差吸収比較後に）異なる場合、ビルド・コンテナ停止前に、旧/新パス・旧データ非削除の明記・`DATA_DIR` 設定復帰例・移行コピー手順（`Copy-Item` / `cp -a`）の 4 要素を含む警告を出力しなければならない（SHALL）。自動コピーや対話停止は行わず非対話で続行する。

#### Scenario: 既存コンテナと異なる DATA_DIR 指定時の警告表示とデータ移行案内

- **WHEN** 既存コンテナ `tag-based-video-manager` が `/app/data` に `\\wsl.localhost\Ubuntu-24.04\home\user\old_data` をマウントしている状態で、新しい `DATA_DIR="./data"` を指定してデプロイスクリプトを実行したとき
- **THEN** ビルドおよびコンテナの停止・削除の前に、旧マウント元パスと新マウント先パス、旧データは削除されていない旨、設定復帰例、および具体的なコピーコマンド例を含む警告メッセージが出力され、その後デプロイが続行されること

#### Scenario: 既存コンテナと同じ DATA_DIR 指定時は警告を出さない

- **WHEN** 既存コンテナ `tag-based-video-manager` の `/app/data` マウント元と今回解決された `DATA_DIR` が実質的に同一パス（`/` と `\` や大文字小文字の違いのみ等）であるとき
- **THEN** マウント元変更の警告メッセージは出力されず、そのままコンテナ更新が続行されること
