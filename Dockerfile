# check=skip=SecretsUsedInArgOrEnv
# CI's package-docker job builds it from the Linux tarballs, unpacked into amd64/ and arm64/
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled-extra@sha256:56b6c354a140e50e09d45a397b43f9f9d31012c325020239e602e06d1594f8ff

ARG TARGETARCH
LABEL org.opencontainers.image.source="https://github.com/AmyJeanes/OpenBackupManager" \
      org.opencontainers.image.licenses="MIT"

COPY ${TARGETARCH}/ /app/
# No Secret Service in a container. The check skipped at the top takes OBM_SECRET_STORE for a secret by its name
ENV PATH="/app:${PATH}" \
    OBM_HOME=/config \
    OBM_SECRET_STORE=file

# Made as the image's user, so a new volume here starts out theirs
WORKDIR /config

HEALTHCHECK --interval=1m --start-period=10s CMD ["obm", "status"]
ENTRYPOINT ["obmd"]
