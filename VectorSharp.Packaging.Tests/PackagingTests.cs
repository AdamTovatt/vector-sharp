using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;

namespace VectorSharp.Packaging.Tests
{
    /// <summary>
    /// Checks what the repository actually publishes: for every project the solution packs, the
    /// .nupkg a build produces is opened and inspected. These read files off disk rather than the
    /// loaded assembly, which is why they live in a project of their own rather than beside any
    /// one package's API surface tests.
    /// </summary>
    /// <remarks>
    /// The set of packages is taken from the solution rather than listed here, so a package added
    /// later is covered without anyone remembering to add it. A package that needs an exception
    /// has to be named in <see cref="PackagesWithDependencies"/>, which makes the exception
    /// visible instead of making the coverage invisible.
    /// </remarks>
    public class PackagingTests
    {
        /// <summary>
        /// Packages that legitimately declare dependencies, and are therefore exempt from
        /// <see cref="PackedPackage_DeclaresNoDependencies"/>.
        /// </summary>
        /// <remarks>
        /// An exclusion list rather than an inclusion list on purpose: a package added to the
        /// solution is guarded by default and its author has to come here and say why not, rather
        /// than a new package being silently unchecked because nobody updated a list.
        /// </remarks>
        private static readonly IReadOnlySet<string> PackagesWithDependencies = new HashSet<string>(StringComparer.Ordinal)
        {
            // Depends on the ONNX runtime and the tokenizer, and claims no otherwise.
            "VectorSharp.Embedding.NomicEmbed"
        };

        public static TheoryData<string> PackablePackages
        {
            get
            {
                TheoryData<string> data = [];
                foreach (PackableProject project in PackableProjects())
                {
                    data.Add(project.PackageId);
                }
                return data;
            }
        }

        public static TheoryData<string> ZeroDependencyPackages
        {
            get
            {
                TheoryData<string> data = [];
                foreach (PackableProject project in PackableProjects().Where(project => !PackagesWithDependencies.Contains(project.PackageId)))
                {
                    data.Add(project.PackageId);
                }
                return data;
            }
        }

        [Theory]
        [MemberData(nameof(ZeroDependencyPackages))]
        public void PackedPackage_DeclaresNoDependencies(string packageId)
        {
            // Asserted against the nuspec inside the built .nupkg, which is the artifact that
            // reaches nuget.org. The project file alone would miss a dependency injected by a
            // Directory.Build.props, and the compiled assembly would miss one that is declared
            // but unused, since the compiler omits references nothing touches.
            XDocument nuspec = ReadNuspec(packageId);
            XNamespace ns = nuspec.Root!.GetDefaultNamespace();

            string[] dependencies = nuspec.Descendants(ns + "dependency")
                .Select(dependency => dependency.Attribute("id")?.Value ?? "<unnamed>")
                .ToArray();

            // Asserted on the joined names rather than on the sequence, because a failure has to
            // name the dependency that appeared. Assert.Empty prints no contents.
            Assert.Equal(string.Empty, string.Join(", ", dependencies));
        }

        [Theory]
        [MemberData(nameof(PackablePackages))]
        public void PackedPackage_ShipsItsReadme(string packageId)
        {
            // Without the readme file packed alongside PackageReadmeFile, the nuget.org listing
            // renders blank — a failure invisible from inside the repository.
            using ZipArchive package = ZipFile.OpenRead(PackagePath(packageId));

            Assert.Contains(package.Entries, entry => entry.FullName.Equals("README.md", StringComparison.Ordinal));
        }

        [Fact]
        public void PackableProjects_AreAllReferencedByThisProject()
        {
            // The theories above resolve a package by finding its built .nupkg, so a packable
            // project this test project does not reference would fail on a missing file with no
            // hint as to why. Naming the omission directly is the difference between a puzzling
            // failure and an instruction.
            string[] missing = PackableProjects()
                .Where(project => !File.Exists(Path.Combine(AppContext.BaseDirectory, $"{project.AssemblyName}.dll")))
                .Select(project => project.PackageId)
                .ToArray();

            Assert.Equal(
                string.Empty,
                string.Join(", ", missing));
        }

        private static XDocument ReadNuspec(string packageId)
        {
            using ZipArchive package = ZipFile.OpenRead(PackagePath(packageId));

            ZipArchiveEntry nuspec = package.Entries.FirstOrDefault(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"No nuspec inside the {packageId} package.");

            using Stream content = nuspec.Open();
            return XDocument.Load(content);
        }

        /// <summary>
        /// Finds the package this build produced for the given package id.
        /// </summary>
        /// <remarks>
        /// The version comes from the package's own built assembly, not from the project file's
        /// <c>&lt;Version&gt;</c>. The publish workflow builds and packs with
        /// <c>-p:Version=&lt;tag version&gt;</c>, an MSBuild global property that overrides
        /// <c>&lt;Version&gt;</c> for every project — so the project file names the version of a
        /// local build and nothing else, and reading it here would look for a file the release
        /// build never wrote. The assembly is stamped from the same property, so it agrees with
        /// the .nupkg however the build was invoked.
        ///
        /// The configuration is pinned to the one these tests were built in, rather than searched
        /// for. Searching the whole bin tree lets a Release run pass by inspecting a Debug
        /// artifact left over from a developer's last build, and configuration is exactly what CI
        /// varies.
        /// </remarks>
        private static string PackagePath(string packageId)
        {
            PackableProject project = PackableProjects().FirstOrDefault(candidate => candidate.PackageId == packageId)
                ?? throw new InvalidOperationException($"No packable project in the solution produces {packageId}.");

            string version = BuiltVersion(project);
            string expectedFileName = $"{packageId}.{version}.nupkg";
            string packageDirectory = Path.Combine(project.Directory, "bin", Configuration);
            string packagePath = Path.Combine(packageDirectory, expectedFileName);

            if (!File.Exists(packagePath))
            {
                throw new InvalidOperationException(
                    $"No {expectedFileName} in {packageDirectory}. The packages are produced by a build, so build the solution before running these.");
            }

            return packagePath;
        }

        /// <summary>
        /// The version the current build stamped into the package's assembly, which is the version
        /// it also named the .nupkg after. Build metadata after a '+' is dropped, since the package
        /// file name carries the version alone.
        /// </summary>
        private static string BuiltVersion(PackableProject project)
        {
            string assemblyPath = Path.Combine(AppContext.BaseDirectory, $"{project.AssemblyName}.dll");

            if (!File.Exists(assemblyPath))
            {
                throw new InvalidOperationException(
                    $"No {project.AssemblyName}.dll beside the tests. Add a ProjectReference to {project.PackageId} in VectorSharp.Packaging.Tests.csproj.");
            }

            string informationalVersion = FileVersionInfo.GetVersionInfo(assemblyPath).ProductVersion
                ?? throw new InvalidOperationException($"{assemblyPath} carries no product version.");

            int metadataStart = informationalVersion.IndexOf('+');
            return metadataStart < 0 ? informationalVersion : informationalVersion[..metadataStart];
        }

        /// <summary>
        /// The build configuration these tests were built in, read from their own output path:
        /// bin/&lt;Configuration&gt;/&lt;TargetFramework&gt;.
        /// </summary>
        private static string Configuration
        {
            get
            {
                DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));

                while (directory?.Parent != null && !directory.Parent.Name.Equals("bin", StringComparison.OrdinalIgnoreCase))
                {
                    directory = directory.Parent;
                }

                return directory?.Name
                    ?? throw new InvalidOperationException($"No bin directory above {AppContext.BaseDirectory}, so the build configuration cannot be determined.");
            }
        }

        /// <summary>
        /// Every project the solution packs, taken from the solution file so that this set cannot
        /// drift from what the repository actually publishes.
        /// </summary>
        private static IEnumerable<PackableProject> PackableProjects()
        {
            string root = RepositoryRoot();
            XDocument solution = XDocument.Load(Path.Combine(root, "VectorSharp.slnx"));

            foreach (XElement projectElement in solution.Descendants("Project"))
            {
                string? relativePath = projectElement.Attribute("Path")?.Value;
                if (relativePath == null)
                    continue;

                string projectFile = Path.Combine(root, relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(projectFile))
                    throw new InvalidOperationException($"The solution lists {relativePath}, which does not exist.");

                XDocument project = XDocument.Load(projectFile);

                bool packable = project.Descendants("GeneratePackageOnBuild")
                    .Any(element => bool.TryParse(element.Value, out bool value) && value);

                if (!packable)
                    continue;

                string packageId = project.Descendants("PackageId").FirstOrDefault()?.Value
                    ?? throw new InvalidOperationException($"{relativePath} is packed but declares no PackageId.");

                string assemblyName = project.Descendants("AssemblyName").FirstOrDefault()?.Value
                    ?? Path.GetFileNameWithoutExtension(projectFile);

                yield return new PackableProject(packageId, assemblyName, Path.GetDirectoryName(projectFile)!);
            }
        }

        /// <summary>
        /// Walks up from the test binary to the directory holding the solution file. Throws rather
        /// than returning something empty if it never finds one.
        /// </summary>
        private static string RepositoryRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "VectorSharp.slnx")))
            {
                directory = directory.Parent;
            }

            if (directory == null)
                throw new InvalidOperationException($"No VectorSharp.slnx found above {AppContext.BaseDirectory}.");

            return directory.FullName;
        }

        private sealed record PackableProject(string PackageId, string AssemblyName, string Directory);
    }
}
