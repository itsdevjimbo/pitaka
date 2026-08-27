# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "PitakaApp.Api.csproj"
RUN dotnet publish "PitakaApp.Api.csproj" -c Release -o /app/publish

# dotnet-ef is not part of the SDK image by default — pinned here since this is
# the only place it's installed (migrations are generated/applied via `dev`/
# `migrator`, not locally, so there's no separate host-side manifest to match).
RUN dotnet tool install --global dotnet-ef --version 10.0.11
ENV PATH="${PATH}:/root/.dotnet/tools"

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PitakaApp.Api.dll"]