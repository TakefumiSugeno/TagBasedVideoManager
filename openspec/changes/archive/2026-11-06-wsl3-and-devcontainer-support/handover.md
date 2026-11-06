# 申し送り事項 (Handover): wsl3-and-devcontainer-support

- **変更名**: `wsl3-and-devcontainer-support`
- **日付**: 2026-11-06
- **ブランチ**: `feature/wsl3-and-devcontainer-support`
- **関連 Issue / PR**: https://github.com/TakefumiSugeno/TagBasedVideoManager/issues/17

---

## 1. 変更サマリ

Windows Subsystem for Linux 3.0（WSL 3.0）のネイティブコンテナ CLI（`wslc.exe`）および Linux 上の `docker` / `podman` に対応した単一コンテナ運用基盤を構築しました。
併せて、ソースコード変更後にワンコマンドで再ビルド・コンテナ再作成・ヘルスチェックを行うデプロイスクリプト（`scripts/deploy.ps1`, `scripts/deploy.sh`）、本番コンテナ（ポート 5620）と競合しない VS Code DevContainer 環境（ポート 5621）、および Desktop Companion アプリ（`TagBasedVideoManager.Renamer`）の単一コンテナ OCI CLI 抽象化（`ContainerController.fs`）を実装しました。

---

## 2. 品質・テストエビデンス

- **単体・結合テスト（Desktop Companion / DevContainer / DeployScript）**:
  `pwsh ./test/TagBasedVideoManager.Renamer.Tests/run_tests_with_coverage.ps1` → **294 件 Pass**（失敗 0 / スキップ 0）
- **単体・結合・E2Eテスト（Web アプリケーション）**:
  `dotnet run --project test/TagBasedVideoManager.Tests` → **83 件 Pass**（失敗 0 / スキップ 0）
- **カバレッジ**:
  - `TagBasedVideoManager.Renamer`: Line **83.9%** / Branch **63.09%** / Method **60.55%**（新規 `ContainerController.fs` の複合条件 C2 100% 網羅）
- **エビデンス出力先**: `test/TagBasedVideoManager.Renamer.Tests/TestResults/TestRun_2026-11-06_05_35_42/`

---

## 3. 保留指摘・技術的負債

| 指摘元（フェーズ/タスク/役割） | 内容                                     | 保留理由 | 推奨対応時期 |
| ------------------------------ | ---------------------------------------- | -------- | ------------ |
| なし                           | なし（全タスク・全観点において解消済み） | -        | -            |

---

## 4. 将来の改善・リファクタ候補

- **WSL 3.0 (`wslc`) における AMD GPU デバイスパススルー対応**:
  現時点では `wslc` が `--device` フラグをサポートしていないため、Windows + WSL 3.0 環境下の AMD GPU は CPU ソフトウェアデコードにフォールバックさせています（Linux ホスト上の Docker のみ `/dev/dri` パススルー有効）。将来 WSL 3.0 側で `--device` サポートが追加された際、`scripts/common.ps1` の `New-RunArguments` で有効化可能です。
- **デプロイスクリプトへのオプション `-Pull` 追加**:
  現状はローカルの変更をそのままビルド・デプロイする設計ですが、リモートリポジトリからの自動更新ニーズが高まった場合は `-Pull` / `--pull` オプションで `git pull --ff-only` を実行する拡張が検討可能です。
- **旧 `DockerController.fs` の段階的廃止**:
  後方互換のためファイル自体は残していますが、新規呼び出しはすべて `ContainerController.fs` へ移行済みです。

---

## 5. 運用上の注意事項

- **DevContainer 設定反映時のコンテナ再作成**:
  `.devcontainer/devcontainer.json` のポートバインド（`appPort: ["5621:5621"]`）、マウント設定（`mounts`）、および実行ユーザー（`remoteUser: "root"`）を変更した場合は、VS Code のコマンドパレットから `Dev Containers: Rebuild Container` を実行する必要があります。
- **環境変数設定ファイル (`.env`) の切り替え**:
  `.env.sample` に【パターン1】Windows ホスト環境、【パターン2】Linux ホスト環境、【パターン3】VS Code DevContainer 開発環境（デフォルト有効）の3パターンを記載しています。実行環境に合わせてパス（Windows ドライブパス vs コンテナ内 `/workspaces/...` パス）を切り替えて使用してください。
