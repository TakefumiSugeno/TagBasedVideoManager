#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
DuckDuckGo Search CLI Wrapper for TagBasedVideoManager.Renamer
Uses the `ddgs` library to search DuckDuckGo and output JSON snippets.
"""

import sys
import json
import argparse

def search(query: str, max_results: int = 3):
    if not query or not query.strip():
        return []

    try:
        from ddgs import DDGS
    except ImportError:
        try:
            # フォールバック (duckduckgo_search旧称)
            from duckduckgo_search import DDGS
        except ImportError as e:
            sys.stderr.write(f"Error: ddgs library not found ({e}). Run `pip install ddgs`\n")
            return []

    results = []
    try:
        with DDGS() as ddgs:
            raw_results = ddgs.text(query.strip(), max_results=max_results)
            for item in raw_results:
                results.append({
                    "title": item.get("title", ""),
                    "snippet": item.get("body", ""),
                    "url": item.get("href", "")
                })
    except Exception as ex:
        sys.stderr.write(f"Search warning/error for query '{query}': {ex}\n")
        # 検索エラー時も空リストを返し、後続処理を落とさない
        return []

    return results

def main():
    # Windowsコンソール等でのUTF-8出力を保証
    if sys.stdout.encoding != 'utf-8':
        try:
            sys.stdout.reconfigure(encoding='utf-8')
        except AttributeError:
            pass

    parser = argparse.ArgumentParser(description="DuckDuckGo Search CLI")
    parser.add_argument("query", nargs="?", default="", help="Search query string")
    parser.add_argument("--query", "-q", dest="query_opt", default="", help="Search query string (option)")
    parser.add_argument("--max-results", "-n", type=int, default=3, help="Max results to fetch (default: 3)")

    args = parser.parse_args()
    query = args.query_opt if args.query_opt else args.query

    results = search(query, max_results=args.max_results)
    print(json.dumps(results, ensure_ascii=False, indent=2))

if __name__ == "__main__":
    main()
