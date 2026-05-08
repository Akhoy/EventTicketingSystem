# --- STAGE 1: THE BUILD ENVIRONMENT ---
# We pull the heavy .NET SDK image. This contains compilers, NuGet, and everything needed to build code.
# We label this stage "build" so we can grab files from it later.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# Set the working directory inside the container to /src
WORKDIR /src

# We copy ONLY the project file (.csproj) first, before copying the rest of the code.
# WHY? Docker caches layers. If you change a line of C# code but don't change your NuGet packages, 
# Docker uses the cached 'dotnet restore' layer, making your builds take 2 seconds instead of 20.
# If a layer is forced to rebuild, EVERY layer below it is also forced to rebuild. The cache is broken for the rest of the file. By separating the .csproj file from the rest of the code, you protect the heavy dotnet restore step from being triggered every time you make a tiny code change.
COPY ["Booking.API.csproj", "./"]
RUN dotnet restore "Booking.API.csproj"

# Now that dependencies are restored, copy the rest of your C# files into the container.
COPY . .

# Compile the application.
# '-c Release': Builds the optimized version (strips out debug symbols).
# '-o /app/publish': Outputs the final, compiled DLLs into this specific folder.
RUN dotnet publish "Booking.API.csproj" -c Release -o /app/publish /p:UseAppHost=false


# --- STAGE 2: THE RUNTIME ENVIRONMENT ---
# We abandon the heavy SDK and start fresh with the ASP.NET runtime image.
# This image is tiny because it only knows how to RUN code, not compile it.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# Set the working directory for our running app
WORKDIR /app

# Tell Docker that this container will listen on port 8080 internally.
# (This is the default port for .NET 10 minimal APIs inside a container).
EXPOSE 8080

# The magic step: We reach back into the "build" stage and copy the compiled DLLs 
# from '/app/publish' into our current lightweight '/app' directory.
COPY --from=build /app/publish .

# Define the command that executes when the container boots up.
ENTRYPOINT ["dotnet", "Booking.API.dll"]