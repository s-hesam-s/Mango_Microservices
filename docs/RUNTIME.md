# Runtime and dependency maintenance

All application and test projects target **.NET 10 LTS**. Install the latest supported .NET 10 SDK and runtime patch. `global.json` permits stable .NET 10 feature-band updates; CI installs `10.0.x`. A successful source build does not update a developer machine's installed runtime.

## Dependency policy

`Directory.Build.props` audits direct and transitive NuGet dependencies. Restore fails on advisory severities Low through Critical, and when vulnerability data cannot be obtained. Feed access is required; an unavailable audit is not a clean result. The check uses the advisories available at restore time and does not certify vendored JavaScript, operating-system packages or a deployed host.

SQL Server projects pin `System.Security.Cryptography.Pkcs` 10.0.14 to override the vulnerable version otherwise selected by SqlClient. Projects using older Stripe packages explicitly resolve Newtonsoft.Json 13.0.4. Scaffolding projects pin patched NuGet.Protocol/NuGet.Packaging 6.12.5. These are focused transitive fixes, rather than provider API rewrites.

```powershell
dotnet restore
dotnet list package --vulnerable --include-transitive --no-restore
```

For repositories with multiple entry points, pass the solution or project path shown in the README. Build and isolated tests do not run database migrations, seeds, payments, email or cloud consumers. Review the configured development targets before starting the original hosts.

## References

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [NuGet audit](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages)

## Explicit DTO mapping

AutoMapper and its DI extension have been removed. `MappingConfig` now contains ordinary typed methods with explicit property assignments. Controllers and repositories call those methods directly through DI. There is no reflection mapper, runtime mapping registry or license-key requirement. Collection methods materialize DTOs, and nested order details are converted without serializing entity back-references. Public identity DTOs contain only their declared public fields.

The existing authorization/business regressions remain, with additional mapping-contract checks where relevant. No database migration was generated for this change.
