FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["ApiGateway.csproj", "./"]
RUN dotnet restore "ApiGateway.csproj"
COPY . .
# what is /p:UseAppHost=false? It's a workaround to prevent dotnet publish from creating an executable that doesn't work in Linux containers. By default, dotnet publish creates an executable that is meant for Windows, which causes issues when running in a Linux container. Setting UseAppHost to false tells dotnet publish to create a framework-dependent application, which works in both Windows and Linux containers.
RUN dotnet publish "ApiGateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# Set the working directory to /app, which is where our published application will be copied to. This is the directory that will be used when the container starts.
WORKDIR /app
# Copy the published application from the build stage to the final stage. The --from=build flag tells Docker to copy from the build stage, and /app/publish is the directory where the published application is located in the build stage. The . at the end means to copy it to the current working directory in the final stage, which is /app.
COPY --from=build /app/publish .
# The ENTRYPOINT instruction specifies the command that will be run when the container starts. In this case, we're telling it to run the dotnet command with our ApiGateway.dll as an argument, which will start our application.
ENTRYPOINT ["dotnet", "ApiGateway.dll"]