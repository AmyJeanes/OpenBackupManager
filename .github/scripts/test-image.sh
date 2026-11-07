#!/bin/sh
# Checks the Docker image runs: the engine starts, both as the image's own user with a volume and as another user with a
# folder of theirs, the healthcheck and obm status through docker exec reach it, and docker stop stops it cleanly
set -eu
image=$1
name=obm-image-test

check() {
    # The healthcheck as the image has it, only sooner
    docker run --detach --name "$name" --health-interval 2s "$@" "$image" > /dev/null
    tries=0
    until [ "$(docker inspect --format '{{.State.Health.Status}}' "$name")" = healthy ]; do
        tries=$((tries + 1))
        if [ "$tries" -ge 60 ] || [ "$(docker inspect --format '{{.State.Running}}' "$name")" != true ]; then
            docker logs "$name"
            docker inspect --format '{{json .State.Health}}' "$name"
            docker rm --force "$name" > /dev/null
            exit 1
        fi
        sleep 1
    done

    docker exec "$name" obm status
    docker stop "$name" > /dev/null
    code=$(docker inspect --format '{{.State.ExitCode}}' "$name")
    docker logs "$name"
    docker rm "$name" > /dev/null
    [ "$code" = 0 ] || { echo "obmd exited with $code"; exit 1; }
}

check --volume "$name:/config"
docker volume rm "$name" > /dev/null

# As the guide's compose file runs it, as the user's own ID, which the image has no account for, with a folder of theirs
config=$(mktemp -d)
check --user "$(id -u):$(id -g)" --volume "$config:/config"
test -n "$(find "$config" -mindepth 1)" || { echo "The engine kept nothing in the folder"; exit 1; }
