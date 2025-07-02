# syntax=docker/dockerfile:1.4

# =========================
# 1. dotnet build
# =========================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-dotnet
WORKDIR /src

COPY Bakabase.Infrastructures Bakabase.Infrastructures 
COPY LazyMortal LazyMortal
COPY Bakabase Bakabase

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore "Bakabase/src/Bakabase.Service/Bakabase.Service.csproj"

RUN dotnet publish "Bakabase/src/Bakabase.Service/Bakabase.Service.csproj" -c Release -o /app/publish

# =========================
# 2. node build
# =========================
FROM node:22-alpine AS build-node
WORKDIR /app

# 只复制依赖相关文件
COPY Bakabase/src/ClientApp ./

RUN corepack enable yarn
RUN yarn -v

# 用 BuildKit 缓存 yarn
RUN --mount=type=cache,target=.yarn/cache yarn install

RUN yarn build

# =========================
# 3. runtime
# =========================
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 34567
EXPOSE 34568
EXPOSE 34569

# Final image
FROM base AS final
WORKDIR /app
COPY Bakabase.CI/copy .
COPY --from=build-dotnet /app/publish .
COPY --from=build-node /app/build ./ClientApp/build
ENTRYPOINT ["dotnet", "Bakabase.Service.dll"]