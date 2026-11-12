using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CNF.Data;
using CNF.AbilitySystem;
using NUnit.Framework;
using UnityEngine;
using Random = System.Random;

namespace AlmaAscention.Deck.Tests
{
    public sealed class DeckModelTests
    {
        private readonly List<CardDataSO> _createdData = new List<CardDataSO>();

        private CardDataSO CreateData(string cardId = "test_card")
        {
            CardDataSO data = ScriptableObject.CreateInstance<CardDataSO>();
            data.CardID = cardId;
            _createdData.Add(data);
            return data;
        }

        private CardInstance CreateCard(string cardId = "test_card")
        {
            return new CardInstance(CreateData(cardId));
        }

        private static BattleDeckManager CreateManager(MasterDeck master = null)
        {
            return new BattleDeckManager(master ?? new MasterDeck(), new Random(1234));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (CardDataSO data in _createdData)
            {
                UnityEngine.Object.DestroyImmediate(data);
            }

            _createdData.Clear();
        }

        #region MasterDeck

        [TestCase(TestName = "초기 덱은 전달된 카드 객체와 식별자를 유지한다 | Constructor_PreservesExistingCardsAndTheirIdentity")]
        public void Constructor_PreservesExistingCardsAndTheirIdentity()
        {
            CardDataSO data = CreateData();
            CardInstance first = new CardInstance(data);
            CardInstance second = new CardInstance(data, true, "test_enchantment");
            MasterDeck deck = new MasterDeck(new[] { first, second });

            Assert.That(deck.Count, Is.EqualTo(2));
            Assert.That(deck.Cards[0], Is.SameAs(first));
            Assert.That(deck.Cards[1], Is.SameAs(second));
            Assert.That(first.InstanceGUID, Is.Not.EqualTo(second.InstanceGUID));
            Assert.That(second.IsUpgraded, Is.True);
            Assert.That(second.EnchantmentID, Is.EqualTo("test_enchantment"));
        }

        [TestCase(TestName = "같은 원본으로 생성한 카드도 서로 다른 GUID로 덱에 추가된다 | CreateAndAddCard_CreatesDistinctCardsWithoutChangingSourceData")]
        public void CreateAndAddCard_CreatesDistinctCardsWithoutChangingSourceData()
        {
            CardDataSO data = CreateData();
            MasterDeck deck = new MasterDeck();
            int notifications = 0;
            deck.Changed += () => notifications++;

            CardInstance first = deck.CreateAndAddCardOrNull(data, true, "test_enchantment");
            CardInstance second = deck.CreateAndAddCardOrNull(data);

            Assert.That(deck.Cards, Is.EqualTo(new[] { first, second }));
            Assert.That(first.Data, Is.SameAs(data));
            Assert.That(second.Data, Is.SameAs(data));
            Assert.That(first.InstanceGUID, Is.Not.EqualTo(second.InstanceGUID));
            Assert.That(first.IsUpgraded, Is.True);
            Assert.That(first.EnchantmentID, Is.EqualTo("test_enchantment"));
            Assert.That(second.IsUpgraded, Is.False);
            Assert.That(notifications, Is.EqualTo(2));
        }

        [TestCase(TestName = "같은 GUID의 카드 추가는 거부하고 변경 알림을 보내지 않는다 | TryAddCard_RejectsExistingGuidWithoutNotifying")]
        public void TryAddCard_RejectsExistingGuidWithoutNotifying()
        {
            CardInstance card = CreateCard();
            MasterDeck deck = new MasterDeck();
            int notifications = 0;
            deck.Changed += () => notifications++;

            Assert.That(deck.TryAddCard(card), Is.True);
            Assert.That(deck.TryAddCard(card), Is.False);
            // 별도 객체여도 저장된 GUID가 같다면 중복으로 처리한다.
            CardInstance restoredCopy = JsonUtility.FromJson<CardInstance>(JsonUtility.ToJson(card));
            Assert.That(deck.TryAddCard(restoredCopy), Is.False);
            Assert.That(deck.Cards.Single(), Is.SameAs(card));
            Assert.That(notifications, Is.EqualTo(1));
        }

        [TestCase(TestName = "일괄 추가는 중복을 건너뛰고 목록 반영 후 한 번 알린다 | AddCards_SkipsExistingAndRequestedDuplicates_UpdatesViewBeforeOneNotification")]
        public void AddCards_SkipsExistingAndRequestedDuplicates_UpdatesViewBeforeOneNotification()
        {
            CardInstance existing = CreateCard();
            CardInstance added = CreateCard();
            MasterDeck deck = new MasterDeck(new[] { existing });
            IReadOnlyList<CardInstance> view = deck.Cards;
            int notifications = 0;
            deck.Changed += () =>
            {
                notifications++;
                Assert.That(view, Is.EqualTo(new[] { existing, added }));
            };

            Assert.That(deck.AddCards(new[] { existing, added, added }), Is.EqualTo(1));
            Assert.That(deck.AddCards(new[] { existing, added }), Is.Zero);
            Assert.That(deck.AddCards(Array.Empty<CardInstance>()), Is.Zero);
            Assert.That(notifications, Is.EqualTo(1));
        }

        [TestCase(TestName = "덱에서 추출한 지연 목록으로도 안전하게 카드를 추가한다 | AddCards_DeckDerivedEnumeration_DoesNotModifyCollectionDuringEnumeration")]
        public void AddCards_DeckDerivedEnumeration_DoesNotModifyCollectionDuringEnumeration()
        {
            MasterDeck deck = new MasterDeck(new[] { CreateCard("first"), CreateCard("second") });

            Assert.That(deck.AddCards(deck.Cards.Select(card => card.Clone())), Is.EqualTo(2));
            Assert.That(deck.Count, Is.EqualTo(4));
            Assert.That(deck.Cards.Select(card => card.InstanceGUID).Distinct().Count(), Is.EqualTo(4));
        }

        [TestCase(TestName = "선택한 카드만 삭제하고 실제 삭제 시에만 알린다 | RemoveCard_RemovesOnlySelectedCopyAndNotifiesOnce")]
        public void RemoveCard_RemovesOnlySelectedCopyAndNotifiesOnce()
        {
            CardInstance first = CreateCard();
            CardInstance second = CreateCard();
            MasterDeck deck = new MasterDeck(new[] { first, second });
            int notifications = 0;
            deck.Changed += () => notifications++;

            Assert.That(deck.RemoveCard(first.InstanceGUID), Is.True);
            Assert.That(deck.FindCardOrNull(first.InstanceGUID), Is.Null);
            Assert.That(deck.FindCardOrNull(second.InstanceGUID), Is.SameAs(second));
            Assert.That(deck.RemoveCard(first.InstanceGUID), Is.False);
            Assert.That(deck.Count, Is.EqualTo(1));
            Assert.That(notifications, Is.EqualTo(1));
        }

        [TestCase(TestName = "일괄 삭제는 중복과 없는 카드를 건너뛰고 실제 삭제 수를 반환한다 | RemoveCards_SkipsDuplicatesAndMissingIds_ReturnsActualCount")]
        public void RemoveCards_SkipsDuplicatesAndMissingIds_ReturnsActualCount()
        {
            CardInstance first = CreateCard();
            CardInstance second = CreateCard();
            MasterDeck deck = new MasterDeck(new[] { first, second });
            int notifications = 0;
            deck.Changed += () => notifications++;

            Assert.That(deck.RemoveCards(new[] { first.InstanceGUID, Guid.NewGuid().ToString(), first.InstanceGUID }), Is.EqualTo(1));
            Assert.That(deck.Cards.Single(), Is.SameAs(second));
            Assert.That(deck.RemoveCards(new[] { first.InstanceGUID }), Is.Zero);
            Assert.That(deck.RemoveCards(Array.Empty<string>()), Is.Zero);
            Assert.That(notifications, Is.EqualTo(1));
        }

        [TestCase(TestName = "덱에서 추출한 ID 목록으로 전체 삭제 후 한 번 알린다 | RemoveCards_DeckDerivedEnumeration_RemovesAllBeforeNotifying")]
        public void RemoveCards_DeckDerivedEnumeration_RemovesAllBeforeNotifying()
        {
            MasterDeck deck = new MasterDeck(new[] { CreateCard(), CreateCard() });
            IReadOnlyList<CardInstance> view = deck.Cards;
            int notifications = 0;
            deck.Changed += () =>
            {
                notifications++;
                Assert.That(view, Is.Empty);
            };

            Assert.That(deck.RemoveCards(deck.Cards.Select(card => card.InstanceGUID)), Is.EqualTo(2));
            Assert.That(notifications, Is.EqualTo(1));
        }

        [TestCase(TestName = "존재하는 미강화 카드가 실제 강화될 때만 알린다 | UpgradeCard_OnlyNotifiesWhenCardIsActuallyUpgraded")]
        public void UpgradeCard_OnlyNotifiesWhenCardIsActuallyUpgraded()
        {
            CardInstance card = CreateCard();
            MasterDeck deck = new MasterDeck(new[] { card });
            int notifications = 0;
            deck.Changed += () => notifications++;

            Assert.That(deck.UpgradeCard(card.InstanceGUID), Is.True);
            Assert.That(card.IsUpgraded, Is.True);
            Assert.That(deck.UpgradeCard(card.InstanceGUID), Is.False);
            Assert.That(deck.UpgradeCard(Guid.NewGuid().ToString()), Is.False);
            Assert.That(notifications, Is.EqualTo(1));
        }

        #endregion

        #region CardHand

        // 매니저가 노출하지 않는 손패 자체의 조회·꺼내기 계약만 직접 검증한다.
        [TestCase(TestName = "손패에서 꺼낸 카드의 객체를 유지하고 조회 목록에도 제거를 반영한다 | CardHand_TakeCard_ReturnsSameObjectAndUpdatesExistingView")]
        public void CardHand_TakeCard_ReturnsSameObjectAndUpdatesExistingView()
        {
            CardHand hand = new CardHand();
            IReadOnlyList<CardInstance> view = hand.Cards;
            CardInstance card = CreateCard();
            hand.AddCard(card);

            Assert.That(hand.TakeCardOrNull(card.InstanceGUID), Is.SameAs(card));
            Assert.That(view, Is.Empty);
            Assert.That(hand.Count, Is.Zero);
            Assert.That(hand.TakeCardOrNull(card.InstanceGUID), Is.Null);
        }

        #endregion

        #region BattleDeck

        [TestCase(TestName = "인터페이스의 드로우 요청이 실제 손패 추가로 이어진다 | DeckOperations_DrawCards_UsesExistingDrawFlow")]
        public void DeckOperations_DrawCards_UsesExistingDrawFlow()
        {
            BattleDeckManager manager = CreateManager(new MasterDeck(new[] { CreateCard() }));
            IDeckOperations operations = manager;

            operations.DrawCards(2);

            Assert.That(manager.HandCards.Count, Is.EqualTo(1));
            Assert.That(manager.DrawPile, Is.Empty);
        }

        [TestCase(TestName = "전체 손패 이동 후 소멸을 알리고 패시브로 뽑은 카드는 보존한다 | DeckOperations_ExhaustEntireHand_CompletesMovesBeforePassivesAndPreservesNewDraws")]
        public void DeckOperations_ExhaustEntireHand_CompletesMovesBeforePassivesAndPreservesNewDraws()
        {
            BattleDeckManager manager = CreateManager();
            CardInstance first = manager.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.Hand);
            CardInstance second = manager.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.Hand);
            CardInstance drawn = manager.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.DrawPileTop);
            List<CardInstance> exhaustedEvents = new List<CardInstance>();
            int notifications = 0;
            manager.Changed += () => notifications++;
            manager.CardExhausted += card =>
            {
                Assert.That(manager.ExhaustPile, Is.EquivalentTo(new[] { first, second }));
                exhaustedEvents.Add(card);
                manager.DrawCards(1);
            };

            Assert.That(((IDeckOperations)manager).ExhaustEntireHand(), Is.EqualTo(2));
            Assert.That(exhaustedEvents, Is.EqualTo(new[] { first, second }));
            Assert.That(manager.HandCards.Single(), Is.SameAs(drawn));
            Assert.That(notifications, Is.EqualTo(2));
        }

        [TestCase(TestName = "빈 손패의 전체 소멸은 변경이나 소멸을 알리지 않는다 | DeckOperations_EmptyHandExhaust_DoesNotNotify")]
        public void DeckOperations_EmptyHandExhaust_DoesNotNotify()
        {
            BattleDeckManager manager = CreateManager();
            int notifications = 0;
            int exhaustEvents = 0;
            manager.Changed += () => notifications++;
            manager.CardExhausted += card => exhaustEvents++;

            Assert.That(((IDeckOperations)manager).ExhaustEntireHand(), Is.Zero);
            Assert.That(notifications, Is.Zero);
            Assert.That(exhaustEvents, Is.Zero);
        }

        [TestCase(TestName = "손패 전체 강화는 전투 복사본만 변경하고 한 번 알린다 | DeckOperations_UpgradeAll_OnlyChangesBattleCopiesAndNotifiesOnce")]
        public void DeckOperations_UpgradeAll_OnlyChangesBattleCopiesAndNotifiesOnce()
        {
            CardInstance source = CreateCard();
            BattleDeckManager manager = CreateManager(new MasterDeck(new[] { source }));
            manager.Draw(1);
            CardInstance alreadyUpgraded = manager.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.Hand);
            alreadyUpgraded.Upgrade();
            int notifications = 0;
            manager.Changed += () => notifications++;

            ((IDeckOperations)manager).UpgradeCardsInHand(true);
            ((IDeckOperations)manager).UpgradeCardsInHand(true);

            Assert.That(manager.HandCards.All(card => card.IsUpgraded), Is.True);
            Assert.That(source.IsUpgraded, Is.False);
            Assert.That(notifications, Is.EqualTo(1));
        }

        [TestCase(TestName = "한 장 강화는 난수 시드에 따라 미강화 카드 중에서 선택한다 | DeckOperations_UpgradeOne_UsesSeededRandomAndSkipsUpgradedCards")]
        public void DeckOperations_UpgradeOne_UsesSeededRandomAndSkipsUpgradedCards()
        {
            BattleDeckManager manager = CreateManager();
            CardInstance upgraded = manager.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.Hand);
            upgraded.Upgrade();
            CardInstance first = manager.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.Hand);
            CardInstance second = manager.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.Hand);
            CardInstance[] candidates = { first, second };
            CardInstance expected = candidates[new Random(1234).Next(candidates.Length)];
            int notifications = 0;
            manager.Changed += () => notifications++;

            ((IDeckOperations)manager).UpgradeCardsInHand(false);

            Assert.That(expected.IsUpgraded, Is.True);
            Assert.That(candidates.Count(card => card.IsUpgraded), Is.EqualTo(1));
            Assert.That(notifications, Is.EqualTo(1));
        }

        [TestCase(TestName = "카드 ID로 원본을 조회하고 강화 상태까지 적용한 뒤 손패 추가를 알린다 | DeckOperations_AddCardToHand_ResolvesRegisteredDataAndAppliesUpgradeBeforeNotification")]
        public void DeckOperations_AddCardToHand_ResolvesRegisteredDataAndAppliesUpgradeBeforeNotification()
        {
            // EditMode에서는 Awake와 플레이 모드 자동 생성에 의존하지 않는다.
            // 전용 인스턴스를 연결하고 테스트 종료 시 기존 정적 상태를 복원한다.
            FieldInfo instanceField = typeof(DataManager).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo quittingField = typeof(DataManager).GetField("_isQuitting", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(instanceField, Is.Not.Null);
            Assert.That(quittingField, Is.Not.Null);
            object previousInstance = instanceField.GetValue(null);
            object previousQuitting = quittingField.GetValue(null);
            GameObject createdManager = null;

            try
            {
                createdManager = new GameObject("Deck test DataManager");
                createdManager.SetActive(false);
                DataManager dataManager = createdManager.AddComponent<DataManager>();
                instanceField.SetValue(null, dataManager);
                quittingField.SetValue(null, false);
                dataManager.InitializeDatabase();

                CardDataSO data = CreateData("deck_test_" + Guid.NewGuid().ToString("N"));
                DataManager.Instance.RegisterCard(data);
                BattleDeckManager manager = CreateManager();
                int notifications = 0;
                manager.Changed += () =>
                {
                    notifications++;
                    Assert.That(manager.HandCards.Single().IsUpgraded, Is.True);
                };

                ((IDeckOperations)manager).AddCardToHand(data.CardID, true);

                Assert.That(manager.HandCards.Single().Data, Is.SameAs(data));
                Assert.That(notifications, Is.EqualTo(1));
            }
            finally
            {
                instanceField.SetValue(null, previousInstance);
                quittingField.SetValue(null, previousQuitting);

                if (createdManager != null)
                {
                    UnityEngine.Object.DestroyImmediate(createdManager);
                }
            }
        }

        [TestCase(TestName = "전투 복사는 상태를 이어받고 GUID를 새로 만들며 영구 카드와 독립적이다 | BattleCopy_PreservesStateButChangesIdentity_AndIsIndependentFromMaster")]
        public void BattleCopy_PreservesStateButChangesIdentity_AndIsIndependentFromMaster()
        {
            CardInstance source = CreateCard();
            source.SetEnchantment("original_enchantment");
            source.TempCostOverride = 2;
            source.BonusDamage = 3;
            source.BonusBlock = 4;
            MasterDeck master = new MasterDeck(new[] { source });
            BattleDeckManager manager = CreateManager(master);
            CardInstance copy = manager.DrawPile.Single();

            Assert.That(copy, Is.Not.SameAs(source));
            Assert.That(copy.Data, Is.SameAs(source.Data));
            Assert.That(copy.CardID, Is.EqualTo(source.CardID));
            Assert.That(copy.InstanceGUID, Is.Not.EqualTo(source.InstanceGUID));
            Assert.That(copy.IsUpgraded, Is.EqualTo(source.IsUpgraded));
            Assert.That(copy.EnchantmentID, Is.EqualTo(source.EnchantmentID));
            Assert.That(copy.TempCostOverride, Is.EqualTo(2));
            Assert.That(copy.BonusDamage, Is.EqualTo(3));
            Assert.That(copy.BonusBlock, Is.EqualTo(4));

            copy.Upgrade();
            copy.SetEnchantment("battle_enchantment");
            copy.TempCostOverride = 0;
            copy.BonusDamage = 10;
            copy.BonusBlock = 20;

            Assert.That(source.IsUpgraded, Is.False);
            Assert.That(source.EnchantmentID, Is.EqualTo("original_enchantment"));
            Assert.That(source.TempCostOverride, Is.EqualTo(2));
            Assert.That(source.BonusDamage, Is.EqualTo(3));
            Assert.That(source.BonusBlock, Is.EqualTo(4));
        }

        [TestCase(BattleDeckManager.CardDestination.Hand, TestName = "임시 카드는 지정한 위치에만 추가하고 한 번 알린다 [손패] | TemporaryCard_OnlyEntersRequestedDestination_AndNotifiesOnce")]
        [TestCase(BattleDeckManager.CardDestination.DrawPileTop, TestName = "임시 카드는 지정한 위치에만 추가하고 한 번 알린다 [드로우 더미 최상단] | TemporaryCard_OnlyEntersRequestedDestination_AndNotifiesOnce")]
        [TestCase(BattleDeckManager.CardDestination.DiscardPile, TestName = "임시 카드는 지정한 위치에만 추가하고 한 번 알린다 [묘지] | TemporaryCard_OnlyEntersRequestedDestination_AndNotifiesOnce")]
        [TestCase(BattleDeckManager.CardDestination.ExhaustPile, TestName = "임시 카드는 지정한 위치에만 추가하고 한 번 알린다 [소멸 더미] | TemporaryCard_OnlyEntersRequestedDestination_AndNotifiesOnce")]
        public void TemporaryCard_OnlyEntersRequestedDestination_AndNotifiesOnce(BattleDeckManager.CardDestination destination)
        {
            MasterDeck master = new MasterDeck();
            BattleDeckManager manager = CreateManager(master);
            CardDataSO data = CreateData();
            int notifications = 0;
            manager.Changed += () => notifications++;

            CardInstance card = manager.CreateTemporaryCardOrNull(data, destination);
            IReadOnlyList<CardInstance> selected = destination == BattleDeckManager.CardDestination.Hand ? manager.HandCards : destination == BattleDeckManager.CardDestination.DrawPileTop ? manager.DrawPile : destination == BattleDeckManager.CardDestination.DiscardPile ? manager.DiscardPile : manager.ExhaustPile;

            Assert.That(selected.Single(), Is.SameAs(card));
            Assert.That(card.Data, Is.SameAs(data));
            Assert.That(manager.HandCards.Count + manager.DrawPile.Count + manager.DiscardPile.Count + manager.ExhaustPile.Count, Is.EqualTo(1));
            Assert.That(master.Count, Is.Zero);
            Assert.That(notifications, Is.EqualTo(1));
        }

        [TestCase(TestName = "잘못된 임시 카드 목적지는 상태 변경과 알림 없이 거부한다 | TemporaryCard_InvalidDestination_DoesNotChangeStateOrNotify")]
        public void TemporaryCard_InvalidDestination_DoesNotChangeStateOrNotify()
        {
            BattleDeckManager manager = CreateManager();
            int notifications = 0;
            manager.Changed += () => notifications++;

            Assert.That(manager.CreateTemporaryCardOrNull(CreateData(), (BattleDeckManager.CardDestination)999), Is.Null);
            Assert.That(manager.HandCards.Count + manager.DrawPile.Count + manager.DiscardPile.Count + manager.ExhaustPile.Count, Is.Zero);
            Assert.That(notifications, Is.Zero);
        }

        [TestCase(false, TestName = "손패 이동은 카드 객체와 상태를 유지하고 이동 완료 후 알린다 [버림] | MoveFromHand_PreservesIdentityAndState_AndNotifiesAfterCompletion")]
        [TestCase(true, TestName = "손패 이동은 카드 객체와 상태를 유지하고 이동 완료 후 알린다 [소멸] | MoveFromHand_PreservesIdentityAndState_AndNotifiesAfterCompletion")]
        public void MoveFromHand_PreservesIdentityAndState_AndNotifiesAfterCompletion(bool exhaust)
        {
            BattleDeckManager manager = CreateManager();
            CardInstance first = manager.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.Hand);
            CardInstance second = manager.CreateTemporaryCardOrNull(first.Data, BattleDeckManager.CardDestination.Hand);
            first.Upgrade();
            first.TempCostOverride = 0;
            int notifications = 0;
            manager.Changed += () =>
            {
                notifications++;
                Assert.That(manager.HandCards.Single(), Is.SameAs(second));
                Assert.That(manager.DiscardPile.Count + manager.ExhaustPile.Count, Is.EqualTo(1));
            };

            Assert.That(exhaust ? manager.ExhaustFromHand(first.InstanceGUID) : manager.DiscardFromHand(first.InstanceGUID), Is.True);
            IReadOnlyList<CardInstance> destination = exhaust ? manager.ExhaustPile : manager.DiscardPile;
            Assert.That(destination.Single(), Is.SameAs(first));
            Assert.That(first.IsUpgraded, Is.True);
            Assert.That(first.CurrentCost, Is.Zero);
            Assert.That(exhaust ? manager.ExhaustFromHand(first.InstanceGUID) : manager.DiscardFromHand(first.InstanceGUID), Is.False);
            Assert.That(exhaust ? manager.ExhaustFromHand(Guid.NewGuid().ToString()) : manager.DiscardFromHand(Guid.NewGuid().ToString()), Is.False);
            Assert.That(notifications, Is.EqualTo(1));
        }

        [TestCase(TestName = "덱 최상단 카드를 손패로 이동한 뒤 드로우를 알린다 | ManagerDraw_MovesTopCardToHandBeforeNotifying")]
        public void ManagerDraw_MovesTopCardToHandBeforeNotifying()
        {
            BattleDeckManager manager = CreateManager();
            CardInstance first = manager.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.DrawPileTop);
            CardInstance top = manager.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.DrawPileTop);
            IReadOnlyList<CardInstance> handView = manager.HandCards;
            int notifications = 0;
            manager.Changed += () =>
            {
                notifications++;
                Assert.That(manager.DrawPile.Single(), Is.SameAs(first));
                Assert.That(handView.Single(), Is.SameAs(top));
            };

            Assert.That(manager.Draw(1), Is.EqualTo(1));
            Assert.That(notifications, Is.EqualTo(1));
        }

        [TestCase(TestName = "드로우 부족 시 묘지를 재사용하되 소멸 카드는 제외한다 | ManagerDraw_RefillsDiscardAndStopsWhenInsufficient_ExcludesExhaust")]
        public void ManagerDraw_RefillsDiscardAndStopsWhenInsufficient_ExcludesExhaust()
        {
            BattleDeckManager manager = CreateManager();
            CardInstance first = manager.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.DrawPileTop);
            CardInstance second = manager.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.DiscardPile);
            CardInstance third = manager.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.DiscardPile);
            CardInstance exhausted = manager.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.ExhaustPile);
            int notifications = 0;
            manager.Changed += () => notifications++;

            Assert.That(manager.Draw(5), Is.EqualTo(3));
            Assert.That(manager.HandCards[0], Is.SameAs(first));
            Assert.That(manager.HandCards, Is.EquivalentTo(new[] { first, second, third }));
            Assert.That(manager.DrawPile, Is.Empty);
            Assert.That(manager.DiscardPile, Is.Empty);
            Assert.That(manager.ExhaustPile.Single(), Is.SameAs(exhausted));
            Assert.That(manager.Draw(1), Is.Zero);
            Assert.That(notifications, Is.EqualTo(1));
        }

        [TestCase(TestName = "드로우 중 묘지 리필은 손패 추가가 끝난 뒤 Changed보다 먼저 한 번 알린다 | ManagerDraw_Refill_NotifiesReshuffledOnceAfterMovesBeforeChanged")]
        public void ManagerDraw_Refill_NotifiesReshuffledOnceAfterMovesBeforeChanged()
        {
            BattleDeckManager manager = CreateManager();
            manager.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.DrawPileTop);
            manager.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.DiscardPile);
            manager.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.DiscardPile);
            List<string> events = new List<string>();
            int handCountAtReshuffle = -1;
            manager.Reshuffled += () =>
            {
                events.Add("Reshuffled");
                handCountAtReshuffle = manager.HandCards.Count;
            };
            manager.Changed += () => events.Add("Changed");

            Assert.That(manager.Draw(3), Is.EqualTo(3));
            Assert.That(events, Is.EqualTo(new[] { "Reshuffled", "Changed" }));
            Assert.That(handCountAtReshuffle, Is.EqualTo(3));
        }

        [TestCase(TestName = "리필이 없는 드로우와 수동 셔플은 리필 알림을 보내지 않는다 | ManagerDrawAndShuffle_WithoutRefill_DoesNotNotifyReshuffled")]
        public void ManagerDrawAndShuffle_WithoutRefill_DoesNotNotifyReshuffled()
        {
            BattleDeckManager manager = CreateManager(new MasterDeck(Enumerable.Range(0, 3).Select(index => CreateCard())));
            int reshuffles = 0;
            manager.Reshuffled += () => reshuffles++;

            manager.Shuffle();
            Assert.That(manager.Draw(2), Is.EqualTo(2));
            Assert.That(manager.Draw(5), Is.EqualTo(1));

            Assert.That(reshuffles, Is.Zero);
        }

        [TestCase(TestName = "묘지 일부 재드로우 후에도 모든 카드가 중복이나 누락 없이 남는다 | ManagerDraw_PartialRefill_PreservesEveryCardExactlyOnce")]
        public void ManagerDraw_PartialRefill_PreservesEveryCardExactlyOnce()
        {
            BattleDeckManager manager = CreateManager();
            List<CardInstance> originals = new List<CardInstance>();
            for (int index = 0; index < 5; index++)
            {
                originals.Add(manager.CreateTemporaryCardOrNull(CreateData(), index < 2 ? BattleDeckManager.CardDestination.DrawPileTop : BattleDeckManager.CardDestination.DiscardPile));
            }

            Assert.That(manager.Draw(4), Is.EqualTo(4));
            Assert.That(manager.HandCards.Count, Is.EqualTo(4));
            Assert.That(manager.DrawPile.Count, Is.EqualTo(1));
            Assert.That(manager.DiscardPile, Is.Empty);
            Assert.That(manager.HandCards.Concat(manager.DrawPile), Is.EquivalentTo(originals));
        }

        [TestCase(0, TestName = "0 이하 장수의 드로우는 상태와 알림을 변경하지 않는다 [0] | ManagerDraw_NonPositiveCount_DoesNotChangeStateOrNotify")]
        [TestCase(-1, TestName = "0 이하 장수의 드로우는 상태와 알림을 변경하지 않는다 [-1] | ManagerDraw_NonPositiveCount_DoesNotChangeStateOrNotify")]
        public void ManagerDraw_NonPositiveCount_DoesNotChangeStateOrNotify(int count)
        {
            BattleDeckManager manager = CreateManager(new MasterDeck(new[] { CreateCard() }));
            int notifications = 0;
            manager.Changed += () => notifications++;

            Assert.That(manager.Draw(count), Is.Zero);
            Assert.That(manager.DrawPile.Count, Is.EqualTo(1));
            Assert.That(manager.HandCards, Is.Empty);
            Assert.That(notifications, Is.Zero);
        }

        [TestCase(TestName = "손패 제거는 어느 더미에도 넣지 않고 소멸 알림 없이 한 번 알린다 | RemoveFromHand_RemovesWithoutPileOrExhaustNotification")]
        public void RemoveFromHand_RemovesWithoutPileOrExhaustNotification()
        {
            BattleDeckManager manager = CreateManager();
            CardInstance card = manager.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.Hand);
            CardInstance other = manager.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.Hand);
            int notifications = 0;
            int exhaustNotifications = 0;
            manager.Changed += () => notifications++;
            manager.CardExhausted += _ => exhaustNotifications++;

            Assert.That(manager.RemoveFromHand(card.InstanceGUID), Is.True);
            Assert.That(manager.RemoveFromHand(card.InstanceGUID), Is.False);

            Assert.That(manager.HandCards.Single(), Is.SameAs(other));
            Assert.That(manager.DrawPile, Is.Empty);
            Assert.That(manager.DiscardPile, Is.Empty);
            Assert.That(manager.ExhaustPile, Is.Empty);
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(exhaustNotifications, Is.Zero);
        }

        [TestCase(TestName = "강화 상태로 생성한 임시 카드는 알림 시점에 이미 강화되어 있다 | TemporaryCard_Upgraded_IsUpgradedBeforeNotification")]
        public void TemporaryCard_Upgraded_IsUpgradedBeforeNotification()
        {
            BattleDeckManager manager = CreateManager();
            bool? upgradedAtNotification = null;
            manager.Changed += () => upgradedAtNotification = manager.DiscardPile.Single().IsUpgraded;

            CardInstance card = manager.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.DiscardPile, true);

            Assert.That(card.IsUpgraded, Is.True);
            Assert.That(upgradedAtNotification, Is.True);
        }

        [TestCase(TestName = "같은 시드의 셔플은 순서를 재현하고 다른 더미는 유지한다 | ManagerShuffle_SameSeed_ReproducesOrderAndPreservesOtherPiles")]
        public void ManagerShuffle_SameSeed_ReproducesOrderAndPreservesOtherPiles()
        {
            MasterDeck master = new MasterDeck(Enumerable.Range(0, 8).Select(index => CreateCard("card_" + index)));
            BattleDeckManager first = CreateManager(master);
            BattleDeckManager second = CreateManager(master);
            CardInstance[] originals = first.DrawPile.ToArray();
            CardInstance discarded = first.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.DiscardPile);
            CardInstance exhausted = first.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.ExhaustPile);
            int notifications = 0;
            first.Changed += () => notifications++;

            first.Shuffle();
            second.Shuffle();

            Assert.That(first.DrawPile, Is.EquivalentTo(originals));
            Assert.That(first.DrawPile.Select(card => card.CardID), Is.EqualTo(second.DrawPile.Select(card => card.CardID)));
            Assert.That(first.DiscardPile.Single(), Is.SameAs(discarded));
            Assert.That(first.ExhaustPile.Single(), Is.SameAs(exhausted));
            Assert.That(notifications, Is.EqualTo(1));
        }

        [TestCase(TestName = "전투 덱은 생성 시 시드에 따라 섞이며 카드는 누락 없이 유지된다 | Constructor_ShufflesWithSeed_PreservesAllCards")]
        public void Constructor_ShufflesWithSeed_PreservesAllCards()
        {
            MasterDeck master = new MasterDeck(Enumerable.Range(0, 20).Select(index => CreateCard("card_" + index)));
            BattleDeckManager first = CreateManager(master);
            BattleDeckManager second = CreateManager(master);

            IEnumerable<string> masterOrder = master.Cards.Select(card => card.CardID);

            Assert.That(first.DrawPile.Select(card => card.CardID), Is.EquivalentTo(masterOrder));
            Assert.That(first.DrawPile.Select(card => card.CardID), Is.Not.EqualTo(masterOrder));
            Assert.That(first.DrawPile.Select(card => card.CardID), Is.EqualTo(second.DrawPile.Select(card => card.CardID)));
        }

        [TestCase(0, TestName = "두 장 미만의 덱은 셔플 알림을 보내지 않는다 [0] | ManagerShuffle_FewerThanTwoCards_DoesNotNotify")]
        [TestCase(1, TestName = "두 장 미만의 덱은 셔플 알림을 보내지 않는다 [1] | ManagerShuffle_FewerThanTwoCards_DoesNotNotify")]
        public void ManagerShuffle_FewerThanTwoCards_DoesNotNotify(int count)
        {
            BattleDeckManager manager = CreateManager(new MasterDeck(Enumerable.Range(0, count).Select(index => CreateCard())));
            int notifications = 0;
            manager.Changed += () => notifications++;

            manager.Shuffle();

            Assert.That(manager.DrawPile.Count, Is.EqualTo(count));
            Assert.That(notifications, Is.Zero);
        }

        [TestCase(TestName = "임시 카드를 버리고 다시 뽑아도 객체와 상태를 유지하며 영구 덱은 그대로다 | TemporaryCard_DiscardThenDraw_ReturnsSameCardWithoutChangingMaster")]
        public void TemporaryCard_DiscardThenDraw_ReturnsSameCardWithoutChangingMaster()
        {
            MasterDeck master = new MasterDeck();
            BattleDeckManager manager = CreateManager(master);
            CardInstance card = manager.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.Hand);
            card.Upgrade();
            card.BonusDamage = 3;

            Assert.That(manager.DiscardFromHand(card.InstanceGUID), Is.True);
            Assert.That(manager.Draw(1), Is.EqualTo(1));
            Assert.That(manager.HandCards.Single(), Is.SameAs(card));
            Assert.That(card.IsUpgraded, Is.True);
            Assert.That(card.BonusDamage, Is.EqualTo(3));
            Assert.That(manager.DiscardPile, Is.Empty);
            Assert.That(master.Count, Is.Zero);
        }

        [TestCase(TestName = "새 전투는 이전 전투 변경 없이 영구 덱에서 다시 복사한다 | NewManager_StartsFreshBattleFromUnchangedMaster")]
        public void NewManager_StartsFreshBattleFromUnchangedMaster()
        {
            CardInstance source = CreateCard();
            MasterDeck master = new MasterDeck(new[] { source });
            BattleDeckManager first = CreateManager(master);
            first.Draw(1);
            CardInstance previous = first.HandCards.Single();
            previous.Upgrade();
            previous.TempCostOverride = 0;
            previous.SetEnchantment("battle_only");
            first.ExhaustFromHand(previous.InstanceGUID);
            first.CreateTemporaryCardOrNull(CreateData(), BattleDeckManager.CardDestination.DiscardPile);

            BattleDeckManager next = CreateManager(master);
            CardInstance nextCard = next.DrawPile.Single();

            Assert.That(next.HandCards, Is.Empty);
            Assert.That(next.DiscardPile, Is.Empty);
            Assert.That(next.ExhaustPile, Is.Empty);
            Assert.That(nextCard.InstanceGUID, Is.Not.EqualTo(previous.InstanceGUID));
            Assert.That(nextCard.InstanceGUID, Is.Not.EqualTo(source.InstanceGUID));
            Assert.That(nextCard.IsUpgraded, Is.False);
            Assert.That(nextCard.TempCostOverride, Is.EqualTo(-1));
            Assert.That(nextCard.EnchantmentID, Is.EqualTo(source.EnchantmentID));
            Assert.That(master.Cards.Single(), Is.SameAs(source));
            Assert.That(source.IsUpgraded, Is.False);
            Assert.That(source.TempCostOverride, Is.EqualTo(-1));
        }

        #endregion
    }
}
