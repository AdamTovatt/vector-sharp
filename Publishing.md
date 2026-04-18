# Publishing NuGet Packages

Packages are published to nuget.org automatically via GitHub Actions when a version tag is pushed.

## Tag Format

```
{package}-v{version}
```

Where `{package}` is the lowercase package name and `{version}` is a valid semver version.

### Available Packages

| Tag prefix | NuGet package |
|------------|--------------|
| `storage` | VectorSharp.Storage |
| `embedding` | VectorSharp.Embedding |
| `nomic-embed` | VectorSharp.Embedding.NomicEmbed |

## Publishing a Package

```bash
git tag storage-v1.0.0
git push origin storage-v1.0.0
```

This triggers the publish workflow which will build, run tests, pack the specified project with the given version, and push to nuget.org.

## Examples

```bash
# Publish VectorSharp.Storage 1.0.0
git tag storage-v1.0.0
git push origin storage-v1.0.0

# Publish VectorSharp.Storage 1.1.0 (independent of other packages)
git tag storage-v1.1.0
git push origin storage-v1.1.0

# Publish VectorSharp.Embedding 1.0.0 (independent of other packages)
git tag embedding-v1.0.0
git push origin embedding-v1.0.0

# Publish VectorSharp.Embedding.NomicEmbed 1.0.0
git tag nomic-embed-v1.0.0
git push origin nomic-embed-v1.0.0
```

## Prerequisites

A `NUGET_API_KEY` secret must be configured in the repository settings (Settings > Secrets and variables > Actions). The key can be generated at https://www.nuget.org/account/apikeys.

## Adding a New Package

1. Add the project to the solution
2. Add a new case in `.github/workflows/publish.yml` mapping the tag prefix to the project name
3. Update the table above
