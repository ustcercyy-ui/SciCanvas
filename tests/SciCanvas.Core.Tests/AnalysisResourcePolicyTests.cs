using SciCanvas.Core.Science;

namespace SciCanvas.Core.Tests;

public sealed class AnalysisResourcePolicyTests
{
    [Fact]
    public void DefaultPolicy_IsValidAndUsesFiniteSafetyBudgets()
    {
        AnalysisResourcePolicy policy = AnalysisResourcePolicy.Default;

        Assert.True(policy.IsValid);
        Assert.InRange(policy.MaxPixels, 1, long.MaxValue);
        Assert.InRange(policy.MaxComponentsSafety, 1, int.MaxValue);
        Assert.InRange(policy.MaxBoundaryPoints, 4, int.MaxValue);
        Assert.InRange(policy.MemoryBudgetBytes, 1, long.MaxValue);
    }

    [Fact]
    public void Policy_RejectsNonPositiveOrUnusableLimits()
    {
        AnalysisResourcePolicy valid = AnalysisResourcePolicy.Default;

        Assert.False((valid with { MaxPixels = 0 }).IsValid);
        Assert.False((valid with { MaxComponentsSafety = 0 }).IsValid);
        Assert.False((valid with { MaxBoundaryPoints = 3 }).IsValid);
        Assert.False((valid with { MemoryBudgetBytes = 0 }).IsValid);
    }

    [Fact]
    public void TooComplexException_ReportsStructuredLimitAndRecoveryAdvice()
    {
        var error = new AnalysisTooComplexException(
            AnalysisResourceLimitKind.MaxComponentsSafety,
            11,
            10,
            "符合筛选条件的连通域数量过多");

        Assert.Equal(AnalysisTooComplexException.ErrorCode, "AnalysisTooComplex");
        Assert.Equal(AnalysisResourceLimitKind.MaxComponentsSafety, error.LimitKind);
        Assert.Equal(11, error.Observed);
        Assert.Equal(10, error.Limit);
        Assert.Contains("未返回残缺科研结果", error.Message, StringComparison.Ordinal);
        Assert.Contains("MinimumArea", error.Message, StringComparison.Ordinal);
        Assert.Contains("threshold", error.Message, StringComparison.Ordinal);
        Assert.Contains("ROI", error.Message, StringComparison.Ordinal);
    }
}
