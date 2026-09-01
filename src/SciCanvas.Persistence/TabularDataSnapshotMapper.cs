using SciCanvas.Core.Data;
using SciCanvas.Core.Sources;

namespace SciCanvas.Persistence;

public static class TabularDataSnapshotMapper
{
    public static ProjectTabularDataAssetSnapshot ToSnapshot(TabularDataAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        asset.EnsureValid();
        return new ProjectTabularDataAssetSnapshot
        {
            Id = asset.Id,
            Name = asset.Name,
            SourcePath = asset.SourcePath,
            Fingerprint = asset.Fingerprint is null
                ? null
                : new ProjectFingerprintSnapshot
                {
                    ByteLength = asset.Fingerprint.ByteLength,
                    LastWriteTimeUtc = asset.Fingerprint.LastWriteTimeUtc,
                    Sha256 = asset.Fingerprint.Sha256,
                    WindowsFileId = asset.Fingerprint.WindowsFileId,
                },
            SourceRevision = asset.SourceRevision,
            Columns = asset.Columns.Select(column => new ProjectDataColumnSnapshot
            {
                Id = column.Id,
                Name = column.Name,
                DataType = ToDataTypeKey(column.DataType),
                Unit = column.Unit,
                Role = column.Role is null ? null : ToRoleKey(column.Role.Value),
            }).ToArray(),
            Rows = asset.Rows.Select(row => new ProjectTabularDataRowSnapshot
            {
                Values = row.Values.Select(value => new ProjectTabularDataValueSnapshot
                {
                    RawText = value.RawText,
                    NumericValue = value.NumericValue,
                    BooleanValue = value.BooleanValue,
                    DateTimeValue = value.DateTimeValue,
                }).ToArray(),
            }).ToArray(),
            ImportMetadata = new ProjectTabularImportMetadataSnapshot
            {
                Format = ToFormatKey(asset.ImportMetadata.Format),
                ImportedAt = asset.ImportMetadata.ImportedAt,
                EncodingName = asset.ImportMetadata.EncodingName,
                Delimiter = asset.ImportMetadata.Delimiter?.ToString(),
                SheetName = asset.ImportMetadata.SheetName,
                SelectedRange = asset.ImportMetadata.SelectedRange,
                HeaderRow = asset.ImportMetadata.HeaderRow,
                DataRowCount = asset.ImportMetadata.DataRowCount,
                InferenceRowCount = asset.ImportMetadata.InferenceRowCount,
                OriginalHeaders = asset.ImportMetadata.OriginalHeaders.ToArray(),
            },
        };
    }

    public static TabularDataAsset ToModel(ProjectTabularDataAssetSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        SourceFingerprint? fingerprint = snapshot.Fingerprint is null
            ? null
            : new SourceFingerprint(
                snapshot.Fingerprint.ByteLength,
                snapshot.Fingerprint.LastWriteTimeUtc,
                snapshot.Fingerprint.Sha256,
                snapshot.Fingerprint.WindowsFileId);
        IReadOnlyList<DataColumn> columns = snapshot.Columns
            .Select(column => new DataColumn(
                column.Id,
                column.Name,
                ParseDataType(column.DataType),
                column.Unit,
                column.Role is null ? null : ParseRole(column.Role)))
            .ToArray();
        IReadOnlyList<TabularDataRow> rows = snapshot.Rows
            .Select(row => new TabularDataRow(row.Values
                .Select(value => new TabularDataValue(
                    value.RawText,
                    value.NumericValue,
                    value.BooleanValue,
                    value.DateTimeValue))
                .ToArray()))
            .ToArray();
        ProjectTabularImportMetadataSnapshot metadata = snapshot.ImportMetadata;
        char? delimiter = metadata.Delimiter switch
        {
            null => null,
            { Length: 1 } value => value[0],
            _ => throw new InvalidDataException("工程包含无效的表格分隔符。"),
        };
        return new TabularDataAsset(
            snapshot.Id,
            snapshot.Name,
            snapshot.SourcePath,
            fingerprint,
            snapshot.SourceRevision,
            columns,
            rows,
            new TabularImportMetadata
            {
                Format = ParseFormat(metadata.Format),
                ImportedAt = metadata.ImportedAt,
                EncodingName = metadata.EncodingName,
                Delimiter = delimiter,
                SheetName = metadata.SheetName,
                SelectedRange = metadata.SelectedRange,
                HeaderRow = metadata.HeaderRow,
                DataRowCount = metadata.DataRowCount,
                InferenceRowCount = metadata.InferenceRowCount,
                OriginalHeaders = metadata.OriginalHeaders.ToArray(),
            }).EnsureValid();
    }

    private static string ToDataTypeKey(TabularDataType type) => type switch
    {
        TabularDataType.Numeric => "numeric",
        TabularDataType.Text => "text",
        TabularDataType.Boolean => "boolean",
        TabularDataType.DateTime => "dateTime",
        _ => throw new InvalidDataException("未知表格列类型。"),
    };

    private static TabularDataType ParseDataType(string value) => value.ToLowerInvariant() switch
    {
        "numeric" => TabularDataType.Numeric,
        "text" => TabularDataType.Text,
        "boolean" => TabularDataType.Boolean,
        "datetime" => TabularDataType.DateTime,
        _ => throw new InvalidDataException($"未知表格列类型：{value}"),
    };

    private static string ToRoleKey(DataColumnRole role) => role switch
    {
        DataColumnRole.X => "x",
        DataColumnRole.Y => "y",
        DataColumnRole.YError => "yError",
        DataColumnRole.Category => "category",
        DataColumnRole.Label => "label",
        _ => "other",
    };

    private static DataColumnRole ParseRole(string value) => value.ToLowerInvariant() switch
    {
        "x" => DataColumnRole.X,
        "y" => DataColumnRole.Y,
        "yerror" => DataColumnRole.YError,
        "category" => DataColumnRole.Category,
        "label" => DataColumnRole.Label,
        "other" => DataColumnRole.Other,
        _ => throw new InvalidDataException($"未知表格列角色：{value}"),
    };

    private static string ToFormatKey(TabularDataFormat format) => format switch
    {
        TabularDataFormat.Csv => "csv",
        TabularDataFormat.Tsv => "tsv",
        TabularDataFormat.Xlsx => "xlsx",
        _ => throw new InvalidDataException("未知表格来源格式。"),
    };

    private static TabularDataFormat ParseFormat(string value) => value.ToLowerInvariant() switch
    {
        "csv" => TabularDataFormat.Csv,
        "tsv" => TabularDataFormat.Tsv,
        "xlsx" => TabularDataFormat.Xlsx,
        _ => throw new InvalidDataException($"未知表格来源格式：{value}"),
    };
}
