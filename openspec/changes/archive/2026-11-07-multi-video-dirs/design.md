## Context

現行は `VIDEO_DIR` 単一フォルダを `Scanner.syncVideoDirectory videoDir ...` が再帰走査する。`enumerateFiles` は `PathHelper.resolvePhysicalPath` でリンクを解決して追跡するが、Docker ではリンク先の実体がコンテナ内に無いため走査できない（Issue #22）。物理フォルダを個別にマウントし、全てをスキャン対象にする。

また、従来はスキャン失敗時に UI 側で固定文「スキャン同期に失敗しました。」しかトースト表示されず、設定不備（`VIDEO_DIRS` 未設定や旧 `VIDEO_DIR` 残存など）が把握できなかったため、エラー詳細と対処法をトーストに表示する（ユーザー回答 A1）。

```
 host .env : VIDEO_DIRS=D:\X;F:\Y            VIDEO_DIR=E:\Videos(Renamer用・不変)
        |
        +-- local run : 本体が VIDEO_DIRS をそのまま走査
        |               UI: スキャン失敗時に設定例付きエラーをトースト表示
        |
        +-- deploy script : 早期検証（未設定/旧変数/重複/ドライブ直下/不在）-> build -> stop/rm -> run
                            -v /mnt/d/X:/app/videos/X:ro -v /mnt/f/Y:/app/videos/Y:ro
                            -e VIDEO_DIRS=/app/videos/X;/app/videos/Y
                                 |
                                 v
                  Scanner: ルートごとに enumerate -> パス正規化・重複除去 -> 登録
                  タグ: leaf(root) + 相対サブフォルダ名（入れ子ルートは全所属ルートの和集合）
```

## Goals / Non-Goals

**Goals**

- `VIDEO_DIRS` による複数物理フォルダの走査（ローカル／Docker 共通）。
- ルート名タグ付与（包含関係時は全所属ルートの和集合）。
- パス正規化（大文字小文字・末尾区切り・物理パス解決）による二重登録防止と、既存動画への不足タグ追加。
- デプロイスクリプトでの複数マウント自動生成、安全な `.env` 読み込み、および停止前入力検証。
- UI におけるスキャン失敗詳細トースト表示（`mockup.html` 準拠）。

**Non-Goals**

- Renamer の変更（`VIDEO_DIR` 維持）、ルート別名、コンテナ内でのリンク解決、既存 DB `FilePath` の自動書き換え。

## Decisions

### D1: 設定形式と安全な環境変数読み込み

- 設定は `VIDEO_DIRS`（`;` 区切り）のみとし、本体は `VIDEO_DIR` を読まない。
- `deploy.sh` では従来の `source .env` による読み込みを廃止する。bash の `source` はセミコロンをコマンド区切りと解釈し、バックスラッシュをエスケープとして除去してしまうためである。安全なパース関数（行ごとにコメントを除去し、`key=value` を安全に抽出・クォート解除して環境変数へ設定）を実装する。

### D2: 設定解析関数 `VideoDirs.parse` と環境変数読み込みの分離

- 純粋関数 `VideoDirs.parse : string -> Result<string list, string>` を定義する。
  - 文字列の分割（`;`）、前後の空白 trim、空要素の除去を行う。
  - 有効要素が 0 件の場合はエラー。
  - 各要素について、フォルダ名（最終パス要素）が空になるもの（`D:\`, `/` など）が 1 件でも含まれていればエラーとする（ローカルとデプロイスクリプトで共通規則）。
- 環境変数読み込み関数 `VideoDirs.fromEnvironment : (string -> string option) -> Result<string list, string>` を定義し、`VIDEO_DIRS` 未設定時に `VIDEO_DIR` が設定されているかを判定して、旧変数廃止案内と設定例（`VIDEO_DIRS=D:\Anime;F:\Movie`）を含むメッセージを返却する。純粋な `parse` と環境変数解決の責務を分離する。

### D3: パス正規化、重複除去、およびタグ自動付与規則

- **パス正規化**:
  - 各ファイルパスは `Path.GetFullPath` で正規化し、末尾のディレクトリ区切り文字を除去する。
  - Windows 環境では大文字小文字を区別せずに既存動画マップ（`Map.tryFind`）と突合する（大文字小文字表記揺れによる多重登録を防止）。
  - ディレクトリだけでなくファイル自体のリパースポイント（シンボリックリンク等）も `PathHelper.resolvePhysicalPath` で解決する。
- **複数ルートと重複除去**:
  - `Scanner.syncVideoDirectory (videoDirs: string list) ...` は各ルートを走査し、正規化物理パスで重複を除去する。
  - 一部ディレクトリが存在しない（`Directory.Exists = false`）場合は、警告ログを出力してスキップし、残りのルートを継続処理する（全滅時は `Ok 0`）。
- **タグ付与規則**:
  - 動画が属するすべてのルートを判定する（親フォルダがルートに指定されている場合など）。
  - 所属するすべてのルートについて、`leaf(root)`（ルートフォルダ名）および各ルートからの相対サブフォルダ名をタグとして収集し、和集合を付与する。
  - 既存動画に対しても、不足しているタグがあれば `Db.addTagToVideo` を呼び出して安全に追記する（既に付与済みの場合は重複付与しない）。

### D4: FilePath 互換性と移行

- **ローカル**: 既存 DB の `FilePath` は解決済み物理パスであるため、`VIDEO_DIRS` に同一物理パスを指定すれば `FilePath` は不変となり、タグ・お気に入り・視聴履歴が引き継がれる。
- **Docker**: マウント先が `/app/videos/<名>` となるため、親フォルダ直下に置いていた動画等は `FilePath` が変わり再登録となる。自動書き換えは行わず、README で事前に警告する。また、`FilePath` を参照する自動タグ付けルールがある場合、パス変更により再評価される旨を明記する。

### D5: デプロイスクリプトの改修と検証順序

- `deploy.ps1` のパラメータを `$VideoDirs` に変更し、旧 `$VideoDir` および既定値（`C:\Users\Public\Videos`）を廃止。
- `deploy.sh` でも既定値（`/mnt/videos`）を廃止し、明示指定を必須化。
- 検証関数 `Resolve-VideoMounts` を作成し、イメージビルド（`build`）および既存コンテナの停止・削除（`stop`/`rm`）よりも**前に**実行する。未設定、旧変数残存、同名衝突、ドライブ直下、不在パスがあれば即座にスクリプトを非 0 終了し、稼働中のコンテナに一切手を付けない。
- Windows (WSL3/docker) では、マウント元のホストパスを `ConvertTo-WslPath` で `/mnt/...` に変換した上で `-v` 引数を組み立てる。マウント先の名前（`/app/videos/<名前>`）は、元の Windows ホストパスの最終要素名を採用する。
- **新コンテナ run 失敗時の扱い (ユーザー回答 A2)**: 検証を stop 前に行う構成を維持する。WSL 側での特殊なマウント不全等により万が一 `run` が失敗した場合、旧コンテナは削除済みとなるためサービス停止状態となる。このリスクを許容し、設計に明記する。

### D6: UI スキャン失敗エラー詳細表示 (ユーザー回答 A1)

- スキャン API（`POST /api/scan`）が 500 エラーを返却した際、エラーレスポンス本文（テキストまたは JSON の `error` フィールド）を取得する。
- `app.js` の `showToast(message, type, detail)` を拡張し、エラー時は詳細文字列をトースト内に展開表示する（表示時間も 7 秒に延長）。
- デザイン・挙動は `openspec/changes/multi-video-dirs/mockup.html` に準拠する。

## Risks / Trade-offs

- **R1 同名ルートでタグが統合される**: 異なるドライブの `D:\a\Clips` と `F:\b\Clips` は、同一のタグ `Clips` に紐づく（合意済み方針 a）。Docker ではマウント名衝突となるためデプロイスクリプトで拒否する。
- **R2 Docker での FilePath 変更と自動タグ付けルール**: Docker のマウントパス構造変更に伴い `FilePath` が変わるため、再登録となり過去のタグ・履歴がリセットされる場合がある。また `FilePath` 条件を持つ自動タグ付けルールが影響を受ける。README に移行時の注意を明記する。
- **R3 デプロイスクリプトでの run 失敗リスク**: stop 前に厳格なバリデーションを行うが、WSL 環境の突発的不整合等で run に失敗した場合はサービス停止が発生する（D5）。
- **R4 不在ルートと動画削除**: 現行スキャナは存在しない動画を削除しないため、外付けドライブの一時的な取り外し等でルートがスキップされても動画レコードは保護される（確認済み）。
- **R5 .env の 2 変数併存**: Renamer 用の `VIDEO_DIR` と本体用の `VIDEO_DIRS` が混在するため、`.env` サンプルおよび README で明記する。

## Migration Plan

1. `.env` に `VIDEO_DIRS` を追加（例: `VIDEO_DIRS=D:\Anime;F:\Movie`）。`VIDEO_DIR` は Renamer 用としてそのまま残す。
2. ローカル実行時はアプリを再起動。Docker 実行時は `scripts/deploy.ps1`（または `deploy.sh`）を実行。
3. ロールバック: 前のコミットへ戻し `VIDEO_DIR` を使用。DB スキーマ変更はないためデータ巻き戻しは不要。

## Open Questions

- なし（すべての検討事項・レビュー指摘は D1〜D6 で解決）。
