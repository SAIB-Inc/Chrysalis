#!/usr/bin/env bash
# From the worktree root: --self-test is offline; real mode requires prior digest approval.
set +x
set -euo pipefail
ulimit -c 0
helper=${BASH_SOURCE[0]%/*}/record-blockfrost.py
expected=e4eaff9f22c76ef623dcbeeea2f1d4c169ee6325a76afc13e3614043c422e12c
if command -v sha256sum >/dev/null 2>&1; then
    digest=$(sha256sum < "$helper")
else
    digest=$(shasum -a 256 < "$helper")
fi
if [[ ${digest%% *} != "$expected" ]]; then
    printf '%s\n' 'recorder helper digest mismatch' >&2
    exit 2
fi
if [[ $# == 1 && $1 == --self-test ]]; then
    printf '%s\n' CANARY-test-Check | python3 -I -B "$helper" --self-test
elif [[ $# == 0 ]]; then
    op read 'op://Grok Bot/Blockfrost Preprod project key/credential' 2>/dev/null | python3 -I -B "$helper"
else
    printf '%s\n' 'invalid recorder arguments' >&2
    exit 2
fi
