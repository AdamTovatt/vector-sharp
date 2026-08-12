using System.Reflection;

namespace VectorSharp.Reranking.Tests
{
    /// <summary>
    /// Pins what the package exposes. The promise is that it stays small — one interface and its
    /// result types — and that is the kind of claim that erodes one convenience type at a time.
    /// </summary>
    public class PackageSurfaceTests
    {
        private static readonly Assembly PackageAssembly = typeof(IRerankProvider).Assembly;

        [Fact]
        public void PublicSurface_IsTheInterfaceAndItsResultTypes()
        {
            // Enumerated rather than counted, so a new public type has to be named here to pass
            // and a reviewer sees exactly what the package exposes.
            string[] expected =
            [
                nameof(IRerankProvider),
                nameof(RerankMatch),
                nameof(RerankResult),
                nameof(RerankUsage)
            ];

            string[] actual = PackageAssembly
                .GetExportedTypes()
                .Select(type => type.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), actual);
        }

        [Fact]
        public void PublicSurface_HasNoServiceOrWorkerMachinery()
        {
            // A second gate behind the enumeration above, for the likely path: someone adds a
            // service type, sees that test go red, and makes it green by adding the name to the
            // list. This one is what stops them.
            //
            // It matches on names, so it catches the shapes anyone would reach for first and not
            // a name that avoids them. The enumeration is the real fence; this is the reason to
            // stop and think before widening it.
            string[] serviceLikeNames = PackageAssembly
                .GetExportedTypes()
                .Select(type => type.Name)
                .Where(name => name.EndsWith("Service", StringComparison.Ordinal)
                    || name.EndsWith("Options", StringComparison.Ordinal)
                    || name.Contains("Worker", StringComparison.Ordinal)
                    || name.Contains("Queue", StringComparison.Ordinal))
                .ToArray();

            Assert.Empty(serviceLikeNames);
        }

        [Fact]
        public void RerankMatch_IsAReferenceType()
        {
            // Backs up the behavioural test in the contract suite: as a struct, an absent match
            // reads as a real one at index 0. Stated here too because the type decision is what
            // the contract test is protecting, and this names it directly.
            Assert.True(typeof(RerankMatch).IsClass);
        }
    }
}
