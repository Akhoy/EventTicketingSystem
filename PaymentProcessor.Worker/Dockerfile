FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["PaymentProcessor.Worker.csproj", "./"]
RUN dotnet restore "PaymentProcessor.Worker.csproj"
COPY . .
RUN dotnet publish "PaymentProcessor.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PaymentProcessor.Worker.dll"]