# Proposal

## Why

TagBasedVideoManagerのリネームツール（TagBasedVideoManager.Renamer）において、海外動画や英数字・記号のみで命名された動画ファイルを対象にLLMで日本語タイトルへのAIリネームを行いたい場合、現状は「パス長閾値（240文字以上）」しか抽出条件が存在しないため、パス長が短い英数ファイル名の動画を抽出して効率的にリネームすることができません。
抽出オプションとして「ファイル名に日本語を含まないもの」を指定可能にすることで、英数ファイル名の動画を一括抽出してAIによる自動命名・整理を素早く実行できるようにします。

## What Changes

- **日本語除外フィルタ機能の追加**:
  - ファイルスキャン時、ファイル名本体（拡張子を除くベース名）に日本語文字（ひらがな、カタカナ、CJK統合漢字）が1文字も含まれないファイルのみを抽出するフィルタ処理を導入します。
  - 既存の「パス長 ≧ 閾値」とのAND条件として動作します。パス長閾値を0に指定することで、パス長に関係なく日本語を含まない動画ファイルを全件抽出可能です。
- **GUI設定コントロールの追加**:
  - Avalonia GUIツールバーの抽出基準エリア（Row 1）に `[ ] ファイル名に日本語を含まないもののみ抽出` チェックボックスを追加します。
  - 初期値は `false`（OFF）とし、セッション中の一時変更としてメモリ保持します（設定ファイルへの永続化は行わず、安全な初期状態で起動）。
- **非同期スキャン・AI提案パイプラインとの連携**:
  - フィルタオプションが有効な場合でも、既存の即時IO描画、非同期逐次AI提案、キャンセル処理、リネームおよびUndo機能がそのまま完全動作します。

## Non-goals

- フォルダパス（ディレクトリ名）に対する日本語判定（フォルダに日本語が含まれていても、ファイル名自体が英数等であれば抽出対象とします）。
- 日本語以外の特定言語（中国語簡体字/繁体字、韓国語等）の個別言語判定（ひらがな・カタカナ・CJK漢字のいずれかを含むか否かで判定）。
- TagBasedVideoManager 本体Webフロントエンドのスマホ最適化（本Changeとは独立した次期Changeとして実施）。

## Capabilities

### Modified Capabilities

- `companion-file-rename`: スキャン・抽出要件に、ファイル名に日本語文字を含まないファイルのみを抽出対象とするフィルタオプション仕様を追加。

## Impact

- **影響コード**:
  - `src/TagBasedVideoManager.Renamer/Domain.fs`: スキャンパラメータ等の型拡張（必要時）
  - `src/TagBasedVideoManager.Renamer/FileScanner.fs`: 日本語文字判定ロジック（`containsJapanese`）およびスキャンフィルタの実装
  - `src/TagBasedVideoManager.Renamer/State.fs`: Elmish Model / Msg へのフィルタ状態（`FilterNonJapaneseOnly`）の追加
  - `src/TagBasedVideoManager.Renamer/Views.fs`: ツールバー抽出基準エリアへのチェックボックスコントロール配置
  - `test/TagBasedVideoManager.Renamer.Tests/`: 日本語判定ロジックおよびフィルタ条件の単体テスト
