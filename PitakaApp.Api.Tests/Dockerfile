# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "PitakaApp.Api.Tests/PitakaApp.Api.Tests.csproj"
ENTRYPOINT ["dotnet", "test", "PitakaApp.Api.Tests/PitakaApp.Api.Tests.csproj"]