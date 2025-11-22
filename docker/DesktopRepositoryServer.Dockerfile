FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY --link DesktopRepositoryServer/*.csproj DesktopRepositoryServer/
COPY --link *.props .
RUN dotnet restore DesktopRepositoryServer/DesktopRepositoryServer.csproj

COPY --link DesktopRepositoryServer/. DesktopRepositoryServer/

RUN dotnet publish --no-restore -c Release DesktopRepositoryServer/DesktopRepositoryServer.csproj -o /app

# final is the final runtime stage for running the app
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

COPY --link --from=build /app .
COPY docker/appsettings.Container.json /app/appsettings.Container.json
COPY docker/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

RUN apk update && apk add --no-cache openssl


ENTRYPOINT ["/bin/ash", "/entrypoint.sh"]