using System;
using System.Collections.Generic;
using UnityEngine;
using CNF.Data;

namespace CNF.AbilitySystem
{
    /// <summary>
    /// 언리얼의 UCNF_DamageExecution 및 CalculatePreviewDamage를 하나로 통합한 대미지 계산기.
    /// 카드 설명 프리뷰, 적 공격 의도(Intent) 프리뷰, 실제 대미지 타격이 100% 동일한 이 함수를 호출합니다.
    /// </summary>
    public static class DamageCalculator
    {
        public const float WeakDamageMultiplier = 0.75f;       // 약화: 주는 피해 25% 감소
        public const float VulnerableDamageMultiplier = 1.50f; // 취약: 받는 피해 50% 증가
        public const float FrailBlockMultiplier = 0.75f;       // 손상: 카드로 얻는 방어도 25% 감소

        public const string BuffedValueColorHex = "#44FF66";   // 기본 수치보다 높아졌을 때 (초록색)
        public const string DebuffedValueColorHex = "#FF5555"; // 기본 수치보다 낮아졌을 때 (빨간색)

        private struct PrioritizedStatus
        {
            public string Key;
            public int Stacks;
            public StatusBehaviorSO Behavior;
        }

        // 매 프레임 UI 프리뷰 갱신 시 가비지(GC) 할당 0 Byte를 보장하는 정적 재사용 버퍼
        private static readonly HashSet<string> _handledStatuses = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<PrioritizedStatus> _sortedBehaviors = new(8);
        private static readonly Comparison<PrioritizedStatus> _priorityComparer =
            (a, b) => a.Behavior.CalculationPriority.CompareTo(b.Behavior.CalculationPriority);

        /// <summary>
        /// 공격자의 버프/디버프(힘[합연산] -> 커스텀 StatusBehaviorSO -> 약화[곱연산])와
        /// 방어자의 디버프(커스텀 StatusBehaviorSO -> 취약[곱연산])를 순서대로 반영한 최종 대미지를 계산합니다.
        /// </summary>
        public static int CalculateDamage(
            int baseDamage,
            StatusController attacker,
            StatusController target = null,
            bool ignoreStunCheck = false)
        {
            if (!ignoreStunCheck && attacker != null && attacker.GetStatus(StatusKeys.Stunned) > 0)
                return 0;

            float damage = baseDamage;

            if (attacker != null)
            {
                _handledStatuses.Clear();
                CollectSortedBehaviors(attacker);

                // 1. 기본 힘(Strength) 합연산을 먼저 적용 (커스텀 SO로 오버라이드되지 않은 경우)
                if (!_handledStatuses.Contains(StatusKeys.Strength))
                    damage += attacker.GetStatus(StatusKeys.Strength);

                // 2. 커스텀 StatusBehaviorSO를 CalculationPriority 오름차순으로 적용
                for (int i = 0; i < _sortedBehaviors.Count; i++)
                {
                    var item = _sortedBehaviors[i];
                    item.Behavior.ModifyOutgoingDamage(attacker, target, ref damage, item.Stacks);
                }

                // 3. 기본 약화(Weak x0.75) 곱연산 적용
                if (!_handledStatuses.Contains(StatusKeys.Weak) && attacker.GetStatus(StatusKeys.Weak) > 0)
                    damage *= WeakDamageMultiplier;
            }

            if (target != null)
            {
                _handledStatuses.Clear();
                CollectSortedBehaviors(target);

                for (int i = 0; i < _sortedBehaviors.Count; i++)
                {
                    var item = _sortedBehaviors[i];
                    item.Behavior.ModifyIncomingDamage(target, attacker, ref damage, item.Stacks);
                }

                if (!_handledStatuses.Contains(StatusKeys.Vulnerable) && target.GetStatus(StatusKeys.Vulnerable) > 0)
                    damage *= VulnerableDamageMultiplier;
            }

            return Mathf.Max(0, Mathf.FloorToInt(damage));
        }

        /// <summary>
        /// 카드의 방어도(Block) 프리뷰 수치를 계산합니다 (민첩[합연산] -> 커스텀 StatusBehaviorSO -> 손상[Frail x0.75]).
        /// </summary>
        public static int CalculatePreviewBlock(int baseBlock, StatusController owner)
        {
            float block = baseBlock;

            if (owner != null)
            {
                _handledStatuses.Clear();
                CollectSortedBehaviors(owner);

                if (!_handledStatuses.Contains(StatusKeys.Dexterity))
                    block += owner.GetStatus(StatusKeys.Dexterity);

                for (int i = 0; i < _sortedBehaviors.Count; i++)
                {
                    var item = _sortedBehaviors[i];
                    item.Behavior.ModifyBlockGain(owner, ref block, item.Stacks);
                }

                if (!_handledStatuses.Contains(StatusKeys.Frail) && owner.GetStatus(StatusKeys.Frail) > 0)
                    block *= FrailBlockMultiplier;
            }
            return Mathf.Max(0, Mathf.FloorToInt(block));
        }

        private static void CollectSortedBehaviors(StatusController controller)
        {
            _sortedBehaviors.Clear();
            if (DataManager.Instance == null) return;

            foreach (var kvp in controller.GetAllStatuses())
            {
                if (kvp.Value == 0) continue;
                var behavior = DataManager.Instance.GetStatusBehavior(kvp.Key);
                if (behavior != null)
                {
                    _handledStatuses.Add(kvp.Key);
                    _sortedBehaviors.Add(new PrioritizedStatus
                    {
                        Key = kvp.Key,
                        Stacks = kvp.Value,
                        Behavior = behavior
                    });
                }
            }

            if (_sortedBehaviors.Count > 1)
                _sortedBehaviors.Sort(_priorityComparer);
        }

        /// <summary>
        /// 기본 수치와 실시간 프리뷰 수치를 비교해 TextMeshPro 리치 텍스트 색상 태그(초록/빨강)를 자동으로 입혀 반환합니다.
        /// </summary>
        public static string FormatColoredValue(int previewValue, int baseValue)
        {
            if (previewValue > baseValue)
                return $"<color={BuffedValueColorHex}>{previewValue}</color>";
            if (previewValue < baseValue)
                return $"<color={DebuffedValueColorHex}>{previewValue}</color>";
            return previewValue.ToString();
        }
    }
}
