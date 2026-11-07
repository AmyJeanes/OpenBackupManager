# Docker

The engine, `obmd`, also comes as a Docker image, for a NAS or home server such as Unraid, Synology or TrueNAS. It has the same `obm` and `obmd` as the Linux downloads, for x64 and Arm64. Backing up isn't built yet, so for now the engine only runs and answers `obm`.

!!! warning "Linux only"

    Run it on a Linux computer or NAS. Docker Desktop on Windows and macOS shares your folders with its own Linux in a way that doesn't reliably say when files change, so changes could be missed.

## Run it

Save this as `compose.yaml` in a folder of its own, and run `docker compose up -d` there:

```yaml
services:
  openbackupmanager:
    image: ghcr.io/amyjeanes/openbackupmanager:latest
    user: "1000:1000"
    environment:
      TZ: Europe/London
    volumes:
      - ./config:/config
    restart: unless-stopped
```

- **`user`** is the user and group the engine runs as, so the files it writes are theirs. Use your own, which `id` shows, such as `uid=1000(me) gid=1000(me)`. On Unraid, files usually belong to `nobody` and `users`, which are `99:100`.
- **`/config`** is where it keeps everything, such as `config.toml`, its logs and its crash reports. The folder must belong to that user. Without `user`, the engine runs as the image's own user, which suits a named volume rather than a folder.
- **`TZ`** sets the time zone for the times in its logs. Without it, they're in UTC.

If the folder belongs to someone else, the engine stops straight away, and `docker logs openbackupmanager` says why:

```
obmd: the engine can't keep its state in /config. Access to the path '/config/engine.lock' is denied.
```

Change the folder's owner with `sudo chown -R 1000:1000 config`, using the same numbers as `user`, or change `user` to whoever owns it.

## Use it

Run `obm` in the container with `docker exec`:

```
docker exec openbackupmanager obm status
```

`docker logs openbackupmanager` shows what the engine printed, which is also in `config/logs`. Settings go in `config/config.toml`, as on any computer (see [Settings](settings.md)), and changes apply as soon as you save.

Docker checks the engine with `obm status` every minute, so `docker ps` shows it as healthy or unhealthy.

Secrets, such as the password that will protect your sign-ins, are kept in `config/secrets.json`, protected only by its permissions, as there's no keyring in a container. Anyone who can read the folder, or a copy of it, such as a backup of your appdata, can read them too, so keep it private. See [`secret-store`](settings.md#secret-store).

To share diagnostics with a bug report, `docker exec openbackupmanager obm diagnostics export` saves the zip in `config`. See [Sharing diagnostics](files.md#sharing-diagnostics).

## Updating

`docker compose pull` then `docker compose up -d` updates it to the newest image for its tag:

| Tag | What it is |
| --- | --- |
| `latest` | The newest release |
| A version, such as `0.2.0` | That release, which never changes. Prereleases, such as `0.2.0-beta.1`, only have this tag |
| `edge` | The newest build of the code, which may not work. For trying out what's coming |

To check an image came from the project's build unchanged, see [Checking a download](checking-downloads.md).
