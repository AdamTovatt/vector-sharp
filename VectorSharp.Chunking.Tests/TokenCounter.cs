namespace VectorSharp.Chunking.Tests
{
    internal static class TokenCounter
    {
        /// <summary>
        /// One token per character. A word counter can never cut mid-word — adding a character to
        /// a word does not raise its count — so it cannot express where a limit falls inside a
        /// run of text. This one reaches the limit at a position a test can state exactly, and is
        /// the closer model of a real subword tokenizer.
        /// </summary>
        internal static int CountCharacters(string text) => text.Length;

        internal static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            int count = 0;
            bool inWord = false;

            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    inWord = false;
                }
                else if (!inWord)
                {
                    inWord = true;
                    count++;
                }
            }

            return count;
        }
    }
}
