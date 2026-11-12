using System.Collections.Generic;
using System.Linq;
using CNF.AbilitySystem;
using CNF.Data;
using NUnit.Framework;
using UnityEngine;

namespace AlmaAscention.Relic.Tests
{
    public sealed class RelicManagerTests
    {
        private readonly List<RelicDataSO> _createdData = new List<RelicDataSO>();
        private readonly List<GameObject> _createdObjects = new List<GameObject>();

        private RelicDataSO CreateRelic(string relicId, ERelicTriggerType triggerType, string effectKey, int effectValue)
        {
            RelicDataSO data = ScriptableObject.CreateInstance<RelicDataSO>();
            data.RelicID = relicId;
            data.TriggerType = triggerType;
            data.EffectKey = effectKey;
            data.EffectValue = effectValue;
            _createdData.Add(data);
            return data;
        }

        private StatusController CreatePlayer()
        {
            GameObject owner = new GameObject("Relic test player");
            _createdObjects.Add(owner);
            StatusController status = owner.AddComponent<StatusController>();
            status.InitializeStats(50, 50);
            status.TeamID = 0;
            return status;
        }

        private CombatContext CreateContext(StatusController player)
        {
            return new CombatContext
            {
                Caster = player,
                AllEnemies = new StatusController[0]
            };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (RelicDataSO data in _createdData)
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

        #region 획득

        [TestCase(TestName = "null 데이터 획득은 변경이나 알림 없이 실패한다 | Acquire_Null_FailsWithoutNotify")]
        public void Acquire_Null_FailsWithoutNotify()
        {
            RelicManager manager = new RelicManager();
            int notifications = 0;
            manager.Changed += () =>
            {
                notifications++;
            };

            Assert.That(manager.TryAcquireRelic(null), Is.False);
            Assert.That(manager.Count, Is.Zero);
            Assert.That(notifications, Is.Zero);
        }

        [TestCase(null, TestName = "ID가 없는 유물 획득은 변경이나 알림 없이 실패한다 [null] | Acquire_EmptyId_FailsWithoutNotify")]
        [TestCase("", TestName = "ID가 없는 유물 획득은 변경이나 알림 없이 실패한다 [빈 문자열] | Acquire_EmptyId_FailsWithoutNotify")]
        public void Acquire_EmptyId_FailsWithoutNotify(string relicId)
        {
            RelicManager manager = new RelicManager();
            int notifications = 0;
            manager.Changed += () =>
            {
                notifications++;
            };

            Assert.That(manager.TryAcquireRelic(CreateRelic(relicId, ERelicTriggerType.Passive, null, 0)), Is.False);
            Assert.That(manager.Count, Is.Zero);
            Assert.That(notifications, Is.Zero);
        }

        #endregion

        #region 제거

        [TestCase(TestName = "선택한 유물만 제거하고 실제 제거 시에만 한 번 알린다 | Remove_RemovesOnlySelectedAndNotifiesOnce")]
        public void Remove_RemovesOnlySelectedAndNotifiesOnce()
        {
            RelicManager manager = new RelicManager();
            RelicDataSO kept = CreateRelic("kept", ERelicTriggerType.Passive, null, 0);
            manager.TryAcquireRelic(kept);
            manager.TryAcquireRelic(CreateRelic("removed", ERelicTriggerType.Passive, null, 0));
            int notifications = 0;
            manager.Changed += () =>
            {
                notifications++;
            };

            Assert.That(manager.TryRemoveRelic("REMOVED"), Is.True);
            Assert.That(manager.TryRemoveRelic("removed"), Is.False);
            Assert.That(manager.TryRemoveRelic(null), Is.False);

            Assert.That(manager.Count, Is.EqualTo(1));
            Assert.That(manager.Relics[0].OriginalRelicData, Is.SameAs(kept));
            Assert.That(notifications, Is.EqualTo(1));
        }

        [TestCase(TestName = "제거한 유물은 다시 획득할 수 있다 | Remove_ThenAcquireAgain_Succeeds")]
        public void Remove_ThenAcquireAgain_Succeeds()
        {
            RelicManager manager = new RelicManager();
            RelicDataSO data = CreateRelic("relic", ERelicTriggerType.Passive, null, 0);
            manager.TryAcquireRelic(data);
            manager.TryExpireRelic("relic");

            Assert.That(manager.TryRemoveRelic("relic"), Is.True);
            Assert.That(manager.TryAcquireRelic(data), Is.True);
            Assert.That(manager.FindRelicOrNull("relic").IsExpired, Is.False);
        }

        [TestCase(TestName = "일괄 제거는 없는 ID와 중복 ID를 건너뛰고 반영 후 한 번 알린다 | RemoveRelics_SkipsMissingAndDuplicates_NotifiesOnce")]
        public void RemoveRelics_SkipsMissingAndDuplicates_NotifiesOnce()
        {
            RelicManager manager = new RelicManager();
            manager.TryAcquireRelic(CreateRelic("a", ERelicTriggerType.Passive, null, 0));
            manager.TryAcquireRelic(CreateRelic("b", ERelicTriggerType.Passive, null, 0));
            manager.TryAcquireRelic(CreateRelic("c", ERelicTriggerType.Passive, null, 0));
            int notifications = 0;
            int countAtNotification = -1;
            manager.Changed += () =>
            {
                notifications++;
                countAtNotification = manager.Count;
            };

            int removed = manager.RemoveRelics(new[] { "a", "a", "missing", "c" });

            Assert.That(removed, Is.EqualTo(2));
            Assert.That(manager.Count, Is.EqualTo(1));
            Assert.That(manager.Relics[0].OriginalRelicData.RelicID, Is.EqualTo("b"));
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(countAtNotification, Is.EqualTo(1));
        }

        [TestCase(TestName = "실제 제거가 없는 일괄 제거는 알리지 않는다 | RemoveRelics_NothingRemoved_DoesNotNotify")]
        public void RemoveRelics_NothingRemoved_DoesNotNotify()
        {
            RelicManager manager = new RelicManager();
            manager.TryAcquireRelic(CreateRelic("a", ERelicTriggerType.Passive, null, 0));
            int notifications = 0;
            manager.Changed += () =>
            {
                notifications++;
            };

            Assert.That(manager.RemoveRelics(new[] { "missing" }), Is.Zero);
            Assert.That(manager.RemoveRelics(null), Is.Zero);
            Assert.That(manager.Count, Is.EqualTo(1));
            Assert.That(notifications, Is.Zero);
        }

        [TestCase(TestName = "보유 목록에서 추출한 지연 ID 목록으로 전체 제거해도 안전하다 | RemoveRelics_ListDerivedEnumeration_RemovesAll")]
        public void RemoveRelics_ListDerivedEnumeration_RemovesAll()
        {
            RelicManager manager = new RelicManager();
            manager.TryAcquireRelic(CreateRelic("a", ERelicTriggerType.Passive, null, 0));
            manager.TryAcquireRelic(CreateRelic("b", ERelicTriggerType.Passive, null, 0));

            // 보유 목록을 지연 열거하는 입력이어도 순회 중 수정 예외가 나지 않아야 한다.
            IEnumerable<string> derivedIds = manager.Relics.Select(relic =>
            {
                return relic.OriginalRelicData.RelicID;
            });

            int removed = manager.RemoveRelics(derivedIds);

            Assert.That(removed, Is.EqualTo(2));
            Assert.That(manager.Count, Is.Zero);
        }

        [TestCase(TestName = "발동 중 앞선 효과로 제거된 유물은 발동하지 않는다 | Trigger_RemovedDuringTrigger_IsSkipped")]
        public void Trigger_RemovedDuringTrigger_IsSkipped()
        {
            RelicManager manager = new RelicManager();
            manager.TryAcquireRelic(CreateRelic("first", ERelicTriggerType.OnTurnStart, "GainBlock", 5));
            manager.TryAcquireRelic(CreateRelic("second", ERelicTriggerType.OnTurnStart, "GainBlock", 3));
            StatusController player = CreatePlayer();
            manager.Triggered += relic =>
            {
                manager.TryRemoveRelic("second");
            };

            int executed = manager.TriggerRelics(ERelicTriggerType.OnTurnStart, CreateContext(player));

            Assert.That(executed, Is.EqualTo(1));
            Assert.That(player.Block, Is.EqualTo(5));
        }

        #endregion

        [TestCase(TestName = "발동 시점이 일치하는 유물만 획득 순서대로 실행하고 실행 수를 반환한다 | Trigger_RunsOnlyMatchingTimingInAcquireOrder")]
        public void Trigger_RunsOnlyMatchingTimingInAcquireOrder()
        {
            RelicManager manager = new RelicManager();
            RelicDataSO first = CreateRelic("first", ERelicTriggerType.OnTurnStart, "GainBlock", 5);
            RelicDataSO other = CreateRelic("other", ERelicTriggerType.OnTurnEnd, "GainBlock", 100);
            RelicDataSO second = CreateRelic("second", ERelicTriggerType.OnTurnStart, "GainBlock", 3);
            manager.TryAcquireRelic(first);
            manager.TryAcquireRelic(other);
            manager.TryAcquireRelic(second);
            StatusController player = CreatePlayer();
            List<string> triggered = new List<string>();
            manager.Triggered += relic =>
            {
                triggered.Add(relic.OriginalRelicData.RelicID);
            };

            int executed = manager.TriggerRelics(ERelicTriggerType.OnTurnStart, CreateContext(player));

            Assert.That(executed, Is.EqualTo(2));
            Assert.That(player.Block, Is.EqualTo(8));
            Assert.That(triggered, Is.EqualTo(new[] { "first", "second" }));
        }

        [TestCase(TestName = "만료된 유물은 발동하지 않는다 | Trigger_SkipsExpiredRelic")]
        public void Trigger_SkipsExpiredRelic()
        {
            RelicManager manager = new RelicManager();
            manager.TryAcquireRelic(CreateRelic("expired", ERelicTriggerType.OnTurnStart, "GainBlock", 5));
            manager.TryAcquireRelic(CreateRelic("alive", ERelicTriggerType.OnTurnStart, "GainBlock", 3));
            manager.TryExpireRelic("expired");
            StatusController player = CreatePlayer();

            int executed = manager.TriggerRelics(ERelicTriggerType.OnTurnStart, CreateContext(player));

            Assert.That(executed, Is.EqualTo(1));
            Assert.That(player.Block, Is.EqualTo(3));
        }

        [TestCase(ERelicTriggerType.Passive, TestName = "발동형이 아닌 시점은 실행하지 않는다 [Passive] | Trigger_NonTriggerTiming_DoesNothing")]
        [TestCase(ERelicTriggerType.OnAcquire, TestName = "발동형이 아닌 시점은 실행하지 않는다 [OnAcquire] | Trigger_NonTriggerTiming_DoesNothing")]
        public void Trigger_NonTriggerTiming_DoesNothing(ERelicTriggerType timing)
        {
            RelicManager manager = new RelicManager();
            manager.TryAcquireRelic(CreateRelic("relic", timing, "GainBlock", 5));
            StatusController player = CreatePlayer();
            int triggeredCount = 0;
            manager.Triggered += relic =>
            {
                triggeredCount++;
            };

            Assert.That(manager.TriggerRelics(timing, CreateContext(player)), Is.Zero);
            Assert.That(player.Block, Is.Zero);
            Assert.That(triggeredCount, Is.Zero);
        }

        [TestCase(TestName = "효과가 실행되지 않은 유물은 발동 알림과 실행 수에 포함하지 않는다 | Trigger_UnknownEffect_NotCounted")]
        public void Trigger_UnknownEffect_NotCounted()
        {
            RelicManager manager = new RelicManager();
            manager.TryAcquireRelic(CreateRelic("unknown", ERelicTriggerType.OnTurnStart, "NotExistingKey", 5));
            StatusController player = CreatePlayer();
            int triggeredCount = 0;
            manager.Triggered += relic =>
            {
                triggeredCount++;
            };

            Assert.That(manager.TriggerRelics(ERelicTriggerType.OnTurnStart, CreateContext(player)), Is.Zero);
            Assert.That(triggeredCount, Is.Zero);
        }

        [TestCase(TestName = "발동 중 획득한 유물은 이번 발동에 포함하지 않는다 | Trigger_AcquireDuringTrigger_UsesSnapshot")]
        public void Trigger_AcquireDuringTrigger_UsesSnapshot()
        {
            RelicManager manager = new RelicManager();
            manager.TryAcquireRelic(CreateRelic("first", ERelicTriggerType.OnTurnStart, "GainBlock", 5));
            RelicDataSO late = CreateRelic("late", ERelicTriggerType.OnTurnStart, "GainBlock", 3);
            StatusController player = CreatePlayer();
            manager.Triggered += relic =>
            {
                manager.TryAcquireRelic(late);
            };

            int executed = manager.TriggerRelics(ERelicTriggerType.OnTurnStart, CreateContext(player));

            Assert.That(executed, Is.EqualTo(1));
            Assert.That(player.Block, Is.EqualTo(5));
            Assert.That(manager.Count, Is.EqualTo(2));
        }

        [TestCase(TestName = "같은 시점 발동 중 같은 시점 재요청은 무시하고 다른 시점은 허용한다 | Trigger_ReentrantSameTiming_IsIgnored")]
        public void Trigger_ReentrantSameTiming_IsIgnored()
        {
            RelicManager manager = new RelicManager();
            manager.TryAcquireRelic(CreateRelic("start", ERelicTriggerType.OnTurnStart, "GainBlock", 5));
            manager.TryAcquireRelic(CreateRelic("end", ERelicTriggerType.OnTurnEnd, "GainBlock", 3));
            StatusController player = CreatePlayer();
            CombatContext context = CreateContext(player);
            int sameTimingResult = -1;
            int otherTimingResult = -1;
            manager.Triggered += relic =>
            {
                if (relic.OriginalRelicData.RelicID == "start")
                {
                    sameTimingResult = manager.TriggerRelics(ERelicTriggerType.OnTurnStart, context);
                    otherTimingResult = manager.TriggerRelics(ERelicTriggerType.OnTurnEnd, context);
                }
            };

            Assert.That(manager.TriggerRelics(ERelicTriggerType.OnTurnStart, context), Is.EqualTo(1));
            Assert.That(sameTimingResult, Is.Zero);
            Assert.That(otherTimingResult, Is.EqualTo(1));
            Assert.That(player.Block, Is.EqualTo(8));

            // 발동이 끝나면 같은 시점을 다시 실행할 수 있다.
            Assert.That(manager.TriggerRelics(ERelicTriggerType.OnTurnStart, context), Is.EqualTo(1));
        }
    }
}
