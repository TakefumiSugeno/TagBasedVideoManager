# docker-infrastructure Specification Delta

## MODIFIED Requirements

### Requirement: ホスト動画ディレクトリの読み取り専用マウント

システムは、WSL 3.0 (`wslc`) または Docker による単一コンテナ実行時において、ホスト（Windows / WSL3 / Linux）上の動画ディレクトリをコンテナ内の `/app/videos` に読み取り専用（`:ro`）でバインドマウントし、Webアプリ側からのファイル改変や誤削除を防止しなければならない（SHALL）。

#### Scenario: 読み取り専用ボリュームの保護

- **WHEN** コンテナが起動して `/app/videos` にホスト動画フォルダがマウントされたとき
- **THEN** コンテナ内から動画ファイルの読み取りおよびメタデータ抽出が可能であり、かつコンテナからの書き込み・削除操作が拒否されること

### Requirement: アプリケーションデータとサムネイルのボリューム永続化

システムは、SQLite データベース（`metadata.db`）および生成されたサムネイル画像ディレクトリ（`thumbnails`）をホスト側の永続ディレクトリ（既定: `./data`）にバインドマウント（`./data:/app/data`）し、デプロイスクリプト等によるコンテナの破棄・再作成を行っても登録済みタグ情報やサムネイルが喪失しないよう永続化しなければならない（SHALL）。

#### Scenario: コンテナ再起動後のデータ永続性

- **WHEN** デプロイスクリプト等により既存コンテナが停止・削除された後に新コンテナが起動されたとき
- **THEN** 以前に登録された動画メタデータ、タグ情報、および生成済みサムネイル画像がそのまま維持・復元されること

### Requirement: CPUおよびマルチGPU（AMD/NVIDIA）アクセラレーションの切り替え

システムは、サムネイル生成時の FFmpeg デコード処理において、ホスト環境に応じた GPU アクセラレーションを適用できなければならない（SHALL）。WSL 3.0 (`wslc`) または Docker 環境下での NVIDIA GPU 利用時は `--gpus all` フラグにより CUDA アクセラレーションを有効化する。WSL 3.0 環境における AMD GPU 利用時は `--device` 非対応の制約により CPU フォールバックで安全に動作させ、Linux ホスト上の Docker 環境に限り `/dev/dri` デバイスパススルーを適用する。GPU が存在しない環境または非対応環境では自動的に CPU ソフトウェアデコードでフォールバック動作しなければならない（SHALL）。

#### Scenario: CPUのみの既定実行 (デフォルト)

- **WHEN** GPU フラグを指定せず、または対応 GPU がない環境（WSL 3.0 での AMD GPU 環境含む）でコンテナを起動したとき
- **THEN** エラーなく標準の CPU ソフトウェアデコードによりサムネイル生成が正常に完了すること

#### Scenario: AMD GPU (VAAPI/ROCm) によるハードウェア支援

- **WHEN** Linux ホスト環境上の Docker で `/dev/dri` デバイスパススルーが有効化されて起動されたとき
- **THEN** コンテナ内の FFmpeg がホストの AMD GPU レンダリングノードを利用して高速デコードを実行できること

#### Scenario: NVIDIA GPU (CUDA) によるハードウェア支援

- **WHEN** WSL 3.0 (`wslc`) または Docker で `--gpus all` が指定されてコンテナが起動されたとき
- **THEN** コンテナ内の FFmpeg がホストの NVIDIA CUDA コアを利用して高速デコードを実行できること
