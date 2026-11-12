using System;
using System.Collections.Generic;
using CNF.Data;
using CNF.AbilitySystem;

namespace AlmaAscention.Deck
{
    public sealed class BattleDeckManager : IDeckOperations
    {
        public enum CardDestination
        {
            Hand,
            DrawPileTop,
            DiscardPile,
            ExhaustPile
        }

        // TODO:: Random을 그대로 쓰는 건 임시임. 아직 난수를 어떻게 설정할지 고려하지 않음.
        public BattleDeckManager(MasterDeck masterDeck, Random random)
        {
            _random = random;
            _battleDeck = new BattleDeck(masterDeck, random);
            _cardHand = new CardHand();
        }

        private readonly BattleDeck _battleDeck;
        private readonly CardHand _cardHand;
        private readonly Random _random;

        public IReadOnlyList<CardInstance> HandCards
        {
            get
            {
                return _cardHand.Cards;
            }
        }

        public IReadOnlyList<CardInstance> DrawPile
        {
            get
            {
                return _battleDeck.DrawPile;
            }
        }

        public IReadOnlyList<CardInstance> DiscardPile
        {
            get
            {
                return _battleDeck.DiscardPile;
            }
        }

        public IReadOnlyList<CardInstance> ExhaustPile
        {
            get
            {
                return _battleDeck.ExhaustPile;
            }
        }

        // 실제 손패에 추가한 카드 수를 반환한다.
        public int Draw(int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            IReadOnlyList<CardInstance> drawnCards = _battleDeck.Draw(count, out bool reshuffled);

            foreach (CardInstance card in drawnCards)
            {
                _cardHand.AddCard(card);
            }

            // 이동을 모두 끝낸 뒤 알린다. (리스너가 덱을 조작해도 상태가 꼬이지 않도록)
            if (reshuffled)
            {
                Reshuffled?.Invoke();
            }

            if (drawnCards.Count > 0)
            {
                Changed?.Invoke();
            }

            return drawnCards.Count;
        }

        // 카드 효과가 사용하는 드로우 인터페이스. 실제 추가 장수는 Draw에서 반환한다.
        public void DrawCards(int count)
        {
            Draw(count);
        }

        // 핸드에서 카드를 묘지에 보냄.
        // 선조건: instanceGuid는 유효하다.
        public bool DiscardFromHand(string instanceGuid)
        {
            CardInstance card = _cardHand.TakeCardOrNull(instanceGuid);

            if (card == null)
            {
                return false;
            }

            _battleDeck.AddToDiscardPile(card);
            Changed?.Invoke();

            return true;
        }

        // 핸드에서 카드를 소멸 상태로 처리함.
        // 선조건: instanceGuid는 유효하다.
        public bool ExhaustFromHand(string instanceGuid)
        {
            CardInstance card = _cardHand.TakeCardOrNull(instanceGuid);

            if (card == null)
            {
                return false;
            }

            _battleDeck.AddToExhaustPile(card);
            Changed?.Invoke();
            CardExhausted?.Invoke(card);

            return true;
        }

        // 핸드에서 카드를 어느 더미에도 넣지 않고 전투에서 제거함. (예: 사용한 파워 카드)
        // 소멸이 아니므로 CardExhausted는 발생하지 않는다.
        // 선조건: instanceGuid는 유효하다.
        public bool RemoveFromHand(string instanceGuid)
        {
            CardInstance card = _cardHand.TakeCardOrNull(instanceGuid);

            if (card == null)
            {
                return false;
            }

            Changed?.Invoke();

            return true;
        }

        // 호출 시점의 손패 전체를 소멸시킨 뒤 실제 소멸한 장수를 반환한다.
        // 소멸 패시브로 새로 뽑은 카드가 이번 소멸에 포함되지 않도록 먼저 이동을 완료한다.
        public int ExhaustEntireHand()
        {
            List<CardInstance> handSnapshot = new List<CardInstance>(_cardHand.Cards);
            List<CardInstance> exhaustedCards = new List<CardInstance>();

            foreach (CardInstance card in handSnapshot)
            {
                CardInstance removed = _cardHand.TakeCardOrNull(card.InstanceGUID);

                if (removed == null)
                {
                    continue;
                }

                _battleDeck.AddToExhaustPile(removed);
                exhaustedCards.Add(removed);
            }

            if (exhaustedCards.Count > 0)
            {
                Changed?.Invoke();

                foreach (CardInstance card in exhaustedCards)
                {
                    CardExhausted?.Invoke(card);
                }
            }

            return exhaustedCards.Count;
        }

        // 카드 ID로 원본 데이터를 조회하고 전투용 카드를 손패에 생성한다.
        // 카드 효과용 IDeckOperations 진입점이며, 실제 생성은 CreateTemporaryCardOrNull이 담당한다.
        // 선조건: cardId는 등록된 카드 ID이며 DataManager가 초기화되어 있다.
        public void AddCardToHand(string cardId, bool isUpgraded = false)
        {
            CardDataSO cardData = DataManager.Instance.GetCard(cardId);
            CreateTemporaryCardOrNull(cardData, CardDestination.Hand, isUpgraded);
        }

        // 전투 중 손패의 미강화 카드만 강화한다. 영구 덱에는 반영하지 않는다.
        public void UpgradeCardsInHand(bool upgradeAll)
        {
            List<CardInstance> candidates = new List<CardInstance>();

            foreach (CardInstance card in _cardHand.Cards)
            {
                if (!card.IsUpgraded)
                {
                    candidates.Add(card);
                }
            }

            if (candidates.Count == 0)
            {
                return;
            }

            if (upgradeAll)
            {
                foreach (CardInstance card in candidates)
                {
                    card.Upgrade();
                }
            }
            else
            {
                // TODO:: 임시로 무작위 한 장을 강화한다. 추후 사용자에게 강화할 카드를 선택받도록 수정해야 한다.
                candidates[_random.Next(candidates.Count)].Upgrade();
            }

            Changed?.Invoke();
        }

        // 전투 중 카드를 생성하는 경우. (임시 카드 생성의 단일 구현)
        // 강화 상태는 생성 시점에 적용되므로 알림을 받는 쪽은 최종 상태를 본다.
        // 선조건: cardData는 유효하다.
        public CardInstance CreateTemporaryCardOrNull(CardDataSO cardData, CardDestination destination, bool isUpgraded = false)
        {
            // 카드 생성 전에 목적지부터 검증한다.
            switch (destination)
            {
                case CardDestination.Hand:
                case CardDestination.DrawPileTop:
                case CardDestination.DiscardPile:
                case CardDestination.ExhaustPile:
                    break;

                // ★ 발생해서는 안 되는 Case.
                // Try-Catch를 쓰더라도 상태를 원복할 방법이 없기 때문에 쓰지 않음.
                default:
                    return null;
            }

            CardInstance card = new CardInstance(cardData, isUpgraded);

            switch (destination)
            {
                case CardDestination.Hand:
                    _cardHand.AddCard(card);
                    break;

                case CardDestination.DrawPileTop:
                    _battleDeck.AddToTopOfDrawPile(card);
                    break;

                case CardDestination.DiscardPile:
                    _battleDeck.AddToDiscardPile(card);
                    break;

                case CardDestination.ExhaustPile:
                    _battleDeck.AddToExhaustPile(card);
                    break;
            }

            Changed?.Invoke();

            return card;
        }

        public void Shuffle()
        {
            if (_battleDeck.DrawPile.Count < 2)
            {
                return;
            }

            _battleDeck.Shuffle();
            Changed?.Invoke();
        }

        // TODO:: 연출 시스템 연결 시 처리 순서와 당시 정보를 전달하도록 확장한다.

        // 매니저가 요청받은 작업을 완료한 뒤 알린다. (전투 중 덱·손패 변경의 단일 알림 창구)
        // TODO:: UI 갱신에 필요한 데이터가 확정되면 변경된 영역(손패/더미)을 인자로 전달하도록 확장.
        public event Action Changed;

        // 드로우 도중 묘지를 섞어 뽑을 더미로 되돌렸을 때, 드로우가 끝난 뒤 한 번 알린다. (Changed보다 먼저 발생)
        // 수동 Shuffle()에서는 발생하지 않는다. ("셔플 시 발동" 유물, 셔플 연출 연결용)
        public event Action Reshuffled;

        // 소멸 이동 완료 후 전투 쪽에서 패시브와 전역 이벤트를 연결한다.
        // 예: card => CardExecutor.TriggerCardExhaustPassives(card, context)
        // 카드 실행기가 이미 소멸 패시브를 처리한 카드에 대해서는 중복 호출하지 않는다.
        public event Action<CardInstance> CardExhausted;
    }
}
