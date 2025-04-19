#!/usr/bin/env bash
# Re‑exec in Bash if we weren’t started with it
[[ -n $BASH_VERSION ]] || { exec bash "$0" "$@"; exit; }

FILE="/var/lib/backy/pool-metadata.json"
HIGHLIGHT_WINDOW=60     # seconds lines stay green
SLEEP_INTERVAL=5        # seconds between refreshes

# ── colour helpers ────────────────────────────────────────────────────────────
green() { printf '\e[32m%s\e[0m\n' "$1"; }
plain() { printf '%s\n' "$1"; }

# ── state ─────────────────────────────────────────────────────────────────────
declare -A status    # status[line] = new | static
declare -A seen      # seen[line]   = epoch‑seconds when it became "new"
first_load=true

while true; do
    now=$(date +%s)

    if [[ -f $FILE ]]; then
        mapfile -t cur_lines < "$FILE"

        # quick membership map
        declare -A cur_map=()
        for ln in "${cur_lines[@]}"; do
            cur_map["$ln"]=1
            if [[ -z ${status["$ln"]} ]]; then
                if $first_load; then
                    status["$ln"]="static"           # baseline stays white
                else
                    status["$ln"]="new"              # brand‑new after startup
                    seen["$ln"]=$now
                fi
            fi
        done
        first_load=false

        # purge lines that disappeared from the file
        for ln in "${!status[@]}"; do
            [[ -z ${cur_map["$ln"]} ]] && unset status["$ln"] seen["$ln"]
        done

        # fade green back to white after HIGHLIGHT_WINDOW seconds
        for ln in "${!status[@]}"; do
            if [[ ${status["$ln"]} == "new" && $((now - seen["$ln"])) -gt $HIGHLIGHT_WINDOW ]]; then
                status["$ln"]="static"
            fi
        done

        clear
        for ln in "${cur_lines[@]}"; do
            [[ ${status["$ln"]} == "new" ]] && green "$ln" || plain "$ln"
        done
    else
        clear
        echo "Cannot find file, will continue to monitor $FILE"
    fi

    sleep "$SLEEP_INTERVAL"
done
