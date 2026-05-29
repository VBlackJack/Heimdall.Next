/*
 * Copyright 2026 Julien Bombled
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Text;
using System.Text.RegularExpressions;
using Heimdall.Terminal.Logging;

namespace Heimdall.Terminal.Tests.Logging;

public sealed class StreamingAnsiStripperTests
{
    private const string AnsiPattern = @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~]|\].*?(?:\x07|\x1B\\))";

    [Fact]
    public void Strip_WithCompleteSequences_MatchesRegexOracle()
    {
        string[] corpus = BuildCorpus();
        string completeCorpus = string.Concat(corpus) + "Z";
        AssertMatchesOracle(completeCorpus);

        foreach (string input in corpus)
        {
            AssertMatchesOracle(input);
        }

        Random random = new Random(20260529);
        for (int iteration = 0; iteration < 500; iteration++)
        {
            string input = BuildRandomInput(corpus, random);
            AssertMatchesOracle(input);
        }
    }

    [Fact]
    public void Strip_WhenCompleteSequencesAreFragmented_MatchesRegexOracle()
    {
        string[] corpus = BuildCorpus();

        foreach (string input in corpus)
        {
            string expected = Oracle(input);
            for (int split = 0; split <= input.Length; split++)
            {
                StreamingAnsiStripper stripper = new StreamingAnsiStripper();
                string first = input.Substring(0, split);
                string second = input.Substring(split);
                string actual = stripper.Strip(first) + stripper.Strip(second);

                Assert.Equal(expected, actual);
            }

            StreamingAnsiStripper charByCharStripper = new StreamingAnsiStripper();
            StringBuilder charByChar = new StringBuilder();
            foreach (char current in input)
            {
                charByChar.Append(charByCharStripper.Strip(current.ToString()));
            }

            Assert.Equal(expected, charByChar.ToString());
        }
    }

    [Fact]
    public void Strip_WithCrossChunkCsi_RemovesCompleteSequence()
    {
        StreamingAnsiStripper stripper = new StreamingAnsiStripper();

        string first = stripper.Strip("\u001B[31");
        string second = stripper.Strip("m");
        string third = stripper.Strip("X");

        Assert.Equal(string.Empty, first);
        Assert.Equal(string.Empty, second);
        Assert.Equal("X", third);
    }

    [Fact]
    public void Flush_DiscardsIncompletePendingSequence()
    {
        StreamingAnsiStripper csiStripper = new StreamingAnsiStripper();
        string csiPrefix = csiStripper.Strip("ok\u001B[31");
        csiStripper.Flush();
        string csiAfterFlush = csiStripper.Strip("more");

        StreamingAnsiStripper escapeStripper = new StreamingAnsiStripper();
        string escapePrefix = escapeStripper.Strip("ok\u001B");
        escapeStripper.Flush();
        string escapeAfterFlush = escapeStripper.Strip("more");

        Assert.Equal("ok", csiPrefix);
        Assert.Equal("more", csiAfterFlush);
        Assert.Equal("ok", escapePrefix);
        Assert.Equal("more", escapeAfterFlush);
    }

    [Fact]
    public void Reset_ClearsIncompletePendingSequence()
    {
        StreamingAnsiStripper stripper = new StreamingAnsiStripper();

        string prefix = stripper.Strip("ab\u001B[");
        stripper.Reset();
        string suffix = stripper.Strip("cd");

        Assert.Equal("ab", prefix);
        Assert.Equal("cd", suffix);
    }

    [Fact]
    public void Strip_WithInvalidEscape_EmitsRegexOracleOutput()
    {
        string input = "a\u001Bxb";
        StreamingAnsiStripper stripper = new StreamingAnsiStripper();

        string actual = stripper.Strip(input);

        Assert.Equal(Oracle(input), actual);
    }

    private static void AssertMatchesOracle(string input)
    {
        StreamingAnsiStripper stripper = new StreamingAnsiStripper();

        string actual = stripper.Strip(input);

        Assert.Equal(Oracle(input), actual);
    }

    private static string Oracle(string input)
    {
        return Regex.Replace(input, AnsiPattern, string.Empty);
    }

    private static string[] BuildCorpus()
    {
        return new string[]
        {
            "plain text",
            "\u001B[0m",
            "\u001B[31m",
            "\u001B[1;31m",
            "\u001B[?25h",
            "\u001B[2J",
            "\u001B[ q",
            "\u001BE",
            "\u001BM",
            "\u001BD",
            "\u001B\\",
            "\u001B]0;title\u0007",
            "\u001B7",
            "\u001Bxb",
            "\u001B[3Ztext",
            "\u001B[ 3Z"
        };
    }

    private static string BuildRandomInput(string[] corpus, Random random)
    {
        StringBuilder builder = new StringBuilder();
        int tokenCount = random.Next(1, 32);
        for (int index = 0; index < tokenCount; index++)
        {
            string token = corpus[random.Next(corpus.Length)];
            builder.Append(token);
        }

        builder.Append('Z');
        return builder.ToString();
    }
}
