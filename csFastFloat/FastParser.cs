﻿using csFastFloat.Enums;
using csFastFloat.Structures;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("TestcsFastFloat")]

namespace csFastFloat
{
  public abstract class FastParser
  {
    public static double ParseDouble(string s, chars_format expectedFormat = chars_format.is_general)
    {
      unsafe
      {
        fixed (char* pStart = s)
        {
          return FastParser.ParseDouble(pStart, pStart + s.Length, expectedFormat);
        }
      }
    }

    public static float ParseFloat(string s, chars_format expectedFormat = chars_format.is_general)
    {
      unsafe
      {
        fixed (char* pStart = s)
        {
          return ParseFloat(pStart, pStart + s.Length, expectedFormat);
        }
      }
    }

    unsafe static public double ParseDouble(char* first, char* last, chars_format expectedFormat = chars_format.is_general)
      => ParseNumber<double>(first, last, new DoubleBinaryFormat(), expectedFormat);

    unsafe static public float ParseFloat(char* first, char* last, chars_format expectedFormat = chars_format.is_general)
                  => ParseNumber<float>(first, last, new FloatBinaryFormat(), expectedFormat);

    unsafe static internal T ParseNumber<T>(char* first, char* last, IBinaryFormat<T> binaryFormat, chars_format expectedFormat = chars_format.is_general)
    {
      while ((first != last) && Utils.is_space((byte)(*first)))
      {
        first++;
      }
      if (first == last)
      {
        throw new ArgumentException();
      }
      ParsedNumberString pns = ParseNumberString(first, last, expectedFormat);
      if (!pns.valid)
      {
        return HandleInvalidInput<T>(first, last, binaryFormat);
      }

      // Next is Clinger's fast path.
      if (binaryFormat.min_exponent_fast_path() <= pns.exponent && pns.exponent <= binaryFormat.max_exponent_fast_path() && pns.mantissa <= binaryFormat.max_mantissa_fast_path() && !pns.too_many_digits)
      {
        return binaryFormat.FastPath(pns);
      }

      AdjustedMantissa am = ComputeFloat(pns.exponent, pns.mantissa, binaryFormat);
      if (pns.too_many_digits)
      {
        if (am != ComputeFloat(pns.exponent, pns.mantissa + 1, binaryFormat))
        {
          am.power2 = -1; // value is invalid.
        }
      }
      // If we called compute_float<binary_format<T>>(pns.exponent, pns.mantissa) and we have an invalid power (am.power2 < 0),
      // then we need to go the long way around again. This is very uncommon.
      if (am.power2 < 0) { am = ParseLongMantissa(first, last, binaryFormat); }
      return binaryFormat.ToFloat(pns.negative, am);
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="q"></param>
    /// <param name="w"></param>
    ///
    /// <returns></returns>

    internal static AdjustedMantissa ComputeFloat<T>(long q, ulong w, IBinaryFormat<T> binaryFormat)
    {
      var answer = new AdjustedMantissa();

      if ((w == 0) || (q < binaryFormat.smallest_power_of_ten()))
      {
        answer.power2 = 0;
        answer.mantissa = 0;
        // result should be zero
        return answer;
      }
      if (q > binaryFormat.largest_power_of_ten())
      {
        // we want to get infinity:
        answer.power2 = binaryFormat.infinite_power();
        answer.mantissa = 0;
        return answer;
      }
      // At this point in time q is in [smallest_power_of_five, largest_power_of_five].

      // We want the most significant bit of i to be 1. Shift if needed.
      int lz = BitOperations.LeadingZeroCount(w);
      w <<= lz;

      // The required precision is mantissa_explicit_bits() + 3 because
      // 1. We need the implicit bit
      // 2. We need an extra bit for rounding purposes
      // 3. We might lose a bit due to the "upperbit" routine (result too small, requiring a shift)

      value128 product = Utils.compute_product_approximation(binaryFormat.mantissa_explicit_bits() + 3, q, w);
      if (product.low == 0xFFFFFFFFFFFFFFFF)
      { //  could guard it further
        // In some very rare cases, this could happen, in which case we might need a more accurate
        // computation that what we can provide cheaply. This is very, very unlikely.
        //
        bool inside_safe_exponent = (q >= -27) && (q <= 55); // always good because 5**q <2**128 when q>=0,
                                                             // and otherwise, for q<0, we have 5**-q<2**64 and the 128-bit reciprocal allows for exact computation.
        if (!inside_safe_exponent)
        {
          answer.power2 = -1; // This (a negative value) indicates an error condition.
          return answer;
        }
      }
      // The "compute_product_approximation" function can be slightly slower than a branchless approach:
      // value128 product = compute_product(q, w);
      // but in practice, we can win big with the compute_product_approximation if its additional branch
      // is easily predicted. Which is best is data specific.
      int upperbit = (int)(product.high >> 63);

      answer.mantissa = product.high >> (upperbit + 64 - binaryFormat.mantissa_explicit_bits() - 3);

      answer.power2 = (int)(Utils.power((int)(q)) + upperbit - lz - binaryFormat.minimum_exponent());
      if (answer.power2 <= 0)
      { // we have a subnormal?
        // Here have that answer.power2 <= 0 so -answer.power2 >= 0
        if (-answer.power2 + 1 >= 64)
        { // if we have more than 64 bits below the minimum exponent, you have a zero for sure.
          answer.power2 = 0;
          answer.mantissa = 0;
          // result should be zero
          return answer;
        }
        // next line is safe because -answer.power2 + 1 < 64
        answer.mantissa >>= -answer.power2 + 1;
        // Thankfully, we can't have both "round-to-even" and subnormals because
        // "round-to-even" only occurs for powers close to 0.
        answer.mantissa += (answer.mantissa & 1); // round up
        answer.mantissa >>= 1;
        // There is a weird scenario where we don't have a subnormal but just.
        // Suppose we start with 2.2250738585072013e-308, we end up
        // with 0x3fffffffffffff x 2^-1023-53 which is technically subnormal
        // whereas 0x40000000000000 x 2^-1023-53  is normal. Now, we need to round
        // up 0x3fffffffffffff x 2^-1023-53  and once we do, we are no longer
        // subnormal, but we can only know this after rounding.
        // So we only declare a subnormal if we are smaller than the threshold.
        answer.power2 = (answer.mantissa < ((ulong)(1) << binaryFormat.mantissa_explicit_bits())) ? 0 : 1;
        return answer;
      }

      // usually, we round *up*, but if we fall right in between and and we have an
      // even basis, we need to round down
      // We are only concerned with the cases where 5**q fits in single 64-bit word.
      if ((product.low <= 1) && (q >= binaryFormat.min_exponent_round_to_even()) && (q <= binaryFormat.max_exponent_round_to_even()) &&
          ((answer.mantissa & 3) == 1))
      { // we may fall between two floats!
        // To be in-between two floats we need that in doing
        //   answer.mantissa = product.high >> (upperbit + 64 - mantissa_explicit_bits() - 3);
        // ... we dropped out only zeroes. But if this happened, then we can go back!!!
        if ((answer.mantissa << (upperbit + 64 - binaryFormat.mantissa_explicit_bits() - 3)) == product.high)
        {
          answer.mantissa &= ~(ulong)(1);          // flip it so that we do not round up
        }
      }

      answer.mantissa += (answer.mantissa & 1); // round up
      answer.mantissa >>= 1;
      if (answer.mantissa >= ((ulong)(2) << binaryFormat.mantissa_explicit_bits()))
      {
        answer.mantissa = ((ulong)(1) << binaryFormat.mantissa_explicit_bits());
        answer.power2++; // undo previous addition
      }

      answer.mantissa &= ~((ulong)(1) << binaryFormat.mantissa_explicit_bits());
      if (answer.power2 >= binaryFormat.infinite_power())
      { // infinity
        answer.power2 = binaryFormat.infinite_power();
        answer.mantissa = 0;
      }
      return answer;
    }

    internal static AdjustedMantissa ComputeFloat<T>(DecimalInfo d, IBinaryFormat<T> binaryFormat)
    {
      AdjustedMantissa answer = new AdjustedMantissa();
      if (d.num_digits == 0)
      {
        // should be zero
        answer.power2 = 0;
        answer.mantissa = 0;
        return answer;
      }
      // At this point, going further, we can assume that d.num_digits > 0.
      //
      // We want to guard against excessive decimal point values because
      // they can result in long running times. Indeed, we do
      // shifts by at most 60 bits. We have that log(10**400)/log(2**60) ~= 22
      // which is fine, but log(10**299995)/log(2**60) ~= 16609 which is not
      // fine (runs for a long time).
      //
      if (d.decimal_point < -324)
      {
        // We have something smaller than 1e-324 which is always zero
        // in binary64 and binary32.
        // It should be zero.
        answer.power2 = 0;
        answer.mantissa = 0;
        return answer;
      }
      else if (d.decimal_point >= 310)
      {
        // We have something at least as large as 0.1e310 which is
        // always infinite.
        answer.power2 = binaryFormat.infinite_power();
        answer.mantissa = 0;
        return answer;
      }
      const int max_shift = 60;
      const uint num_powers = 19;
      byte[] powers = {
                              0,  3,  6,  9,  13, 16, 19, 23, 26, 29, //
                              33, 36, 39, 43, 46, 49, 53, 56, 59,     //
                          };
      int exp2 = 0;
      while (d.decimal_point > 0)
      {
        uint n = (uint)(d.decimal_point);
        int shift = (n < num_powers) ? powers[n] : max_shift;
        d.decimal_right_shift(shift);
        if (d.decimal_point < -Constants.decimal_point_range)
        {
          // should be zero
          answer.power2 = 0;
          answer.mantissa = 0;
          return answer;
        }
        exp2 += (int)(shift);
      }
      // We shift left toward [1/2 ... 1].
      while (d.decimal_point <= 0)
      {
        int shift;
        if (d.decimal_point == 0)
        {
          if (d.digits[0] >= 5)
          {
            break;
          }
          if (d.digits[0] < 2)
          { shift = 2; }
          else { shift = 1; }
        }
        else
        {
          uint n = (uint)(-d.decimal_point);
          shift = (n < num_powers) ? powers[n] : max_shift;
        }
        d.decimal_left_shift(shift);
        if (d.decimal_point > Constants.decimal_point_range)
        {
          // we want to get infinity:
          answer.power2 = binaryFormat.infinite_power();
          answer.mantissa = 0;
          return answer;
        }
        exp2 -= (int)(shift);
      }
      // We are now in the range [1/2 ... 1] but the binary format uses [1 ... 2].
      exp2--;

      int min_exp = binaryFormat.minimum_exponent();

      while ((min_exp + 1) > exp2)
      {
        int n = (int)((min_exp + 1) - exp2);
        if (n > max_shift)
        {
          n = max_shift;
        }
        d.decimal_right_shift(n);
        exp2 += (int)(n);
      }
      if ((exp2 - min_exp) >= binaryFormat.infinite_power())
      {
        answer.power2 = binaryFormat.infinite_power();
        answer.mantissa = 0;
        return answer;
      }

      int mantissa_size_in_bits = binaryFormat.mantissa_explicit_bits() + 1;
      d.decimal_left_shift((int)mantissa_size_in_bits);

      ulong mantissa = d.round();
      // It is possible that we have an overflow, in which case we need
      // to shift back.
      if (mantissa >= ((ulong)(1) << mantissa_size_in_bits))
      {
        d.decimal_right_shift(1);
        exp2 += 1;
        mantissa = d.round();
        if ((exp2 - min_exp) >= binaryFormat.infinite_power())
        {
          answer.power2 = binaryFormat.infinite_power();
          answer.mantissa = 0;
          return answer;
        }
      }
      answer.power2 = exp2 - min_exp;
      if (mantissa < ((ulong)(1) << binaryFormat.mantissa_explicit_bits())) { answer.power2--; }
      answer.mantissa = mantissa & (((ulong)(1) << binaryFormat.mantissa_explicit_bits()) - 1);
      return answer;
    }

    unsafe static internal AdjustedMantissa ParseLongMantissa<T>(char* first, char* last, IBinaryFormat<T> binaryFormat)
    {
      DecimalInfo d = DecimalInfo.parse_decimal(first, last);
      return ComputeFloat(d, binaryFormat);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    unsafe static internal T HandleInvalidInput<T>(char* first, char* last, IBinaryFormat<T> binaryFormat)
    {
      if (last - first >= 3)
      {
        if (Utils.strncasecmp(first, "nan", 3))
        {
          return binaryFormat.NaN();
        }
        if (Utils.strncasecmp(first, "inf", 3))
        {
          if ((last - first >= 8) && Utils.strncasecmp(first, "infinity", 8))
            return binaryFormat.PositiveInfinity();
          return binaryFormat.PositiveInfinity();
        }
        if (last - first >= 4)
        {
          if (Utils.strncasecmp(first, "+nan", 4) || Utils.strncasecmp(first, "-nan", 4))
          {
            return binaryFormat.NaN();
          }
          if (Utils.strncasecmp(first, "+inf", 4) ||
              Utils.strncasecmp(first, "-inf", 4) ||
              ((last - first >= 8) && Utils.strncasecmp(first + 1, "infinity", 8)))
          {
            return (first[0] == '-') ? binaryFormat.NegativeInfinity() : binaryFormat.PositiveInfinity();
          }
        }
      }
      throw new ArgumentException();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    unsafe static internal ParsedNumberString ParseNumberString(char* p, char* pend, chars_format expectedFormat = chars_format.is_general)
    {
      ParsedNumberString answer = new ParsedNumberString();
      answer.valid = false;
      answer.too_many_digits = false;
      answer.negative = (*p == '-');
      if ((*p == '-') || (*p == '+'))
      {
        ++p;
        if (p == pend)
        {
          return answer;
        }
        if (!Utils.is_integer(*p) && ((*p != '.') || (*p != ','))) // culture info ?
        { // a  sign must be followed by an integer or the dot
          return answer;
        }
      }
      char* start_digits = p;

      ulong i = 0; // an unsigned int avoids signed overflows (which are bad)

      while ((p != pend) && Utils.is_integer(*p))
      {
        // a multiplication by 10 is cheaper than an arbitrary integer
        // multiplication
        i = 10 * i +
            (ulong)(*p - '0'); // might overflow, we will handle the overflow later
        ++p;
      }
      char* end_of_integer_part = p;
      long digit_count = (long)(end_of_integer_part - start_digits);
      long exponent = 0;
      if ((p != pend) && (*p == '.'))
      {
        ++p;
        //#if FASTFLOAT_IS_BIG_ENDIAN == 0
        // Fast approach only tested under little endian systems
        if ((p + 8 <= pend) && Utils.is_made_of_eight_digits_fast(p))
        {
          i = i * 100000000 + Utils.parse_eight_digits_unrolled(p); // in rare cases, this will overflow, but that's ok
          p += 8;
          if ((p + 8 <= pend) && Utils.is_made_of_eight_digits_fast(p))
          {
            i = i * 100000000 + Utils.parse_eight_digits_unrolled(p); // in rare cases, this will overflow, but that's ok
            p += 8;
          }
        }
        //#endif
        while ((p != pend) && Utils.is_integer(*p))
        {
          byte digit = (byte)(*p - '0');
          ++p;
          i = i * 10 + digit; // in rare cases, this will overflow, but that's ok
        }
        exponent = end_of_integer_part + 1 - p;
        digit_count -= exponent;
      }
      // we must have encountered at least one integer!
      if (digit_count == 0)
      {
        return answer;
      }
      long exp_number = 0;            // explicit exponential part
      if (expectedFormat.HasFlag(chars_format.is_scientific) && (p != pend) && (('e' == *p) || ('E' == *p)))
      {
        char* location_of_e = p;
        ++p;
        bool neg_exp = false;
        if ((p != pend) && ('-' == *p))
        {
          neg_exp = true;
          ++p;
        }
        else if ((p != pend) && ('+' == *p))
        {
          ++p;
        }
        if ((p == pend) || !Utils.is_integer(*p))
        {
          if (expectedFormat != chars_format.is_fixed)
          {
            // We are in error.
            return answer;
          }
          // Otherwise, we will be ignoring the 'e'.
          p = location_of_e;
        }
        else
        {
          while ((p != pend) && Utils.is_integer(*p))
          {
            byte digit = (byte)(*p - '0');
            if (exp_number < 0x10000)
            {
              exp_number = 10 * exp_number + digit;
            }
            ++p;
          }
          if (neg_exp) { exp_number = -exp_number; }
          exponent += exp_number;
        }
      }
      else
      {
        // If it scientific and not fixed, we have to bail out.
        if ((expectedFormat.HasFlag(chars_format.is_scientific)) && !(expectedFormat.HasFlag(chars_format.is_fixed))) { return answer; }
      }
      //answer.lastmatch = p;
      answer.valid = true;

      // If we frequently had to deal with long strings of digits,
      // we could extend our code by using a 128-bit integer instead
      // of a 64-bit integer. However, this is uncommon.
      //
      // We can deal with up to 19 digits.
      if (digit_count > 19)
      { // this is uncommon
        // It is possible that the integer had an overflow.
        // We have to handle the case where we have 0.0000somenumber.
        // We need to be mindful of the case where we only have zeroes...
        // E.g., 0.000000000...000.
        char* start = start_digits;
        while ((start != pend) && (*start == '0' || *start == '.'))
        {
          if (*start == '0') { digit_count--; }
          start++;
        }
        if (digit_count > 19)
        {
          answer.too_many_digits = true;
          // Let us start again, this time, avoiding overflows.
          i = 0;
          p = start_digits;
          const ulong minimal_nineteen_digit_integer = 1000000000000000000;
          while ((i < minimal_nineteen_digit_integer) && (p != pend) && Utils.is_integer(*p))
          {
            i = i * 10 + (ulong)(*p - '0');
            ++p;
          }
          if (i >= minimal_nineteen_digit_integer)
          { // We have a big integers
            exponent = end_of_integer_part - p + exp_number;
          }
          else
          { // We have a value with a fractional component.
            p++; // skip the '.'
            char* first_after_period = p;
            while ((i < minimal_nineteen_digit_integer) && (p != pend) && Utils.is_integer(*p))
            {
              i = i * 10 + (ulong)(*p - '0');
              ++p;
            }
            exponent = first_after_period - p + exp_number;
          }
          // We have now corrected both exponent and i, to a truncated value
        }
      }
      answer.exponent = exponent;
      answer.mantissa = i;
      return answer;
    }

    // This should always succeed since it follows a call to parse_number_string
    // This function could be optimized. In particular, we could stop after 19 digits
    // and try to bail out. Furthermore, we should be able to recover the computed
    // exponent from the pass in parse_number_string.
  }
}