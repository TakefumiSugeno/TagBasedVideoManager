# Tasks

## 1. 設定ファイル管理とGit除外

- [x] 1.1 `.gitignore` に `**/appsettings.Development.json` を追記し、`git rm --cached src/TagBasedVideoManager.Renamer/appsettings.Development.json` でGitインデックス追跡を解除して、`git status` で追跡対象外となっていることを確認する
- [x] 1.2 設定ファイル読み込みの単体テスト（`SettingsTests.fs` 等）を実行し、`appsettings.Development.json` がない環境でもベース設定（`appsettings.json`）から正常に値が読み込まれる回帰テストを確認する

## 2. AIモデルのハードコーディング撤廃（案A）

- [x] 2.1 AIモデル選択に関する仕様妥当性検証テストを作成/更新し、`standardModels` のハードコード一覧が排除され、設定値（`SelectedModel`）のみが選択肢として提供される期待値をコード化する（Test First / Red）
- [x] 2.2 `Views.fs` の `standardModels` ハードコード一覧（llama-3.3, gemini-2.0, mistral-small, nemotron-3-ultra）を削除し、`model.Settings.SelectedModel` のみを提供するよう修正してテストを通過させる（Green & Refactor）

## 3. リスト最下部スクロール余白の確保とビジュアル検証

- [ ] 3.1 `Views.fs` の `comparisonList` において、`ScrollViewer` 内の `StackPanel` に下部マージン（`margin (0.0, 0.0, 0.0, 36.0)`）を設定し、カードリスト末尾に十分なスクロール余白を確保する
- [ ] 3.2 Renamer の全単体テストおよびGUIレンダリングテストを実行し、全テストPassおよびテストエビデンス（TRX / キャプチャ）を出力して末尾カード（#4等）の完全視認性を確認する
