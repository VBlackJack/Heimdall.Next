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

using Heimdall.App.Services;
using Heimdall.Core.Identifiers;

namespace Heimdall.App.Tests;

public sealed class UuidGeneratorToolServiceTests
{
    private readonly UuidGeneratorToolService _service = new();

    [Fact]
    public void Generate_DelegatesToEngine_V4()
    {
        Assert.Equal('4', _service.Generate(UuidVersion.V4).ToString("D")[14]);
    }

    [Fact]
    public void Generate_DelegatesToEngine_V7()
    {
        Assert.Equal('7', _service.Generate(UuidVersion.V7).ToString("D")[14]);
    }

    [Fact]
    public void Format_DelegatesToEngine_Default()
    {
        var guid = Guid.Parse("A1B2C3D4-E5F6-47A8-9123-ABCDEF123456");

        Assert.Equal(UuidGenerator.Format(guid, UuidFormat.Default), _service.Format(guid, UuidFormat.Default));
    }

    [Fact]
    public void Format_DelegatesToEngine_UppercaseNoHyphens()
    {
        var guid = Guid.Parse("a1b2c3d4-e5f6-47a8-9123-abcdef123456");
        var format = new UuidFormat(true, false);

        Assert.Equal(UuidGenerator.Format(guid, format), _service.Format(guid, format));
    }
}
