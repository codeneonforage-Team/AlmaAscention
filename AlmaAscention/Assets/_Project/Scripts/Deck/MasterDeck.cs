using System;
using System.Collections.Generic;
using CNF.Data;

namespace AlmaAscention.Deck
{

    public sealed class MasterDeck
    {
        private readonly List<CardInstance> _cards = new List<CardInstance>();

        public IReadOnlyList<CardInstance> Cards { get; }


        public int Count
        {
            get
            {
                return _cards.Count;
            }
        }


        public MasterDeck()
        {
            Cards = _cards.AsReadOnly();
        }


        public MasterDeck(IEnumerable<CardInstance> startingCards)
            : this()
        {
            _cards.AddRange(startingCards);
        }


        // TODO:: MasterDeck에서 카드를 생성할 케이스가 있나? 확인 후 없으면 이 메서드 삭제
        // 새 카드를 획득한다.
        // CardInstance 생성자가 새로운 GUID를 발급한다.
        public CardInstance CreateAndAddCardOrNull(CardDataSO cardData, bool isUpgraded = false, string enchantmentId = null)
        {
            CardInstance card = new CardInstance(cardData, isUpgraded, enchantmentId);

            if (TryAddCard(card))
            {
                return card;
            }

            return null;
        }

        #region MasterDeck에 Card 추가 


        // 외부에서 생성된 카드를 덱에 추가한다.
        // 같은 카드 (InstanceGUID가 동일함)가 이미 덱에 있으면 추가하지 않고 실패한다.
        public bool TryAddCard(CardInstance card)
        {
            if (FindCardOrNull(card.InstanceGUID) != null)
            {
                return false;
            }

            _cards.Add(card);
            Changed?.Invoke();

            return true;
        }


        // 선조건: cards와 각 카드는 유효하다.
        // GUID가 이미 등록된 카드는 건너뛰고, 실제 추가한 카드 수를 반환한다.
        public int AddCards(IEnumerable<CardInstance> cards)
        {
            HashSet<string> registeredIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (CardInstance existingCard in _cards)
            {
                registeredIds.Add(existingCard.InstanceGUID);
            }

            List<CardInstance> cardsToAdd = new List<CardInstance>();

            foreach (CardInstance card in cards)
            {
                if (registeredIds.Add(card.InstanceGUID))
                {
                    cardsToAdd.Add(card);
                }
            }

            if (cardsToAdd.Count > 0)
            {
                _cards.AddRange(cardsToAdd);
                Changed?.Invoke();
            }

            return cardsToAdd.Count;
        }

        #endregion     

        #region MasterDeck에 Card 삭제 


        public bool RemoveCard(string instanceGuid)
        {
            CardInstance card = FindCardOrNull(instanceGuid);

            if (card == null)
            {
                return false;
            }

            _cards.Remove(card);
            Changed?.Invoke();

            return true;
        }

        // 삭제된 카드 수를 반환한다.        
        public int RemoveCards(IEnumerable<string> instanceGuids)
        {
            // 입력이 덱 목록에서 파생된 열거형이어도 안전하게 처리한다.
            List<string> requestedIds = new List<string>(instanceGuids);
            int removedCount = 0;

            foreach (string instanceGuid in requestedIds)
            {
                CardInstance card = FindCardOrNull(instanceGuid);

                if (card == null)
                {
                    continue;
                }

                _cards.Remove(card);
                removedCount++;
            }

            if (removedCount > 0)
            {
                Changed?.Invoke();
            }

            return removedCount;
        }

        #endregion

        // instanceGuid를 토대로 덱에서 카드를 조회한다.
        public CardInstance FindCardOrNull(string instanceGuid)
        {
            foreach (CardInstance card in _cards)
            {
                if (string.Equals(card.InstanceGUID, instanceGuid, StringComparison.Ordinal))
                {
                    return card;
                }
            }

            return null;
        }

        // TODO:: card.Upgrade()가 bool값을 반환하면, MasterDeck에서 card.IsUpgraded를 확인할 필요가 사라짐

        public bool UpgradeCard(string instanceGuid)
        {
            CardInstance card = FindCardOrNull(instanceGuid);

            if (card == null || card.IsUpgraded)
            {
                return false;
            }

            card.Upgrade();
            Changed?.Invoke();

            return true;
        }


        // TODO:: Notify 인데 Custom으로 바꿀 수도 있음. 아직 보류
        public event Action Changed;

    }
}