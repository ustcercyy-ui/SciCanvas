using SciCanvas.Core.Channels;
using SciCanvas.Core.Linking;
using SciCanvas.Core.Science;
using SpatialLinkGroup = SciCanvas.Core.Linking.LinkGroup;
using CoreLinkSyncOptions = SciCanvas.Core.Linking.LinkSyncOptions;
using CoreSpatialMapping = SciCanvas.Core.Linking.SpatialMapping;
using CoreSpatialMappingKind = SciCanvas.Core.Linking.SpatialMappingKind;

namespace SciCanvas.Core.Workspace;

internal sealed class PreciseScientificObjectRule()
    : QcRuleBase("analysis.integrity", QcCategory.Integrity)
{
    public override IEnumerable<QcResult> Evaluate(QcContext context)
    {
        HashSet<Guid> channelIds = context.EffectiveMultiChannelGroups
            .SelectMany(group => group.Members)
            .Select(member => member.ChannelId)
            .ToHashSet();
        Dictionary<Guid, CoreSpatialMapping> mappings = context.EffectiveLinkGroups
            .SelectMany(group => group.Mappings)
            .DistinctBy(mapping => mapping.Id)
            .ToDictionary(mapping => mapping.Id);

        foreach (AnalysisResultObject analysisObject in context.Project.ScientificObjects.Values
                     .OfType<AnalysisResultObject>())
        {
            ScientificImageAnalysisResult analysis = analysisObject.Result;
            Guid assetId = analysis.SourceAssetId;
            Guid? roiId = analysis is RoiStatisticsResult roi ? roi.RoiId : null;
            Guid? channelId = analysis is RoiStatisticsResult channel ? channel.ScientificChannelId : null;
            Guid? linkGroupId = analysis is RoiStatisticsResult linked ? linked.LinkGroupId : null;
            Guid? mappingId = analysis is RoiStatisticsResult mapped ? mapped.MappingId : null;
            var location = new QcIssueLocation(
                ProjectId: context.Project.Id,
                PanelId: analysisObject.PanelId,
                AssetId: assetId,
                ScientificObjectId: analysisObject.Id,
                AnalysisResultId: analysis.Id,
                ChannelId: channelId,
                LinkGroupId: linkGroupId,
                MappingId: mappingId,
                SourceRegion: analysis is RoiStatisticsResult region ? region.Region : null);

            if (!context.Project.Assets.TryGetValue(assetId, out ScientificAsset? asset))
            {
                yield return Issue(QcSeverity.Error, $"source:{analysis.Id:N}",
                    "Analysis source asset is missing.", location: location);
                continue;
            }

            if (analysis.SourceRevision != asset.Source.SourceRevision)
            {
                yield return Issue(QcSeverity.Error, $"revision:{analysis.Id:N}",
                    $"Analysis uses source revision {analysis.SourceRevision}; current revision is {asset.Source.SourceRevision}.",
                    location: location) with { RuleId = "analysis.revision-stale" };
            }

            if (roiId is Guid requiredRoi &&
                (!context.Project.ScientificObjects.TryGetValue(requiredRoi, out ScientificObject? candidate) ||
                 candidate is not RoiObject))
            {
                yield return Issue(QcSeverity.Error, $"roi-missing:{analysis.Id:N}",
                    "Analysis references a missing ROI.", location: location) with { RuleId = "analysis.roi-missing" };
            }
            else if (roiId is Guid validRoi &&
                     context.Project.ScientificObjects.GetValueOrDefault(validRoi) is RoiObject roiObject)
            {
                bool roiIsValid = true;
                try
                {
                    roiObject.EnsureValid();
                }
                catch (InvalidOperationException)
                {
                    roiIsValid = false;
                }

                if (!roiIsValid)
                {
                    yield return Issue(QcSeverity.Error, $"roi-invalid:{analysis.Id:N}",
                        "Analysis references an invalid ROI geometry or provenance.", location: location) with
                    {
                        RuleId = "analysis.roi-invalid",
                    };
                }
            }

            if (channelId is Guid requiredChannel && !channelIds.Contains(requiredChannel))
            {
                yield return Issue(QcSeverity.Error, $"channel:{analysis.Id:N}",
                    "Analysis references a missing scientific channel.", location: location) with
                {
                    RuleId = "analysis.channel-missing",
                };
            }

            if (mappingId is Guid requiredMapping)
            {
                if (!mappings.TryGetValue(requiredMapping, out CoreSpatialMapping? mapping))
                {
                    yield return Issue(QcSeverity.Error, $"mapping:{analysis.Id:N}",
                        "Analysis references a missing spatial mapping.", location: location) with
                    {
                        RuleId = "analysis.mapping-stale",
                    };
                }
                else if (!IsMappingCurrent(context, mapping))
                {
                    yield return Issue(QcSeverity.Error, $"mapping-stale:{analysis.Id:N}",
                        "Analysis spatial mapping is stale for the current source revisions.", location: location) with
                    {
                        RuleId = "analysis.mapping-stale",
                    };
                }
            }
        }

        foreach (MeasurementOverlayObject overlay in context.Project.ScientificObjects.Values
                     .OfType<MeasurementOverlayObject>())
        {
            var location = new QcIssueLocation(
                ProjectId: context.Project.Id,
                PanelId: overlay.PanelId,
                AssetId: overlay.AssetId,
                ScientificObjectId: overlay.Id,
                MeasurementId: overlay.MeasurementId);
            if (!context.Project.ScientificObjects.Values.OfType<MeasurementObject>()
                    .Any(measurement => measurement.Measurement.Id == overlay.MeasurementId))
            {
                yield return Issue(QcSeverity.Error, $"measurement:{overlay.Id:N}",
                    "Measurement overlay references a missing measurement.", location: location) with
                {
                    RuleId = "measurement-overlay.measurement-missing",
                };
            }
        }
    }

    private static bool IsMappingCurrent(QcContext context, CoreSpatialMapping mapping) =>
        context.Project.Assets.TryGetValue(mapping.SourceAssetId, out ScientificAsset? source) &&
        context.Project.Assets.TryGetValue(mapping.TargetAssetId, out ScientificAsset? target) &&
        mapping.MatchesRevisions(source.Source.SourceRevision, target.Source.SourceRevision);
}

internal sealed class MultiChannelIntegrityRule()
    : QcRuleBase("multichannel.integrity", QcCategory.Integrity)
{
    public override IEnumerable<QcResult> Evaluate(QcContext context)
    {
        foreach (MultiChannelAssetGroup group in context.EffectiveMultiChannelGroups)
        {
            ChannelGroupMember[] members = group.Members.ToArray();
            foreach (ChannelGroupMember member in members)
            {
                var location = new QcIssueLocation(
                    ProjectId: context.Project.Id,
                    AssetId: member.AssetId,
                    ChannelId: member.ChannelId);
                if (!context.Project.Assets.TryGetValue(member.AssetId, out ScientificAsset? asset))
                {
                    yield return Issue(QcSeverity.Error, $"source:{group.Id:N}:{member.ChannelId:N}",
                        $"Channel {member.Name} references a missing source asset.", location: location) with
                    {
                        RuleId = "multichannel.channel-source-missing",
                    };
                }
                else if (member.SourceRevision is long captured && captured != asset.Source.SourceRevision)
                {
                    yield return Issue(QcSeverity.Error, $"revision:{group.Id:N}:{member.ChannelId:N}",
                        $"Channel {member.Name} uses source revision {captured}; current revision is {asset.Source.SourceRevision}.",
                        location: location) with { RuleId = "multichannel.channel-revision-stale" };
                }

                bool displayRangeIsValid = true;
                try
                {
                    member.DisplaySettings.EnsureValid();
                }
                catch (InvalidOperationException)
                {
                    displayRangeIsValid = false;
                }

                if (!displayRangeIsValid)
                {
                    yield return Issue(QcSeverity.Error, $"range:{group.Id:N}:{member.ChannelId:N}",
                        $"Channel {member.Name} has an invalid display range.", location: location) with
                    {
                        RuleId = "multichannel.display-range-invalid",
                    };
                }
            }

            foreach (IGrouping<string, ChannelGroupMember> duplicate in members
                         .GroupBy(member => member.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                         .Where(items => items.Count() > 1))
            {
                ChannelGroupMember first = duplicate.First();
                yield return Issue(QcSeverity.Warning, $"name:{group.Id:N}:{duplicate.Key}",
                    $"Multi-channel group contains duplicate channel name “{duplicate.Key}”.",
                    location: new QcIssueLocation(
                        ProjectId: context.Project.Id,
                        AssetId: first.AssetId,
                        ChannelId: first.ChannelId)) with { RuleId = "multichannel.duplicate-channel-name" };
            }

            if (members.Count(member => member.AssetId == group.ReferenceAssetId) != 1)
            {
                yield return Issue(QcSeverity.Warning, $"reference:{group.Id:N}",
                    "Multi-channel group has no unique reference channel.",
                    location: new QcIssueLocation(ProjectId: context.Project.Id, AssetId: group.ReferenceAssetId)) with
                {
                    RuleId = "multichannel.no-reference-channel",
                };
            }

            if (members.Length == 0 || members.All(member => !member.DisplaySettings.Visible))
            {
                yield return Issue(QcSeverity.Error, $"empty:{group.Id:N}",
                    "Multi-channel composite contains no visible channel.",
                    location: new QcIssueLocation(ProjectId: context.Project.Id, AssetId: group.ReferenceAssetId)) with
                {
                    RuleId = "multichannel.composite-empty",
                };
            }
        }
    }
}

internal sealed class LinkedViewIntegrityRule()
    : QcRuleBase("linked-view.integrity", QcCategory.Integrity)
{
    private const double ResidualWarningPixels = 2;
    private const double ResidualErrorPixels = 10;

    public override IEnumerable<QcResult> Evaluate(QcContext context)
    {
        foreach (SpatialLinkGroup group in context.EffectiveLinkGroups)
        {
            foreach (Guid targetAssetId in group.AssetIds.Where(id => id != group.ReferenceAssetId))
            {
                CoreSpatialMapping? mapping = group.Mappings.FirstOrDefault(item => item.TargetAssetId == targetAssetId);
                if (mapping is null)
                {
                    yield return Issue(QcSeverity.Error, $"missing:{group.Id:N}:{targetAssetId:N}",
                        "Linked view is missing a spatial mapping for a target asset.",
                        location: new QcIssueLocation(
                            ProjectId: context.Project.Id,
                            AssetId: targetAssetId,
                            LinkGroupId: group.Id)) with { RuleId = "linked-view.mapping-missing" };
                    continue;
                }

                var location = new QcIssueLocation(
                    ProjectId: context.Project.Id,
                    AssetId: targetAssetId,
                    LinkGroupId: group.Id,
                    MappingId: mapping.Id);
                if (!context.Project.Assets.TryGetValue(mapping.SourceAssetId, out ScientificAsset? source) ||
                    !context.Project.Assets.TryGetValue(mapping.TargetAssetId, out ScientificAsset? target) ||
                    !mapping.MatchesRevisions(source.Source.SourceRevision, target.Source.SourceRevision))
                {
                    yield return Issue(QcSeverity.Error, $"stale:{mapping.Id:N}",
                        "Spatial mapping must be reviewed because a source revision changed.", location: location) with
                    {
                        RuleId = "linked-view.mapping-revision-stale",
                    };
                }

                if (mapping.ResidualPixels is double residual && residual >= ResidualWarningPixels)
                {
                    QcSeverity severity = residual >= ResidualErrorPixels ? QcSeverity.Error : QcSeverity.Warning;
                    yield return Issue(severity, $"residual:{mapping.Id:N}",
                        $"Registration RMS residual is {residual:0.###} px.", location: location) with
                    {
                        RuleId = "linked-view.registration-residual",
                    };
                }

                if (mapping.Kind == CoreSpatialMappingKind.Affine && group.SyncOptions.HasFlag(CoreLinkSyncOptions.Crop))
                {
                    yield return Issue(QcSeverity.Info, $"crop:{mapping.Id:N}",
                        "Affine linked crop uses the mapped polygon bounding box; it is an approximation.",
                        location: location) with { RuleId = "linked-view.crop-bounding-box" };
                }
            }

            foreach (RoiObject roi in context.Project.ScientificObjects.Values.OfType<RoiObject>()
                         .Where(roi => roi.Propagation?.LinkGroupId == group.Id))
            {
                if (roi.AssetId is not Guid assetId ||
                    !context.Project.Assets.TryGetValue(assetId, out ScientificAsset? asset))
                {
                    continue;
                }

                bool outside = roi.SourceGeometry.Any(point =>
                    point.X < 0 || point.Y < 0 ||
                    point.X > asset.Image.PixelSize.Width || point.Y > asset.Image.PixelSize.Height);
                if (outside)
                {
                    yield return Issue(QcSeverity.Warning, $"roi:{roi.Id:N}",
                        "Propagated ROI extends outside the target asset.",
                        location: new QcIssueLocation(
                            ProjectId: context.Project.Id,
                            PanelId: roi.PanelId,
                            AssetId: assetId,
                            ScientificObjectId: roi.Id,
                            LinkGroupId: group.Id,
                            MappingId: roi.Propagation?.MappingId)) with
                    {
                        RuleId = "linked-view.roi-outside-target",
                    };
                }
            }
        }
    }
}

internal sealed class ColorbarIntegrityRule()
    : QcRuleBase("colorbar.integrity", QcCategory.Integrity)
{
    public override IEnumerable<QcResult> Evaluate(QcContext context)
    {
        Dictionary<Guid, ChannelGroupMember> channels = context.EffectiveMultiChannelGroups
            .SelectMany(group => group.Members)
            .DistinctBy(member => member.ChannelId)
            .ToDictionary(member => member.ChannelId);
        ColorbarObject[] colorbars = context.Project.ScientificObjects.Values.OfType<ColorbarObject>().ToArray();
        foreach (ColorbarObject colorbar in colorbars.Where(item => item.ChannelId is not null))
        {
            Guid channelId = colorbar.ChannelId!.Value;
            var location = new QcIssueLocation(
                ProjectId: context.Project.Id,
                PanelId: colorbar.PanelId,
                AssetId: colorbar.AssetId,
                ScientificObjectId: colorbar.Id,
                ChannelId: channelId);
            if (!channels.TryGetValue(channelId, out ChannelGroupMember? channel))
            {
                yield return Issue(QcSeverity.Error, $"missing:{colorbar.Id:N}",
                    "Colorbar references a missing scientific channel.", location: location) with
                {
                    RuleId = "colorbar.channel-missing",
                };
                continue;
            }

            if (Math.Abs(colorbar.Minimum - channel.DisplaySettings.DisplayMinimum) > 1e-9 ||
                Math.Abs(colorbar.Maximum - channel.DisplaySettings.DisplayMaximum) > 1e-9)
            {
                yield return Issue(QcSeverity.Error, $"range:{colorbar.Id:N}",
                    "Colorbar range does not match its channel display range.", location: location) with
                {
                    RuleId = "colorbar.range-mismatch",
                };
            }
        }

        foreach (Guid channelId in context.QuantitativeChannelIds ?? new HashSet<Guid>())
        {
            if (!colorbars.Any(colorbar => colorbar.ChannelId == channelId))
            {
                ChannelGroupMember? channel = channels.GetValueOrDefault(channelId);
                yield return Issue(QcSeverity.Warning, $"required:{channelId:N}",
                    "Quantitative map has no colorbar.",
                    location: new QcIssueLocation(
                        ProjectId: context.Project.Id,
                        AssetId: channel?.AssetId,
                        ChannelId: channelId)) with { RuleId = "colorbar.missing-for-quantitative-map" };
            }
        }
    }
}

internal sealed class ExactTransformedDuplicateRule()
    : QcRuleBase("integrity.exact-transformed-duplicate", QcCategory.Integrity)
{
    public override IEnumerable<QcResult> Evaluate(QcContext context)
    {
        foreach (TransformedDuplicateMatch match in
                 TransformedDuplicateDetector.FindExactDuplicates(context.EffectiveRawCrops))
        {
            yield return Issue(
                QcSeverity.Warning,
                $"{match.First.Location.PanelId:N}:{match.Second.Location.PanelId:N}:{match.Transform}",
                $"Panels ({match.First.DisplayName}) and ({match.Second.DisplayName}) contain exactly identical raw source pixels after {match.Transform}.",
                location: match.Second.Location,
                relatedLocations: match.Locations);
        }
    }
}
