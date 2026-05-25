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

using System.Globalization;
using Heimdall.App.Converters;

namespace Heimdall.App.Tests;

public sealed class SessionStatusToDisplayConverterTests
{
    private static SessionStatusToDisplayConverter MakeConverter(out List<string> resolvedKeys)
    {
        List<string> captured = new();
        resolvedKeys = captured;
        return new SessionStatusToDisplayConverter(key =>
        {
            captured.Add(key);
            return $"L:{key}";
        });
    }

    [Theory]
    [InlineData("Connected", "SessionStatusConnected")]
    [InlineData("Connecting", "SessionStatusConnecting")]
    [InlineData("Disconnected", "SessionStatusDisconnected")]
    [InlineData("Disconnecting", "SessionStatusDisconnecting")]
    [InlineData("Reconnecting", "SessionStatusReconnecting")]
    [InlineData("Error", "SessionStatusError")]
    public void KnownStatus_LocalizesViaExpectedKey(string status, string expectedKey)
    {
        SessionStatusToDisplayConverter converter = MakeConverter(out List<string> keys);

        object? result = converter.Convert(status, typeof(string), null!, CultureInfo.InvariantCulture);

        Assert.Equal(expectedKey, keys.Single());
        Assert.Equal($"L:{expectedKey}", result);
    }

    [Fact]
    public void KnownStatus_IsCaseInsensitive()
    {
        SessionStatusToDisplayConverter converter = MakeConverter(out List<string> keys);

        object? result = converter.Convert("disconnected", typeof(string), null!, CultureInfo.InvariantCulture);

        Assert.Equal("SessionStatusDisconnected", keys.Single());
        Assert.Equal("L:SessionStatusDisconnected", result);
    }

    [Fact]
    public void UnknownStatus_PassesThroughUnchanged()
    {
        SessionStatusToDisplayConverter converter = MakeConverter(out List<string> keys);

        object? result = converter.Convert("Session lost: timeout", typeof(string), null!, CultureInfo.InvariantCulture);

        Assert.Empty(keys);
        Assert.Equal("Session lost: timeout", result);
    }

    [Fact]
    public void NullValue_PassesThrough()
    {
        SessionStatusToDisplayConverter converter = MakeConverter(out List<string> keys);

        object? result = converter.Convert(null, typeof(string), null!, CultureInfo.InvariantCulture);

        Assert.Empty(keys);
        Assert.Null(result);
    }

    [Fact]
    public void WhitespaceValue_PassesThrough()
    {
        SessionStatusToDisplayConverter converter = MakeConverter(out List<string> keys);

        object? result = converter.Convert("   ", typeof(string), null!, CultureInfo.InvariantCulture);

        Assert.Empty(keys);
        Assert.Equal("   ", result);
    }
}
