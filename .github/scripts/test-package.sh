#!/bin/sh
# Checks a .deb or .rpm installs with the distro's own apt or dnf, which brings what it depends on, and runs. Then that
# upgrading to it from an older one, with the engine running, has the engine say it should be restarted, and that
# removing it stops the engine. CI runs it as root in each distro's container
set -eu
package=$1
older=$2

case $package in
*.deb)
    apt-get update -qq
    install() { apt-get install -qq -y "./$1" > /dev/null; }
    remove() { apt-get remove -qq -y openbackupmanager > /dev/null; }
    ;;
*.rpm)
    install() { dnf install -q -y "./$1" > /dev/null; }
    remove() { dnf remove -q -y openbackupmanager > /dev/null; }
    ;;
esac

install "$package"
sh .github/scripts/test-tarball.sh /usr/lib/openbackupmanager
# So obm update says to install the newer package instead
grep -qx "${package##*.}" /usr/lib/openbackupmanager/installed-by || { echo "installed-by doesn't say ${package##*.}"; exit 1; }
obm --version
obmd --version
remove

install "$older"
home=$(mktemp -d /tmp/obm.XXXXXX)
obmd --home "$home" > "$home/obmd.out" 2>&1 &
engine=$!
tries=0
until obm status --home "$home" > /dev/null; do
    tries=$((tries + 1))
    if [ "$tries" -ge 30 ] || ! kill -0 "$engine" 2> /dev/null; then
        cat "$home/obmd.out"
        exit 1
    fi
    sleep 1
done

install "$package"
installed="Installed: $(obmd --version), which runs once you restart the engine"
tries=0
until obm status --home "$home" | grep -qF "$installed"; do
    tries=$((tries + 1))
    if [ "$tries" -ge 30 ]; then
        obm status --home "$home" || true
        cat "$home/obmd.out"
        exit 1
    fi
    sleep 1
done
obm status --home "$home"

remove
tries=0
while kill -0 "$engine" 2> /dev/null; do
    tries=$((tries + 1))
    if [ "$tries" -ge 30 ]; then
        echo "The engine kept running once it was removed"
        cat "$home/obmd.out"
        exit 1
    fi
    sleep 1
done
wait "$engine" || { echo "The engine exited with $?"; cat "$home/obmd.out"; exit 1; }
cat "$home/obmd.out"
