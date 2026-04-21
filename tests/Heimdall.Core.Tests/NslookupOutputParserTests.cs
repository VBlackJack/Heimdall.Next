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

using Heimdall.Core.Network;

namespace Heimdall.Core.Tests;

public class NslookupOutputParserTests
{
    [Fact]
    public void Parse_NullInput_ReturnsEmpty()
    {
        var result = NslookupOutputParser.Parse(null);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsEmpty()
    {
        var result = NslookupOutputParser.Parse(string.Empty);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Parse_WhitespaceOnlyInput_ReturnsEmpty()
    {
        var result = NslookupOutputParser.Parse("   \n\n  \n");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Parse_HeaderOnly_ReturnsEmpty()
    {
        const string input = "Server:  8.8.8.8\nAddress:  8.8.8.8#53\n";
        var result = NslookupOutputParser.Parse(input);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Parse_HeaderWithTrailingBlank_ReturnsEmpty()
    {
        const string input = "Server:  1.1.1.1\nAddress:  1.1.1.1#53\n\n";
        var result = NslookupOutputParser.Parse(input);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Parse_StandardWindowsOutput_StripsHeaderAndKeepsBody()
    {
        const string input =
            "Server:  UnKnown\n" +
            "Address:  192.168.1.1\n" +
            "\n" +
            "Non-authoritative answer:\n" +
            "Name:    example.com\n" +
            "Address: 93.184.216.34\n";

        var result = NslookupOutputParser.Parse(input);

        Assert.Contains("Non-authoritative answer:", result);
        Assert.Contains("Name:    example.com", result);
        Assert.Contains("Address: 93.184.216.34", result);
        Assert.DoesNotContain("Server:", result);
        Assert.DoesNotContain("192.168.1.1", result);
    }

    [Fact]
    public void Parse_StandardLinuxOutput_StripsHeaderAndKeepsBody()
    {
        const string input =
            "Server:\t\t8.8.8.8\n" +
            "Address:\t8.8.8.8#53\n" +
            "\n" +
            "Non-authoritative answer:\n" +
            "example.com\tmail exchanger = 10 mx.example.com.\n";

        var result = NslookupOutputParser.Parse(input);

        Assert.Contains("Non-authoritative answer:", result);
        Assert.Contains("mail exchanger = 10 mx.example.com.", result);
        Assert.DoesNotContain("8.8.8.8", result);
    }

    [Fact]
    public void Parse_NoSeparatorBlankLine_StillEntersBodyAfterHeader()
    {
        // Defensive fallback: no blank line between header and body.
        const string input =
            "Server:  1.1.1.1\n" +
            "Address:  1.1.1.1#53\n" +
            "Non-authoritative answer:\n" +
            "Name:    example.org\n";

        var result = NslookupOutputParser.Parse(input);

        Assert.Contains("Non-authoritative answer:", result);
        Assert.Contains("Name:    example.org", result);
        Assert.DoesNotContain("Server:", result);
    }

    [Fact]
    public void Parse_MultipleBodyRecords_PreservesAll()
    {
        const string input =
            "Server:  8.8.8.8\n" +
            "Address:  8.8.8.8#53\n" +
            "\n" +
            "example.com  nameserver = ns1.example.com.\n" +
            "example.com  nameserver = ns2.example.com.\n" +
            "example.com  nameserver = ns3.example.com.\n";

        var result = NslookupOutputParser.Parse(input);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.All(lines, line => Assert.StartsWith("example.com", line));
    }

    [Fact]
    public void Parse_LeadingWhitespaceOnBodyLines_IsTrimmed()
    {
        const string input =
            "Server:  1.1.1.1\n" +
            "Address:  1.1.1.1#53\n" +
            "\n" +
            "    Name:    example.net\n" +
            "\tAddress: 10.0.0.1\n";

        var result = NslookupOutputParser.Parse(input);

        Assert.Contains("Name:    example.net", result);
        Assert.Contains("Address: 10.0.0.1", result);
        Assert.DoesNotContain("    Name", result);
        Assert.DoesNotContain("\tAddress", result);
    }

    [Fact]
    public void Parse_CrlfLineEndings_AreHandled()
    {
        // Per-line CR chars from CRLF inputs are stripped by the internal Trim();
        // the outer line-ending strategy is left to the caller (AppendLine).
        const string input =
            "Server:  8.8.8.8\r\n" +
            "Address:  8.8.8.8#53\r\n" +
            "\r\n" +
            "Non-authoritative answer:\r\n" +
            "Name:    example.com\r\n";

        var result = NslookupOutputParser.Parse(input);

        Assert.Contains("Non-authoritative answer:", result);
        Assert.Contains("Name:    example.com", result);
        Assert.DoesNotContain("Server:", result);
    }

    [Fact]
    public void Parse_MultipleLeadingBlankLines_AreSkipped()
    {
        const string input =
            "\n\n" +
            "Server:  8.8.8.8\n" +
            "Address:  8.8.8.8#53\n" +
            "\n" +
            "Name: example.io\n";

        var result = NslookupOutputParser.Parse(input);

        Assert.Contains("Name: example.io", result);
        Assert.DoesNotContain("Server:", result);
    }

    [Fact]
    public void Parse_BlankLinesInsideBody_AreDropped()
    {
        const string input =
            "Server:  8.8.8.8\n" +
            "Address:  8.8.8.8#53\n" +
            "\n" +
            "Non-authoritative answer:\n" +
            "\n" +
            "Name: example.com\n" +
            "\n" +
            "Address: 1.2.3.4\n";

        var result = NslookupOutputParser.Parse(input);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.Equal("Non-authoritative answer:", lines[0].Trim());
        Assert.Equal("Name: example.com", lines[1].Trim());
        Assert.Equal("Address: 1.2.3.4", lines[2].Trim());
    }

    [Fact]
    public void Parse_NoHeaderAtAll_PreservesEverything()
    {
        // Some CLI variants print only the body (e.g. after filtering, or
        // when a non-standard DNS binary is aliased to "nslookup").
        const string input =
            "example.com has address 93.184.216.34\n" +
            "example.com has IPv6 address 2606:2800:220:1::1:1\n";

        var result = NslookupOutputParser.Parse(input);

        Assert.Contains("example.com has address 93.184.216.34", result);
        Assert.Contains("example.com has IPv6 address 2606:2800:220:1::1:1", result);
    }

    [Fact]
    public void Parse_TimeoutErrorOutput_IsReturnedAsBody()
    {
        // When nslookup times out, it does not emit the usual Server/Address
        // header — the error message should flow through as body.
        const string input =
            "DNS request timed out.\n" +
            "    timeout was 2 seconds.\n" +
            "*** Can't find example.invalid: No response from server\n";

        var result = NslookupOutputParser.Parse(input);

        Assert.Contains("DNS request timed out.", result);
        Assert.Contains("timeout was 2 seconds.", result);
        Assert.Contains("Can't find example.invalid", result);
    }

    [Fact]
    public void Parse_MixedCaseHeaders_AreStripped()
    {
        // Be tolerant of case variations in header labels.
        const string input =
            "server:  8.8.8.8\n" +
            "ADDRESS:  8.8.8.8#53\n" +
            "\n" +
            "Name: mixed.example\n";

        var result = NslookupOutputParser.Parse(input);

        Assert.Contains("Name: mixed.example", result);
        Assert.DoesNotContain("server:", result);
        Assert.DoesNotContain("ADDRESS:", result);
    }

    [Fact]
    public void Parse_AddressesPlural_IsNotMistakenForHeader()
    {
        // Windows nslookup uses "Addresses:" (plural) inside the body for IPv6
        // follow-up lines; the parser must not strip them.
        const string input =
            "Server:  8.8.8.8\n" +
            "Address:  8.8.8.8#53\n" +
            "\n" +
            "Name:    google.com\n" +
            "Addresses:  2607:f8b0:4005:802::200e\n" +
            "          142.250.80.142\n";

        var result = NslookupOutputParser.Parse(input);

        Assert.Contains("Name:    google.com", result);
        Assert.Contains("Addresses:  2607:f8b0:4005:802::200e", result);
        Assert.Contains("142.250.80.142", result);
    }

    [Fact]
    public void Parse_TrailingWhitespaceOnFinalLine_IsTrimmed()
    {
        const string input =
            "Server:  8.8.8.8\n" +
            "Address:  8.8.8.8#53\n" +
            "\n" +
            "Name: trimmed.example\n\n\n";

        var result = NslookupOutputParser.Parse(input);

        Assert.Equal("Name: trimmed.example", result);
    }
}
