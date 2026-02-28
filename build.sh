#!/usr/bin/env bash
# Build and test DemaConsulting NuGet Caching

set -e  # Exit on error

echo "🔧 Building DemaConsulting NuGet Caching..."
dotnet build --configuration Release

echo "🧪 Running unit tests..."
dotnet test --configuration Release

echo "✨ Build and tests completed successfully!"
