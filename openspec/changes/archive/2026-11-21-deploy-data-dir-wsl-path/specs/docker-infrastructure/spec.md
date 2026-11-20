# docker-infrastructure Delta Specification

## MODIFIED Requirements

### Requirement: ホスト動画ディレクトリの読み取り専用マウント

システムは、WSL 3.0 (`wslc`) または Docker による単一コンテナ実行時において、`VIDEO_DIRS` で指定されたホスト（Windows / WSL3 / Linux）上の各物理動画ディレクトリを、コンテナ内の `/app/videos/<フォルダ名>` に読み取り専用（`:ro`）でバインドマウントし、Webアプリ側からのファイル改変や誤削除を防止しなければならない（SHALL）。Windows 上の `wslc` / Docker CLI からマウントする際のホスト側パスは、Windows ドライブパスについてはスラッシュ区切り絶対パス（例: `D:/X`）、WSL3 内パスについては WSL UNC パス（`\\wsl.localhost\<Distro>\<Path>`）として指定されなければならない（SHALL）。

#### Scenario: 読み取り専用ボリュームの保護

- **WHEN** コンテナが起動して `/app/videos/<フォルダ名>` に各ホスト動画フォルダがマウントされたとき
- **THEN** コンテナ内から動画ファイルの読み取りおよびメタデータ抽出が可能であり、かつコンテナからの書き込み・削除操作が拒否されること

#### Scenario: 複数物理フォルダのマウント

- **WHEN** Windows ホストの `VIDEO_DIRS=D:\X;F:\Y` でコンテナが起動したとき
- **THEN** `D:/X` が `/app/videos/X`、`F:/Y` が `/app/videos/Y` にそれぞれ読み取り専用でマウントされ、両配下の動画がアプリから参照できること

### Requirement: アプリケーションデータとサムネイルのボリューム永続化

システムは、SQLite データベース（`metadata.db`）および生成されたサムネイル画像ディレクトリ（`thumbnails`）を、ホスト側の永続ディレクトリ（既定: `./data`、または `DATA_DIR` で指定した Windows 絶対パス・WSL3 ディストロ内パス `\\wsl.localhost\<Distro>\...` / `/home/...`・Linux 絶対パス）にバインドマウント（`<永続ディレクトリ>:/app/data`）し、デプロイスクリプト等によるコンテナの破棄・再作成を行っても登録済みタグ情報やサムネイルが喪失しないよう永続化しなければならない（SHALL）。

#### Scenario: コンテナ再起動後のデータ永続性

- **WHEN** デプロイスクリプト等により既存コンテナが停止・削除された後に新コンテナが起動されたとき
- **THEN** 以前に登録された動画メタデータ、タグ情報、および生成済みサムネイル画像がそのまま維持・復元されること

#### Scenario: WSL3 ディストロ内パスへのデータ永続化

- **WHEN** Windows ホストのデプロイ設定 `DATA_DIR` に WSL3 内パス（`\\wsl.localhost\Ubuntu-24.04\home\user\tbvm\data` または `/home/user/tbvm/data`）を指定してコンテナが起動されたとき
- **THEN** WSL3 ディストロ内の該当ディレクトリが `/app/data` に読み書き可能でバインドマウントされ、`metadata.db` と `thumbnails/` が WSL3 内に永続化されること
