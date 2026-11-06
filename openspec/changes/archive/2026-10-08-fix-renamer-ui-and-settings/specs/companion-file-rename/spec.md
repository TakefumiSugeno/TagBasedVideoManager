# Spec Delta

## MODIFIED Requirements

### Requirement: 設定値AIモデルのドロップダウン最上位配置と初期選択

システムは、AIモデル選択ドロップダウンにおいて、コード内にハードコードされた固定モデル一覧（`standardModels` 等）を提供してはならず（SHALL NOT）、設定ファイル（`appsettings.json` や `appsettings.Development.json` 等）の `SelectedModel` で指定されたモデルIDのみをドロップダウンの選択肢（単一項目）として表示・保持しなければならない（SHALL）。

#### Scenario: 設定値モデルの最上位表示と初期選択

- **WHEN** アプリケーションが起動したとき
- **THEN** ドロップダウンには設定ファイル（`SelectedModel`）で指定されたモデル名のみが単一の選択肢として最上位に表示・初期選択され、ハードコードされた他のモデル（llama-3.3, gemini-2.0, mistral-small, nemotron-3-ultra 等）は一切表示されないこと

## ADDED Requirements

### Requirement: 対比確認リスト最下部の完全スクロール表示と末尾余白

システムは、Before/After対比確認リスト（`ScrollViewer`）において、マウスホイールまたはスクロールバーで最下部までスクロールした際に、最後の候補カード全体（BEFORE行、AFTER行、AI COMMENT枠、およびカード外枠）が完全に視認可能となるよう、十分な下部余白（スクロールマージン／パディング）を確保しなければならない（SHALL）。リスト最下部の要素がウィンドウ境界線やフッターバーに隠れて見切れてはならない（SHALL NOT）。

#### Scenario: 最下部スクロール時の末尾カードおよびAIコメント枠の完全表示

- **WHEN** 候補一覧を最下部までスクロールしたとき
- **THEN** リスト内の最後の候補カードの下端、AI COMMENT枠、およびカード外枠の全体が完全に画面内に表示され、その下に快適な余白（約30px以上）が存在すること

### Requirement: 開発環境設定ファイル（appsettings.Development.json）のGit除外

システムは、ローカル環境固有のAPIキーや環境依存設定を含む `appsettings.Development.json` を `.gitignore` に登録してバージョン管理から除外しなければならない（SHALL）。ベース設定 `appsettings.json` がリポジトリに保持されることで、新規クローン後の初回起動性を損なわないものとする。

#### Scenario: appsettings.Development.json の Git 管理除外

- **WHEN** 開発環境で `appsettings.Development.json` を編集または新規作成したとき
- **THEN** `git status` において `appsettings.Development.json` が未追跡/変更対象として検出されず、リポジトリに誤コミットされないこと
