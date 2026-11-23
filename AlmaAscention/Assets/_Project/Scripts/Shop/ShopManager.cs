using System;
using System.Collections.Generic;
using AlmaAscention.Deck;
using AlmaAscention.Potion;
using AlmaAscention.Relic;
using CNF.AbilitySystem;
using CNF.Data;

namespace AlmaAscention.Shop
{
    public sealed class ShopCardOffer
    {
        public readonly CardDataSO CardData;
        public readonly int Price;
        public bool IsSold { get; internal set; }

        public ShopCardOffer(CardDataSO cardData, int price)
        {
            CardData = cardData;
            Price = Math.Max(0, price);
        }
    }

    public sealed class ShopRelicOffer
    {
        public readonly RelicDataSO RelicData;
        public readonly int Price;
        public bool IsSold { get; internal set; }

        public ShopRelicOffer(RelicDataSO relicData, int price)
        {
            RelicData = relicData;
            Price = Math.Max(0, price);
        }
    }

    public sealed class ShopPotionOffer
    {
        public readonly PotionDataSO PotionData;
        public readonly int Price;
        public bool IsSold { get; internal set; }

        public ShopPotionOffer(PotionDataSO potionData, int price)
        {
            PotionData = potionData;
            Price = Math.Max(0, price);
        }
    }

    /// <summary>
    /// 상점 구역의 진열 상품(카드, 성유물, 물약) 생성 및 구매, 덱 카드 제거 서비스를 전담하는 매니저.
    /// </summary>
    public sealed class ShopManager
    {
        public const int DefaultCardOfferCount = 3;
        public const int DefaultCardPrice = 15;
        public const int DefaultCardRemovalPrice = 20;

        private static readonly string[] FallbackStockIds = { "IronWave", "TwinStrike", "Inflame" };

        private readonly List<ShopCardOffer> _cardOffers = new();
        private readonly IReadOnlyList<ShopCardOffer> _cardOffersView;

        public IReadOnlyList<ShopCardOffer> CardOffers => _cardOffersView;
        public ShopRelicOffer RelicOffer { get; private set; }
        public ShopPotionOffer PotionOffer { get; private set; }

        public int CardRemovalPrice { get; private set; } = DefaultCardRemovalPrice;
        public bool IsCardRemovalUsed { get; private set; }

        public event Action Changed;

        public ShopManager()
        {
            _cardOffersView = _cardOffers.AsReadOnly();
        }

        /// <summary>
        /// 상점 진입 시 진열대를 새로 구성합니다.
        /// 13번 황금색 깃털(GoldenFeather) 보유 시 희귀(Rare) 카드가 진열될 확률이 자동으로 높아집니다.
        /// </summary>
        public void OpenShop(
            RelicManager relicManager = null,
            int cardOfferCount = DefaultCardOfferCount,
            int baseCardPrice = DefaultCardPrice,
            int cardRemovalPrice = DefaultCardRemovalPrice)
        {
            _cardOffers.Clear();
            RelicOffer = null;
            PotionOffer = null;
            CardRemovalPrice = Math.Max(0, cardRemovalPrice);
            IsCardRemovalUsed = false;

            DataManager dm = DataManager.Instance;
            if (dm != null)
            {
                bool hasGoldenFeather = relicManager != null && relicManager.FindRelicOrNull("GoldenFeather") != null;
                float rareWeight = hasGoldenFeather ? 2.5f : 1f;

                List<CardDataSO> rolledCards = dm.GetRandomCards(Math.Max(1, cardOfferCount), rareWeightMultiplier: rareWeight);
                for (int i = 0; i < rolledCards.Count; i++)
                {
                    CardDataSO card = rolledCards[i];
                    if (card != null)
                        _cardOffers.Add(new ShopCardOffer(card, GetCardPrice(card, baseCardPrice)));
                }

                if (_cardOffers.Count == 0)
                {
                    for (int i = 0; i < FallbackStockIds.Length && _cardOffers.Count < cardOfferCount; i++)
                    {
                        CardDataSO fallback = dm.GetCard(FallbackStockIds[i]);
                        if (fallback != null)
                            _cardOffers.Add(new ShopCardOffer(fallback, baseCardPrice));
                    }
                }

                HashSet<string> ownedRelicIds = null;
                if (relicManager != null && relicManager.Count > 0)
                {
                    ownedRelicIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var r in relicManager.Relics)
                    {
                        if (r?.OriginalRelicData != null && !string.IsNullOrEmpty(r.OriginalRelicData.RelicID))
                            ownedRelicIds.Add(r.OriginalRelicData.RelicID);
                    }
                }

                RelicDataSO randomRelic = dm.GetRandomRelic(null, ownedRelicIds);
                if (randomRelic != null)
                {
                    int relicPrice = randomRelic.ShopPrice > 0 ? randomRelic.ShopPrice : 150;
                    RelicOffer = new ShopRelicOffer(randomRelic, relicPrice);
                }

                PotionDataSO randomPotion = dm.GetRandomPotion();
                if (randomPotion != null)
                {
                    int potionPrice = randomPotion.ShopPrice > 0 ? randomPotion.ShopPrice : 50;
                    PotionOffer = new ShopPotionOffer(randomPotion, potionPrice);
                }
            }

            Changed?.Invoke();
        }

        private static int GetCardPrice(CardDataSO card, int basePrice)
        {
            if (card == null) return basePrice;
            switch (card.Rarity)
            {
                case ECardRarity.Uncommon:
                    return basePrice + 5;
                case ECardRarity.Rare:
                    return basePrice + 15;
                default:
                    return basePrice;
            }
        }

        public ShopCardOffer GetCardOfferOrNull(int index)
        {
            return (index >= 0 && index < _cardOffers.Count) ? _cardOffers[index] : null;
        }

        public bool CanBuyCard(int index, int currentGold)
        {
            ShopCardOffer offer = GetCardOfferOrNull(index);
            return offer != null && !offer.IsSold && offer.CardData != null && currentGold >= offer.Price;
        }

        public bool TryBuyCard(int index, MasterDeck deck, ref int gold, out CardInstance purchasedCard)
        {
            purchasedCard = null;
            if (deck == null || !CanBuyCard(index, gold))
                return false;

            ShopCardOffer offer = _cardOffers[index];
            purchasedCard = deck.CreateAndAddCardOrNull(offer.CardData);
            if (purchasedCard == null)
                return false;

            gold -= offer.Price;
            offer.IsSold = true;
            Changed?.Invoke();
            return true;
        }

        public bool TryBuyRelic(RelicManager relicManager, ref int gold, StatusController playerStatus = null)
        {
            if (relicManager == null || RelicOffer == null || RelicOffer.IsSold || RelicOffer.RelicData == null)
                return false;

            if (gold < RelicOffer.Price)
                return false;

            int price = RelicOffer.Price;
            int bonusGold = 0;
            if (!relicManager.TryAcquireRelic(RelicOffer.RelicData, playerStatus, g => bonusGold += g))
                return false;

            gold = Math.Max(0, gold - price + bonusGold);
            RelicOffer.IsSold = true;
            Changed?.Invoke();
            return true;
        }

        public bool TryBuyPotion(PotionManager potionManager, ref int gold)
        {
            if (potionManager == null || PotionOffer == null || PotionOffer.IsSold || PotionOffer.PotionData == null)
                return false;

            if (gold < PotionOffer.Price)
                return false;

            if (!potionManager.TryAcquirePotion(PotionOffer.PotionData))
                return false;

            gold -= PotionOffer.Price;
            PotionOffer.IsSold = true;
            Changed?.Invoke();
            return true;
        }

        public bool CanBuyCardRemoval(MasterDeck deck, int currentGold)
        {
            return !IsCardRemovalUsed && deck != null && deck.Count > 0 && currentGold >= CardRemovalPrice;
        }

        public bool TryBuyCardRemoval(string cardInstanceGuid, MasterDeck deck, ref int gold)
        {
            if (!CanBuyCardRemoval(deck, gold) || string.IsNullOrEmpty(cardInstanceGuid))
                return false;

            if (!deck.RemoveCard(cardInstanceGuid))
                return false;

            gold -= CardRemovalPrice;
            IsCardRemovalUsed = true;
            Changed?.Invoke();
            return true;
        }
    }
}
