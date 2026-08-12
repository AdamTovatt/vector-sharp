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
| `chunking` | VectorSharp.Chunking |
| `reranking` | VectorSharp.Reranking |

## Publishing a Package

```bash
git tag storage-v1.0.0
git push origin storage-v1.0.0
```

This triggers the publish workflow which will build, run tests, pack the specified project with the given version, and push to nuget.org.

Packages are versioned independently: a tag names one package and one version, and publishing one
leaves the others alone. Take the prefix from the table above.

### The tag is what sets the version

The workflow builds and packs with `-p:Version=<the version in the tag>`, which overrides the
project file's `<Version>` for every project in the build. **The tag decides what is published;
the project file does not.** `embedding-v1.0.3` shipped from a project file that read
`<Version>1.0.0</Version>`, and nothing noticed.

Keep `<Version>` in step with the last tag anyway, and bump it in the same change that adds the
behaviour it names. It is what a local `dotnet pack` produces, what a developer reads to see what
is in the working tree, and how a reviewer tells a patch from a minor.

## Prerequisites

A `NUGET_API_KEY` secret must be configured in the repository settings (Settings > Secrets and variables > Actions). The key can be generated at https://www.nuget.org/account/apikeys.

## Adding a New Package

1. Add both the package project and its test project to `VectorSharp.slnx`
2. Add a new case in `.github/workflows/publish.yml` mapping the tag prefix to the project name
3. Update the tag prefix table above
4. Add a row to the Packages table in the root `README.md` — a package missing from it is invisible
   to anyone arriving at the repository
5. Write the package's own `README.md`, and wire it up in the project file with both
   `<PackageReadmeFile>README.md</PackageReadmeFile>` and
   `<None Include="README.md" Pack="true" PackagePath="\" />`. Without the second, the build still
   succeeds and the nuget.org listing renders blank
6. Add a `<ProjectReference>` to it in `VectorSharp.Packaging.Tests.csproj`

`VectorSharp.Packaging.Tests` takes the set of packages from `VectorSharp.slnx` rather than a list
of its own, so step 1 is what puts a new package under its checks — there is no list to forget to
update. Step 6 is what lets those checks find the built `.nupkg`; without it they fail naming the
missing reference. A package that legitimately has dependencies must be named in that project's
`PackagesWithDependencies`, with the reason.
