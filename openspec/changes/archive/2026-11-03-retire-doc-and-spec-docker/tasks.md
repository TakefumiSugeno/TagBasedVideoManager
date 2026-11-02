# Tasks

## 1. アセット移管と README.md の拡充

- [x] 1.1 `doc/images/` のスクリーンショット画像を `assets/images/` へ移動し、`README.md` 内の画像パス（`./assets/images/...`）を更新して画像が正常に参照できることを確認する
- [x] 1.2 `doc/design_detail.md` の SQLite 物理スキーマ（6テーブルDDL）および全体アーキテクチャ図を `README.md` に統合し、ドキュメントの完全性を担保する

## 2. 旧 `doc/` ディレクトリの完全削除

- [x] 2.1 不要となった `doc/` ディレクトリ全体をリポジトリから削除し（`git rm -r doc`）、不要ファイルが残存していないことを確認する

## 3. 全体検証とエビデンス確認

- [x] 3.1 Webアプリの全テストスイートを実行し、リグレッションなく全件PASSすることを確認する (`dotnet exec test/TagBasedVideoManager.Tests/bin/Debug/net10.0/TagBasedVideoManager.Tests.dll`)
- [x] 3.2 OpenSpecバリデーションを実行し、すべての仕様が妥当であることを確認する (`openspec validate --specs`)
- [x] 3.3 フェーズゲート検証（Gate 2）を実行し、DoDを満たすことを確認する (`pwsh ./openspec/scripts/verify-gate.ps1 -Gate apply -ChangeName "retire-doc-and-spec-docker"`)

## Workflow follow-up

- アーカイブを実行してメイン仕様（`openspec/specs/`）へデルタ仕様を同期する (`openspec-archive-change`)
