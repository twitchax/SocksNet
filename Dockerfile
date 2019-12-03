FROM mcr.microsoft.com/dotnet/core/sdk:3.0-alpine as builder

WORKDIR /build
COPY src/Core/SocksNet.csproj .
RUN dotnet restore
COPY src/Core/. .

RUN dotnet publish -r linux-musl-x64 -c release /p:PublishSingleFile=true /p:PublishTrimmed=true

FROM mcr.microsoft.com/dotnet/core/runtime-deps:3.0-alpine

COPY --from=builder /build/bin/release/netcoreapp3.0/linux-musl-x64/publish/SocksNet .

ENTRYPOINT ["/SocksNet"]