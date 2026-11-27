# Docker

The engine, `obmd`, also comes as a Docker image, for a NAS or home server such as Unraid, Synology or TrueNAS. It has the same `obm` and `obmd` as the Linux downloads, for x64 and Arm64. On x64 it needs a processor from about 2009 or newer (x86-64-v2), which leaves out some older Atom-based NAS boxes. Backing up isn't built yet, so for now the engine only runs and answers `obm`.

!!! warning "Linux only"

    Run it on a Linux computer or NAS. Docker Desktop on Windows and macOS shares your folders with its own Linux in a way that doesn't reliably say when files change, so changes could be missed.

## Run it

Save this as `compose.yaml` in a folder of its own:

```yaml
services:
  openbackupmanager:
    image: ghcr.io/amyjeanes/openbackupmanager:latest
    container_name: openbackupmanager
    user: "1000:1000"
    environment:
      TZ: Europe/London
      OBM_RCLONE_CONFIG_SECRET_FILE: /run/secrets/password
    secrets:
      - password
    volumes:
      - ./config:/config
    restart: unless-stopped

secrets:
  password:
    file: ./password.txt
```

- **`image`**'s `latest` is the newest full release. Until the first one, there's no `latest`, so use a version from the [releases on GitHub](https://github.com/AmyJeanes/OpenBackupManager/releases), without its `v`, such as `ghcr.io/amyjeanes/openbackupmanager:<version>`.
- **`container_name`** is the name the commands below use for it.
- **`user`** is the user and group the engine runs as, so the files it writes are theirs. Use your own, which `id` shows, such as `uid=1000(me) gid=1000(me)`. On Unraid, files usually belong to `nobody` and `users`, which are `99:100`. With rootless Docker, use `"0:0"`, as the container's root is you outside it, and any other user is one Docker set aside for it.
- **`TZ`** sets the time zone for the times in its logs. Without it, they're in UTC.
- **`OBM_RCLONE_CONFIG_SECRET_FILE`** and **`secrets`** give the engine its password from `password.txt`, which you'll make next.
- **`/config`** is where it keeps everything else, such as `config.toml`, its logs and its crash reports. Without `user`, the engine runs as the image's own user, which suits a named volume rather than a folder.

Then, in that folder, run:

```
mkdir config                                     # before Docker makes it, as root
head -c 30 /dev/urandom | base64 > password.txt  # a long random password
chmod 600 password.txt                           # only its owner can read it
sudo chown 1000:1000 config password.txt         # change owner to the engine's user
docker compose up -d
```

Leave out `sudo` if you're root, as on Unraid's console. Both have to belong to the engine's user: `config` so it can write there, and `password.txt` because Docker passes it in as it is, so only its owner can read it.

`password.txt` is the password that protects your remotes' connection details. Docker gives it to the engine as a [secret](https://docs.docker.com/compose/how-tos/use-secrets/), rather than it being kept in `config` with them, so a copy of `config`, such as a backup of your appdata, can't connect to your remotes. Keep a copy of it somewhere safe, such as a password manager, as without it each remote needs setting up again.

If `config` belongs to someone else, the engine stops straight away, and `docker logs openbackupmanager` says why:

```
obmd: the engine can't keep its state in /config. Access to the path '/config/engine.lock' is denied.
```

Change its owner with `sudo chown -R 1000:1000 config`, or change `user` to whoever owns it. If `password.txt` does, the engine runs, but setting up a remote says it can't read it, as does `obm status` for any already set up, and `sudo chown 1000:1000 password.txt` fixes it.

## Use it

Run `obm` in the container with `docker exec`:

```
docker exec openbackupmanager obm status
```

`docker logs openbackupmanager` shows what the engine printed, which is also in `config/logs`. Settings go in `config/config.toml`, as on any computer (see [Settings](settings.md)), and changes apply as soon as you save.

Docker checks the engine with `obm status` every minute, so `docker ps` shows it as healthy or unhealthy.

Other secrets are kept in `config/secrets.json`, protected only by its permissions, as there's no keyring in a container. Anyone who can read the folder, or a copy of it, can read them too, so keep it private. See [`secret-store`](settings.md#secret-store).

To share diagnostics with a bug report, `docker exec openbackupmanager obm diagnostics export` saves the zip in `config`. See [Sharing diagnostics](files.md#sharing-diagnostics).

### Moving the password out of config {#moving-the-password}

When neither `OBM_RCLONE_CONFIG_SECRET_FILE` nor `OBM_RCLONE_CONFIG_SECRET` is set, the engine makes the password itself when you first set up a remote, and keeps it in `config/secrets.json`, where a copy of `config` could connect to your remotes. To move it, put that password, the value after `"rclone-config"` in `secrets.json`, in `password.txt` as [Run it](#run-it) makes it, then add `OBM_RCLONE_CONFIG_SECRET_FILE` and the secret to `compose.yaml` as there, and run `docker compose up -d`. The engine takes it out of `secrets.json` once it's using the file. A different password would mean setting your remotes up again.

`OBM_RCLONE_CONFIG_SECRET` takes the password itself instead of a file, though `docker inspect` shows it to anyone who can run Docker.

## Updating

`docker compose pull` then `docker compose up -d` updates it to the newest image for its tag:

| Tag | What it is |
| --- | --- |
| `latest` | The newest full release |
| A version, such as `0.2.0` | That release, which never changes. Prereleases, such as `0.2.0-beta.1`, only have this tag |
| `edge` | The newest build of the code, which may not work. For trying out what's coming |

To check an image came from the project's build unchanged, see [Checking a download](checking-downloads.md).
