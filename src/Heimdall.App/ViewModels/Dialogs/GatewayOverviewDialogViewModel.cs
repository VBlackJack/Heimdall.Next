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

using System.Collections.ObjectModel;
using Heimdall.App.Services;
using Heimdall.Core.Localization;

namespace Heimdall.App.ViewModels.Dialogs;

public sealed class GatewayOverviewDialogViewModel
{
    public GatewayOverviewDialogViewModel(GatewayOverview overview, LocalizationManager localizer)
    {
        ArgumentNullException.ThrowIfNull(overview);
        ArgumentNullException.ThrowIfNull(localizer);

        DialogTitle = localizer["GatewayOverviewTitle"];
        Description = localizer["GatewayOverviewDescription"];
        CloseLabel = localizer["BtnClose"];
        GatewaySummary = localizer.Format("GatewayOverviewSummaryGateways", overview.GatewayCount);
        RoutedSessionSummary = localizer.Format("GatewayOverviewSummaryRoutedSessions", overview.RoutedSessionCount);
        MissingReferenceSummary = localizer.Format(
            "GatewayOverviewSummaryMissingReferences",
            overview.MissingReferenceCount);
        EmptyGatewaysText = localizer["GatewayOverviewEmpty"];
        MissingReferencesDescription = localizer["GatewayOverviewMissingDescription"];

        Gateways = new ObservableCollection<GatewayOverviewGatewayItemViewModel>(
            overview.Gateways.Select(gateway => new GatewayOverviewGatewayItemViewModel(gateway, localizer)));
        MissingReferences = new ObservableCollection<GatewayOverviewMissingReferenceItemViewModel>(
            overview.MissingReferences.Select(reference =>
                new GatewayOverviewMissingReferenceItemViewModel(reference, localizer)));
    }

    public string DialogTitle { get; }

    public string Description { get; }

    public string CloseLabel { get; }

    public string GatewaySummary { get; }

    public string RoutedSessionSummary { get; }

    public string MissingReferenceSummary { get; }

    public string EmptyGatewaysText { get; }

    public string MissingReferencesDescription { get; }

    public ObservableCollection<GatewayOverviewGatewayItemViewModel> Gateways { get; }

    public ObservableCollection<GatewayOverviewMissingReferenceItemViewModel> MissingReferences { get; }

    public bool HasGateways => Gateways.Count > 0;

    public bool HasMissingReferences => MissingReferences.Count > 0;
}

public sealed class GatewayOverviewGatewayItemViewModel
{
    public GatewayOverviewGatewayItemViewModel(GatewayOverviewGatewayGroup group, LocalizationManager localizer)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(localizer);

        GatewayName = group.GatewayName;
        Endpoint = group.Endpoint;
        ParentText = string.IsNullOrWhiteSpace(group.ParentGatewayName)
            ? ""
            : localizer.Format("GatewayOverviewParentFormat", group.ParentGatewayName);
        SessionCountText = localizer.Format("GatewayOverviewSessionCount", group.Sessions.Count);
        EmptySessionsText = localizer["GatewayOverviewNoSessions"];
        Sessions = new ObservableCollection<GatewayOverviewSessionItemViewModel>(
            group.Sessions.Select(session => new GatewayOverviewSessionItemViewModel(session)));
    }

    public string GatewayName { get; }

    public string Endpoint { get; }

    public string ParentText { get; }

    public bool HasParent => !string.IsNullOrWhiteSpace(ParentText);

    public string SessionCountText { get; }

    public string EmptySessionsText { get; }

    public ObservableCollection<GatewayOverviewSessionItemViewModel> Sessions { get; }

    public bool HasSessions => Sessions.Count > 0;
}

public sealed class GatewayOverviewMissingReferenceItemViewModel
{
    public GatewayOverviewMissingReferenceItemViewModel(
        GatewayOverviewMissingReferenceGroup group,
        LocalizationManager localizer)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(localizer);

        GatewayId = group.GatewayId;
        HeaderText = localizer.Format("GatewayOverviewMissingHeader", group.GatewayId);
        SessionCountText = localizer.Format("GatewayOverviewSessionCount", group.Sessions.Count);
        Sessions = new ObservableCollection<GatewayOverviewSessionItemViewModel>(
            group.Sessions.Select(session => new GatewayOverviewSessionItemViewModel(session)));
    }

    public string GatewayId { get; }

    public string HeaderText { get; }

    public string SessionCountText { get; }

    public ObservableCollection<GatewayOverviewSessionItemViewModel> Sessions { get; }
}

public sealed class GatewayOverviewSessionItemViewModel
{
    public GatewayOverviewSessionItemViewModel(GatewayOverviewSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        DisplayName = session.DisplayName;
        Metadata = string.Join(
            " | ",
            new[]
            {
                session.ConnectionType,
                session.Endpoint,
                session.GroupPath
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    public string DisplayName { get; }

    public string Metadata { get; }

    public bool HasMetadata => !string.IsNullOrWhiteSpace(Metadata);
}
