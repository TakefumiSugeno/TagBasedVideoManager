# Design

## Context

See `proposal.md` - Why.
Renamer（Avaloniaデスクトップアプリ）において、3点の改善（開発環境設定ファイルのGit除外、対比確認リストのスクロール見切れ解消、AIモデルハードコード撤廃）を実施する。

## Goals / Non-Goals

**Goals:**

- `appsettings.Development.json` を `.gitignore` に追加し、ローカルAPIキーや設定の誤コミットを恒久的に防ぐ。
- `Views.fs` の `comparisonList` において、最下部スクロール時に最後の候補カード（#4等）全体（AI COMMENT枠やカード下枠線）が完全に見えるよう、十分な下部余白（約36px）を確保する。
- `Views.fs` 内のハードコードされた `standardModels`（4モデル）を全廃し、設定ファイル（`appsettings.json` / `appsettings.Development.json`）の `SelectedModel` で指定されたモデルのみをComboBoxに表示する。

**Non-Goals:**

- 設定スキーマを拡張してモデル一覧配列を持たせること（案Aの単一モデル採用のため対象外）。
- Renamerのバックエンド（ファイル走査、DuckDuckGo検索、OpenRouterクライアント）のロジック変更。

## Decisions

### Decision 1: `.gitignore` への追加とGitキャッシュ除外

- **決定**: `.gitignore` に `**/appsettings.Development.json` を追加し、`git rm --cached src/TagBasedVideoManager.Renamer/appsettings.Development.json` を実行して追跡を解除する。
- **理由**: ローカル開発時に設定したOpenRouter APIキー等の漏洩を防止するため。
- **代替案**: `.env` のみで管理する案もあったが、Renamerは .NET アプリケーションとして `appsettings.Development.json` による開発用オーバーライドを標準サポートしているため、このファイルを安全にローカル除外するのが最も自然。

### Decision 2: 対比確認リスト最下部の余白確保と完全スクロール

- **決定**: `Views.fs` の `comparisonList` において、`ScrollViewer` のコンテンツである `StackPanel` の下部にマージン（`margin (0.0, 0.0, 0.0, 36.0)`）を設定する。
- **理由**: Avalonia 11 の ScrollViewer では、`ScrollViewer.padding` がコンテンツの ExtentHeight（スクロール終端）に正しく反映されない場合があり、下端ギリギリで切れる現象が発生する。`StackPanel` 自体の下部マージン（または末尾スペーサー）を明示的に確保することで、最下部までスクロールした際に最後のカード全体および AI COMMENT 枠が確実に画面内に収まり、さらにその下に快適な視覚的余白（約36px）が生まれる。
- **代替案**: ScrollViewer の padding 値を大きくする案も検討したが、Avalonia の仕様上 Extent に反映されず Viewport 内側でのみクリップされるリスクがあるため、内部パネル（StackPanel）のマージンで下部余白を確保するのが最も確実。

### Decision 3: AIモデルドロップダウンのハードコード全廃（案A）

- **決定**: `Views.fs` の `standardModels` 定義を削除し、ComboBox には `[ model.Settings.SelectedModel ]` のみを提供する。
  ```fsharp
  let availableModels =
      if String.IsNullOrWhiteSpace(model.Settings.SelectedModel) then []
      else [ model.Settings.SelectedModel ]
  ```
- **理由**: ユーザーの要望「ハードコーディングされているモデルは不要」に合致し、設定ファイルに記載されたモデル（例: `nvidia/nemotron-3-super-120b-a12b:free`）のみが忠実に表示され、誤って別モデルが選択される余地を排除できる。
- **代替案**: 設定ファイルに `availableModels` 配列を持たせる案（案B）もあったが、ユーザーとの事前探索（Explore）により案Aが明示的に選択されたため。

## Risks / Trade-offs

- **[Risk]** `git rm --cached` によりリモートリポジトリから `appsettings.Development.json` が消去される。
  - **Mitigation**: ベース設定 `appsettings.json` がリポジトリに残存しているため、新規クローン時やビルド時に設定ファイルが見つからないエラーは発生しない。またローカルの物理ファイルは削除されず手元に残る。
- **[Risk]** AIモデルをGUI上で切り替えることができなくなる。
  - **Mitigation**: ユーザーの要件（設定ファイル指定モデルで固定利用）に適合している。モデルを変更したい場合は `appsettings.json`（または `appsettings.Development.json`）を編集する運用とする。
