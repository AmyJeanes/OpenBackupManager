#!/bin/sh
# Checks obm and obmd unpacked from a tarball run through links onto PATH, as the guide installs them: the engine starts,
# obm status reaches it, it runs the rclone beside it for a remote, and it stops cleanly. Also that rclone and the
# licences came with them. CI runs it on the macOS runners, and for Linux in older distros' containers, which have no
# PowerShell. It runs from the repository's folder, for rclone.version
set -eu
dir=$(cd "$1" && pwd)

test -s "$dir/LICENSE" || { echo "LICENSE is missing or empty"; exit 1; }
# The generated part, not only the hand-written notices it starts with
grep -q "These NuGet packages are used under" "$dir/THIRD-PARTY-NOTICES.txt" ||
    { echo "THIRD-PARTY-NOTICES.txt is missing or lists no packages"; exit 1; }

home=$(mktemp -d /tmp/obm.XXXXXX)

# Which also fails if it's for another processor
pinned=$(grep -o 'v[0-9]*\.[0-9]*\.[0-9]*' rclone.version)
rclone=$("$dir/obm-rclone" version --config "$home/rclone.conf" | head -n 1)
test "$rclone" = "rclone $pinned" || { echo "rclone is \"$rclone\", not $pinned"; exit 1; }

mkdir "$home/bin"
ln -s "$dir/obm" "$dir/obmd" "$home/bin/"
"$home/bin/obm" --version
"$home/bin/obmd" --version

printf '[[remotes]]\nid = "test"\n' > "$home/config.toml"
"$home/bin/obmd" --home "$home" > "$home/obmd.out" 2>&1 &
engine=$!
tries=0
until "$home/bin/obm" status --home "$home" && ls "$home"/run/rc-*.sock > /dev/null 2>&1; do
    tries=$((tries + 1))
    if [ "$tries" -ge 30 ] || ! kill -0 "$engine" 2> /dev/null; then
        cat "$home/obmd.out"
        exit 1
    fi
    sleep 1
done

kill -TERM "$engine"
wait "$engine"
# A killed rclone leaves its socket
if ls "$home"/run/rc-*.sock > /dev/null 2>&1; then
    cat "$home/obmd.out"
    echo "rclone didn't quit with the engine"
    exit 1
fi
