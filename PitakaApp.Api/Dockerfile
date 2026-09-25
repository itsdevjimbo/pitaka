# Build the deployable application with the SDK. This is deliberately separate
# from the development container's watch output.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "PitakaApp.Api/PitakaApp.Api.csproj"
RUN dotnet publish "PitakaApp.Api/PitakaApp.Api.csproj" -c Release -o /app/publish

# Build the migration bundle from the same source as the API. BuildKit runs this
# stage for each target platform, so the bundle matches the migration image's RID.
FROM build AS migration-build
RUN dotnet tool restore \
    && dotnet ef migrations bundle \
        --project "PitakaApp.Api/PitakaApp.Api.csproj" \
        --startup-project "PitakaApp.Api/PitakaApp.Api.csproj" \
        --configuration Release \
        --output /out/efbundle

# The local API runs from the SDK stage so source changes are watched live.
# Compose bind-mounts the checkout at /workspace and mounts separate build
# output and NuGet cache volumes for the watcher and requested commands.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS development
RUN apt-get update \
    && apt-get install --no-install-recommends -y util-linux \
    && rm -rf /var/lib/apt/lists/* \
    && dotnet tool install --tool-path /usr/local/share/dotnet-tools dotnet-ef --version 9.0.19 \
    && ln -s /usr/local/share/dotnet-tools/dotnet-ef /usr/local/bin/dotnet-ef \
    && mkdir -p /watch/nuget/packages /pdotnet/nuget \
    && chmod -R a+rwX /watch /pdotnet
ENV NUGET_PACKAGES=/watch/nuget/packages
WORKDIR /workspace
CMD ["dotnet", "watch", "--non-interactive", "--project", "PitakaApp.Api/PitakaApp.Api.csproj", "run", "--no-launch-profile", "--property:BaseIntermediateOutputPath=/watch/api/obj/", "--property:BaseOutputPath=/watch/api/bin/", "--property:DefaultItemExcludesInProjectFolder=**/obj/**"]

# The migration image is a framework-dependent EF bundle with the ASP.NET runtime.
# It needs only a connection string at execution time, never a source tree or SDK.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS migrations
WORKDIR /app
COPY --from=migration-build /out/efbundle ./efbundle
ENTRYPOINT ["/app/efbundle"]

# Keep the application image as the default Dockerfile target for contributors.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PitakaApp.Api.dll"]
