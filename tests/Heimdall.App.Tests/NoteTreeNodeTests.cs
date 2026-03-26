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

namespace Heimdall.App.Tests;

public class NoteTreeNodeTests
{
    [Fact]
    public void BuildTree_ExposesReadableMetadataForNoteNodes()
    {
        var note = new NoteListItem(
            FilePath: @"C:\notes\ops\server-setup.md",
            RelativePath: "ops/server-setup.md",
            Title: "Server Setup",
            Summary: "Checklist for a fresh environment",
            Tags: ["infra", "prod"],
            LastModifiedUtc: new DateTime(2026, 3, 26, 15, 0, 0, DateTimeKind.Utc),
            LastModifiedDisplay: "2026-03-26 16:00");

        var tree = NoteTreeNode.BuildTree([note], @"C:\notes");
        var folder = Assert.Single(tree);
        var item = Assert.Single(folder.Children);

        Assert.Equal("Server Setup", item.DisplayTitle);
        Assert.Equal("Checklist for a fresh environment", item.DisplaySummary);
        Assert.Equal("2026-03-26 16:00 | server-setup.md", item.DisplayMeta);
        Assert.Equal("#infra   #prod", item.DisplayTags);
    }
}
