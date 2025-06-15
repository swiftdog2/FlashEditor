using System;
using System.IO;
using Xunit;

namespace FlashEditor.Tests.Style
{
    public class LinqChainLoopTests
    {
        [Fact]
        public void NoLinqWhereSelectToListInLoops()
        {
            var root = Path.Combine("..", "..", "FlashEditor");
            foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (line.Contains(".Where(") && line.Contains(".Select(") && line.Contains(".ToList(") && IsInsideLoop(lines, i))
                    {
                        Assert.False(true, $"LINQ chain .Where().Select().ToList() inside loop in {file} at line {i + 1}. Use a single-pass loop instead.");
                    }
                }
            }
        }

        private static bool IsInsideLoop(string[] lines, int index)
        {
            for (int i = index; i >= 0 && index - i < 5; i--)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("for(") || trimmed.StartsWith("for (") ||
                    trimmed.StartsWith("foreach(") || trimmed.StartsWith("foreach (") ||
                    trimmed.StartsWith("while(") || trimmed.StartsWith("while (") )
                {
                    return true;
                }
            }
            return false;
        }
    }
}
