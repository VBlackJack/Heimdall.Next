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
using Heimdall.Terminal.Logging;

namespace Heimdall.Terminal.Tests.Logging;

public sealed class StreamingAnsiStripperTests
{
    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("\u001B[1;31mhi\u001B[0m", "hi")]
    [InlineData("\u001B[2J", "")]
    [InlineData("before\u001B]0;evil window title\u0007after", "beforeafter")]
    [InlineData("\u001B]0;title\u001B\\X", "X")]
    [InlineData("\u001BP1$r0m\u001B\\X", "X")]
    [InlineData("\u001B_app\u001B\\X", "X")]
    [InlineData("\u001B^priv\u0007X", "X")]
    [InlineData("\u001BXsos\u001B\\Y", "Y")]
    [InlineData("\u001B7abc", "abc")]
    [InlineData("\u001B8abc", "abc")]
    [InlineData("\u001B=", "")]
    [InlineData("\u001Bc", "")]
    [InlineData("\u001B(B", "")]
    [InlineData("\u001B)0done", "done")]
    [InlineData("\u001B]0;ab\u001Bcd", "d")]
    [InlineData("a\u001B\u0001b", "a\u001B\u0001b")]
    public void Strip_WithEcma48GrammarCases_RemovesCompleteSequences(string input, string expected)
    {
        StreamingAnsiStripper stripper = new StreamingAnsiStripper();

        string actual = stripper.Strip(input);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Strip_WhenCompleteSequencesAreFragmented_MatchesWholeInputResult()
    {
        string[] corpus = BuildCorpus();

        foreach (string input in corpus)
        {
            string expected = StripWholeInput(input);
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

        Random random = new Random(20260529);
        for (int iteration = 0; iteration < 500; iteration++)
        {
            string input = BuildRandomInput(corpus, random);
            string expected = StripWholeInput(input);

            for (int split = 0; split <= input.Length; split++)
            {
                StreamingAnsiStripper stripper = new StreamingAnsiStripper();
                string first = input.Substring(0, split);
                string second = input.Substring(split);
                string actual = stripper.Strip(first) + stripper.Strip(second);

                Assert.Equal(expected, actual);
            }
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
    public void Strip_WithCrossChunkOscBel_RemovesCompleteString()
    {
        StreamingAnsiStripper stripper = new StreamingAnsiStripper();

        string first = stripper.Strip("\u001B]0;ti");
        string second = stripper.Strip("tle\u0007X");

        Assert.Equal(string.Empty, first);
        Assert.Equal("X", second);
    }

    [Fact]
    public void Strip_WithCrossChunkDcsSt_RemovesCompleteString()
    {
        StreamingAnsiStripper stripper = new StreamingAnsiStripper();

        string first = stripper.Strip("\u001BP1");
        string second = stripper.Strip("$r");
        string third = stripper.Strip("\u001B");
        string fourth = stripper.Strip("\\Z");
        string actual = first + second + third + fourth;

        Assert.Equal("Z", actual);
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

        StreamingAnsiStripper stringStripper = new StreamingAnsiStripper();
        string stringPrefix = stringStripper.Strip("ok\u001B]0;partial");
        stringStripper.Flush();
        string stringAfterFlush = stringStripper.Strip("more");

        Assert.Equal("ok", csiPrefix);
        Assert.Equal("more", csiAfterFlush);
        Assert.Equal("ok", escapePrefix);
        Assert.Equal("more", escapeAfterFlush);
        Assert.Equal("ok", stringPrefix);
        Assert.Equal("more", stringAfterFlush);
    }

    [Fact]
    public void Reset_ClearsIncompletePendingSequence()
    {
        StreamingAnsiStripper csiStripper = new StreamingAnsiStripper();

        string csiPrefix = csiStripper.Strip("ab\u001B[");
        csiStripper.Reset();
        string csiSuffix = csiStripper.Strip("cd");

        StreamingAnsiStripper stringStripper = new StreamingAnsiStripper();
        string stringPrefix = stringStripper.Strip("ab\u001B]0;partial");
        stringStripper.Reset();
        string stringSuffix = stringStripper.Strip("cd");

        Assert.Equal("ab", csiPrefix);
        Assert.Equal("cd", csiSuffix);
        Assert.Equal("ab", stringPrefix);
        Assert.Equal("cd", stringSuffix);
    }

    private static string StripWholeInput(string input)
    {
        StreamingAnsiStripper stripper = new StreamingAnsiStripper();
        return stripper.Strip(input);
    }

    private static string[] BuildCorpus()
    {
        return new string[]
        {
            "plain text",
            "\u001B[0mtext",
            "\u001B[31mred\u001B[0m",
            "\u001B[1;31mhi\u001B[0m",
            "\u001B[?25hcursor",
            "\u001B[2J",
            "\u001B[ q",
            "\u001BE",
            "\u001BM",
            "\u001BD",
            "\u001B\\",
            "\u001B]0;title\u0007after",
            "\u001B]0;title\u001B\\after",
            "\u001BP1$r0m\u001B\\after",
            "\u001B_app\u001B\\after",
            "\u001B^priv\u0007after",
            "\u001BXsos\u001B\\after",
            "\u001B7save",
            "\u001B8restore",
            "\u001B=",
            "\u001Bc",
            "\u001B(B",
            "\u001B)0done",
            "\u001B]0;ab\u001Bcd",
            "a\u001B\u0001b",
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
