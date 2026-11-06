#!/usr/bin/env bash
# =============================================================================
# scripts/deploy.sh: Linux 向け自動デプロイスクリプト
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

# .env ファイルがあれば読み込み
if [ -f "${REPO_ROOT}/.env" ]; then
    set -a
    # shellcheck disable=SC1091
    source "${REPO_ROOT}/.env"
    set +a
fi

PORT="${PORT:-5620}"
VIDEO_DIR="${VIDEO_DIR:-/mnt/videos}"
DATA_DIR="${DATA_DIR:-${REPO_ROOT}/data}"
CONTAINER_NAME="tag-based-video-manager"
IMAGE_NAME="tag-based-video-manager:latest"

mkdir -p "${DATA_DIR}"

echo "=== TagBasedVideoManager デプロイ開始 ==="
echo "ポート: ${PORT}"
echo "動画ディレクトリ: ${VIDEO_DIR}"
echo "データディレクトリ: ${DATA_DIR}"

# 1. コンテナランタイム自動判別 (docker -> podman)
RUNTIME=""
if command -v docker >/dev/null 2>&1; then
    RUNTIME="docker"
elif command -v podman >/dev/null 2>&1; then
    RUNTIME="podman"
else
    echo "エラー: docker または podman が見つかりません。" >&2
    exit 1
fi
echo "検出されたランタイム: ${RUNTIME}"

# 2. ポート事前チェック
if command -v lsof >/dev/null 2>&1; then
    if lsof -i ":${PORT}" >/dev/null 2>&1; then
        echo "警告: ポート ${PORT} は既に使用されています。" >&2
    fi
elif command -v nc >/dev/null 2>&1; then
    if nc -z localhost "${PORT}" >/dev/null 2>&1; then
        echo "警告: ポート ${PORT} は既に使用されています。" >&2
    fi
fi

# 3. イメージビルド
echo "--- コンテナイメージをビルド中 ---"
${RUNTIME} build -t "${IMAGE_NAME}" -f "${REPO_ROOT}/Dockerfile" "${REPO_ROOT}"

# 4. 既存コンテナの停止 & 削除
echo "--- 既存コンテナの停止・削除 ---"
${RUNTIME} stop "${CONTAINER_NAME}" 2>/dev/null || true
${RUNTIME} rm -f "${CONTAINER_NAME}" 2>/dev/null || true

# 5. GPU 判定
GPU_ARGS=()
if command -v nvidia-smi >/dev/null 2>&1; then
    GPU_ARGS=(--gpus all)
elif [ -e /dev/dri ]; then
    GPU_ARGS=(--device /dev/dri:/dev/dri)
fi

# 6. 新コンテナ起動
echo "--- 新コンテナを起動中 ---"
${RUNTIME} run -d \
    --name "${CONTAINER_NAME}" \
    -p "${PORT}:5620" \
    -v "${VIDEO_DIR}:/app/videos:ro" \
    -v "${DATA_DIR}:/app/data" \
    -e "PORT=5620" \
    -e "VIDEO_DIR=/app/videos" \
    -e "THUMBNAIL_DIR=/app/data/thumbnails" \
    -e "DATABASE_PATH=/app/data/metadata.db" \
    "${GPU_ARGS[@]+"${GPU_ARGS[@]}"}" \
    --restart unless-stopped \
    "${IMAGE_NAME}"

# 7. ヘルスチェック (最大30秒)
echo "--- ヘルスチェック実行中 (ポート ${PORT}) ---"
HEALTH_URL="http://localhost:${PORT}/"
MAX_RETRIES=10
RETRY_COUNT=0
IS_HEALTHY=false

while [ "${RETRY_COUNT}" -lt "${MAX_RETRIES}" ]; do
    sleep 3
    RETRY_COUNT=$((RETRY_COUNT + 1))
    if command -v curl >/dev/null 2>&1; then
        if curl -s -f -o /dev/null "${HEALTH_URL}"; then
            IS_HEALTHY=true
            break
        fi
    elif command -v wget >/dev/null 2>&1; then
        if wget -q -O /dev/null "${HEALTH_URL}"; then
            IS_HEALTHY=true
            break
        fi
    fi
    echo "待機中... (${RETRY_COUNT}/${MAX_RETRIES})"
done

if [ "${IS_HEALTHY}" = true ]; then
    echo "=== デプロイ成功！TagBasedVideoManager は正常に稼働しています (${HEALTH_URL}) ==="
    exit 0
else
    echo "エラー: ヘルスチェックに失敗しました。タイムアウトしました。" >&2
    echo "--- 直近のコンテナログ ---" >&2
    ${RUNTIME} logs --tail 50 "${CONTAINER_NAME}" || true
    exit 1
fi
