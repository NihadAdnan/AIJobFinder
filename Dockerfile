# Base runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["FindJob/FindJob.csproj", "FindJob/"]
RUN dotnet restore "FindJob/FindJob.csproj"

COPY . .
WORKDIR "/src/FindJob"
RUN dotnet build "FindJob.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "FindJob.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final runtime image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FindJob.dll"]
