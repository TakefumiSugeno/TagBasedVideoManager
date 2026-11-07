## MODIFIED Requirements

### Requirement: ホスト動画ディレクトリの読み取り専用マウント

システムは、WSL 3.0 (`wslc`) または Docker による単一コンテナ実行時において、`VIDEO_DIRS` で指定されたホスト（Windows / WSL3 / Linux）上の各物理動画ディレクトリを、コンテナ内の `/app/videos/<フォルダ名>` に読み取り専用（`:ro`）でバインドマウントし、Webアプリ側からのファイル改変や誤削除を防止しなければならない（SHALL）。

#### Scenario: 読み取り専用ボリュームの保護

- **WHEN** コンテナが起動して `/app/videos/<フォルダ名>` に各ホスト動画フォルダがマウントされたとき
- **THEN** コンテナ内から動画ファイルの読み取りおよびメタデータ抽出が可能であり、かつコンテナからの書き込み・削除操作が拒否されること

#### Scenario: 複数物理フォルダのマウント

- **WHEN** `VIDEO_DIRS=D:\X;F:\Y` でコンテナが起動したとき
- **THEN** `D:\X` が `/app/videos/X`、`F:\Y` が `/app/videos/Y` にそれぞれ読み取り専用でマウントされ、両配下の動画がアプリから参照できること

### Requirement: ポート公開と環境変数注入

システムは、Kestrel Web サーバーのポート（既定: 5620）をホスト側にマッピング（`5620:5620`）し、必須環境変数（`PORT=5620`, `VIDEO_DIRS=/app/videos/<名1>;/app/videos/<名2>;...`, `THUMBNAIL_DIR=/app/data/thumbnails`, `DATABASE_PATH=/app/data/metadata.db`）をコンテナ起動時に確実に注入しなければならない（SHALL）。`VIDEO_DIRS` はマウント先のコンテナ内パスを `;` 区切りで列挙しなければならない（SHALL）。

#### Scenario: 環境変数の注入と疎通確認

- **WHEN** デプロイスクリプト（`scripts/deploy.ps1` / `scripts/deploy.sh`）または単一コンテナ CLI の `run` コマンドによりコンテナが起動したとき
- **THEN** ポート 5620 で HTTP サーバーが待機し、指定されたコンテナ内パスで DB や動画ディレクトリが初期化されること

#### Scenario: コンテナ内 VIDEO_DIRS の整合

- **WHEN** ホスト `VIDEO_DIRS=D:\X;F:\Y` でコンテナが起動したとき
- **THEN** コンテナ内の環境変数 `VIDEO_DIRS` は `/app/videos/X;/app/videos/Y` となり、マウント先と一致すること
