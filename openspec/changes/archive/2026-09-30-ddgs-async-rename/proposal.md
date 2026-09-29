# Proposal

## Why

長パス動画ファイルのリネームにおいて、暗号的な略称や断片的なファイル名の場合、ファイル名文字列のみからLLMが正式な作品名・エピソードを推測するのは困難でした。また、従来の全件一括AI問い合わせでは、提案が返るまで画面全体が待たされ、ユーザーの体感待機時間が長いという課題がありました。

本変更では、Pythonライブラリ `ddgs` (DuckDuckGo Search) を活用したWeb検索結果をLLMのコンテキストに付与できるようにし、さらにスキャン直後の即時IO描画とファイルごとの非同期逐次描画（行アニメーション付き）、3行常時表示、文字数抵触時の視覚的警告を導入することで、提案精度とUI応答性を飛躍的に向上させます。

## What Changes

- **Web検索連携（`ddgs`）**:
  - Pythonスクリプト `scripts/ddgs_search.py` を呼び出し、DuckDuckGo検索結果（タイトル・スニペット）を取得してLLMプロンプトに注入する仕組みの追加。
  - 命名規則（`NamingRule`）ごとにWeb検索を使用するかどうかの設定トグルを追加。
- **即時IO描画と非同期逐次更新（行アニメーション）**:
  - スキャン完了時に対象ファイルを即座にUIへ一覧描画（1回目の画面反映）。
  - 各ファイル単位で制御された並行キューを用いて非同期にWeb検索およびLLM問い合わせを実行。
  - AI提案処理中の行にはインジケーター等のアニメーションを付与し、完了した行から即座に新ファイル名とAIコメントを逐次反映。
- **3行常時表示レイアウト（BREAKING）**:
  - 「旧ファイル名 (BEFORE)」「新ファイル名 (AFTER)」「AIコメント (AI COMMENT)」の3行を全候補カードで常時表示。従来の「例外時のみ表示」から刷新。
- **AFTER行の背景色と文字数抵触警告**:
  - 新ファイル名（AFTER）行は緑色を基調とし、パス文字数が抽出基準文字数（閾値）に抵触する場合は、左側の「AFTER (〇〇字 [危険])」ラベルバッジの背景色のみを赤色に変更して直感的に警告。

## Non-goals（やらないこと）

- Pythonランタイムおよび `ddgs` ライブラリの自動サイレントインストーラーの作成（Python未検出・ライブラリ未導入時はWeb検索を自動スキップし、AIコメントにスキップ案内を表示する）。
- GoogleやBingなど DuckDuckGo 以外の検索エンジンAPIのサポート。
- Avaloniaデスクトップアプリ以外のWeb版への同機能の同時展開。

## Capabilities

### Modified Capabilities

- `companion-file-rename`: Web検索（`ddgs`）コンテキスト注入、スキャン直後の即時IO描画、ファイルごとの非同期逐次描画と行アニメーション、旧ファイル名・新ファイル名・AIコメントの3行常時表示、および文字数抵触時のAFTERラベル背景色赤化警告の要件を追加・更新。

## Impact

- **対象コード**:
  - `src/TagBasedVideoManager.Renamer/Domain.fs`: 命名規則・処理中ステータス等の型拡張
  - `src/TagBasedVideoManager.Renamer/OpenRouterClient.fs`: 単一ファイル用プロンプト生成およびWeb検索結果注入
  - `src/TagBasedVideoManager.Renamer/State.fs`: ファイル単位の非同期Elmishコマンド・逐次反映処理
  - `src/TagBasedVideoManager.Renamer/Views.fs`: 3行常時表示、行アニメーション描画、AFTERラベル背景色切り替え
  - `scripts/ddgs_search.py`: DuckDuckGo検索を実行する新規Pythonスクリプト
- **外部依存**: Python 3.x, `ddgs` (`pip install ddgs`)
- **テスト**: `test/TagBasedVideoManager.Renamer.Tests/` に単体テストおよび回帰テストを追加
