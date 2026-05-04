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
using System.Windows.Media;
using Heimdall.App.Converters;

namespace Heimdall.App.Tests;

public sealed class ServerStatusToColorConverterTests
{
    [Fact]
    public void ServerStatus_LaunchedExternalClient_UsesWarningBrushKey()
    {
        string? requestedKey = null;
        var warningBrush = Brushes.Orange;
        var converter = new ServerStatusToColorConverter(key =>
        {
            requestedKey = key;
            return warningBrush;
        });

        var result = converter.Convert(
            ["RDP", "LaunchedExternalClient"],
            typeof(Brush),
            parameter: null!,
            CultureInfo.InvariantCulture);

        Assert.Equal("WarningBrush", requestedKey);
        Assert.Same(warningBrush, result);
    }

    [Fact]
    public void ConnectionState_LaunchedExternalClient_UsesWarningBrushKey()
    {
        string? requestedKey = null;
        var warningBrush = Brushes.Orange;
        var converter = new ConnectionStateToBrushConverter(key =>
        {
            requestedKey = key;
            return warningBrush;
        });

        var result = converter.Convert(
            "LaunchedExternalClient",
            typeof(Brush),
            parameter: null!,
            CultureInfo.InvariantCulture);

        Assert.Equal("WarningBrush", requestedKey);
        Assert.Same(warningBrush, result);
    }
}
