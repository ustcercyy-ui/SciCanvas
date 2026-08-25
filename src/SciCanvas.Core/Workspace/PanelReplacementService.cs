namespace SciCanvas.Core.Workspace;

public sealed record PanelReplacementResult(
    FigurePanel Panel,
    IReadOnlyList<ScientificObject> ScientificObjects,
    IReadOnlyList<string> Warnings);

public static class PanelReplacementService
{
    public static PanelReplacementResult Replace(
        FigurePanel panel,
        ScientificAsset previousAsset,
        ScientificAsset replacementAsset,
        IReadOnlyList<ScientificObject> scientificObjects)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(previousAsset);
        ArgumentNullException.ThrowIfNull(replacementAsset);
        ArgumentNullException.ThrowIfNull(scientificObjects);
        if (panel.AssetId != previousAsset.Id)
        {
            throw new InvalidOperationException("Panel 当前 Asset 与替换请求不一致。" );
        }

        FigurePanel replacementPanel = panel with { AssetId = replacementAsset.Id };
        List<string> warnings = [];
        ScientificObject[] replacedObjects = scientificObjects
            .Select(item => ReplaceScientificObject(
                item,
                panel.Id,
                previousAsset,
                replacementAsset,
                warnings))
            .ToArray();

        return new PanelReplacementResult(replacementPanel, replacedObjects, warnings);
    }

    private static ScientificObject ReplaceScientificObject(
        ScientificObject item,
        Guid panelId,
        ScientificAsset previousAsset,
        ScientificAsset replacementAsset,
        ICollection<string> warnings)
    {
        if (item.PanelId != panelId)
        {
            return item;
        }

        switch (item)
        {
            case AnnotationObject or PanelLabelObject:
                return item;

            case ScaleBarObject scaleBar:
            {
                ScientificValidity validity = ValidateScaleBar(scaleBar, replacementAsset);
                if (validity.State != ScientificValidityState.Valid)
                {
                    warnings.Add("Scale bar requires review after source replacement." );
                }

                return scaleBar with
                {
                    AssetId = replacementAsset.Id,
                    SourceRevision = replacementAsset.Source.SourceRevision,
                    Validity = validity,
                };
            }

            case MeasurementObject measurement:
                warnings.Add("Measurement requires review because the panel source changed." );
                return measurement with
                {
                    Validity = ScientificValidity.ReviewRequired(
                        "Source asset changed; measurement remains bound to the previous source coordinates."),
                };

            case RoiObject roi:
                warnings.Add("ROI requires review because the panel source changed." );
                return roi with
                {
                    Validity = ScientificValidity.ReviewRequired(
                        "Source asset changed; ROI mapping has not been established."),
                };

            case InsetObject inset:
                warnings.Add("Inset requires review because its source ROI may no longer match." );
                return inset with
                {
                    Validity = ScientificValidity.ReviewRequired(
                        "Source asset changed; inset/ROI relationship must be verified."),
                };

            case ColorbarObject colorbar:
                warnings.Add("Colorbar range requires review after source replacement." );
                return colorbar with
                {
                    AssetId = replacementAsset.Id,
                    SourceRevision = replacementAsset.Source.SourceRevision,
                    Validity = ScientificValidity.ReviewRequired(
                        "Source asset changed; verify data range and colormap."),
                };

            default:
                return item with
                {
                    Validity = ScientificValidity.ReviewRequired(
                        $"Source changed from {previousAsset.Name} to {replacementAsset.Name}."),
                };
        }
    }

    private static ScientificValidity ValidateScaleBar(
        ScaleBarObject scaleBar,
        ScientificAsset replacementAsset)
    {
        if (!replacementAsset.HasValidCalibration)
        {
            return ScientificValidity.Invalid(
                "Scale bar has no valid calibration after source replacement." );
        }

        try
        {
            _ = ScientificLengthUnits.Convert(
                scaleBar.PhysicalLength,
                scaleBar.Unit,
                replacementAsset.Calibration!.Unit);
            return ScientificValidity.Valid;
        }
        catch (NotSupportedException)
        {
            return ScientificValidity.Warning(
                "Scale bar unit is not convertible to the replacement asset calibration unit." );
        }
    }
}
