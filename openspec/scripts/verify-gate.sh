#!/usr/bin/env bash
# =============================================================================
# openspec-kit/scripts/verify-gate.sh
#   OpenSpec SDD/TDD ワークフローのフェーズゲート（完了基準 / Definition of Done）を
#   機械的に検証する（Bash 版: Linux / macOS / Git Bash / WSL 対応）。
#
# 使い方:
#   ./openspec/scripts/verify-gate.sh --gate pre-propose
#   ./openspec/scripts/verify-gate.sh --gate propose --change-name <name> [--level L2]
#   ./openspec/scripts/verify-gate.sh --gate apply   --change-name <name>
#   ./openspec/scripts/verify-gate.sh --gate archive --change-name <name>
#   ※ PowerShell 版と同じ引数名（-Gate, -ChangeName, -Level, -TargetPath, -BaseBranch, -SkipGitCheck）も指定可能
# =============================================================================
set -euo pipefail

GATE=""
CHANGE_NAME=""
LEVEL=""
TARGET_PATH="."
BASE_BRANCH="develop"
SKIP_GIT_CHECK=0

FAILURES=0

pass() {
    echo "  [PASS] $1"
}

fail() {
    echo "  [FAIL] $1"
    FAILURES=$((FAILURES + 1))
}

warn() {
    echo "  [WARN] $1"
}

usage() {
    cat <<'EOF'
Usage:
  verify-gate.sh --gate <pre-propose|propose|apply|archive> [--change-name <name>] [options]

Options:
  -g, --gate, -Gate <gate>                  検証ゲート（pre-propose | propose | apply | archive）
  -c, --change-name, -ChangeName <name>     対象の OpenSpec change 名（pre-propose 以外は必須）
  -l, --level, -Level <L1|L2|L3>            適用規模レベル（省略時は自動判定、既定: L2）
  -t, --target-path, -TargetPath <path>     プロジェクトルートパス（既定: .）
  -b, --base-branch, -BaseBranch <branch>   ベースブランチ名（既定: develop）
      --skip-git-check, -SkipGitCheck       Git ブランチ検証をスキップ
  -h, --help                                このヘルプを表示
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -g|--gate|-Gate)
            GATE="${2:-}"; shift 2 ;;
        -c|--change-name|-ChangeName)
            CHANGE_NAME="${2:-}"; shift 2 ;;
        -l|--level|-Level)
            LEVEL="${2:-}"; shift 2 ;;
        -t|--target-path|-TargetPath)
            TARGET_PATH="${2:-}"; shift 2 ;;
        -b|--base-branch|-BaseBranch)
            BASE_BRANCH="${2:-}"; shift 2 ;;
        --skip-git-check|-SkipGitCheck)
            SKIP_GIT_CHECK=1; shift ;;
        -h|--help|-Help)
            usage; exit 0 ;;
        *)
            echo "Error: 未知の引数: $1" >&2
            usage >&2
            exit 1 ;;
    esac
done

case "$GATE" in
    pre-propose|propose|apply|archive) ;;
    *)
        echo "Error: --gate (-Gate) には pre-propose | propose | apply | archive のいずれかを指定してください。" >&2
        exit 1 ;;
esac

ROOT="$(cd "$TARGET_PATH" && pwd)"
echo "=== OpenSpec Phase Gate Verification: [$GATE] ==="

# --- Gate 0: Git ブランチ安全チェック -----------------------------------------
if [[ "$SKIP_GIT_CHECK" -eq 0 && -d "$ROOT/.git" ]]; then
    BRANCH="$(git -C "$ROOT" branch --show-current 2>/dev/null | tr -d '\r' || true)"
    if [[ -z "$BRANCH" ]]; then
        fail "Git ブランチを特定できません（Detached HEAD の可能性があります）"
    elif [[ "$BRANCH" == "main" || "$BRANCH" == "master" || "$BRANCH" == "develop" || "$BRANCH" == "$BASE_BRANCH" ]]; then
        fail "ベースブランチ '$BRANCH' 上で直接作業しています。feature/<name> または fix/<name> ブランチへ切り替えてください"
    elif [[ ! "$BRANCH" =~ ^(feature|fix|refactor|chore)/.+ ]]; then
        warn "ブランチ名 '$BRANCH' が標準規約（feature/*, fix/*, refactor/*, chore/*）に従っていません"
    else
        pass "トピックブランチ確認: $BRANCH"
    fi
fi

if [[ "$GATE" == "pre-propose" ]]; then
    if [[ "$FAILURES" -gt 0 ]]; then
        echo "Gate [pre-propose] NG: ${FAILURES} 件の違反があります。"
        exit 1
    fi
    echo "Gate [pre-propose] OK"
    exit 0
fi

# --- ChangeName 必須確認 ------------------------------------------------------
if [[ -z "$CHANGE_NAME" ]]; then
    echo "Error: -Gate $GATE には -ChangeName (--change-name) <name> の指定が必須です。" >&2
    exit 1
fi

CHANGE_DIR="$ROOT/openspec/changes/$CHANGE_NAME"
if [[ ! -d "$CHANGE_DIR" ]]; then
    fail "変更ディレクトリが存在しません: $CHANGE_DIR"
    exit 1
fi

# --- 適用規模レベル（L1/L2/L3）と skip_specs の解決 --------------------------
OPENSPEC_YAML="$CHANGE_DIR/.openspec.yaml"
SKIP_SPECS="False"
if [[ -f "$OPENSPEC_YAML" ]] && grep -Eq '^[[:space:]]*skip_specs:[[:space:]]*true([[:space:]]|$)' "$OPENSPEC_YAML"; then
    SKIP_SPECS="True"
fi

PROPOSAL_PATH="$CHANGE_DIR/proposal.md"
if [[ -z "$LEVEL" ]]; then
    if [[ -f "$OPENSPEC_YAML" ]]; then
        DETECTED="$(sed -nE 's/^[[:space:]]*level:[[:space:]]*(L[123]).*/\1/p' "$OPENSPEC_YAML" | tr -d '\r' | head -n 1)"
        [[ -n "$DETECTED" ]] && LEVEL="$DETECTED"
    fi
    if [[ -z "$LEVEL" && -f "$PROPOSAL_PATH" ]]; then
        DETECTED="$(grep -Eo '\bL[123]\b' "$PROPOSAL_PATH" | head -n 1 || true)"
        [[ -n "$DETECTED" ]] && LEVEL="$DETECTED"
    fi
    [[ -z "$LEVEL" ]] && LEVEL="L2"
fi

echo "  Change: $CHANGE_NAME / Level: $LEVEL / skip_specs: $SKIP_SPECS"

# --- 共通関数: reviews.md の検証 ----------------------------------------------
assert_reviews_valid() {
    local phase_label="$1"
    local reviews_path="$CHANGE_DIR/reviews.md"
    if [[ ! -f "$reviews_path" ]]; then
        fail "reviews.md が存在しません（${phase_label} レビューが未実施です）"
        return
    fi
    if grep -Fq "✅/❌/⚠️" "$reviews_path"; then
        fail "reviews.md に未判定のチェックリスト項目（'✅/❌/⚠️'）が残っています"
    elif grep -E -q '(:[[:space:]]*|-[[:space:]]*)<(具体的な確認内容|ファイル名|テスト名|実行したコマンド|未記入|指摘内容|対応方針の理由)[^>]*>' "$reviews_path"; then
        fail "reviews.md に未記入のプレースホルダ（'<...>'）が残っています"
    else
        pass "reviews.md の全レビュー設問・確認項目が記入済み"
    fi

    local last_lgtm
    last_lgtm="$(tr -d '\r' < "$reviews_path" | sed -nE 's/^-[[:space:]]*\*\*LGTM\*\*:[[:space:]]*(true|false|TRUE|FALSE).*/\L\1/p' | tail -n 1)"
    if [[ -z "$last_lgtm" ]]; then
        fail "reviews.md に '- **LGTM**: true' の判定記録がありません"
    elif [[ "$last_lgtm" != "true" ]]; then
        fail "reviews.md の最新判定が LGTM: true ではありません（現在: $last_lgtm）"
    else
        pass "reviews.md 最新レビュー判定: LGTM = true"
    fi
}

# --- Gate 1: Propose ゲート ---------------------------------------------------
if [[ "$GATE" == "propose" || "$GATE" == "archive" ]]; then
    if [[ ! -f "$PROPOSAL_PATH" ]]; then
        fail "proposal.md が存在しません"
    else
        pass "proposal.md 存在確認"
    fi

    DESIGN_PATH="$CHANGE_DIR/design.md"
    if [[ "$LEVEL" == "L2" || "$LEVEL" == "L3" || "$SKIP_SPECS" == "False" ]]; then
        if [[ ! -f "$DESIGN_PATH" ]]; then
            fail "design.md が存在しません（Level $LEVEL では必須）"
        else
            pass "design.md 存在確認"
        fi
    fi

    TEST_PLAN_PATH="$CHANGE_DIR/test_plan.md"
    if [[ "$LEVEL" == "L2" || "$LEVEL" == "L3" ]]; then
        if [[ ! -f "$TEST_PLAN_PATH" ]]; then
            fail "test_plan.md が存在しません（Level $LEVEL ではテストケース・境界値定義が必須です）"
        elif grep -Fq "<change-name>" "$TEST_PLAN_PATH"; then
            fail "test_plan.md に未記入のプレースホルダ '<change-name>' が残っています"
        else
            pass "test_plan.md 存在・記入確認"
        fi
    fi

    if [[ "$SKIP_SPECS" == "False" ]]; then
        SPEC_COUNT=0
        if [[ -d "$CHANGE_DIR/specs" ]]; then
            SPEC_COUNT="$(find "$CHANGE_DIR/specs" -type f -name 'spec.md' | wc -l | tr -d ' ')"
        fi
        if [[ "$SPEC_COUNT" -eq 0 ]]; then
            fail "specs/**/spec.md が存在しません（仕様変更がない場合は .openspec.yaml に 'skip_specs: true' を設定してください）"
        else
            pass "デルタ仕様確認: ${SPEC_COUNT} ファイル"
        fi
    fi

    TASKS_PATH="$CHANGE_DIR/tasks.md"
    if [[ ! -f "$TASKS_PATH" ]]; then
        fail "tasks.md が存在しません"
    elif ! grep -Eq '^[[:space:]]*-[[:space:]]*\[[ xX]\][[:space:]]+.+' "$TASKS_PATH"; then
        fail "tasks.md にチェックボックス形式のタスク（- [ ] ...）が含まれていません"
    else
        pass "tasks.md 存在・タスク定義確認"
    fi

    assert_reviews_valid "Propose"
fi

# --- Gate 2: Apply ゲート -----------------------------------------------------
if [[ "$GATE" == "apply" || "$GATE" == "archive" ]]; then
    if [[ "$GATE" == "apply" ]]; then
        assert_reviews_valid "Apply"
    fi

    SKIPPED_FILES=()
    for td in "$ROOT/test" "$ROOT/tests"; do
        [[ -d "$td" ]] || continue
        while IFS= read -r -d '' f; do
            if grep -Eq '\b(it|test|describe)\.skip[[:space:]]*\(|\b(xit|xdescribe|xtest)[[:space:]]*\(|\[(Fact|Theory)[[:space:]]*\([^]]*Skip[[:space:]]*=|^[[:space:]]*It[[:space:]]+["'\''].+["'\''][[:space:]]+-Skip\b' "$f"; then
                REL="${f#"$ROOT"/}"
                SKIPPED_FILES+=("$REL")
            fi
        done < <(find "$td" -type d \( -name 'node_modules' -o -name 'bin' -o -name 'obj' -o -name 'TestResults' -o -name '.git' \) -prune -o -type f \( -name '*.ts' -o -name '*.tsx' -o -name '*.js' -o -name '*.mjs' -o -name '*.cjs' -o -name '*.fs' -o -name '*.cs' -o -name '*.py' -o -name '*.ps1' -o -name '*.sh' \) -print0)
    done

    if [[ "${#SKIPPED_FILES[@]}" -gt 0 ]]; then
        JOINED="$(IFS=', '; echo "${SKIPPED_FILES[*]}")"
        fail "Skip / Ignore 状態のテストが放置されています: $JOINED"
    else
        pass "Skip / Ignore テスト放置なし"
    fi
fi

# --- Gate 3: Archive ゲート（Definition of Done） -----------------------------
if [[ "$GATE" == "archive" ]]; then
    TASKS_PATH="$CHANGE_DIR/tasks.md"
    if [[ -f "$TASKS_PATH" ]]; then
        UNCHECKED="$(grep -Ec '^[[:space:]]*-[[:space:]]*\[[[:space:]]\][[:space:]]+.+' "$TASKS_PATH" || true)"
        CHECKED="$(grep -Ec '^[[:space:]]*-[[:space:]]*\[[xX]\][[:space:]]+.+' "$TASKS_PATH" || true)"
        if [[ "$UNCHECKED" -gt 0 ]]; then
            fail "tasks.md に未完了タスク（- [ ]）が ${UNCHECKED} 件残っています"
        elif [[ "$CHECKED" -eq 0 ]]; then
            fail "tasks.md に完了済みタスク（- [x]）が 1 件もありません"
        else
            pass "tasks.md 全 ${CHECKED} タスク完了（未完了 0 件）"
        fi
    fi

    HANDOVER_PATH="$CHANGE_DIR/handover.md"
    if [[ ! -f "$HANDOVER_PATH" ]]; then
        fail "handover.md が存在しません（Archive 前に申し送り事項の作成が必須です）"
    elif grep -Fq "<change-name>" "$HANDOVER_PATH" || grep -Fq "<YYYY-MM-DD>" "$HANDOVER_PATH"; then
        fail "handover.md に未記入のプレースホルダ（<change-name> / <YYYY-MM-DD>）が残っています"
    else
        pass "handover.md 作成・記入確認"
    fi

    # 4ロール総括レビュー判定確認（LGTM: true、未記入項目なし）
    assert_reviews_valid "Archive"
fi

# --- 結果サマリ ---------------------------------------------------------------
if [[ "$FAILURES" -gt 0 ]]; then
    echo "=== Gate [$GATE] FAILED (${FAILURES} 件の未達項目) ==="
    exit 1
fi

echo "=== Gate [$GATE] PASSED ==="
exit 0
