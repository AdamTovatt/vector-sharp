namespace VectorSharp.Chunking.Tests
{
    internal static class TokenCounter
    {
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
