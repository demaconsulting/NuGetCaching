// Copyright (c) DEMA Consulting
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Runtime.CompilerServices;

namespace DemaConsulting.NuGet.Caching.Tests;

/// <summary>
///     Gives every test process a private, empty NuGet HTTP cache directory.
/// </summary>
/// <remarks>
///     NuGet caches service indexes, version lists and v2 feed pages on disk, keyed by the source URL
///     (which includes the port). The local-integration tests start WireMock servers on random ephemeral
///     ports, so a port reused by a later test run would otherwise be served stale responses cached by an
///     earlier run from the user-wide HTTP cache - for example a previously cached successful v3 service
///     index masking the simulated <c>/index.json</c> failure, so the v2 fallback is never attempted.
///     Pointing <c>NUGET_HTTP_CACHE_PATH</c> at a fresh per-process directory removes that cross-run
///     state; tests that need a cache hit still get one within the same process.
/// </remarks>
internal static class HttpCacheIsolation
{
    /// <summary>
    ///     Redirects the NuGet HTTP cache to a unique temporary directory before any test runs.
    /// </summary>
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "nuget-caching-tests-http-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheDir);
        Environment.SetEnvironmentVariable("NUGET_HTTP_CACHE_PATH", cacheDir);

        // Best-effort cleanup so repeated runs do not accumulate temporary cache directories
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(cacheDir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignore: a leftover temporary directory is harmless
            }
        };
    }
}
