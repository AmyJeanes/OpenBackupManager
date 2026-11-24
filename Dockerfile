# check=skip=SecretsUsedInArgOrEnv
# CI's package-docker job builds it from the Linux tarballs, unpacked into amd64/ and arm64/. The base's own non-root
# user runs obmd, unless Docker's user picks another
FROM mcr.microsoft.com/dotnet/runtime-deps:11.0-resolute-chiseled-extra@sha256:d4f2520240bb1af6acf586ae761b4fd438c2b0da541d81d2cafc91034c854e25

ARG TARGETARCH
LABEL org.opencontainers.image.source="https://github.com/AmyJeanes/OpenBackupManager" \
      org.opencontainers.image.licenses="MIT"

COPY ${TARGETARCH}/ /app/
# So obm update says to update the image instead
COPY <<EOF /app/installed-by
docker
EOF
# No Secret Service in a container. The check skipped at the top takes OBM_SECRET_STORE for a secret by its name
# (https://github.com/moby/buildkit/issues/5775)
ENV PATH="/app:${PATH}" \
    OBM_HOME=/config \
    OBM_SECRET_STORE=file

# Made as the image's user, so a new volume here starts out theirs
WORKDIR /config

HEALTHCHECK --interval=1m --start-period=10s CMD ["obm", "status"]
ENTRYPOINT ["obmd"]
