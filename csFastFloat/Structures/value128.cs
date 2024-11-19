﻿using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TestcsFastFloat, PublicKey=0024000004800000940000000602000000240000525341310004000001000100bba0148c4f19a2ccd5407f9ca02253083d25bc399d9391be690cb6c2125563414afebc227082d74392cc916ce78731c50d58fba5cc51176a90946ea3a606c6b106322235859516ef661f32f74eab006ff4d754c0a9ffc9819e599660b7be94d3e146a09917f88897d533d333a2bd42c2a2b3a51cd8cbe88f5a42c5266b1a35ac")]

namespace csFastFloat.Structures
{
  internal struct value128
  {
    public ulong low;
    public ulong high;

    public value128(ulong h, ulong l) : this()
    {
      high = h;
      low = l;
    }
  }
}
