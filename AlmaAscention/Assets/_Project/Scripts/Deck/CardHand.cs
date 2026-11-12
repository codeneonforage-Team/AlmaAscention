using System;
using System.Collections.Generic;
using CNF.Data;

namespace AlmaAscention.Deck
{
    public sealed class CardHand
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

        public CardHand()
        {
            Cards = _cards.AsReadOnly();
        }

        // 선조건:
        // card는 유효하다.
        // 호출자는 카드를 기존 위치에서 제거한 뒤 손패에 추가해야 한다.
        // 새로 생성한 카드는 바로 추가해도 상관없다.
        public void AddCard(CardInstance card)
        {
            _cards.Add(card);
        }

        // 손패에서 카드를 삭제하고 해당 객체를 반환한다.
        // 선조건: instanceGuid는 유효하다.
        // 해당 카드가 없으면 null을 반환한다.
        public CardInstance TakeCardOrNull(string instanceGuid)
        {
            for (int index = 0; index < _cards.Count; index++)
            {
                CardInstance card = _cards[index];

                if (string.Equals(card.InstanceGUID, instanceGuid, StringComparison.Ordinal))
                {
                    _cards.RemoveAt(index);

                    return card;
                }
            }

            return null;
        }

        // 변경 알림은 소유자인 BattleDeckManager가 일괄 담당한다.
    }
}