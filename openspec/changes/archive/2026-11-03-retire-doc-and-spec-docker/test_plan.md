# テスト計画・テストケース定義書 (Test Plan): retire-doc-and-spec-docker

- **変更名**: `retire-doc-and-spec-docker`
- **適用規模レベル**: `L2`
- **変更種別**: `chore` / `docs`

---

## 1. カバレッジ目標と完了基準

| 指標                 | 定義                                                     | 目標値        | 適用対象・備考                             |
| :------------------- | :------------------------------------------------------- | :------------ | :----------------------------------------- |
| **Scenario 網羅率**  | `docker-infrastructure` の全 Scenario に対する検証対応率 | **100%**      | Docker構成・マウント・環境変数の整合性検証 |
| **全体テスト成功率** | 既存テストスイートの実行結果                             | **100% Pass** | 全66件のテストリグレッションなし           |
| **ファイル存在検証** | `assets/images/` 画像存在および `doc/` 削除確認          | **100%**      | リンク切れ・未削除ファイルの不在           |

---

## 2. シナリオ・トレーサビリティマトリクス

| Spec Capability / Requirement                  | Scenario 名                                 |    検証種別    | 対応検証コマンド / ファイル                         |
| :--------------------------------------------- | :------------------------------------------ | :------------: | :-------------------------------------------------- |
| `docker-infrastructure` / マルチステージビルド | コンテナイメージのビルド                    | 構文・定義検証 | `Dockerfile` ステージ定義検証                       |
| `docker-infrastructure` / 読み取り専用マウント | 読み取り専用ボリュームの保護                | 構文・定義検証 | `docker-compose.yml` (`/app/videos:ro`)             |
| `docker-infrastructure` / ボリューム永続化     | コンテナ再起動後のデータ永続性              | 構文・定義検証 | `docker-compose.yml` (`./data:/app/data`)           |
| `docker-infrastructure` / CPU・GPU支援切り替え | CPUのみの既定実行 (デフォルト)              | 構文・定義検証 | `docker-compose.yml` (既定コメントアウト)           |
| `docker-infrastructure` / CPU・GPU支援切り替え | AMD GPU (VAAPI/ROCm) によるハードウェア支援 | 構文・定義検証 | `docker-compose.yml` (`/dev/dri`)                   |
| `docker-infrastructure` / CPU・GPU支援切り替え | NVIDIA GPU (CUDA) によるハードウェア支援    | 構文・定義検証 | `docker-compose.yml` (`capabilities: [gpu, video]`) |
| `docker-infrastructure` / ポート・環境変数     | 環境変数の注入と疎通確認                    | 構文・定義検証 | `docker-compose.yml` (`5620:5620`, ENV定義)         |

---

## 3. ドキュメント・アセット移行検証ケース

| ケースID | 検証対象            | 検証手順                                              | 期待される結果                           |
| :------- | :------------------ | :---------------------------------------------------- | :--------------------------------------- |
| `DOC-01` | `assets/images/`    | 画像ファイル（screenshot_*.png）の移動確認            | 4枚の画像ファイルが正常に存在すること    |
| `DOC-02` | `README.md`         | 画像リンク（`./assets/images/...`）の確認             | リンク切れがなくプレビュー可能であること |
| `DOC-03` | `doc/` ディレクトリ | `Test-Path doc` による存在確認                        | ディレクトリが存在しない（削除済み）こと |
| `DOC-04` | 全テストスイート    | `dotnet exec test/.../TagBasedVideoManager.Tests.dll` | 全66件がPassすること                     |
