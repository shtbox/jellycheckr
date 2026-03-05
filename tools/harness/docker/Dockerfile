# syntax=docker/dockerfile:1.6
ARG JELLYFIN_VERSION=10.11.6
ARG DOTNET_TARGET_FRAMEWORK=net9.0

FROM node:20-bookworm-slim AS node-runtime

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS plugin-build
ARG JELLYFIN_VERSION
ARG DOTNET_TARGET_FRAMEWORK

COPY --from=node-runtime /usr/local/bin/node /usr/local/bin/node
COPY --from=node-runtime /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s /usr/local/lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
    && ln -s /usr/local/lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx

WORKDIR /src
COPY . .
RUN dotnet publish apps/server-plugin/src/Jellycheckr.Server/Jellycheckr.Server.csproj \
    -c Release \
    -f ${DOTNET_TARGET_FRAMEWORK} \
    -p:TargetFrameworks=${DOTNET_TARGET_FRAMEWORK} \
    -p:JellyfinPackageVersion=${JELLYFIN_VERSION} \
    -p:JellycheckrCreatePluginZip=false \
    -o /out/plugin

FROM jellyfin/jellyfin:${JELLYFIN_VERSION}
RUN mkdir -p /config/plugins/JellycheckrAysw
COPY --from=plugin-build /out/plugin/ /config/plugins/JellycheckrAysw/
