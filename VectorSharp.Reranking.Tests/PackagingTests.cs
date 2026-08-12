using System.IO.Compression;
using System.Xml.Linq;

namespace VectorSharp.Reranking.Tests
{
    /// <summary>
    /// Checks what the repository actually publishes, for every package whose description or
    /// README claims zero dependencies. These read files off disk rather than the loaded assembly,
    /// which is why they live apart from the API surface tests.
    /// </summary>
    /// <remarks>
    /// They cover the whole repository from one place rather than being copied into each test
    /// project, so the rule has a single home. The reranking test project happens to host them
    /// because it is where the claim was first guarded.
    /// </remarks>
    public class PackagingTests
    {
        /// <summary>
        /// Every package that states it has no dependencies. NomicEmbed is absent on purpose: it
        /// depends on the ONNX runtime and makes no such claim.
        /// </summary>
        public static TheoryData<string> ZeroDependencyPackages =>
        [
            "VectorSharp.Storage",
            "VectorSharp.Embedding",
            "VectorSharp.Chunking",
            "VectorSharp.Reranking"
        ];

        [Theory]
        [MemberData(nameof(ZeroDependencyPackages))]
        public void PublishedPackage_DeclaresNoDependencies(string packageName)
        {
            // Asserted against the nuspec inside the built .nupkg, which is the artifact that
            // reaches nuget.org. The project file alone would miss a dependency injected by a
            // Directory.Build.props, and the compiled assembly would miss one that is declared
            // but unused, since the compiler omits references nothing touches.
            XDocument nuspec = ReadNuspec(packageName);
            XNamespace ns = nuspec.Root!.GetDefaultNamespace();

            XElement[] dependencies = nuspec.Descendants(ns + "dependency").ToArray();

            Assert.Empty(dependencies.Select(dependency => dependency.Attribute("id")?.Value ?? "unnamed"));
        }

        [Theory]
        [MemberData(nameof(ZeroDependencyPackages))]
        public void PublishedPackage_ShipsItsReadme(string packageName)
        {
            // Without the readme file packed alongside PackageReadmeFile, the nuget.org listing
            // renders blank — a failure invisible from inside the repository.
            using ZipArchive package = ZipFile.OpenRead(PackagePath(packageName));

            Assert.Contains(package.Entries, entry => entry.FullName.Equals("README.md", StringComparison.Ordinal));
        }

        private static XDocument ReadNuspec(string packageName)
        {
            using ZipArchive package = ZipFile.OpenRead(PackagePath(packageName));

            ZipArchiveEntry nuspec = package.Entries.FirstOrDefault(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"No nuspec inside the {packageName} package.");

            using Stream content = nuspec.Open();
            return XDocument.Load(content);
        }

        /// <summary>
        /// Finds the built package for the version the project file currently declares, so a stale
        /// artifact from an earlier version cannot stand in for it. Throws rather than returning
        /// nothing when it is missing, so a test built on this fails loudly instead of quietly
        /// checking a file that is not there.
        /// </summary>
        private static string PackagePath(string packageName)
        {
            string projectDirectory = Path.Combine(RepositoryRoot(), packageName);
            string projectFile = Path.Combine(projectDirectory, $"{packageName}.csproj");

            if (!File.Exists(projectFile))
                throw new InvalidOperationException($"No project file at {projectFile}.");

            string version = XDocument.Load(projectFile)
                .Descendants("Version")
                .FirstOrDefault()?.Value
                ?? throw new InvalidOperationException($"{packageName} declares no Version.");

            string expectedFileName = $"{packageName}.{version}.nupkg";

            string binDirectory = Path.Combine(projectDirectory, "bin");
            string[] candidates = Directory.Exists(binDirectory)
                ? Directory.GetFiles(binDirectory, expectedFileName, SearchOption.AllDirectories)
                : [];

            if (candidates.Length == 0)
            {
                throw new InvalidOperationException(
                    $"No {expectedFileName} under {binDirectory}. The packages are produced by a build, so build the solution before running these.");
            }

            return candidates[0];
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
    }
}
