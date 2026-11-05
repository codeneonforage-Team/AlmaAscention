using System;
using CNF.Data;

namespace AlmaAscention.Relic
{
    // Run에 필요한 부분만 담당하는 Relic
    // ex) 전투 N회 후 사용을 멈추는 Relic 혹은 1회성 발동 Relic 등
    public sealed class Relic
    {
        public readonly RelicDataSO OriginalRelicData;

        public bool IsExpired { get; private set; }

        // 선조건: relicData는 유효하다.
        public Relic(RelicDataSO relicData)
        {
            OriginalRelicData = relicData;
        }

        // 같은 어셈블리 내부에서만 상태 변경을 허용한다.
        internal void Expire()
        {
            IsExpired = true;
        }
    }

}