## ADDED Requirements

### Requirement: VIDEO_DIRS のマウント変換と入力検証

デプロイスクリプト（`scripts/deploy.ps1`, `scripts/deploy.sh`）は、ホストの `VIDEO_DIRS`（`;` 区切り）を解釈し、各物理フォルダを `/app/videos/<フォルダ名>` へ読み取り専用でマウントする引数を生成し、コンテナには対応する `VIDEO_DIRS` を注入しなければならない（SHALL）。スクリプトは旧引数（`-VideoDir`）および既定値（`C:\Users\Public\Videos`, `/mnt/videos`）を廃止し、明示的な `VIDEO_DIRS` の指定を必須としなければならない（SHALL）。また、検証処理はコンテナイメージのビルドおよび既存コンテナの停止・削除処理よりも前に実行し、不備がある場合は既存環境を変更せず非 0 終了しなければならない（SHALL）。

#### Scenario: 複数フォルダの変換

- **WHEN** `VIDEO_DIRS=D:\X;F:\Y` でデプロイスクリプトが実行されたとき
- **THEN** WSL/Linux パス変換を考慮した上で各フォルダが `/app/videos/X:ro` および `/app/videos/Y:ro` にマウントされ、`-e VIDEO_DIRS=/app/videos/X;/app/videos/Y` が注入されること

#### Scenario: 単一フォルダ指定

- **WHEN** `VIDEO_DIRS=D:\X` の 1 件のみでデプロイスクリプトが実行されたとき
- **THEN** `/app/videos/X` への 1 件のマウントとなり、正常に起動すること

#### Scenario: 未設定時の早期エラー

- **WHEN** `VIDEO_DIRS` が未設定でデプロイスクリプトが実行されたとき
- **THEN** イメージビルドやコンテナの停止・削除は実行されず、設定例を伴うエラーメッセージを表示して即座に非 0 終了すること

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

#### Scenario: deploy.sh における .env の安全な読み込み

- **WHEN** `deploy.sh` がセミコロンを含む `VIDEO_DIRS="D:\A;D:\B"` を定義した `.env` を読み込むとき
- **THEN** bash のコマンド区切りとして誤実行されることなく、値全体が正しく `VIDEO_DIRS` 変数に展開されること
