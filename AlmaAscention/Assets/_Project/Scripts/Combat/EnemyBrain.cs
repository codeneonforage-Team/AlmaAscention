using System;
using System.Collections.Generic;
using CNF.Data;

namespace AlmaAscention.Combat
{
    // 몬스터 하나당 패턴 선택 로직만 담당하는 순수 C# 클래스임 (MonoBehaviour 아님).
    // MonsterDataSO(또는 보스 페이즈) 읽어서 다음 의도 정하는데, 시드 기반 Random을 받아서 쓰니까
    // 씬/에디터 없이도 테스트로 선택 결과 재현 가능함.
    //
    // 쿨다운/연속 사용/턴 주기/체력 비율/특수 우선순위 같은 가변 상태는 다 이 클래스 안에만 들고 있음.
    // MonsterDataSO 같은 공유 에셋에는 절대 안 씀.
    public sealed class EnemyBrain
    {
        private readonly MonsterDataSO _data;
        private readonly Random _random;
        private readonly IReadOnlyList<MonsterPhaseData> _phases;
        private readonly Dictionary<string, int> _cooldownRemaining = new(StringComparer.Ordinal);
        private readonly Dictionary<int, int> _sequentialCursorByPhase = new();

        private int _phaseIndex;
        private int _actionOpportunityNumber;
        private string _lastPatternID;
        private int _consecutiveUseCount;

        // 선택할 때 재사용하는 버퍼임 (GC 할당 줄이려고)
        private readonly List<MonsterSkillPatternData> _specialCandidates = new(4);
        private readonly List<MonsterSkillPatternData> _normalCandidates = new(8);

        public int PhaseIndex => _phaseIndex;
        public int ActionOpportunityNumber => _actionOpportunityNumber;
        public string LastPatternID => _lastPatternID;
        public int ConsecutiveUseCount => _consecutiveUseCount;
        public MonsterPhaseData CurrentPhase => _phases[_phaseIndex];

        public EnemyBrain(MonsterDataSO data, Random random)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            _data = data;
            _random = random ?? new Random();
            _phases = data.GetEffectivePhases();
            if (_phases.Count == 0)
                throw new InvalidOperationException($"MonsterDataSO '{data.MonsterID}'에 유효한 페이즈가 없습니다.");

            _phaseIndex = 0;
            _actionOpportunityNumber = 0;
        }

        public int GetCooldownRemaining(string patternId)
        {
            if (string.IsNullOrEmpty(patternId)) return 0;
            return _cooldownRemaining.TryGetValue(patternId, out int remaining) ? remaining : 0;
        }

        // 다음 행동 기회에 쓸 패턴 고르는 함수. 행동 기회 번호(1부터 시작) 1 증가시키고
        // 체력 비율로 페이즈 전진(역행 없음)시킨 다음, 특수 패턴부터 보고 없으면 일반 풀에서 고름.
        // 후보가 아예 없으면(그 페이즈에 일반 패턴 자체가 없으면) null 반환함.
        public MonsterSkillPatternData SelectNextPattern(float currentHPRatio)
        {
            _actionOpportunityNumber++;
            AdvancePhase(currentHPRatio);

            MonsterPhaseData phase = CurrentPhase;

            MonsterSkillPatternData selected = TrySelectSpecialCandidate(phase, currentHPRatio);
            if (selected == null)
                selected = SelectNormalCandidate(phase, currentHPRatio);

            if (selected == null)
            {
                // 후보 없음 상태. 쿨다운은 그래도 깎아줘서 다음 기회 준비함.
                TickCooldowns();
                _lastPatternID = null;
                _consecutiveUseCount = 0;
                return null;
            }

            if (string.Equals(selected.PatternID, _lastPatternID, StringComparison.Ordinal))
                _consecutiveUseCount++;
            else
                _consecutiveUseCount = 1;
            _lastPatternID = selected.PatternID;

            TickCooldowns();
            if (selected.CooldownTurns > 0 && !string.IsNullOrEmpty(selected.PatternID))
                _cooldownRemaining[selected.PatternID] = selected.CooldownTurns;

            return selected;
        }

        private void TickCooldowns()
        {
            if (_cooldownRemaining.Count == 0) return;

            List<string> keys = new List<string>(_cooldownRemaining.Keys);
            foreach (string key in keys)
            {
                int remaining = _cooldownRemaining[key] - 1;
                if (remaining <= 0)
                    _cooldownRemaining.Remove(key);
                else
                    _cooldownRemaining[key] = remaining;
            }
        }

        private void AdvancePhase(float currentHPRatio)
        {
            int furthestQualifying = _phaseIndex;
            for (int i = _phaseIndex + 1; i < _phases.Count; i++)
            {
                if (currentHPRatio <= _phases[i].HPRatioThreshold)
                    furthestQualifying = i;
            }

            if (furthestQualifying != _phaseIndex)
            {
                _phaseIndex = furthestQualifying;
                _sequentialCursorByPhase[_phaseIndex] = 0;
            }
        }

        private bool PassesConditions(MonsterSkillPatternData pattern, float currentHPRatio)
        {
            if (GetCooldownRemaining(pattern.PatternID) > 0) return false;
            if (pattern.MaxConsecutiveUses > 0 &&
                string.Equals(pattern.PatternID, _lastPatternID, StringComparison.Ordinal) &&
                _consecutiveUseCount >= pattern.MaxConsecutiveUses)
                return false;
            if (pattern.TurnModulo > 0 && _actionOpportunityNumber % pattern.TurnModulo != 0) return false;
            if (currentHPRatio > pattern.MaxHPRatioCondition) return false;
            return true;
        }

        private MonsterSkillPatternData TrySelectSpecialCandidate(MonsterPhaseData phase, float currentHPRatio)
        {
            _specialCandidates.Clear();
            int bestPriority = int.MinValue;

            for (int i = 0; i < phase.Patterns.Count; i++)
            {
                MonsterSkillPatternData pattern = phase.Patterns[i];
                if (pattern == null || !pattern.IsSpecialPattern) continue;
                if (!PassesConditions(pattern, currentHPRatio)) continue;

                if (pattern.SpecialPriority > bestPriority)
                {
                    bestPriority = pattern.SpecialPriority;
                    _specialCandidates.Clear();
                    _specialCandidates.Add(pattern);
                }
                else if (pattern.SpecialPriority == bestPriority)
                {
                    _specialCandidates.Add(pattern);
                }
            }

            if (_specialCandidates.Count == 0) return null;
            if (_specialCandidates.Count == 1) return _specialCandidates[0];

            // 특수 패턴끼리 동률이면: 양수 가중치 있으면 가중 랜덤 돌리고, 다 0 이하면 그냥 목록 순서상 첫 놈으로 정함.
            return PickWeighted(_specialCandidates) ?? _specialCandidates[0];
        }

        private MonsterSkillPatternData SelectNormalCandidate(MonsterPhaseData phase, float currentHPRatio)
        {
            // 1단계: 조건(쿨다운/연속 사용/주기/체력) 다 만족하는 일반 패턴만 봄.
            MonsterSkillPatternData result = CollectAndPick(phase, currentHPRatio, ignoreCooldown: false, ignoreAllConditions: false);
            if (result != null) return result;

            // 2단계(완화 1): 쿨다운만 빼고 다시 봄.
            result = CollectAndPick(phase, currentHPRatio, ignoreCooldown: true, ignoreAllConditions: false);
            if (result != null) return result;

            // 3단계(완화 2): 조건 다 무시하고 이 페이즈 일반 패턴 중에서 그냥 정함.
            result = CollectAndPick(phase, currentHPRatio, ignoreCooldown: true, ignoreAllConditions: true);
            if (result != null) return result;

            // 최종 안전망: 일반 패턴은 있는데(가중치 다 0 이하 등) 뽑기 자체가 실패한 경우,
            // 그냥 목록 첫 패턴으로 확정함. 일반 패턴이 아예 없으면 그때 null(후보 없음).
            for (int i = 0; i < phase.Patterns.Count; i++)
            {
                MonsterSkillPatternData pattern = phase.Patterns[i];
                if (pattern != null && !pattern.IsSpecialPattern)
                    return pattern;
            }
            return null;
        }

        private MonsterSkillPatternData CollectAndPick(MonsterPhaseData phase, float currentHPRatio, bool ignoreCooldown, bool ignoreAllConditions)
        {
            _normalCandidates.Clear();
            for (int i = 0; i < phase.Patterns.Count; i++)
            {
                MonsterSkillPatternData pattern = phase.Patterns[i];
                if (pattern == null || pattern.IsSpecialPattern) continue;

                if (!ignoreAllConditions)
                {
                    if (ignoreCooldown)
                    {
                        // 쿨다운만 빼고 나머진 그대로 검사.
                        bool passesConsecutive = pattern.MaxConsecutiveUses <= 0 ||
                            !string.Equals(pattern.PatternID, _lastPatternID, StringComparison.Ordinal) ||
                            _consecutiveUseCount < pattern.MaxConsecutiveUses;
                        bool passesTurnModulo = pattern.TurnModulo <= 0 || _actionOpportunityNumber % pattern.TurnModulo == 0;
                        bool passesHPRatio = currentHPRatio <= pattern.MaxHPRatioCondition;
                        if (!passesConsecutive || !passesTurnModulo || !passesHPRatio) continue;
                    }
                    else if (!PassesConditions(pattern, currentHPRatio))
                    {
                        continue;
                    }
                }

                _normalCandidates.Add(pattern);
            }

            if (_normalCandidates.Count == 0) return null;

            if (phase.SelectionMode == EPatternSelectionMode.Sequential)
                return PickSequential(phase, _normalCandidates);

            // 가중 랜덤 모드: 양수 가중치 후보가 하나도 없으면 null 반환해서 위에서 완화 단계로 넘어가게 함.
            return PickWeighted(_normalCandidates);
        }

        private MonsterSkillPatternData PickSequential(MonsterPhaseData phase, List<MonsterSkillPatternData> candidates)
        {
            // 순차 모드는 페이즈 전체 패턴 목록 기준 순서(A→B→C→A) 유지하는데,
            // 이번에 조건 통과한 candidates 안에서만 다음 커서 위치 찾음.
            if (!_sequentialCursorByPhase.TryGetValue(_phaseIndex, out int cursor))
                cursor = 0;

            List<MonsterSkillPatternData> fullList = phase.Patterns;
            int fullCount = fullList.Count;
            if (fullCount == 0) return candidates[0];

            for (int step = 0; step < fullCount; step++)
            {
                int idx = (cursor + step) % fullCount;
                MonsterSkillPatternData candidate = fullList[idx];
                if (candidate != null && !candidate.IsSpecialPattern && candidates.Contains(candidate))
                {
                    _sequentialCursorByPhase[_phaseIndex] = (idx + 1) % fullCount;
                    return candidate;
                }
            }

            // 커서로 못 찾으면(사실 여기 올 일 없음) 후보 목록 첫 패턴으로 대체함.
            return candidates[0];
        }

        private MonsterSkillPatternData PickWeighted(List<MonsterSkillPatternData> candidates)
        {
            float totalWeight = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].SelectionWeight > 0f)
                    totalWeight += candidates[i].SelectionWeight;
            }

            if (totalWeight <= 0f) return null; // 양수 가중치 후보 없음 -> 호출한 쪽에서 다른 전략 쓰게 함

            double roll = _random.NextDouble() * totalWeight;
            double cumulative = 0.0;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].SelectionWeight <= 0f) continue;
                cumulative += candidates[i].SelectionWeight;
                if (roll < cumulative)
                    return candidates[i];
            }

            // 부동소수 오차로 못 고른 경우 마지막 양수 가중치 후보 반환함.
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                if (candidates[i].SelectionWeight > 0f)
                    return candidates[i];
            }
            return null;
        }
    }
}
