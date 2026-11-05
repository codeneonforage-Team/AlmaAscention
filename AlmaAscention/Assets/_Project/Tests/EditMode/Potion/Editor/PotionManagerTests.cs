using System.Collections.Generic;
using CNF.Data;
using NUnit.Framework;
using UnityEngine;

namespace AlmaAscention.Potion.Tests
{
    public sealed class PotionManagerTests
    {
        private readonly List<PotionDataSO> _createdData = new List<PotionDataSO>();

        private PotionDataSO CreatePotion()
        {
            PotionDataSO data = ScriptableObject.CreateInstance<PotionDataSO>();
            data.PotionID = "test_potion_" + _createdData.Count;
            _createdData.Add(data);
            return data;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (PotionDataSO data in _createdData)
            {
                Object.DestroyImmediate(data);
            }

            _createdData.Clear();
        }

        [TestCase(TestName = "초기 물약 슬롯은 세 칸이며 모두 비어 있다 | Constructor_CreatesEmptySlots")]
        public void Constructor_CreatesEmptySlots()
        {
            PotionManager manager = new PotionManager();

            Assert.That(manager.SlotCount, Is.EqualTo(3));
            Assert.That(manager.Slots.Count, Is.EqualTo(manager.SlotCount));
            Assert.That(manager.Slots, Is.All.Null);
        }

        [TestCase(TestName = "물약 획득은 원본 SO를 첫 빈 슬롯에 보관하고 반영 후 한 번 알린다 | Acquire_StoresSourceBeforeNotification")]
        public void Acquire_StoresSourceBeforeNotification()
        {
            PotionManager manager = new PotionManager();
            IReadOnlyList<PotionDataSO> view = manager.Slots;
            PotionDataSO data = CreatePotion();
            data.EffectValue = 42;
            int notifications = 0;
            manager.Changed += () =>
            {
                notifications++;
                Assert.That(view[0], Is.SameAs(data));
            };

            Assert.That(manager.TryAcquirePotion(data), Is.True);
            Assert.That(manager.GetPotionOrNull(0), Is.SameAs(data));
            Assert.That(data.EffectValue, Is.EqualTo(42));
            Assert.That(view[1], Is.Null);
            Assert.That(notifications, Is.EqualTo(1));
        }

        [TestCase(TestName = "같은 종류의 물약도 서로 다른 슬롯에 중복 획득할 수 있다 | Acquire_SamePotionUsesSeparateSlots")]
        public void Acquire_SamePotionUsesSeparateSlots()
        {
            PotionManager manager = new PotionManager();
            PotionDataSO data = CreatePotion();

            Assert.That(manager.TryAcquirePotion(data), Is.True);
            Assert.That(manager.TryAcquirePotion(data), Is.True);
            Assert.That(manager.Slots[0], Is.SameAs(data));
            Assert.That(manager.Slots[1], Is.SameAs(data));
        }

        [TestCase(TestName = "빈 슬롯이 없으면 물약 획득을 거부하고 기존 물약과 알림을 유지한다 | Acquire_FullSlotsDoesNotChangeState")]
        public void Acquire_FullSlotsDoesNotChangeState()
        {
            PotionManager manager = new PotionManager();
            for (int index = 0; index < manager.SlotCount; index++)
            {
                manager.TryAcquirePotion(CreatePotion());
            }

            PotionDataSO[] before = new List<PotionDataSO>(manager.Slots).ToArray();
            int notifications = 0;
            manager.Changed += () => notifications++;

            Assert.That(manager.TryAcquirePotion(CreatePotion()), Is.False);
            Assert.That(manager.Slots, Is.EqualTo(before));
            Assert.That(notifications, Is.Zero);
        }

        [TestCase(TestName = "null 물약 획득은 슬롯 변경이나 알림 없이 실패한다 | Acquire_NullDoesNotNotify")]
        public void Acquire_NullDoesNotNotify()
        {
            PotionManager manager = new PotionManager();
            int notifications = 0;
            manager.Changed += () => notifications++;

            Assert.That(manager.TryAcquirePotion(null), Is.False);
            Assert.That(manager.Slots, Is.All.Null);
            Assert.That(notifications, Is.Zero);
        }

        [TestCase(-1, TestName = "음수 슬롯 조회는 null을 반환한다 | GetPotion_InvalidSlot [-1]")]
        [TestCase(3, TestName = "슬롯 수와 같은 인덱스 조회는 null을 반환한다 | GetPotion_InvalidSlot [3]")]
        public void GetPotion_InvalidSlot(int slotIndex)
        {
            PotionManager manager = new PotionManager();

            Assert.That(manager.GetPotionOrNull(slotIndex), Is.Null);
        }

        [TestCase(TestName = "물약 폐기는 선택한 슬롯만 비우고 다음 획득 시 그 빈칸을 재사용한다 | Discard_LeavesOtherSlotsAndReusesGap")]
        public void Discard_LeavesOtherSlotsAndReusesGap()
        {
            PotionManager manager = new PotionManager();
            PotionDataSO first = CreatePotion();
            PotionDataSO second = CreatePotion();
            manager.TryAcquirePotion(first);
            manager.TryAcquirePotion(second);
            IReadOnlyList<PotionDataSO> view = manager.Slots;
            int notifications = 0;
            manager.Changed += () => notifications++;

            Assert.That(manager.TryDiscardPotion(0), Is.True);
            Assert.That(view[0], Is.Null);
            Assert.That(view[1], Is.SameAs(second));
            Assert.That(manager.SlotCount, Is.EqualTo(3));
            Assert.That(manager.TryDiscardPotion(0), Is.False);
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(manager.TryAcquirePotion(first), Is.True);
            Assert.That(view[0], Is.SameAs(first));
            Assert.That(view[1], Is.SameAs(second));
            Assert.That(notifications, Is.EqualTo(2));
        }

        [TestCase(-1, TestName = "음수 슬롯의 폐기는 상태 변경이나 알림 없이 실패한다 | Discard_InvalidSlot [-1]")]
        [TestCase(3, TestName = "범위 밖 슬롯의 폐기는 상태 변경이나 알림 없이 실패한다 | Discard_InvalidSlot [3]")]
        [TestCase(1, TestName = "빈 슬롯의 폐기는 상태 변경이나 알림 없이 실패한다 | Discard_InvalidSlot [빈칸]")]
        public void Discard_InvalidSlot(int slotIndex)
        {
            PotionManager manager = new PotionManager();
            PotionDataSO data = CreatePotion();
            manager.TryAcquirePotion(data);
            int notifications = 0;
            manager.Changed += () => notifications++;

            Assert.That(manager.TryDiscardPotion(slotIndex), Is.False);
            Assert.That(manager.Slots[0], Is.SameAs(data));
            Assert.That(manager.SlotCount, Is.EqualTo(3));
            Assert.That(notifications, Is.Zero);
        }

        [TestCase(TestName = "슬롯 확장은 기존 물약을 유지하고 뒤에 빈칸을 추가한 후 한 번 알린다 | Expand_PreservesPotionsAndAddsUsableSlots")]
        public void Expand_PreservesPotionsAndAddsUsableSlots()
        {
            PotionManager manager = new PotionManager();
            PotionDataSO data = CreatePotion();
            for (int index = 0; index < manager.SlotCount; index++)
            {
                manager.TryAcquirePotion(data);
            }

            IReadOnlyList<PotionDataSO> view = manager.Slots;
            int notifications = 0;
            manager.Changed += () => notifications++;

            Assert.That(manager.TryExpandSlots(2), Is.True);
            Assert.That(manager.SlotCount, Is.EqualTo(5));
            Assert.That(view.Count, Is.EqualTo(5));
            Assert.That(view[0], Is.SameAs(data));
            Assert.That(view[1], Is.SameAs(data));
            Assert.That(view[2], Is.SameAs(data));
            Assert.That(view[3], Is.Null);
            Assert.That(view[4], Is.Null);
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(manager.TryAcquirePotion(data), Is.True);
            Assert.That(manager.GetPotionOrNull(3), Is.SameAs(data));
        }

        [TestCase(0, TestName = "0칸 슬롯 확장은 상태 변경이나 알림 없이 실패한다 | Expand_NonPositive [0]")]
        [TestCase(-1, TestName = "음수 슬롯 확장은 상태 변경이나 알림 없이 실패한다 | Expand_NonPositive [-1]")]
        public void Expand_NonPositive(int count)
        {
            PotionManager manager = new PotionManager();
            int notifications = 0;
            manager.Changed += () => notifications++;

            Assert.That(manager.TryExpandSlots(count), Is.False);
            Assert.That(manager.SlotCount, Is.EqualTo(3));
            Assert.That(manager.Slots, Is.All.Null);
            Assert.That(notifications, Is.Zero);
        }
    }
}
