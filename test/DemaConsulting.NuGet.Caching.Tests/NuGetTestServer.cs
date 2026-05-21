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

using NuGet.Configuration;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace DemaConsulting.NuGet.Caching.Tests;

/// <summary>
///     Wraps a <see cref="WireMockServer"/> instance and exposes helper methods for configuring
///     NuGet v3 and v2 feed endpoints used by the local-integration tests.
/// </summary>
/// <remarks>
///     Each test creates its own <see cref="NuGetTestServer"/> instance on a random port so that
///     multiple tests can run in parallel without port conflicts. The server is stopped and its
///     temporary configuration directory is deleted when the instance is disposed via
///     <see cref="DisposeAsync"/>.
/// </remarks>
internal sealed class NuGetTestServer : IAsyncDisposable
{
    /// <summary>The underlying WireMock server instance.</summary>
    private readonly WireMockServer _server;

    /// <summary>
    ///     Temporary directory used to write nuget.config files for
    ///     <see cref="CreateSettings(string, string)"/> calls.
    /// </summary>
    private readonly string _tempConfigDir;

    /// <summary>
    ///     Initializes a new <see cref="NuGetTestServer"/> instance by starting a
    ///     <see cref="WireMockServer"/> on a randomly assigned port.
    /// </summary>
    public NuGetTestServer()
    {
        // Start on a random available port so tests can run in parallel
        _server = WireMockServer.Start();

        // Create a dedicated temp directory to hold the test nuget.config files written
        // by CreateSettings — each test instance gets its own isolated directory
        _tempConfigDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempConfigDir);
    }

    /// <summary>
    ///     Gets the base URL of the mock server (e.g. <c>http://localhost:54321</c>).
    /// </summary>
    internal string BaseUrl => _server.Url!;

    /// <summary>
    ///     Gets the NuGet v3 service index URL (e.g. <c>http://localhost:54321/index.json</c>).
    /// </summary>
    internal string IndexUrl => $"{BaseUrl}/index.json";

    /// <summary>
    ///     Gets the read-only list of HTTP request log entries recorded by the server since it started.
    /// </summary>
    /// <remarks>
    ///     Tests use this property to assert whether specific requests were (or were not) made
    ///     to the server — for example to verify a cache-hit path made no HTTP calls.
    /// </remarks>
    internal IEnumerable<object> LogEntries => _server.LogEntries;

    /// <summary>
    ///     Registers the NuGet v3 flat-container endpoints needed to serve a single package version.
    /// </summary>
    /// <remarks>
    ///     Three endpoints are registered:
    ///     <list type="number">
    ///         <item><description>
    ///             <c>GET /index.json</c> — the NuGet v3 service index, advertising the flat-container
    ///             base URL so the NuGet SDK can discover the <c>PackageBaseAddress/3.0.0</c> resource.
    ///         </description></item>
    ///         <item><description>
    ///             <c>GET /v3-flatcontainer/{id}/index.json</c> — the package version list endpoint
    ///             returning a single-element <c>versions</c> array containing <paramref name="version"/>.
    ///         </description></item>
    ///         <item><description>
    ///             <c>GET /v3-flatcontainer/{id}/{ver}/{id}.{ver}.nupkg</c> — serves the raw .nupkg
    ///             bytes supplied by <paramref name="nupkgBytes"/>.
    ///         </description></item>
    ///     </list>
    ///     All path segments use lower-cased identifiers to match NuGet flat-container conventions.
    /// </remarks>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="version">The package version string.</param>
    /// <param name="nupkgBytes">The raw .nupkg bytes to serve for the download endpoint.</param>
    internal void RegisterV3Package(string packageId, string version, byte[] nupkgBytes)
    {
        var id = packageId.ToLowerInvariant();
        var ver = version.ToLowerInvariant();

        // Register the v3 service index so the NuGet SDK can discover the flat-container resource
        _server
            .Given(Request.Create().WithPath("/index.json").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody(BuildV3ServiceIndexJson())
                .WithHeader("Content-Type", "application/json"));

        // Register the version list so the SDK knows this version exists in the feed
        _server
            .Given(Request.Create().WithPath($"/v3-flatcontainer/{id}/index.json").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody($"{{\"versions\":[\"{ver}\"]}}")
                .WithHeader("Content-Type", "application/json"));

        // Register the nupkg download endpoint serving the supplied package bytes
        _server
            .Given(Request.Create()
                .WithPath($"/v3-flatcontainer/{id}/{ver}/{id}.{ver}.nupkg")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody(nupkgBytes)
                .WithHeader("Content-Type", "application/zip"));
    }

    /// <summary>
    ///     Registers the NuGet v3 flat-container version list and service index for a package,
    ///     but makes the .nupkg download endpoint return HTTP 500 to simulate a protocol error
    ///     during the download phase.
    /// </summary>
    /// <remarks>
    ///     Use this method for tests that verify a <c>NuGetProtocolException</c>
    ///     thrown during <c>CopyNupkgToStreamAsync</c> is surfaced as
    ///     <see cref="InvalidOperationException"/> by <c>NuGetCache.EnsureCachedAsync</c>.
    /// </remarks>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="version">The package version string.</param>
    internal void RegisterV3PackageWithDownloadProtocolError(string packageId, string version)
    {
        var id = packageId.ToLowerInvariant();
        var ver = version.ToLowerInvariant();

        // Register the service index and version list so resource resolution succeeds
        _server
            .Given(Request.Create().WithPath("/index.json").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody(BuildV3ServiceIndexJson())
                .WithHeader("Content-Type", "application/json"));

        _server
            .Given(Request.Create().WithPath($"/v3-flatcontainer/{id}/index.json").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody($"{{\"versions\":[\"{ver}\"]}}")
                .WithHeader("Content-Type", "application/json"));

        // Make the download endpoint return HTTP 500 to trigger NuGetProtocolException
        _server
            .Given(Request.Create()
                .WithPath($"/v3-flatcontainer/{id}/{ver}/{id}.{ver}.nupkg")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithBody("Internal Server Error"));
    }

    /// <summary>
    ///     Registers the NuGet v3 flat-container version list and service index for a package,
    ///     but makes the .nupkg download endpoint drop the connection to simulate a network failure
    ///     during the download phase.
    /// </summary>
    /// <remarks>
    ///     Use this method for tests that verify an <see cref="System.Net.Http.HttpRequestException"/>
    ///     thrown during <c>CopyNupkgToStreamAsync</c> is silently swallowed and ultimately surfaces
    ///     as <see cref="InvalidOperationException"/> (package-not-found) by
    ///     <c>NuGetCache.EnsureCachedAsync</c>.
    /// </remarks>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="version">The package version string.</param>
    internal void RegisterV3PackageWithDownloadNetworkFailure(string packageId, string version)
    {
        var id = packageId.ToLowerInvariant();
        var ver = version.ToLowerInvariant();

        // Register the service index and version list so resource resolution succeeds
        _server
            .Given(Request.Create().WithPath("/index.json").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody(BuildV3ServiceIndexJson())
                .WithHeader("Content-Type", "application/json"));

        _server
            .Given(Request.Create().WithPath($"/v3-flatcontainer/{id}/index.json").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody($"{{\"versions\":[\"{ver}\"]}}")
                .WithHeader("Content-Type", "application/json"));

        // Configure the download endpoint to return an empty response, causing HttpRequestException in HttpClient
        _server
            .Given(Request.Create()
                .WithPath($"/v3-flatcontainer/{id}/{ver}/{id}.{ver}.nupkg")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithFault(FaultType.EMPTY_RESPONSE));
    }

    /// <summary>
    ///     Makes the <c>/index.json</c> endpoint return HTTP 500 to simulate a v3 protocol error
    ///     on the service index, forcing the NuGet SDK to throw
    ///     <c>NuGetProtocolException</c> during service index fetch.
    /// </summary>
    /// <remarks>
    ///     This is the first step in the v2-fallback test scenario: after the v3 service index fails
    ///     with a protocol error, <see cref="NuGetCache"/> strips <c>/index.json</c> and retries the
    ///     base URL as a v2 OData feed.
    /// </remarks>
    internal void SimulateV3IndexProtocolError()
    {
        // Returning 500 causes NuGet SDK to throw NuGetProtocolException when fetching the index
        _server
            .Given(Request.Create().WithPath("/index.json").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithBody("Internal Server Error"));
    }

    /// <summary>
    ///     Makes the base URL endpoint (<c>/</c>) return HTTP 500 to simulate a v2 protocol error
    ///     when the NuGet SDK tries the base URL as a v2 OData fallback.
    /// </summary>
    /// <remarks>
    ///     When both the v3 service index and the v2 fallback base URL return HTTP 500, the NuGet SDK
    ///     throws <c>NuGetProtocolException</c> for both candidates.
    ///     <see cref="NuGetCache"/> accumulates the first protocol error message (referencing the
    ///     original configured URL) and includes it in the final <see cref="InvalidOperationException"/>
    ///     — allowing callers to distinguish a feed misconfiguration from a genuine package-absent outcome.
    /// </remarks>
    internal void SimulateBaseUrlProtocolError()
    {
        // Returning 500 on the base URL causes NuGetProtocolException rather than HttpRequestException,
        // so the accumulated error message is preserved and included in the final exception
        _server
            .Given(Request.Create().WithPath("/").UsingAnyMethod())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithBody("Internal Server Error"));
    }

    /// <summary>
    ///     Configures the v2 OData endpoints needed to serve a single package version, enabling the
    ///     NuGet SDK to discover, resolve, and download the package over the v2 feed protocol.
    /// </summary>
    /// <remarks>
    ///     Four endpoints are registered to satisfy the NuGet v2 OData protocol:
    ///     <list type="number">
    ///         <item><description>
    ///             <c>GET /</c> — the OData service document, identifying the <c>Packages</c>
    ///             entity set so the NuGet SDK recognizes the endpoint as a v2 feed.
    ///         </description></item>
    ///         <item><description>
    ///             <c>GET /$metadata</c> — the minimal EDMX metadata document describing the
    ///             <c>V2FeedPackage</c> entity type and the <c>FindPackagesById</c> function import.
    ///         </description></item>
    ///         <item><description>
    ///             <c>GET /FindPackagesById()</c> — the OData function that returns an Atom feed
    ///             entry for the package, including the download content URL.
    ///         </description></item>
    ///         <item><description>
    ///             A wildcard download endpoint matching any request whose path starts with
    ///             <c>/Packages(</c>, serving the raw .nupkg bytes.
    ///         </description></item>
    ///     </list>
    /// </remarks>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="version">The package version string.</param>
    /// <param name="nupkgBytes">The raw .nupkg bytes to serve for the download endpoint.</param>
    internal void SimulateV2Package(string packageId, string version, byte[] nupkgBytes)
    {
        // Register the OData service document so the NuGet SDK recognizes this as a v2 feed
        _server
            .Given(Request.Create().WithPath("/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody(BuildV2ServiceDocumentXml())
                .WithHeader("Content-Type", "application/xml"));

        // Register the OData metadata document describing the V2FeedPackage entity type
        _server
            .Given(Request.Create().WithPath("/$metadata").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody(BuildV2MetadataXml(packageId))
                .WithHeader("Content-Type", "application/xml"));

        // Register the FindPackagesById OData function endpoint; the NuGet SDK appends
        // query parameters (id, $filter, semVerLevel) so match on path only
        _server
            .Given(Request.Create().WithPath("/FindPackagesById()").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody(BuildV2FindPackagesByIdXml(packageId, version))
                .WithHeader("Content-Type", "application/atom+xml; charset=utf-8"));

        // Register the download endpoint; the NuGet SDK requests the URL from the content
        // element's src attribute in the Atom feed entry
        _server
            .Given(Request.Create()
                .WithPath($"/Packages(Id='{packageId}',Version='{version}')/$value")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody(nupkgBytes)
                .WithHeader("Content-Type", "application/zip"));
    }

    /// <summary>
    ///     Makes the <c>/index.json</c> endpoint return an empty response to simulate a network-level
    ///     failure (e.g. connection dropped) when the NuGet SDK fetches the service index.
    /// </summary>
    /// <remarks>
    ///     An empty HTTP response causes the NuGet SDK to throw <see cref="System.Net.Http.HttpRequestException"/>.
    ///     <see cref="NuGetCache"/> treats network failures as non-actionable and
    ///     skips the source silently — the final exception is the base package-not-found message without any
    ///     per-source diagnostic.
    /// </remarks>
    internal void SimulateNetworkFailureOnIndex()
    {
        // Return an empty response to trigger HttpRequestException in the NuGet SDK
        _server
            .Given(Request.Create().WithPath("/index.json").UsingGet())
            .RespondWith(Response.Create()
                .WithFault(FaultType.EMPTY_RESPONSE));
    }

    /// <summary>
    ///     Makes the base URL endpoint (<c>/</c>) return an empty response to simulate a network-level
    ///     failure when the NuGet SDK tries the base URL as a v2 OData fallback.
    /// </summary>
    /// <remarks>
    ///     An empty HTTP response at the base URL causes the NuGet SDK to throw
    ///     <see cref="System.Net.Http.HttpRequestException"/>. When this happens as part of the v2
    ///     fallback attempt, <see cref="NuGetCache"/> treats it as non-actionable: the accumulated
    ///     v3 protocol error message is discarded and the final exception contains only the base
    ///     package-not-found message.
    /// </remarks>
    internal void SimulateNetworkFailureOnFallback()
    {
        // Return an empty response to trigger HttpRequestException on the v2 fallback base URL
        _server
            .Given(Request.Create().WithPath("/").UsingAnyMethod())
            .RespondWith(Response.Create()
                .WithFault(FaultType.EMPTY_RESPONSE));
    }

    /// <summary>
    ///     Creates an <see cref="ISettings"/> instance backed by a temporary <c>nuget.config</c>
    ///     file that configures exactly one package source at <paramref name="sourceUrl"/> and
    ///     sets the global packages folder to <paramref name="globalPackagesFolder"/>.
    /// </summary>
    /// <remarks>
    ///     The generated config includes <c>&lt;clear /&gt;</c> inside <c>&lt;packageSources&gt;</c>
    ///     to prevent any machine-level or user-level NuGet sources from being inherited. This ensures
    ///     that tests exercise only the configured WireMock server and do not accidentally download
    ///     packages from the real internet.
    ///     <para>
    ///         The config file is written to the server's private temporary directory and cleaned
    ///         up automatically when <see cref="DisposeAsync"/> is called.
    ///     </para>
    /// </remarks>
    /// <param name="globalPackagesFolder">
    ///     Absolute path to the directory to use as the NuGet global packages folder.
    ///     Each test should supply its own isolated temp directory here.
    /// </param>
    /// <param name="sourceUrl">
    ///     The URL of the NuGet source to configure (typically <see cref="IndexUrl"/>).
    /// </param>
    /// <returns>An <see cref="ISettings"/> instance loaded from the generated config file.</returns>
    internal ISettings CreateSettings(string globalPackagesFolder, string sourceUrl)
    {
        // Write a minimal nuget.config that points at the WireMock server with a clear
        // source list so no machine-level sources bleed into the test
        var configPath = Path.Combine(_tempConfigDir, $"nuget-{Guid.NewGuid():N}.config");
        File.WriteAllText(configPath, $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="{globalPackagesFolder}" />
              </config>
              <packageSources>
                <clear />
                <add key="test-source" value="{sourceUrl}" />
              </packageSources>
            </configuration>
            """);

        // Load only this specific config file; the caller receives settings with a single
        // source and the custom global packages folder already configured
        return Settings.LoadSpecificSettings(
            Path.GetDirectoryName(configPath)!,
            Path.GetFileName(configPath));
    }

    /// <summary>
    ///     Creates an <see cref="ISettings"/> instance backed by a temporary <c>nuget.config</c>
    ///     file that configures two package sources and sets the global packages folder.
    /// </summary>
    /// <remarks>
    ///     Use this overload for multi-source tests (e.g. verifying that a package found only in
    ///     the second source is still downloaded successfully). As with the single-source overload,
    ///     <c>&lt;clear /&gt;</c> prevents machine-level sources from being inherited.
    /// </remarks>
    /// <param name="globalPackagesFolder">
    ///     Absolute path to the directory to use as the NuGet global packages folder.
    /// </param>
    /// <param name="primarySourceUrl">URL of the first (primary) package source.</param>
    /// <param name="secondarySourceUrl">URL of the second (secondary) package source.</param>
    /// <returns>An <see cref="ISettings"/> instance loaded from the generated config file.</returns>
    internal ISettings CreateSettings(
        string globalPackagesFolder,
        string primarySourceUrl,
        string secondarySourceUrl)
    {
        // Write a nuget.config with two sources; the primary is tried first
        var configPath = Path.Combine(_tempConfigDir, $"nuget-{Guid.NewGuid():N}.config");
        File.WriteAllText(configPath, $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="{globalPackagesFolder}" />
              </config>
              <packageSources>
                <clear />
                <add key="test-source-primary" value="{primarySourceUrl}" />
                <add key="test-source-secondary" value="{secondarySourceUrl}" />
              </packageSources>
            </configuration>
            """);

        return Settings.LoadSpecificSettings(
            Path.GetDirectoryName(configPath)!,
            Path.GetFileName(configPath));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        // Stop and dispose the WireMock server to release the port and associated resources
        _server.Stop();
        _server.Dispose();

        // Remove the temporary config directory and all nuget.config files written during the test
        if (Directory.Exists(_tempConfigDir))
        {
            Directory.Delete(_tempConfigDir, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Builds the NuGet v3 service index JSON advertising the flat-container base URL.
    /// </summary>
    /// <returns>A JSON string with the <c>PackageBaseAddress/3.0.0</c> resource entry.</returns>
    private string BuildV3ServiceIndexJson() => $$"""
        {
          "version": "3.0.0",
          "resources": [
            {
              "@id": "{{BaseUrl}}/v3-flatcontainer/",
              "@type": "PackageBaseAddress/3.0.0",
              "@comment": "Base URL for NuGet package content"
            }
          ]
        }
        """;

    /// <summary>
    ///     Builds the minimal OData v2 service document XML for the feed root (<c>GET /</c>).
    /// </summary>
    /// <returns>An XML string representing the Atom Publishing Protocol service document.</returns>
    private string BuildV2ServiceDocumentXml() => $"""
        <?xml version="1.0" encoding="utf-8" standalone="yes"?>
        <service xml:base="{BaseUrl}/"
                 xmlns:atom="http://www.w3.org/2005/Atom"
                 xmlns="http://www.w3.org/2007/app">
          <workspace>
            <atom:title>Default</atom:title>
            <collection href="Packages">
              <atom:title>Packages</atom:title>
            </collection>
          </workspace>
        </service>
        """;

    /// <summary>
    ///     Builds the minimal OData EDMX metadata XML that the NuGet SDK needs to recognize
    ///     the <c>V2FeedPackage</c> entity type and the <c>FindPackagesById</c> function import.
    /// </summary>
    /// <param name="packageId">
    ///     Unused at present; reserved for future per-package metadata customization.
    /// </param>
    /// <returns>An EDMX XML string describing the v2 feed schema.</returns>
    private static string BuildV2MetadataXml(string packageId)
    {
        // Suppress unused parameter warning - reserved for potential future use
        _ = packageId;

        return """
            <?xml version="1.0" encoding="utf-8"?>
            <edmx:Edmx Version="1.0"
                       xmlns:edmx="http://schemas.microsoft.com/ado/2007/06/edmx">
              <edmx:DataServices m:DataServiceVersion="2.0"
                                 m:MaxDataServiceVersion="3.0"
                                 xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata">
                <Schema Namespace="NuGetGallery"
                        xmlns="http://schemas.microsoft.com/ado/2009/11/edm">
                  <EntityType Name="V2FeedPackage" m:HasStream="true">
                    <Key>
                      <PropertyRef Name="Id"/>
                      <PropertyRef Name="Version"/>
                    </Key>
                    <Property Name="Id" Type="Edm.String" Nullable="false"/>
                    <Property Name="Version" Type="Edm.String" Nullable="false"/>
                    <Property Name="NormalizedVersion" Type="Edm.String"/>
                    <Property Name="IsLatestVersion" Type="Edm.Boolean" Nullable="false"/>
                    <Property Name="IsAbsoluteLatestVersion" Type="Edm.Boolean" Nullable="false"/>
                    <Property Name="Listed" Type="Edm.Boolean"/>
                    <Property Name="Description" Type="Edm.String"/>
                    <Property Name="Title" Type="Edm.String"/>
                  </EntityType>
                  <EntityContainer Name="V2FeedContext"
                                   m:IsDefaultEntityContainer="true">
                    <EntitySet Name="Packages"
                               EntityType="NuGetGallery.V2FeedPackage"/>
                    <FunctionImport Name="FindPackagesById"
                                    EntitySet="Packages"
                                    ReturnType="Collection(NuGetGallery.V2FeedPackage)"
                                    m:HttpMethod="GET">
                      <Parameter Name="id" Type="Edm.String" Mode="In"/>
                    </FunctionImport>
                  </EntityContainer>
                </Schema>
              </edmx:DataServices>
            </edmx:Edmx>
            """;
    }

    /// <summary>
    ///     Builds the Atom feed XML returned by the <c>/FindPackagesById()</c> endpoint for a
    ///     single package entry, including the download content URL.
    /// </summary>
    /// <param name="packageId">The NuGet package identifier.</param>
    /// <param name="version">The package version string.</param>
    /// <returns>An Atom XML feed string with one entry for the requested package version.</returns>
    private string BuildV2FindPackagesByIdXml(string packageId, string version) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <feed xml:base="{BaseUrl}/"
              xmlns="http://www.w3.org/2005/Atom"
              xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices"
              xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata">
          <id>http://schemas.datacontract.org/2004/07/</id>
          <title/>
          <entry>
            <id>{BaseUrl}/Packages(Id='{packageId}',Version='{version}')</id>
            <title type="text">{packageId}</title>
            <content type="application/zip"
                     src="{BaseUrl}/Packages(Id='{packageId}',Version='{version}')/$value"/>
            <m:properties>
              <d:Id>{packageId}</d:Id>
              <d:Version>{version}</d:Version>
              <d:NormalizedVersion>{version}</d:NormalizedVersion>
              <d:IsLatestVersion m:type="Edm.Boolean">true</d:IsLatestVersion>
              <d:IsAbsoluteLatestVersion m:type="Edm.Boolean">true</d:IsAbsoluteLatestVersion>
              <d:Listed m:type="Edm.Boolean">true</d:Listed>
            </m:properties>
          </entry>
        </feed>
        """;
}
