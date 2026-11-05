using System;
using System.Collections.Generic;
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

            _slots[slotIndex] = null;
            Changed?.Invoke();

            return true;
        }

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

        // TODO: 물약 사용 조건·대상 선택·효과 실행 연결
        // TODO: 사용 중인 슬롯의 중복 사용·폐기 방지
        // TODO: 효과 성공 시 사용한 물약 소모
        // TODO: 슬롯 축소가 기획되면 초과 물약 처리 규칙 추가
    }
}
