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

using Xunit;

// Several tests (e.g. NuGetCache_EnsureCachedAsync_DefaultRegistrar_RegistersRealCredentialService
// in NuGetCacheServerTests) observe and reset the NuGet SDK's process-wide, shared static
// HttpHandlerResourceV3.CredentialService property to prove that the real, default credential
// registrar performs a genuine null-to-non-null registration transition. Several OTHER test
// classes (e.g. NuGetCachingTests, NuGetCacheTests) also invoke EnsureCachedAsync via the same
// real, default credential registrar, which reads/writes that same shared static state. Because
// xUnit runs different test classes (collections) in parallel by default, disable test-collection
// parallelization for this assembly so no test can observe a torn or racing view of that shared
// static NuGet SDK state.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
