# Test Data

## newtonsoft.json.13.0.1.nupkg

A real, unmodified copy of the Newtonsoft.Json 13.0.1 package from nuget.org, used by
`NuGetCacheSignatureTests` as a validly signed package.

- **Source**: <https://www.nuget.org/packages/Newtonsoft.Json/13.0.1>
- **License**: MIT, copyright James Newton-King (see `LICENSE.md` inside the package)
- **SHA-256 of the file**: `2B6B52556E27E1B7913F33EEDEB95568110C746BD64AFFF74357F1683878323A`
- **Signatures**: signed by the author and countersigned by the nuget.org repository
- **Author certificate** (SHA-256 fingerprint):
  `A3AF7AF11EBB8EF729D2D91548509717E7E0FF55A129ABC3AEAA8A6940267641`
- **Repository certificate** (SHA-256 fingerprint):
  `5A2901D6ADA3D18260B9C6DFE2133C95D74B9EEF6AE0E5DC334C8454D1477DF4`

## Why this package

Signing needs a trusted code-signing certificate and NuGet's signing APIs are internal, so a signed package
cannot be generated in a test. This version is the smallest one carrying both an author and a repository
signature.

## Do not modify

The signature covers the package hash, so any change (including line-ending conversion or trimming) makes the
signature invalid. The `.gitattributes` file marks `*.nupkg` as binary.

## Certificate validity

Both signing certificates have expired. The signatures remain acceptable because NuGet validates them against
their trusted timestamps, so the fixture does not expire over time. Verification needs the DigiCert root
certificates in the operating system trust store; the tests skip, with a clear reason, in an environment where
those roots are missing. The tests set `NUGET_CERT_REVOCATION_MODE=offline`, so they do not need network
access for revocation checking.
