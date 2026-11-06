# Proposal: multi-video-dirs

- **適用規模レベル**: `L3`（環境変数の廃止・置換を伴う破壊的変更、Docker 構成変更、UI エラー表示改修）
- **変更種別**: `feat`
- **関連 Issue**: TakefumiSugeno/TagBasedVideoManager#22

## Why

現在、スキャン対象は `VIDEO_DIR` の単一フォルダのみである。複数の物理フォルダを見るため、親フォルダ配下にホスト OS 側でシンボリックリンク／ジャンクションを作る運用をしている。しかし Docker のバインドマウントではリンク先の実体がコンテナ内に存在せず、リンク配下の動画をアプリから見られない。リンク運用をやめ、実フォルダを直接複数指定できるようにする。

また、従来はスキャン失敗時に UI 側で固定メッセージ「スキャン同期に失敗しました。」しか表示されず、設定不備（`VIDEO_DIRS` 未設定や旧 `VIDEO_DIR` 残存など）が UI から判別不能だったため、設定不備の詳細と対処法をトースト通知に表示して利用者が即座に対処できるようにする。

## What Changes

- スキャン対象を環境変数 `VIDEO_DIRS`（`;` 区切りの複数パス）で指定する。要素 1 件なら従来の単一フォルダ指定と同じ動作とする。
- **BREAKING**: Web アプリ本体は `VIDEO_DIR` を読まなくなる。`VIDEO_DIRS` へのフォールバックは持たない。`.env` / Dockerfile / デプロイスクリプトを `VIDEO_DIRS` に置換する。
- 各ルートフォルダ名（最終要素名）と、その配下のサブフォルダ名を、動画のタグとして自動付与する（同名は統合。包含関係にある場合は全所属ルートのタグを付与）。ドライブ直下（`D:\`）やルート（`/`）などフォルダ名を持たない要素はローカル・デプロイスクリプト双方で明示的に拒否する。
- 既存動画の再スキャン時も、パス正規化（大文字小文字の差異吸収・物理パス解決）により同一動画の二重登録を防止しつつ、不足しているルート名タグを安全に追加付与する。
- スキャン失敗時、API は具体的な原因と設定例を含むエラーメッセージを返却し、フロントエンド（`app.js`）はトースト通知にその詳細内容を表示する（`openspec/changes/multi-video-dirs/mockup.html` 準拠）。
- Docker 実行時、デプロイスクリプト（`deploy.ps1`, `deploy.sh`）はホストの各物理フォルダを `/app/videos/<フォルダ名>` に読み取り専用でマウントし、コンテナには `VIDEO_DIRS=/app/videos/<名1>;/app/videos/<名2>` を注入する。
- デプロイスクリプトの旧引数（`-VideoDir`）および既定値（`/mnt/videos`, `C:\Users\Public\Videos`）を廃止し、`-VideoDirs` / `VIDEO_DIRS` の明示指定を必須化する。検証はイメージビルドやコンテナ停止よりも前に実施し、不備があれば即座に安全停止する。
- `deploy.sh` では `.env` を単純な `source` ではなく安全にパースし、セミコロン（`;`）によるコマンド誤実行やエスケープ消失を防止する。
- Renamer（companion）は対象外。ホスト OS 専用でリンクを解決できるため、`VIDEO_DIR` のまま変更しない（`.env` には `VIDEO_DIR`＝Renamer 用、`VIDEO_DIRS`＝本体用の 2 変数が併存する）。

## Non-goals

- Renamer の複数フォルダ対応・変数統一。
- ルートごとの別名指定（`名前=パス` 形式）。
- Docker でのシンボリックリンク／ジャンクション解決。
- 既存 DB の `FilePath` の自動移行（Docker で再登録となる場合の注意喚起は行うが、DB の書き換えは行わない）。

## Capabilities

### New Capabilities

なし

### Modified Capabilities

- `video-scanner`: 走査対象が単一 `VIDEO_DIR` から複数 `VIDEO_DIRS` に変わる。ルート名タグ付与、未設定・旧変数残存時の設定例付きエラー、存在しないルートのスキップ、パス正規化による二重登録防止、および UI でのエラー詳細トースト表示を規定する。
- `docker-infrastructure`: 単一 `/app/videos` マウントと `VIDEO_DIR` 注入が、`/app/videos/<名前>` の複数マウントと `VIDEO_DIRS` 注入に変わる。
- `container-deployment-scripts`: デプロイスクリプトが `VIDEO_DIRS` を安全に解釈して複数マウントを生成し、旧引数・既定値の廃止、ビルド・停止前の厳格な入力検証（未設定、旧変数残存、同名、空要素、ドライブ直下）を行う。

## Impact

- バックエンド: `src/TagBasedVideoManager/Scanner.fs`, `HttpHandlers.fs`（スキャンエラー応答の詳細化、複数ルート走査、パス正規化）
- フロントエンド: `src/TagBasedVideoManager/wwwroot/app.js`（`triggerScan` での 500 エラー詳細取得・`showToast` での詳細表示）
- コンテナ・スクリプト: `Dockerfile`, `scripts/deploy.sh`, `scripts/deploy.ps1`, `scripts/common.ps1`
- テスト: `ScannerTests.fs`, `DeployScriptTests.fs`, E2E テスト
- ドキュメント: `README.md`, `AGENTS.md` の必須環境変数, `.env` 例
- 利用者影響: 既存の `.env` は `VIDEO_DIRS` を追加しないと本体が動かない（移行手順と設定例をエラー画面および README に提示）。
