using System;
using System.Collections.Generic;
using CNF.Data;

namespace AlmaAscention.Deck
{
    public sealed class BattleDeck
    {
        // TODO:: 랜덤 시스템
        private readonly Random _random;

        // 남은 카드 (리스트 마지막 원소가 다음에 뽑힐 카드)
        private readonly List<CardInstance> _drawPile = new List<CardInstance>();

        // 묘지 카드
        private readonly List<CardInstance> _discardPile = new List<CardInstance>();

        // 소멸된 카드
        private readonly List<CardInstance> _exhaustPile = new List<CardInstance>();

        public IReadOnlyList<CardInstance> DrawPile { get; }
        public IReadOnlyList<CardInstance> DiscardPile { get; }
        public IReadOnlyList<CardInstance> ExhaustPile { get; }

        // BattleDeck은 masterDeck에서 복사해옴, DeckManager가 이를 연결함.
        // 선조건: masterDeck과 random은 유효하다.
        // TODO:: 현재 난수의 경우 Random 그대로 쓸지는 결정되지 않음.
        // TODO:: MasterDeck에 대한 의존성을 갖고 있는 게 타당한가?
        public BattleDeck(MasterDeck masterDeck, Random random)
        {
            _random = random;

            DrawPile = _drawPile.AsReadOnly();
            DiscardPile = _discardPile.AsReadOnly();
            ExhaustPile = _exhaustPile.AsReadOnly();

            foreach (CardInstance card in masterDeck.Cards)
            {
                // 전투 변경이 영구 덱에 반영되지 않도록 별도 객체로 복사한다.
                // Clone 시 새 GUID가 발급되며 강화·인챈트 및 임시 수치도 복사된다.
                _drawPile.Add(card.Clone());
            }

            // 시작 셔플의 실행 시점은 전투 준비 쪽에서 결정.
        }

        // 부족하면 가능한 만큼만 뽑고 종료한다.
        public IReadOnlyList<CardInstance> Draw(int count)
        {
            List<CardInstance> drawnCards = new List<CardInstance>();

            for (int index = 0; index < count; index++)
            {
                if (_drawPile.Count == 0)
                {
                    // 덱과 버림 더미가 모두 비는 상황에 대비해 드로우를 중단한다.
                    if (_discardPile.Count == 0)
                    {
                        break;
                    }

                    RefillDrawPileInternal();
                }

                int topIndex = _drawPile.Count - 1;
                CardInstance card = _drawPile[topIndex];

                _drawPile.RemoveAt(topIndex);
                drawnCards.Add(card);
            }

            // TODO: drawnCards를 CardHand에 전달하는 인터페이스 연결.
            // 현재는 호출자가 반환된 카드들을 손패에 전달해야 한다.

            if (drawnCards.Count > 0)
            {
                Changed?.Invoke();
            }

            return drawnCards.AsReadOnly();
        }

        // 덱 리필 시, 묘지 -> 카드 뭉치로 추가 & 묘지 비움 & 카드 뭉치 섞음.
        private void RefillDrawPileInternal()
        {
            _drawPile.AddRange(_discardPile);
            _discardPile.Clear();

            ShuffleInternal();
        }

        // 덱을 셔플함.
        public void Shuffle()
        {
            if (_drawPile.Count < 2)
            {
                return;
            }

            ShuffleInternal();
            Changed?.Invoke();
        }

        // TODO: 임시 로직임.
        private void ShuffleInternal()
        {
            for (int index = _drawPile.Count - 1; index > 0; index--)
            {
                int randomIndex = _random.Next(index + 1);

                CardInstance temporaryCard = _drawPile[index];
                _drawPile[index] = _drawPile[randomIndex];
                _drawPile[randomIndex] = temporaryCard;
            }
        }

        // 덱 최상단에 카드 추가.
        // 선조건: card는 유효하며 기존 위치에서 제거된 상태다.
        // 새로 생성한 전투 카드는 바로 추가할 수 있다.
        public void AddToTopOfDrawPile(CardInstance card)
        {
            // 리스트의 마지막 원소가 다음에 뽑힐 카드다.
            _drawPile.Add(card);
            Changed?.Invoke();
        }

        // 카드를 묘지에 추가.
        // 선조건: card는 유효하며 기존 위치에서 제거된 상태다.
        public void AddToDiscardPile(CardInstance card)
        {
            _discardPile.Add(card);
            Changed?.Invoke();
        }

        // 카드를 소멸 리스트에 추가.
        // 선조건: card는 유효하며 기존 위치에서 제거된 상태다.
        public void AddToExhaustPile(CardInstance card)
        {
            _exhaustPile.Add(card);
            Changed?.Invoke();
        }

        // TODO: CardHand에서 버림·소멸 더미로 전달하는 인터페이스.
        // TODO: CardHand에 임시 생성 카드를 추가하는 인터페이스.

        // TODO:: Notify인데 Custom으로 바꿀 수도 있음. 아직 보류.
        public event Action Changed;

        // TODO: 연출 시스템 연결 시 드로우·셔플·카드 이동의
        // 처리 순서와 재생에 필요한 당시 정보를 전달하도록 확장한다.
    }
}