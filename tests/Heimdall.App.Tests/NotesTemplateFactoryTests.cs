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
using Heimdall.Core.Models;

namespace Heimdall.App.Tests;

public class NotesTemplateFactoryTests
{
    private static readonly DateTime TestDate = new(2026, 3, 15, 14, 30, 0);

    [Fact]
    public void Blank_HasHeadingAndMarkdownExtension()
    {
        var draft = NotesTemplateFactory.Create(NoteTemplateKind.Blank, null, TestDate);

        Assert.EndsWith(".md", draft.RelativePath);
        Assert.StartsWith("# ", draft.Content);
        Assert.Contains("Working Note", draft.Content);
    }

    [Fact]
    public void Blank_WithContext_IncludesHostInTitle()
    {
        var context = new ToolContext(TargetHost: "server-01", DisplayName: "Server 01");
        var draft = NotesTemplateFactory.Create(NoteTemplateKind.Blank, context, TestDate);

        Assert.Contains("Server 01", draft.Content);
    }

    [Fact]
    public void Daily_HasDateInPath()
    {
        var draft = NotesTemplateFactory.Create(NoteTemplateKind.Daily, null, TestDate);

        Assert.Contains("daily", draft.RelativePath);
        Assert.Contains("2026", draft.RelativePath);
        Assert.Contains("2026-03-15", draft.RelativePath);
        Assert.EndsWith(".md", draft.RelativePath);
    }

    [Fact]
    public void Daily_HasFocusSection()
    {
        var draft = NotesTemplateFactory.Create(NoteTemplateKind.Daily, null, TestDate);

        Assert.Contains("## Focus", draft.Content);
        Assert.Contains("## Journal", draft.Content);
        Assert.Contains("## Follow-up", draft.Content);
    }

    [Fact]
    public void Incident_HasTimelineSection()
    {
        var draft = NotesTemplateFactory.Create(NoteTemplateKind.Incident, null, TestDate);

        Assert.Contains("Incident", draft.Content);
        Assert.Contains("## Timeline", draft.Content);
        Assert.Contains("## Investigation", draft.Content);
        Assert.Contains("## Resolution", draft.Content);
        Assert.Contains("14:30", draft.Content);
    }

    [Fact]
    public void Incident_WithContext_IncludesHostName()
    {
        var context = new ToolContext(TargetHost: "db-primary");
        var draft = NotesTemplateFactory.Create(NoteTemplateKind.Incident, context, TestDate);

        Assert.Contains("db-primary", draft.Content);
    }

    [Fact]
    public void Procedure_HasStepsSection()
    {
        var draft = NotesTemplateFactory.Create(NoteTemplateKind.Procedure, null, TestDate);

        Assert.Contains("## Purpose", draft.Content);
        Assert.Contains("## Steps", draft.Content);
        Assert.Contains("## Validation", draft.Content);
        Assert.Contains("## Rollback", draft.Content);
    }

    [Fact]
    public void MetadataLine_IncludesCreatedDate()
    {
        var draft = NotesTemplateFactory.Create(NoteTemplateKind.Blank, null, TestDate);

        Assert.Contains("created 2026-03-15 14:30", draft.Content);
    }

    [Fact]
    public void MetadataLine_WithFullContext_IncludesAllFields()
    {
        var context = new ToolContext(
            TargetHost: "10.0.0.1",
            TargetPort: 22,
            DisplayName: "MyServer",
            Username: "admin",
            ProjectName: "Infra",
            GroupName: "Prod",
            ConnectionType: "SSH");

        var draft = NotesTemplateFactory.Create(NoteTemplateKind.Blank, context, TestDate);

        Assert.Contains("host 10.0.0.1", draft.Content);
        Assert.Contains("port 22", draft.Content);
        Assert.Contains("display MyServer", draft.Content);
        Assert.Contains("user admin", draft.Content);
        Assert.Contains("project Infra", draft.Content);
        Assert.Contains("group Prod", draft.Content);
        Assert.Contains("type SSH", draft.Content);
    }

    [Fact]
    public void Slugify_RemovesSpecialCharacters()
    {
        var context = new ToolContext(DisplayName: "Server (Prod) #1");
        var draft = NotesTemplateFactory.Create(NoteTemplateKind.Blank, context, TestDate);

        Assert.DoesNotContain("(", draft.RelativePath);
        Assert.DoesNotContain("#", draft.RelativePath);
        Assert.DoesNotContain(" ", draft.RelativePath);
    }

    [Fact]
    public void TimestampPrefix_InFileName()
    {
        var draft = NotesTemplateFactory.Create(NoteTemplateKind.Blank, null, TestDate);

        Assert.StartsWith("20260315-143000", draft.RelativePath);
    }
}
