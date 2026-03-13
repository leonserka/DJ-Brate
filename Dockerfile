FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
 
COPY ["DJBrate.API/DJBrate.API.csproj", "DJBrate.API/"]
RUN dotnet restore "DJBrate.API/DJBrate.API.csproj"
 
COPY . .
WORKDIR "/src/DJBrate.API"
RUN dotnet publish "DJBrate.API.csproj" -c Release -o /app/publish --no-restore
 
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DJBrate.API.dll"]