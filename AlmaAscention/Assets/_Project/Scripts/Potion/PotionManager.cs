using System;
using System.Collections.Generic;
using CNF.AbilitySystem;
using CNF.Data;

namespace AlmaAscention.Potion
{
    public sealed class PotionManager
    {
        public const int DefaultSlotCount = 3;

        // null은 빈 슬롯을 뜻한다.
        private readonly List<PotionDataSO> _slots = new List<PotionDataSO>();

        private readonly IReadOnlyList<PotionDataSO> _slotsView;

        public IReadOnlyList<PotionDataSO> Slots
        {
            get
            {
                return _slotsView;
            }
        }

        // 빈 슬롯을 포함한 전체 슬롯 수
        public int SlotCount
        {
            get
            {
                return _slots.Count;
            }
        }

        public event Action Changed;

        public PotionManager()
        {
            _slotsView = _slots.AsReadOnly();

            for (int index = 0; index < DefaultSlotCount; index++)
            {
                _slots.Add(null);
            }
        }

        // 앞에서부터 첫 빈 슬롯에 보관한다.
        // 잘못된 데이터이거나 빈 슬롯이 없으면 변경 없이 실패한다.
        public bool TryAcquirePotion(PotionDataSO potionData)
        {
            if (potionData == null)
            {
                return false;
            }

            for (int index = 0; index < _slots.Count; index++)
            {
                if (_slots[index] == null)
                {
                    _slots[index] = potionData;
                    Changed?.Invoke();

                    return true;
                }
            }

            return false;
        }

        // 해당 슬롯의 물약을 조회한다.
        // 범위를 벗어나거나 빈 슬롯이면 null을 반환한다.
        public PotionDataSO GetPotionOrNull(int slotIndex)
        {
            if (!IsValidSlotIndex(slotIndex))
            {
                return null;
            }

            return _slots[slotIndex];
        }

        // 뒤의 물약을 당기지 않고 해당 슬롯만 비운다.
        public bool TryDiscardPotion(int slotIndex)
        {
            if (!IsValidSlotIndex(slotIndex))
            {
                return false;
            }

            if (_slots[slotIndex] == null)
            {
                return false;
            }

            // 효과 실행 중인 물약은 버릴 수 없다.
            if (slotIndex == _usingSlotIndex)
            {
                return false;
            }

            _slots[slotIndex] = null;
            Changed?.Invoke();

            return true;
        }

        #region 물약 사용

        // 효과 실행 중인 슬롯. 없으면 -1.
        // 효과 도중 연쇄 반응(드로우 → 패시브 등)으로 다시 사용·폐기 요청이 들어와도 막기 위함.
        private int _usingSlotIndex = -1;

        public bool IsUsingPotion
        {
            get
            {
                return _usingSlotIndex >= 0;
            }
        }

        // 해당 슬롯의 물약을 지금 전투 상황에서 수동 사용할 수 있는지 검사한다. (UI 버튼 활성화용)
        // 실제 효과 조건 검사는 CardExecutor.CanUsePotion에 맡긴다.
        public bool CanUsePotion(int slotIndex, CombatContext context, out string failReason)
        {
            if (IsUsingPotion)
            {
                failReason = "다른 물약을 사용하는 중입니다.";
                return false;
            }

            PotionDataSO potion = GetPotionOrNull(slotIndex);

            if (potion == null)
            {
                failReason = "빈 슬롯입니다.";
                return false;
            }

            return CardExecutor.CanUsePotion(potion, context, out failReason);
        }

        // 물약 효과를 실행하고, 성공했을 때만 해당 슬롯을 비운다.
        // 사용할 수 없거나 효과 실행에 실패하면 물약을 소모하지 않는다.
        // TODO:: 카드 실행 중(CardExecutor.IsBusy) 사용 제한은 호출하는 전투 쪽에서 판단한다.
        public bool TryUsePotion(int slotIndex, CombatContext context)
        {
            if (!CanUsePotion(slotIndex, context, out _))
            {
                return false;
            }

            PotionDataSO potion = _slots[slotIndex];
            bool executed;

            _usingSlotIndex = slotIndex;

            try
            {
                executed = CardExecutor.TryExecutePotion(potion, context);
            }
            finally
            {
                _usingSlotIndex = -1;
            }

            if (!executed)
            {
                return false;
            }

            _slots[slotIndex] = null;
            Changed?.Invoke();

            return true;
        }

        // 치명상 시 앞 슬롯부터 첫 부활 물약을 사용해 사망을 취소한다.
        // StatusController.OnTryInterceptFatalDamage에 그대로 연결할 수 있다.
        // 예: player.OnTryInterceptFatalDamage = potionManager.TryConsumeResurrectPotion;
        // 주의: OnTryInterceptFatalDamage는 대입식 필드이므로 부활 유물 등과 함께 쓰려면 호출 쪽에서 묶어야 한다.
        public bool TryConsumeResurrectPotion(StatusController dyingTarget)
        {
            if (dyingTarget == null)
            {
                return false;
            }

            for (int index = 0; index < _slots.Count; index++)
            {
                PotionDataSO potion = _slots[index];

                if (potion == null || potion.EffectType != EPotionEffectType.Resurrect || index == _usingSlotIndex)
                {
                    continue;
                }

                if (!CardExecutor.TryExecuteResurrectPotion(potion, dyingTarget))
                {
                    continue;
                }

                _slots[index] = null;
                Changed?.Invoke();

                return true;
            }

            return false;
        }

        #endregion

        // 끝에 빈 슬롯을 추가한다.
        // 0 이하의 확장 요청은 변경 없이 실패한다.
        public bool TryExpandSlots(int additionalSlotCount)
        {
            if (additionalSlotCount <= 0)
            {
                return false;
            }

            for (int index = 0; index < additionalSlotCount; index++)
            {
                _slots.Add(null);
            }

            Changed?.Invoke();

            return true;
        }

        private bool IsValidSlotIndex(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < _slots.Count;
        }

        // TODO: 대상 선택 UI 연결 (SingleEnemy 물약은 호출 쪽이 context.PrimaryTarget을 채워야 한다)
        // TODO: 슬롯 축소가 기획되면 초과 물약 처리 규칙 추가
    }
}
