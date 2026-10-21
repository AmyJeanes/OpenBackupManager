#!/bin/sh
# Checks obm and obmd unpacked from a tarball run: the engine starts, obm status reaches it, and it stops cleanly. CI
# runs it on the macOS runners, and for Linux in older distros' containers, which have no PowerShell
set -eu
dir=$1
home=$(mktemp -d /tmp/obm.XXXXXX)

"$dir/obm" --version
"$dir/obmd" --version

"$dir/obmd" --home "$home" > "$home/obmd.out" 2>&1 &
engine=$!
tries=0
until "$dir/obm" status --home "$home"; do
    tries=$((tries + 1))
    if [ "$tries" -ge 30 ] || ! kill -0 "$engine" 2> /dev/null; then
        cat "$home/obmd.out"
        exit 1
    fi
    sleep 1
done

kill -TERM "$engine"
wait "$engine"
