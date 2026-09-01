using SciCanvas.Core.Data;
using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Tests;

public sealed class TabularDataAssetTests
{
    [Fact]
    public void EnsureValid_AcceptsTypedRowsAndTraceableReadOnlySource()
    {
        DataColumn x = new(Guid.NewGuid(), "Strain", TabularDataType.Numeric, "%", DataColumnRole.X);
        DataColumn y = new(Guid.NewGuid(), "Stress", TabularDataType.Numeric, "MPa", DataColumnRole.Y);
        DataColumn label = new(Guid.NewGuid(), "Specimen", TabularDataType.Text, Role: DataColumnRole.Label);
        var asset = new TabularDataAsset(
            Guid.NewGuid(),
            "Tensile test",
            "D:\\data\\tensile.csv",
            new SourceFingerprint(128, DateTimeOffset.UnixEpoch, new string('A', 64), null),
            1,
            [x, y, label],
            [
                new TabularDataRow(
                [
                    TabularDataValue.FromNumber("0.1", 0.1),
                    TabularDataValue.FromNumber("125.50", 125.5),
                    TabularDataValue.FromText("S-01"),
                ]),
            ],
            new TabularImportMetadata
            {
                Format = TabularDataFormat.Csv,
                ImportedAt = DateTimeOffset.UnixEpoch,
                EncodingName = "UTF-8",
                Delimiter = ',',
                DataRowCount = 1,
                InferenceRowCount = 1,
                OriginalHeaders = ["Strain (%)", "Stress (MPa)", "Specimen"],
            });

        Assert.Same(asset, asset.EnsureValid());
        Assert.Equal(125.5, asset.Rows[0].Values[1].NumericValue);
        Assert.Equal("125.50", asset.Rows[0].Values[1].RawText);
    }

    [Fact]
    public void EnsureValid_RejectsSourcePathWithoutFingerprint()
    {
        TabularDataAsset asset = CreateAsset(
            sourcePath: "D:\\data\\values.tsv",
            fingerprint: null);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(asset.EnsureValid);

        Assert.Contains("路径与指纹", exception.Message);
    }

    [Fact]
    public void EnsureValid_RejectsRowWidthAndTypedValueMismatch()
    {
        DataColumn x = new(Guid.NewGuid(), "X", TabularDataType.Numeric, Role: DataColumnRole.X);
        var widthMismatch = new TabularDataAsset(
            Guid.NewGuid(),
            "Width mismatch",
            null,
            null,
            1,
            [x],
            [new TabularDataRow([])],
            Metadata(["X"]));
        var typeMismatch = widthMismatch with
        {
            Name = "Type mismatch",
            Rows = [new TabularDataRow([TabularDataValue.FromText("not numeric")])],
        };

        Assert.Throws<InvalidDataException>(widthMismatch.EnsureValid);
        Assert.Throws<InvalidDataException>(typeMismatch.EnsureValid);
    }

    [Fact]
    public void EnsureValid_RejectsDuplicateColumnNamesAndNonNumericAxisRoles()
    {
        DataColumn first = new(Guid.NewGuid(), "Value", TabularDataType.Numeric);
        DataColumn duplicate = new(Guid.NewGuid(), "value", TabularDataType.Numeric);
        var duplicateAsset = new TabularDataAsset(
            Guid.NewGuid(),
            "Duplicate columns",
            null,
            null,
            1,
            [first, duplicate],
            [new TabularDataRow([
                TabularDataValue.FromNumber("1", 1),
                TabularDataValue.FromNumber("2", 2),
            ])],
            Metadata(["Value", "value"]));

        Assert.Throws<InvalidDataException>(duplicateAsset.EnsureValid);
        Assert.Throws<InvalidDataException>(() =>
            new DataColumn(Guid.NewGuid(), "Category X", TabularDataType.Text, Role: DataColumnRole.X)
                .EnsureValid());
    }

    [Fact]
    public void DataValue_RejectsNonFiniteNumericValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TabularDataValue.FromNumber("NaN", double.NaN));
        Assert.Throws<InvalidDataException>(() =>
            new TabularDataValue("1", NumericValue: 1, BooleanValue: true)
                .EnsureCompatible(TabularDataType.Numeric));
    }

    private static TabularDataAsset CreateAsset(
        string? sourcePath = null,
        SourceFingerprint? fingerprint = null)
    {
        DataColumn column = new(Guid.NewGuid(), "Value", TabularDataType.Numeric);
        return new TabularDataAsset(
            Guid.NewGuid(),
            "Values",
            sourcePath,
            fingerprint,
            1,
            [column],
            [new TabularDataRow([TabularDataValue.FromNumber("1", 1)])],
            Metadata(["Value"]));
    }

    private static TabularImportMetadata Metadata(IReadOnlyList<string> headers) => new()
    {
        Format = TabularDataFormat.Csv,
        ImportedAt = DateTimeOffset.UnixEpoch,
        EncodingName = "UTF-8",
        Delimiter = ',',
        DataRowCount = 1,
        InferenceRowCount = 1,
        OriginalHeaders = headers,
    };
}
