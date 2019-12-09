FROM mcr.microsoft.com/dotnet/core/sdk:3.1-alpine as builder

WORKDIR /build
COPY src/Core/SocksNet.csproj .
RUN dotnet restore
COPY src/Core/. .

RUN dotnet publish -r linux-musl-x64 -c release /p:PublishSingleFile=true /p:PublishTrimmed=true /p:UseAppHost=true

FROM mcr.microsoft.com/dotnet/core/runtime-deps:3.1-alpine

COPY --from=builder /build/bin/release/netcoreapp3.0/linux-musl-x64/publish/SocksNet .

ENTRYPOINT ["/SocksNet"]