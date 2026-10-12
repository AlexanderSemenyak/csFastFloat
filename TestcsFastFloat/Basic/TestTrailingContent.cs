using System;
using System.Globalization;
using csFastFloat;
using Xunit;

namespace TestcsFastFloat.Tests.Basic
{
  /// <summary>
  /// Regression / characterization tests for the "prefix parsing" behaviour of
  /// <see cref="FastDoubleParser.TryParseDouble(string, out double, NumberStyles, char, char)"/>.
  ///
  /// DEFECT (reported 2026-10-12, Fond2019): <c>TryParseDouble</c> is strtod-like — it parses the
  /// longest valid numeric PREFIX of the input and silently ignores any trailing content.
  /// It does NOT behave like <see cref="double.TryParse(string, NumberStyles, IFormatProvider, out double)"/>,
  /// which requires the WHOLE string (modulo leading/trailing whitespace) to be a number.
  ///
  /// The README documents this ("Trailing content in the string is ignored"), but the API name and the
  /// <see cref="NumberStyles"/> parameter strongly imply BCL semantics, so callers get a silent WRONG
  /// value instead of <c>false</c>.
  ///
  /// Real case: <c>"17.9.2026 12:18"</c> with <c>decimal_separator = ','</c> returns <c>true</c> and
  /// <c>17</c> instead of <c>false</c>. The scalar code path used by the net462 target (no
  /// HAS_INTRINSICS) exhibits exactly the same behaviour as the SIMD path.
  ///
  /// Caller-side guard: use the overload exposing <c>characters_consumed</c> and reject when
  /// <c>characters_consumed != input.Length</c> (after allowing trailing whitespace when
  /// <see cref="NumberStyles.AllowTrailingWhite"/> is set).
  /// </summary>
  public class TestTrailingContent
  {
    /// <summary>
    /// The exact scenario reported by the user: a date/time string is parsed as the integer 17.
    /// </summary>
    [Fact]
    [Trait("Category", "Defect")]
    public void TryParseDouble_UserScenario_DateString_ParsedAsSeventeen()
    {
      const string sut = "17.9.2026 12:18";

      bool ok = FastDoubleParser.TryParseDouble(sut, out double value, NumberStyles.Float, ',');

      Assert.True(ok, "csFastFloat accepts the numeric prefix and reports success");
      Assert.Equal(17d, value);
    }

    /// <summary>
    /// The same input is rejected by the BCL reference implementation — this is the divergence.
    /// </summary>
    [Fact]
    [Trait("Category", "Defect")]
    public void TryParseDouble_UserScenario_DivergesFromBcl()
    {
      const string sut = "17.9.2026 12:18";
      var ru = CultureInfo.GetCultureInfo("ru-RU");

      // Reference semantics: the whole string must be a number (decimal separator is ',' in ru-RU).
      Assert.False(double.TryParse(sut, NumberStyles.Float, ru, out _));

      // csFastFloat: accepts the "17" prefix and reports success.
      Assert.True(FastDoubleParser.TryParseDouble(sut, out double value, NumberStyles.Float, ','));
      Assert.Equal(17d, value);
    }

    /// <summary>
    /// Characterization of the current behaviour: only a prefix is consumed, the remainder is ignored.
    /// </summary>
    [Theory]
    [Trait("Category", "Defect")]
    [InlineData("17.9.2026 12:18", ',', 17d, 2)]
    [InlineData("17.9.2026 12:18", '.', 17.9d, 4)]
    [InlineData("12abc", '.', 12d, 2)]
    [InlineData("1.5 trailing", '.', 1.5d, 3)]
    [InlineData("1,234.5", '.', 1d, 1)]
    [InlineData("3.14 and more", '.', 3.14d, 4)]
    public void TryParseDouble_ParsesLongestPrefix_IgnoresTrailingContent(
        string input, char decimalSeparator, double expectedValue, int expectedConsumed)
    {
      bool ok = FastDoubleParser.TryParseDouble(
          input, out int consumed, out double value, NumberStyles.Float, decimalSeparator);

      Assert.True(ok);
      Assert.Equal(expectedValue, value);
      Assert.Equal(expectedConsumed, consumed);
      Assert.True(consumed < input.Length, "trailing content was silently ignored");
    }

    /// <summary>
    /// The practical workaround available today: the consuming overload reports how many characters
    /// were used, so the caller can enforce whole-input parsing.
    /// </summary>
    [Fact]
    [Trait("Category", "Defect")]
    public void WholeInputGuard_IsAvailable_Via_CharactersConsumed()
    {
      const string sut = "17.9.2026 12:18";

      Assert.True(FastDoubleParser.TryParseDouble(
          sut, out int consumed, out _, NumberStyles.Float, ','));

      // Guard: consumed != length -> the string was NOT fully a number.
      Assert.NotEqual(sut.Length, consumed);
    }

    /// <summary>
    /// Desired (BCL-matching) behaviour for the non-consuming overload.
    /// Kept skipped so the suite stays green until the defect is fixed or the overload is
    /// documented/changed with owner approval (public API semantics change).
    /// </summary>
    [Fact(Skip = "Known defect: the non-consuming TryParseDouble ignores trailing content. " +
                 "Un-skip once the overload requires full-string consumption (BCL semantics).")]
    [Trait("Category", "Defect")]
    public void TryParseDouble_NonConsuming_ShouldRejectTrailingContent_LikeBcl()
    {
      Assert.False(FastDoubleParser.TryParseDouble("17.9.2026 12:18", out _, NumberStyles.Float, ','));
      Assert.False(FastDoubleParser.TryParseDouble("12abc", out _));
      Assert.False(FastDoubleParser.TryParseDouble("1.5 trailing", out _));
    }

    /// <summary>
    /// Valid inputs must keep working (no over-correction).
    /// </summary>
    [Theory]
    [InlineData("42", 42d)]
    [InlineData("3.14", 3.14d)]
    [InlineData("-1e3", -1000d)]
    [InlineData("  42  ", 42d)]
    public void TryParseDouble_ValidInput_StillParses(string input, double expected)
    {
      Assert.True(FastDoubleParser.TryParseDouble(input, out double value));
      Assert.Equal(expected, value);
    }

    /// <summary>
    /// <see cref="FastFloatParser"/> shares the same prefix-parsing design, so the same defect
    /// applies to <c>float</c>.
    /// </summary>
    [Fact]
    [Trait("Category", "Defect")]
    public void TryParseFloat_UserScenario_DateString_ParsedAsSeventeen()
    {
      const string sut = "17.9.2026 12:18";
      var ru = CultureInfo.GetCultureInfo("ru-RU");

      Assert.False(float.TryParse(sut, NumberStyles.Float, ru, out _));

      Assert.True(FastFloatParser.TryParseFloat(sut, out float value, NumberStyles.Float, ','));
      Assert.Equal(17f, value);
    }

    [Theory]
    [Trait("Category", "Defect")]
    [InlineData("17.9.2026 12:18", ',', 17f, 2)]
    [InlineData("12abc", '.', 12f, 2)]
    [InlineData("1.5 trailing", '.', 1.5f, 3)]
    public void TryParseFloat_ParsesLongestPrefix_IgnoresTrailingContent(
        string input, char decimalSeparator, float expectedValue, int expectedConsumed)
    {
      bool ok = FastFloatParser.TryParseFloat(
          input, out int consumed, out float value, NumberStyles.Float, decimalSeparator);

      Assert.True(ok);
      Assert.Equal(expectedValue, value);
      Assert.Equal(expectedConsumed, consumed);
      Assert.True(consumed < input.Length, "trailing content was silently ignored");
    }
  }
}
