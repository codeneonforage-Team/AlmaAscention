using System;
using System.Collections.Generic;
using CNF.Data;

namespace AlmaAscention.Deck
{
    // 전투 중 더미(뽑을 카드·묘지·소멸)를 보관한다.
    // 변경 알림은 소유자인 BattleDeckManager가 일괄 담당하므로 이 클래스는 이벤트를 갖지 않는다.
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

            // 전투 시작 셔플.
            // TODO:: 선천성(Innate) 카드가 생기면 셔플 후 최상단으로 올리는 처리를 여기에 추가.
            ShuffleInternal();
        }

        // 부족하면 가능한 만큼만 뽑고 종료한다.
        // reshuffled: 드로우 도중 묘지를 섞어 뽑을 더미로 되돌렸는지 여부.
        // (리필 후에는 묘지가 비므로 한 번의 Draw에서 리필은 최대 1회다.)
        public IReadOnlyList<CardInstance> Draw(int count, out bool reshuffled)
        {
            List<CardInstance> drawnCards = new List<CardInstance>();
            reshuffled = false;

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
                    reshuffled = true;
                }

                int topIndex = _drawPile.Count - 1;
                CardInstance card = _drawPile[topIndex];

                _drawPile.RemoveAt(topIndex);
                drawnCards.Add(card);
            }

            // 호출자가 반환된 카드들을 손패에 전달해야 한다.
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
        }

        // 카드를 묘지에 추가.
        // 선조건: card는 유효하며 기존 위치에서 제거된 상태다.
        public void AddToDiscardPile(CardInstance card)
        {
            _discardPile.Add(card);
        }

        // 카드를 소멸 리스트에 추가.
        // 선조건: card는 유효하며 기존 위치에서 제거된 상태다.
        public void AddToExhaustPile(CardInstance card)
        {
            _exhaustPile.Add(card);
        }

        // TODO: 연출 시스템 연결 시 드로우·셔플·카드 이동의
        // 처리 순서와 재생에 필요한 당시 정보를 전달하도록 확장한다.
    }
}
