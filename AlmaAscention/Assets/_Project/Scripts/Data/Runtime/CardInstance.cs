using System;
using UnityEngine;
using CNF.AbilitySystem;

namespace CNF.Data
{
    /// <summary>
    /// 실제 마스터 덱, 드로우 더미, 손패, 버린 더미, 소멸 더미에 들어가는 런타임 카드 객체.
    /// • 원본 CardDataSO를 오염시키지 않고 개별 카드의 강화(Upgrade), 마법부여(Enchantment), 임시 코스트 변경을 관리합니다.
    /// • [SerializeField] 백업 필드를 통해 JsonUtility 세이브/로드 및 유니티 인스펙터 실시간 노출을 100% 지원하며,
    ///   세이브 파일 로드 후 Data 프로퍼티 접근 시 DataManager에서 원본 SO를 자동으로 재연결(Lazy Rebind)합니다.
    /// </summary>
    [Serializable]
    public class CardInstance
    {
        [SerializeField] private string _instanceGuid;
        [SerializeField] private string _cardId;
        [SerializeField] private bool _isUpgraded;
        [SerializeField] private string _enchantmentId = EnchantmentKeys.None;

        // 전투 중 일시적 수치 변경용 (예: 이번 턴 코스트 0, 이번 전투 동안 피해량 +3 등)
        public int TempCostOverride = -1;
        public int BonusDamage;
        public int BonusBlock;

        [NonSerialized] private CardDataSO _cachedData;
        [NonSerialized] internal bool SuppressHandUpgradeCandidate;

        public string InstanceGUID => _instanceGuid;
        public string CardID => !string.IsNullOrEmpty(_cardId) ? _cardId : (_cachedData != null ? _cachedData.CardID : string.Empty);
        public bool IsUpgraded => _isUpgraded || SuppressHandUpgradeCandidate;
        public string EnchantmentID => _enchantmentId ?? EnchantmentKeys.None;

        /// <summary>
        /// 원본 CardDataSO 참조. 세이브/로드 직후 _cachedData가 비어있더라도 _cardId로 즉시 자동 복원됩니다.
        /// </summary>
        public CardDataSO Data
        {
            get
            {
                if (_cachedData == null && !string.IsNullOrEmpty(_cardId) && DataManager.Instance != null)
                {
                    _cachedData = DataManager.Instance.GetCard(_cardId);
                }
                return _cachedData;
            }
        }

        public event Action<CardInstance> OnCardStateChanged;

        public CardInstance(CardDataSO data, bool isUpgraded = false, string enchantmentId = EnchantmentKeys.None)
            : this(data != null ? data.CardID : string.Empty, isUpgraded, enchantmentId)
        {
            _cachedData = data;
        }

        public CardInstance(string cardId, bool isUpgraded = false, string enchantmentId = EnchantmentKeys.None)
        {
            _instanceGuid = Guid.NewGuid().ToString();
            _cardId = cardId;
            _isUpgraded = isUpgraded;
            _enchantmentId = enchantmentId ?? EnchantmentKeys.None;
        }

        public string DisplayName
        {
            get
            {
                if (Data == null) return _cardId ?? "(Missing Card)";
                return _isUpgraded && !string.IsNullOrEmpty(Data.UpgradedCardName)
                    ? Data.UpgradedCardName
                    : (_isUpgraded ? $"{Data.CardName}+" : Data.CardName);
            }
        }

        public bool IsXCost => Data != null && (Data.IsXCost || Data.BaseCost < 0);

        public int CurrentCost
        {
            get
            {
                if (TempCostOverride >= 0) return TempCostOverride;
                if (Data == null || IsXCost) return 0;
                return _isUpgraded ? Data.UpgradedCost : Data.BaseCost;
            }
        }

        public bool WillExhaust => Data != null && (_isUpgraded ? Data.IsUpgradedExhaust : Data.IsExhaust);

        /// <summary>
        /// 카드를 강화합니다. 이미 강화된 상태라면 false를, 새로 강화되었다면 true를 반환합니다.
        /// </summary>
        public bool Upgrade()
        {
            if (_isUpgraded) return false;
            _isUpgraded = true;
            OnCardStateChanged?.Invoke(this);
            return true;
        }

        public void SetEnchantment(string enchantmentId)
        {
            _enchantmentId = enchantmentId ?? EnchantmentKeys.None;
            OnCardStateChanged?.Invoke(this);
        }

        public void ResetTurnModifiers()
        {
            TempCostOverride = -1;
            OnCardStateChanged?.Invoke(this);
        }

        public CardInstance Clone()
        {
            return new CardInstance(Data, _isUpgraded, _enchantmentId)
            {
                _cardId = this.CardID,
                TempCostOverride = this.TempCostOverride,
                BonusDamage = this.BonusDamage,
                BonusBlock = this.BonusBlock
            };
        }

        /// <summary>
        /// 현재 시전자/타겟의 상태이상(힘, 약화, 취약, 민첩, 손상)을 반영한 실시간 카드 설명 텍스트를 반환합니다.
        /// </summary>
        public string GetDynamicDescription(CombatContext context)
        {
            if (Data == null) return string.Empty;

            string rawDesc = _isUpgraded && !string.IsNullOrEmpty(Data.UpgradedDescription)
                ? Data.UpgradedDescription
                : Data.Description;

            if (Data.Effects == null || Data.Effects.Count == 0)
                return rawDesc;

            // {D}, {D2}, {B}, {M}, {M2} 등의 플레이스홀더를 각 효과 블록의 실시간 프리뷰 수치로 치환
            for (int i = 0; i < Data.Effects.Count; i++)
            {
                ICardEffect effect = Data.Effects[i];
                if (effect == null) continue;
                rawDesc = effect.FormatDescription(rawDesc, context, this);
            }

            return rawDesc;
        }
    }
}
