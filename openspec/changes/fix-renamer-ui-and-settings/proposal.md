# Proposal

## Why

Renamerツール（Avaloniaデスクトップアプリ）において、以下の3つの運用上およびUI/UX上の課題が存在します：

1. 開発環境のAPIキー等の機密情報が含まれうる `appsettings.Development.json` がGit管理対象に含まれており、誤コミットや情報漏洩のリスクがある。
2. 対比確認リスト（`comparisonList`）において、最下部までスクロールした際に最後の候補カードの下部（AI COMMENT枠やカード枠線）が見切れてしまい、視認性が損なわれている。
3. AIモデル選択ドロップダウンにハードコードされたモデル群（`standardModels`）が含まれており、ユーザーが設定ファイル（`appsettings.json`）で明示的に指定したモデル以外の不要な選択肢が混入している。

これらを解消し、設定ファイルの安全な分離、対比リストの快適なスクロール視認性、および設定ファイルに忠実なモデル表示を実現します。

## What Changes

- **Git除外設定**: `.gitignore` に `**/appsettings.Development.json` を追加し、Gitの追跡（tracked）から安全に除外（`git rm --cached`）します。
- **リスト最下部スクロールの改善**: `Views.fs` の `comparisonList` 内 `ScrollViewer` / `StackPanel` の末尾に十分な下部マージン（約 36px）を確保し、最下部スクロール時に最後のカード全体（AI COMMENT枠・カード下枠線）が完全に見えるようにします。
- **AIモデルのハードコーディング撤廃（案A）**: `Views.fs` の `standardModels`（llama-3.3, gemini-2.0, mistral-small, nemotron-3-ultra）の固定定義を撤廃し、設定ファイル（`appsettings.json` / `appsettings.Development.json` 等）で指定された `SelectedModel` のみをドロップダウンに表示・保持します。

## Capabilities

### Modified Capabilities

- `companion-file-rename`:
  - AIモデルドロップダウンにおけるハードコードモデルの全廃および設定値モデル単一表示への改定。
  - 対比確認リスト最下部における完全スクロール視認性および下部余白の保証。
  - 開発環境設定ファイル（`appsettings.Development.json`）のGit除外および設定マージ動作の保証。

## Non-goals

- 設定ファイルに複数の利用可能モデル一覧（`availableModels` 配列）を拡張すること（今回は案Aを採用し、設定値単一モデルのみを使用）。
- Renamerのコアロジック（FileScanner, OpenRouterClient, DockerController, WebSearchClient等）の仕様変更。

## Impact

- 影響コード:
  - `.gitignore`
  - `src/TagBasedVideoManager.Renamer/appsettings.Development.json`（Git管理除外）
  - `src/TagBasedVideoManager.Renamer/Views.fs`（AIモデル選択ComboBoxの定義、comparisonList ScrollViewer / StackPanel のレイアウト）
- 依存関係・システム:
  - 外部ライブラリの追加・変更はなし。
  - 既存のテストスイートおよびビルド・実行への悪影響なし。
