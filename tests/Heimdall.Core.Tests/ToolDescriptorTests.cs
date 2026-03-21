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

using Heimdall.Core.Models;

namespace Heimdall.Core.Tests;

public class ToolDescriptorTests
{
    [Fact]
    public void ToolType_ReturnsPrefixedId()
    {
        var descriptor = new ToolDescriptor(
            "PING", ToolCategory.Network, "ToolCategoryNetwork",
            "PaletteToolPing", null, ["ping"], true);

        Assert.Equal("TOOL:PING", descriptor.ToolType);
    }

    [Fact]
    public void ToolType_PreservesIdCasing()
    {
        var descriptor = new ToolDescriptor(
            "MyTool", ToolCategory.System, "ToolCategorySystem",
            "Label", null, ["mytool"], false);

        Assert.Equal("TOOL:MyTool", descriptor.ToolType);
    }

    [Theory]
    [InlineData(ToolCategory.Network)]
    [InlineData(ToolCategory.Security)]
    [InlineData(ToolCategory.Encoding)]
    [InlineData(ToolCategory.System)]
    public void AllCategoryValues_AreValid(ToolCategory category)
    {
        var descriptor = new ToolDescriptor(
            "TEST", category, "CatKey", "LabelKey", null, ["test"], false);

        Assert.Equal(category, descriptor.Category);
    }

    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var prefixes = new[] { "hash", "md5" };
        var descriptor = new ToolDescriptor(
            "HASH", ToolCategory.Security, "ToolCategorySecurity",
            "PaletteToolHash", "PaletteToolHashWith", prefixes, false, "Icon.Tool.Hash");

        Assert.Equal("HASH", descriptor.Id);
        Assert.Equal(ToolCategory.Security, descriptor.Category);
        Assert.Equal("ToolCategorySecurity", descriptor.CategoryLabelKey);
        Assert.Equal("PaletteToolHash", descriptor.LabelKey);
        Assert.Equal("PaletteToolHashWith", descriptor.LabelWithArgKey);
        Assert.Same(prefixes, descriptor.CommandPrefixes);
        Assert.False(descriptor.IsNetworkTool);
        Assert.Equal("Icon.Tool.Hash", descriptor.IconResourceKey);
    }

    [Fact]
    public void Constructor_IconResourceKey_DefaultsToNull()
    {
        var descriptor = new ToolDescriptor(
            "TEST", ToolCategory.System, "Cat", "Label", null, ["test"], false);

        Assert.Null(descriptor.IconResourceKey);
    }

    [Fact]
    public void Constructor_LabelWithArgKey_CanBeNull()
    {
        var descriptor = new ToolDescriptor(
            "UUID", ToolCategory.System, "Cat", "Label", null, ["uuid"], false);

        Assert.Null(descriptor.LabelWithArgKey);
    }

    [Fact]
    public void IsNetworkTool_True_ForNetworkTools()
    {
        var descriptor = new ToolDescriptor(
            "PING", ToolCategory.Network, "Cat", "Label", null, ["ping"], true);

        Assert.True(descriptor.IsNetworkTool);
    }

    [Fact]
    public void IsNetworkTool_False_ForNonNetworkTools()
    {
        var descriptor = new ToolDescriptor(
            "HASH", ToolCategory.Security, "Cat", "Label", null, ["hash"], false);

        Assert.False(descriptor.IsNetworkTool);
    }

    [Fact]
    public void ToolCategory_HasFourValues()
    {
        var values = Enum.GetValues<ToolCategory>();
        Assert.Equal(4, values.Length);
    }

    [Fact]
    public void Record_Equality_WorksCorrectly()
    {
        var a = new ToolDescriptor("A", ToolCategory.Network, "Cat", "L", null, ["a"], true);
        var b = new ToolDescriptor("A", ToolCategory.Network, "Cat", "L", null, ["a"], true);

        // Records use value equality, but arrays compare by reference
        Assert.NotEqual(a, b); // Different array instances
    }

    [Fact]
    public void Record_WithExpression_CreatesModifiedCopy()
    {
        var original = new ToolDescriptor(
            "PING", ToolCategory.Network, "Cat", "Label", null, ["ping"], true);
        var modified = original with { Id = "DNS" };

        Assert.Equal("DNS", modified.Id);
        Assert.Equal("TOOL:DNS", modified.ToolType);
        Assert.Equal(ToolCategory.Network, modified.Category);
    }

    // ── ToolType for various IDs ────────────────────────────────────────

    [Theory]
    [InlineData("PING", "TOOL:PING")]
    [InlineData("DNS", "TOOL:DNS")]
    [InlineData("HASH", "TOOL:HASH")]
    [InlineData("PORTSCANNER", "TOOL:PORTSCANNER")]
    [InlineData("BASE64", "TOOL:BASE64")]
    [InlineData("UUID", "TOOL:UUID")]
    [InlineData("a", "TOOL:a")]
    public void ToolType_ReturnsCorrectPrefix_ForVariousIds(string id, string expected)
    {
        var descriptor = new ToolDescriptor(
            id, ToolCategory.Network, "Cat", "Label", null, ["test"], false);

        Assert.Equal(expected, descriptor.ToolType);
    }

    [Fact]
    public void Constructor_AllParameters_WithIconResourceKey()
    {
        var descriptor = new ToolDescriptor(
            "PORTSCANNER", ToolCategory.Network, "ToolCategoryNetwork",
            "PaletteToolPortScanner", "PaletteToolPortScannerWith",
            ["portscan", "scan"], true, "Icon.Tool.PortScanner");

        Assert.Equal("PORTSCANNER", descriptor.Id);
        Assert.Equal(ToolCategory.Network, descriptor.Category);
        Assert.Equal("ToolCategoryNetwork", descriptor.CategoryLabelKey);
        Assert.Equal("PaletteToolPortScanner", descriptor.LabelKey);
        Assert.Equal("PaletteToolPortScannerWith", descriptor.LabelWithArgKey);
        Assert.Equal(2, descriptor.CommandPrefixes.Length);
        Assert.True(descriptor.IsNetworkTool);
        Assert.Equal("Icon.Tool.PortScanner", descriptor.IconResourceKey);
        Assert.Equal("TOOL:PORTSCANNER", descriptor.ToolType);
    }

    [Fact]
    public void Constructor_WithNullIconResourceKey_DefaultsToNull()
    {
        var descriptor = new ToolDescriptor(
            "BASE64", ToolCategory.Encoding, "ToolCategoryEncoding",
            "PaletteToolBase64", null, ["base64", "b64"], false, null);

        Assert.Null(descriptor.IconResourceKey);
    }

    [Fact]
    public void Constructor_WithSpecifiedIconResourceKey_StoresIt()
    {
        var descriptor = new ToolDescriptor(
            "WHOIS", ToolCategory.Network, "Cat", "Label", null,
            ["whois"], true, "Icon.Tool.Whois");

        Assert.Equal("Icon.Tool.Whois", descriptor.IconResourceKey);
    }

    [Theory]
    [InlineData(ToolCategory.Network, "ToolCategoryNetwork")]
    [InlineData(ToolCategory.Security, "ToolCategorySecurity")]
    [InlineData(ToolCategory.Encoding, "ToolCategoryEncoding")]
    [InlineData(ToolCategory.System, "ToolCategorySystem")]
    public void AllCategoryValues_CanBeUsedWithCategoryLabelKey(ToolCategory category, string catKey)
    {
        var descriptor = new ToolDescriptor(
            "TEST", category, catKey, "Label", null, ["test"], false);

        Assert.Equal(category, descriptor.Category);
        Assert.Equal(catKey, descriptor.CategoryLabelKey);
    }

    [Fact]
    public void CommandPrefixes_CanBeMultiple()
    {
        var prefixes = new[] { "dns", "nslookup", "dig" };
        var descriptor = new ToolDescriptor(
            "DNS", ToolCategory.Network, "Cat", "Label", "LabelWith",
            prefixes, true);

        Assert.Equal(3, descriptor.CommandPrefixes.Length);
        Assert.Contains("dns", descriptor.CommandPrefixes);
        Assert.Contains("nslookup", descriptor.CommandPrefixes);
        Assert.Contains("dig", descriptor.CommandPrefixes);
    }

    [Fact]
    public void CommandPrefixes_SingleEntry()
    {
        var descriptor = new ToolDescriptor(
            "UUID", ToolCategory.System, "Cat", "Label", null, ["uuid"], false);

        Assert.Single(descriptor.CommandPrefixes);
        Assert.Equal("uuid", descriptor.CommandPrefixes[0]);
    }
}
