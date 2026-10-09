# syntax=docker/dockerfile:1

# Validate artifacts using the builder's CPU. Installing target-platform media
# tools below requires a native builder or QEMU for multi-platform packaging.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/aspnet:10.0.11-noble AS artifacts
# Release artifacts are laid out as docker-amd64/ and docker-arm64/.
# For one already-published directory, pass --build-arg PUBLISH_DIR=.
ARG TARGETARCH
ARG PUBLISH_DIR=docker-${TARGETARCH}
COPY ${PUBLISH_DIR}/ /publish/
RUN test ! -e /publish/libcoreclr.so || { \
    echo "Container artifacts must be published with --self-contained false to use the image's fixed runtime." >&2; exit 1; }

# Includes the ARM64 SME/SVE signal-context fix (dotnet/runtime#127518).
FROM mcr.microsoft.com/dotnet/aspnet:10.0.11-noble AS base
# Noble supplies the required 7zz executable in 7zip-standalone, not 7zip.
RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    rm -f /etc/apt/apt.conf.d/docker-clean && \
    apt-get update && apt-get install -y --no-install-recommends ffmpeg 7zip-standalone && \
    command -v ffmpeg && command -v ffprobe && command -v 7zz && command -v which && \
    rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=artifacts /publish/ .

ENV BAKABASE_DATA_DIR=/data \
    API_LISTENING_PORTS=34567 \
    DOTNET_ROLL_FORWARD=Major

EXPOSE 34567
EXPOSE 34568
EXPOSE 34569

ENTRYPOINT ["dotnet", "Bakabase.Service.dll"]
