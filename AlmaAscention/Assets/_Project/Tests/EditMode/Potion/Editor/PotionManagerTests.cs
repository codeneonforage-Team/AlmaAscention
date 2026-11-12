using System.Collections.Generic;
using CNF.AbilitySystem;
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

        private readonly List<GameObject> _createdObjects = new List<GameObject>();

        private PotionDataSO CreatePotion(EPotionEffectType effectType, int effectValue, ECardTarget targetType = ECardTarget.Self)
        {
            PotionDataSO data = CreatePotion();
            data.EffectType = effectType;
            data.EffectValue = effectValue;
            data.TargetType = targetType;
            return data;
        }

        private StatusController CreateStatus(int teamId, int currentHP, int maxHP)
        {
            GameObject owner = new GameObject("Potion test status");
            _createdObjects.Add(owner);
            StatusController status = owner.AddComponent<StatusController>();
            status.InitializeStats(currentHP, maxHP);
            status.TeamID = teamId;
            return status;
        }

        private CombatContext CreateContext(StatusController player, params StatusController[] enemies)
        {
            return new CombatContext
            {
                Caster = player,
                PrimaryTarget = enemies.Length > 0 ? enemies[0] : null,
                AllEnemies = enemies
            };
        }

        // 물약 효과 도중 다시 물약을 사용·폐기하려는 연쇄 반응을 흉내 낸다.
        private sealed class ReentrantDeckOperations : IDeckOperations
        {
            public System.Action OnDraw;
            public int DrawnCount;

            public void DrawCards(int count)
            {
                DrawnCount += count;
                OnDraw?.Invoke();
            }

            public int ExhaustEntireHand()
            {
                return 0;
            }

            public void UpgradeCardsInHand(bool upgradeAll)
            {
            }

            public void AddCardToHand(string cardId, bool isUpgraded = false)
            {
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (PotionDataSO data in _createdData)
            {
                Object.DestroyImmediate(data);
            }

            _createdData.Clear();

            foreach (GameObject owner in _createdObjects)
            {
                Object.DestroyImmediate(owner);
            }

            _createdObjects.Clear();
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

        #region 물약 사용

        [TestCase(TestName = "물약 사용은 효과를 실행한 뒤 해당 슬롯만 비우고 한 번 알린다 | Use_ExecutesEffectThenConsumesOnlyThatSlot")]
        public void Use_ExecutesEffectThenConsumesOnlyThatSlot()
        {
            PotionManager manager = new PotionManager();
            PotionDataSO other = CreatePotion(EPotionEffectType.Block, 5);
            PotionDataSO heal = CreatePotion(EPotionEffectType.Heal, 10);
            manager.TryAcquirePotion(other);
            manager.TryAcquirePotion(heal);
            StatusController player = CreateStatus(0, 20, 50);
            int notifications = 0;
            manager.Changed += () =>
            {
                notifications++;
                Assert.That(player.CurrentHP, Is.EqualTo(30));
            };

            Assert.That(manager.TryUsePotion(1, CreateContext(player)), Is.True);

            Assert.That(player.CurrentHP, Is.EqualTo(30));
            Assert.That(manager.GetPotionOrNull(0), Is.SameAs(other));
            Assert.That(manager.GetPotionOrNull(1), Is.Null);
            Assert.That(notifications, Is.EqualTo(1));
        }

        [TestCase(-1, TestName = "범위 밖 슬롯 사용은 상태 변경 없이 실패한다 [-1] | Use_InvalidOrEmptySlot_Fails")]
        [TestCase(1, TestName = "빈 슬롯 사용은 상태 변경 없이 실패한다 [1] | Use_InvalidOrEmptySlot_Fails")]
        [TestCase(3, TestName = "범위 밖 슬롯 사용은 상태 변경 없이 실패한다 [3] | Use_InvalidOrEmptySlot_Fails")]
        public void Use_InvalidOrEmptySlot_Fails(int slotIndex)
        {
            PotionManager manager = new PotionManager();
            PotionDataSO heal = CreatePotion(EPotionEffectType.Heal, 10);
            manager.TryAcquirePotion(heal);
            StatusController player = CreateStatus(0, 20, 50);
            int notifications = 0;
            manager.Changed += () =>
            {
                notifications++;
            };

            Assert.That(manager.CanUsePotion(slotIndex, CreateContext(player), out string reason), Is.False);
            Assert.That(reason, Is.Not.Empty);
            Assert.That(manager.TryUsePotion(slotIndex, CreateContext(player)), Is.False);
            Assert.That(manager.GetPotionOrNull(0), Is.SameAs(heal));
            Assert.That(player.CurrentHP, Is.EqualTo(20));
            Assert.That(notifications, Is.Zero);
        }

        [TestCase(TestName = "사용 조건을 만족하지 못하면 물약을 소모하지 않는다 | Use_WhenEffectCannotRun_DoesNotConsume")]
        public void Use_WhenEffectCannotRun_DoesNotConsume()
        {
            PotionManager manager = new PotionManager();
            PotionDataSO damage = CreatePotion(EPotionEffectType.DamageSingle, 20, ECardTarget.SingleEnemy);
            PotionDataSO resurrect = CreatePotion(EPotionEffectType.Resurrect, 30);
            manager.TryAcquirePotion(damage);
            manager.TryAcquirePotion(resurrect);
            StatusController player = CreateStatus(0, 20, 50);
            int notifications = 0;
            manager.Changed += () =>
            {
                notifications++;
            };

            // 살아 있는 적이 없고, 부활 물약은 수동 사용 대상이 아니다.
            Assert.That(manager.TryUsePotion(0, CreateContext(player)), Is.False);
            Assert.That(manager.TryUsePotion(1, CreateContext(player)), Is.False);

            Assert.That(manager.GetPotionOrNull(0), Is.SameAs(damage));
            Assert.That(manager.GetPotionOrNull(1), Is.SameAs(resurrect));
            Assert.That(notifications, Is.Zero);
        }

        [TestCase(TestName = "효과 실행 중에는 같은 물약을 다시 사용하거나 버릴 수 없다 | Use_DuringEffect_BlocksReuseAndDiscard")]
        public void Use_DuringEffect_BlocksReuseAndDiscard()
        {
            PotionManager manager = new PotionManager();
            PotionDataSO draw = CreatePotion(EPotionEffectType.DrawCards, 2);
            PotionDataSO heal = CreatePotion(EPotionEffectType.Heal, 10);
            manager.TryAcquirePotion(draw);
            manager.TryAcquirePotion(heal);
            StatusController player = CreateStatus(0, 20, 50);
            ReentrantDeckOperations deck = new ReentrantDeckOperations();
            CombatContext context = CreateContext(player);
            context.DeckOps = deck;
            bool? reuse = null;
            bool? useOther = null;
            bool? discard = null;
            deck.OnDraw = () =>
            {
                Assert.That(manager.IsUsingPotion, Is.True);
                reuse = manager.TryUsePotion(0, context);
                useOther = manager.TryUsePotion(1, context);
                discard = manager.TryDiscardPotion(0);
            };

            Assert.That(manager.TryUsePotion(0, context), Is.True);

            Assert.That(deck.DrawnCount, Is.EqualTo(2));
            Assert.That(reuse, Is.False);
            Assert.That(useOther, Is.False);
            Assert.That(discard, Is.False);
            Assert.That(manager.IsUsingPotion, Is.False);
            Assert.That(manager.GetPotionOrNull(0), Is.Null);
            Assert.That(manager.GetPotionOrNull(1), Is.SameAs(heal));
        }

        [TestCase(TestName = "치명상 시 첫 부활 물약만 소모해 사망을 취소한다 | Resurrect_OnFatalDamage_ConsumesFirstResurrectPotion")]
        public void Resurrect_OnFatalDamage_ConsumesFirstResurrectPotion()
        {
            PotionManager manager = new PotionManager();
            PotionDataSO heal = CreatePotion(EPotionEffectType.Heal, 10);
            PotionDataSO first = CreatePotion(EPotionEffectType.Resurrect, 30);
            PotionDataSO second = CreatePotion(EPotionEffectType.Resurrect, 30);
            manager.TryAcquirePotion(heal);
            manager.TryAcquirePotion(first);
            manager.TryAcquirePotion(second);
            StatusController player = CreateStatus(0, 10, 50);
            player.OnTryInterceptFatalDamage = manager.TryConsumeResurrectPotion;

            player.TakeDamage(100);

            Assert.That(player.IsAlive, Is.True);
            Assert.That(player.CurrentHP, Is.EqualTo(15));
            Assert.That(manager.GetPotionOrNull(0), Is.SameAs(heal));
            Assert.That(manager.GetPotionOrNull(1), Is.Null);
            Assert.That(manager.GetPotionOrNull(2), Is.SameAs(second));
        }

        [TestCase(TestName = "부활 물약이 없으면 사망을 취소하지 않고 물약도 그대로다 | Resurrect_WithoutPotion_DoesNotIntercept")]
        public void Resurrect_WithoutPotion_DoesNotIntercept()
        {
            PotionManager manager = new PotionManager();
            PotionDataSO heal = CreatePotion(EPotionEffectType.Heal, 10);
            manager.TryAcquirePotion(heal);
            StatusController player = CreateStatus(0, 10, 50);
            player.OnTryInterceptFatalDamage = manager.TryConsumeResurrectPotion;

            player.TakeDamage(100);

            Assert.That(player.IsAlive, Is.False);
            Assert.That(manager.GetPotionOrNull(0), Is.SameAs(heal));
        }

        #endregion
    }
}
