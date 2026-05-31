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

using System.Text.Json;
using Heimdall.Core.Configuration;

namespace Heimdall.Core.Tests;

public sealed class AppSettingsPinLockoutTests
{
    [Fact]
    public void PinFailureCount_DefaultsToZero()
    {
        AppSettings settings = new AppSettings();

        Assert.Equal(0, settings.PinFailureCount);
    }

    [Fact]
    public void PinLockoutUntilUtc_DefaultsToNull()
    {
        AppSettings settings = new AppSettings();

        Assert.Null(settings.PinLockoutUntilUtc);
    }

    [Fact]
    public void JsonRoundTrip_PreservesPinLockoutState()
    {
        DateTime lockoutUntilUtc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        AppSettings settings = new AppSettings
        {
            PinFailureCount = 3,
            PinLockoutUntilUtc = lockoutUntilUtc
        };
        JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        string json = JsonSerializer.Serialize(settings, options);
        AppSettings deserializedSettings = JsonSerializer.Deserialize<AppSettings>(json, options)
            ?? throw new InvalidOperationException("AppSettings JSON round-trip returned null.");

        Assert.Equal(3, deserializedSettings.PinFailureCount);
        Assert.Equal(lockoutUntilUtc, deserializedSettings.PinLockoutUntilUtc);
    }
}
