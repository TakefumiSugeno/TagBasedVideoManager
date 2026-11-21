#!/usr/bin/env python3
"""Antigravity CLI run_workflow 用 Archive 総括レビュー並列実行スクリプト。"""

from contextlib import nullcontext
import re
import sys

_agent = globals().get("agent")
_parallel = globals().get("parallel")
if not callable(_agent) or not callable(_parallel):
    sys.stderr.write(
        "[sdd-archive] このスクリプトは Antigravity CLI の run_workflow ツール専用です。\n"
        "代替手順: .agents/skills/sdd-archive/SKILL.md または .agents/skills/sdd-archive/references/antigravity-orchestration.md に従い、\n"
        "invoke_subagent (Model: 'pro') で 4 ロールを実行してください。\n"
    )
    sys.exit(2)

ROLES = ("user-reviewer", "se-reviewer", "pg-reviewer", "qa-reviewer")
AGENT_FILES = {
    "user-reviewer": "openspec/agents/user-reviewer.md",
    "se-reviewer": "openspec/agents/se-reviewer.md",
    "pg-reviewer": "openspec/agents/pg-reviewer.md",
    "qa-reviewer": "openspec/agents/qa-reviewer.md",
}
CHECKLIST = "openspec/templates/checklist_archive.md"
RECORD_TEMPLATE = "openspec/templates/review_record.md"
SCHEMA = {"type": "string", "description": f"{RECORD_TEMPLATE} 形式の総括レビュー結果テキスト"}
_CHANGE_NAME_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]*$")


def _resolve_change_name() -> str:
    raw_args = globals().get("args")
    if not isinstance(raw_args, dict):
        raise ValueError("args dictionary with 'change_name' is required (例: args={'change_name': '<change-name>'})")
    raw_name = raw_args.get("change_name") if "change_name" in raw_args else raw_args.get("change")
    if not isinstance(raw_name, str):
        raise ValueError("change_name must be a non-empty string (例: args={'change_name': '<change-name>'})")
    name = raw_name.strip()
    if not _CHANGE_NAME_RE.fullmatch(name):
        raise ValueError(f"Invalid change_name: {raw_name!r} (英数字・'.'・'_'・'-' のみ指定可、先頭は英数字)")
    return name


def _build_prompt(role: str, change_name: str) -> str:
    return (
        f"ロール定義 `{AGENT_FILES[role]}` に従い、変更 `{change_name}` "
        f"（`openspec/changes/{change_name}/` 配下および `git diff`）の Archive 総括レビューを実施してください。\n"
        f"- チェックリスト: `{CHECKLIST}` の全項目および挑戦的設問を省略せず検証すること。\n"
        f"- 単一ライター原則: `reviews.md`・`tasks.md`・`handover.md` 等を直接編集せず、`{RECORD_TEMPLATE}` 形式のテキストのみを返却すること。"
    )


def main():
    change_name = _resolve_change_name()
    phase_fn = globals().get("phase")
    log_fn = globals().get("log")
    if callable(log_fn):
        log_fn(f"[sdd-archive] Archive Review: {change_name}")
    ctx = phase_fn("Archive Review") if callable(phase_fn) else nullcontext()
    with ctx:
        return _parallel(
            ROLES,
            lambda r: _agent(prompt=_build_prompt(r, change_name), schema=SCHEMA, role=r, model="pro"),
        )


main()
