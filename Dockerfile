FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["PCStore.Core/PCStore.Core.csproj", "PCStore.Core/"]
COPY ["PCStore.Infrastructure/PCStore.Infrastructure.csproj", "PCStore.Infrastructure/"]
COPY ["PCStore.API/PCStore.API.csproj", "PCStore.API/"]
RUN dotnet restore "PCStore.API/PCStore.API.csproj"
COPY . .
WORKDIR /src/PCStore.API
RUN dotnet publish "PCStore.API.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "PCStore.API.dll"]
