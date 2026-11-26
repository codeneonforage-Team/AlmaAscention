using System;

namespace AlmaAscention.Combat
{
    // 전투 스냅샷과 함께 난수 진행 위치를 저장할 수 있는 PRNG.
    public sealed class CombatRandom : Random
    {
        private ulong _state;
        public ulong State
        {
            get => _state;
            set => _state = value == 0 ? 1UL : value;
        }

        public CombatRandom(int seed) => State = (ulong)(uint)seed + 1UL;

        private ulong NextBits()
        {
            ulong value = _state;
            value ^= value << 13;
            value ^= value >> 7;
            value ^= value << 17;
            _state = value;
            return value;
        }

        public override int Next() => (int)(NextBits() % int.MaxValue);
        public override int Next(int maxValue)
        {
            if (maxValue < 0) throw new ArgumentOutOfRangeException(nameof(maxValue));
            return maxValue == 0 ? 0 : (int)(NextBits() % (uint)maxValue);
        }
        public override int Next(int minValue, int maxValue)
        {
            if (minValue > maxValue) throw new ArgumentOutOfRangeException(nameof(minValue));
            return minValue == maxValue ? minValue : minValue + (int)(NextDouble() * ((long)maxValue - minValue));
        }
        public override double NextDouble() => (NextBits() >> 11) * (1.0 / (1UL << 53));
        protected override double Sample() => NextDouble();
    }
}
