@echo off
REM Build and test DemaConsulting NuGet Caching (Windows)

echo Building DemaConsulting NuGet Caching...
dotnet build --configuration Release
if %errorlevel% neq 0 exit /b %errorlevel%

echo Running unit tests...
dotnet test --configuration Release
if %errorlevel% neq 0 exit /b %errorlevel%

echo Build and tests completed successfully!
