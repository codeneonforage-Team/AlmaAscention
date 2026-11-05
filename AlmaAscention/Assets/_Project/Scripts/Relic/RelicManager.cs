using System;
using System.Collections.Generic;
using CNF.Data;

namespace AlmaAscention.Relic
{
    public sealed class RelicManager
    {
        private readonly List<Relic> _relics = new List<Relic>();
        private readonly IReadOnlyList<Relic> _relicsView;

        public IReadOnlyList<Relic> Relics
        {
            get
            {
                return _relicsView;
            }
        }

        public int Count
        {
            get
            {
                return _relics.Count;
            }
        }

        public event Action Changed;

        public RelicManager()
        {
            _relicsView = _relics.AsReadOnly();
        }

        // DataManager와 같이 ID의 대소문자를 구분하지 않는다.
        public Relic FindRelicOrNull(string relicId)
        {
            foreach (Relic relic in _relics)
            {
                if (string.Equals(relic.OriginalRelicData.RelicID, relicId, StringComparison.OrdinalIgnoreCase))
                {
                    return relic;
                }
            }

            return null;
        }

        // 선조건: relicData Id가 유효한 경우。
        // 만료된 성유물도 보유 중이므로 중복 획득할 수 없다.
        public bool TryAcquireRelic(RelicDataSO relicData)
        {
            if (FindRelicOrNull(relicData.RelicID) != null)
            {
                return false;
            }

            Relic relic = new Relic(relicData);
            _relics.Add(relic);

            // TODO: 획득 시 효과 실행 연결

            Changed?.Invoke();

            return true;
        }

        // 만료해도 보유 목록에서 제거하지 않는다.
        public bool TryExpireRelic(string relicId)
        {
            Relic relic = FindRelicOrNull(relicId);

            if (relic == null || relic.IsExpired)
            {
                return false;
            }

            relic.Expire();

            // TODO: 필요한 지속 효과 해제 연결
            // 이미 적용한 영구 효과는 만료만으로 되돌리지 않는다.

            Changed?.Invoke();

            return true;
        }

        // TODO: 발동 시점별 대상 선별
        // TODO: 효과 실행 요청 및 완료 결과 연결
        // TODO: 수명 규칙 확정 후 카운트·만료 처리 연결
    }
}