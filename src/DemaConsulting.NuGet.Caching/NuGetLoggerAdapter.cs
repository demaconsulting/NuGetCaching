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

using Microsoft.Extensions.Logging;
using NuGet.Common;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;
using NuGetLogLevel = NuGet.Common.LogLevel;

namespace DemaConsulting.NuGet.Caching;

/// <summary>
///     Adapts a <see cref="Microsoft.Extensions.Logging.ILogger"/> to the NuGet SDK's
///     <see cref="global::NuGet.Common.ILogger"/> so NuGet diagnostics reach the caller's logger.
/// </summary>
/// <remarks>
///     Level mapping: Debug to Debug, Verbose to Trace, Information to Information, Minimal to
///     Information, Warning to Warning, Error to Error. Only NuGet's own message text is forwarded.
/// </remarks>
internal sealed class NuGetLoggerAdapter : LoggerBase
{
    private readonly ILogger _logger;

    /// <summary>
    ///     Initializes a new instance that forwards to <paramref name="logger"/>.
    /// </summary>
    /// <param name="logger">The destination logger.</param>
    internal NuGetLoggerAdapter(ILogger logger)
        : base(NuGetLogLevel.Debug)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    ///     Creates an adapter for <paramref name="logger"/>, or returns <see langword="null"/> when no
    ///     logger was supplied.
    /// </summary>
    /// <param name="logger">The optional destination logger.</param>
    /// <returns>The adapter, or <see langword="null"/>.</returns>
    internal static NuGetLoggerAdapter? Create(ILogger? logger) => logger == null ? null : new NuGetLoggerAdapter(logger);

    /// <inheritdoc />
    public override void Log(ILogMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _logger.Log(Map(message.Level), "{Message}", message.Message);
    }

    /// <inheritdoc />
    public override Task LogAsync(ILogMessage message)
    {
        Log(message);
        return Task.CompletedTask;
    }

    private static MelLogLevel Map(NuGetLogLevel level) => level switch
    {
        NuGetLogLevel.Debug => MelLogLevel.Debug,
        NuGetLogLevel.Verbose => MelLogLevel.Trace,
        NuGetLogLevel.Information => MelLogLevel.Information,
        NuGetLogLevel.Minimal => MelLogLevel.Information,
        NuGetLogLevel.Warning => MelLogLevel.Warning,
        NuGetLogLevel.Error => MelLogLevel.Error,
        _ => MelLogLevel.Debug,
    };
}
