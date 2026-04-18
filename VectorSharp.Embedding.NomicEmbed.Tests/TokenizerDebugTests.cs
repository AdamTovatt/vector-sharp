namespace VectorSharp.Embedding.NomicEmbed.Tests
{
    [TestClass]
    [TestCategory("RequiresModel")]
    [DoNotParallelize]
    public class TokenizerDebugTests
    {
        [TestMethod]
        public void Tokenizer_HelloWorld_MatchesPythonTokenIds()
        {
            string tokenizerPath = Path.Combine(
                Path.GetDirectoryName(typeof(TokenizerDebugTests).Assembly.Location)!,
                "Models", "tokenizer.json");

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

            Assert.AreEqual(expected.Length, ids.Length, $"Token count mismatch. Got IDs: [{string.Join(", ", ids)}]");
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], ids[i], $"Token mismatch at position {i}");
            }
        }
    }
}
