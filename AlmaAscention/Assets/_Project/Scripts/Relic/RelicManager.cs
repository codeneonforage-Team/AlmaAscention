using System;
using System.Collections.Generic;
using CNF.AbilitySystem;
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

        // 잘못된 데이터(null 또는 빈 ID)이면 변경 없이 실패한다.
        // 만료된 성유물도 보유 중이므로 중복 획득할 수 없다.
        public bool TryAcquireRelic(RelicDataSO relicData)
        {
            if (relicData == null || string.IsNullOrEmpty(relicData.RelicID))
            {
                return false;
            }

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

        #region 유물 제거

        // 보유 목록에서 유물을 뺀다. (합성 재료 소모, 교환 이벤트 등)
        // 만료와 달리 목록에서 사라지므로 이후 다시 획득할 수 있다.
        // 이미 적용한 영구 효과(최대 체력 등)는 되돌리지 않는다.
        public bool TryRemoveRelic(string relicId)
        {
            Relic relic = FindRelicOrNull(relicId);

            if (relic == null)
            {
                return false;
            }

            _relics.Remove(relic);
            Changed?.Invoke();

            return true;
        }

        // 여러 유물을 한 번에 빼고 실제 제거한 수를 반환한다. 없는 ID와 중복 ID는 건너뛴다.
        // 실제 제거가 있을 때만 모두 반영한 뒤 한 번 알린다.
        public int RemoveRelics(IEnumerable<string> relicIds)
        {
            if (relicIds == null)
            {
                return 0;
            }

            // 입력이 보유 목록에서 파생된 열거형이어도 안전하게 처리한다.
            List<string> requestedIds = new List<string>(relicIds);
            int removedCount = 0;

            foreach (string relicId in requestedIds)
            {
                Relic relic = FindRelicOrNull(relicId);

                if (relic == null)
                {
                    continue;
                }

                _relics.Remove(relic);
                removedCount++;
            }

            if (removedCount > 0)
            {
                Changed?.Invoke();
            }

            return removedCount;
        }

        #endregion

        #region 유물 발동

        // 효과 실행 중인 발동 시점.
        // 유물 효과가 같은 시점을 다시 일으키는 경우(예: 피격 시 자해) 무한 연쇄를 막기 위함.
        private readonly HashSet<ERelicTriggerType> _triggeringTimings = new HashSet<ERelicTriggerType>();

        // 유물 효과가 실제로 실행될 때마다 알린다. (UI 아이콘 반짝임, 로그 등)
        // 보유 목록 변경 알림인 Changed와는 별개다.
        public event Action<Relic> Triggered;

        // 전투 쪽 발동 시점 훅에서 호출한다.
        // 해당 시점의 유물을 획득 순서대로 실행하고, 실제로 효과가 실행된 유물 수를 반환한다.
        // 호출 쪽 책임: 플레이어 시점인지 선별, 해당 시점의 CombatContext 구성(DeckOps, AllEnemies 포함).
        // Passive·OnAcquire는 발동형이 아니므로 실행하지 않는다.
        // 같은 시점의 효과 실행 도중 같은 시점이 다시 요청되면 실행하지 않는다.
        public int TriggerRelics(ERelicTriggerType timing, CombatContext context)
        {
            if (timing == ERelicTriggerType.Passive || timing == ERelicTriggerType.OnAcquire)
            {
                return 0;
            }

            if (!_triggeringTimings.Add(timing))
            {
                return 0;
            }

            int executedCount = 0;

            try
            {
                // 효과 도중 획득·만료가 일어나도 이번 발동 대상은 호출 시점 기준으로 고정한다.
                List<Relic> snapshot = new List<Relic>(_relics);

                foreach (Relic relic in snapshot)
                {
                    // 앞선 효과로 만료되거나 제거된 유물은 건너뛴다.
                    if (relic.IsExpired || !_relics.Contains(relic))
                    {
                        continue;
                    }

                    if (!CardExecutor.TryTriggerRelicEffect(relic.OriginalRelicData, timing, context))
                    {
                        continue;
                    }

                    executedCount++;
                    Triggered?.Invoke(relic);
                }
            }
            finally
            {
                _triggeringTimings.Remove(timing);
            }

            return executedCount;
        }

        #endregion

        // TODO: 수명 규칙 확정 후 카운트·만료 처리 연결
    }
}