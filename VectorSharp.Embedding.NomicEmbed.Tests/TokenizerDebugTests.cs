namespace VectorSharp.Embedding.NomicEmbed.Tests
{
    [Trait("Category", "RequiresModel")]
    public class TokenizerDebugTests
    {
        [SkippableFact]
        public void Tokenizer_HelloWorld_MatchesPythonTokenIds()
        {
            string tokenizerPath = Path.Combine(
                Path.GetDirectoryName(typeof(TokenizerDebugTests).Assembly.Location)!,
                "Models", "tokenizer.json");

            Skip.IfNot(File.Exists(tokenizerPath), "Tokenizer file not found. Run tools/download-nomic-model.sh first.");

            FastBertTokenizer.BertTokenizer tokenizer = new FastBertTokenizer.BertTokenizer();
            using (Stream stream = File.OpenRead(tokenizerPath))
            {
                tokenizer.LoadTokenizerJson(stream);
            }

            string text = "search_document: hello world";
            (Memory<long> inputIds, _, _) = tokenizer.Encode(text, 8192);
            long[] ids = inputIds.ToArray();

            Console.WriteLine($"Text: {text}");
            Console.WriteLine($"Token count: {ids.Length}");
            Console.WriteLine($"Token IDs: [{string.Join(", ", ids)}]");
            Console.WriteLine("Python ref: [101, 3945, 1035, 6254, 1024, 7592, 2088, 102]");

            // Python produces: [101, 3945, 1035, 6254, 1024, 7592, 2088, 102]
            long[] expected = new long[] { 101, 3945, 1035, 6254, 1024, 7592, 2088, 102 };

            Assert.Equal(expected.Length, ids.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], ids[i]);
            }
        }
    }
}
