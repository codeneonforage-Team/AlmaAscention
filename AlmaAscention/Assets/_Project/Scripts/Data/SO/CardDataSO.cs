using System;
using System.Collections.Generic;
using UnityEngine;
using CNF.AbilitySystem;

namespace CNF.Data
{
    [Serializable]
    public struct CustomParam
    {
        public string Key;
        public string Value;
    }

    /// <summary>
    /// 5종 데이터 SO(Card, Relic, Potion, Monster, Event)가 공통으로 상속받는 커스텀 파라미터 컨테이너 베이스.
    /// • CSV에 새롭게 추가된 임의의 컬럼들을 손실 없이 보관하며,
    /// • 런타임에는 내부 딕셔너리 캐시(O(1))를 통해 GetParam / GetIntParam / GetFloatParam / GetBoolParam을 즉시 조회합니다.
    /// </summary>
    public abstract class CustomParamContainerSO : ScriptableObject
    {
        [Header("확장용 커스텀 파라미터 (신규 CSV 컬럼 자동 흡수)")]
        public List<CustomParam> CustomParams = new();

        [NonSerialized] private Dictionary<string, string> _paramCache;

        private void EnsureCache()
        {
            if (_paramCache != null) return;
            _paramCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < CustomParams.Count; i++)
            {
                if (!string.IsNullOrEmpty(CustomParams[i].Key))
                    _paramCache[CustomParams[i].Key] = CustomParams[i].Value;
            }
        }

        public string GetParam(string key, string defaultValue = "")
        {
            if (string.IsNullOrEmpty(key)) return defaultValue;
            EnsureCache();
            return _paramCache.TryGetValue(key, out string val) ? val : defaultValue;
        }

        public int GetIntParam(string key, int defaultValue = 0)
        {
            string raw = GetParam(key, null);
            return int.TryParse(raw, out int result) ? result : defaultValue;
        }

        public float GetFloatParam(string key, float defaultValue = 0f)
        {
            string raw = GetParam(key, null);
            return float.TryParse(raw, out float result) ? result : defaultValue;
        }

        public bool GetBoolParam(string key, bool defaultValue = false)
        {
            string raw = GetParam(key, null);
            return bool.TryParse(raw, out bool result) ? result : defaultValue;
        }

        public void SetOrUpdateParam(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            for (int i = 0; i < CustomParams.Count; i++)
            {
                if (string.Equals(CustomParams[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    CustomParams[i] = new CustomParam { Key = key, Value = value };
                    if (_paramCache != null) _paramCache[key] = value;
                    return;
                }
            }
            CustomParams.Add(new CustomParam { Key = key, Value = value });
            if (_paramCache != null) _paramCache[key] = value;
        }

        public void RemoveParam(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            CustomParams.RemoveAll(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
            _paramCache?.Remove(key);
        }
    }

    /// <summary>
    /// 언리얼 FCardDataRow를 대체하는 불변(Immutable) 카드 원본 데이터 에셋.
    /// [SerializeReference] 기반의 Effects 리스트를 통해 신규 기믹을 코드 수정 없이 레고 블록처럼 조립합니다.
    /// </summary>
    [CreateAssetMenu(fileName = "Card_", menuName = "CNF/Data/CardDataSO")]
    public class CardDataSO : CustomParamContainerSO
    {
        [Header("1. 기본 식별 정보")]
        public string CardID;
        public string CardName;
        public string UpgradedCardName;

        [TextArea(2, 4)] public string Description;
        [TextArea(2, 4)] public string UpgradedDescription;

        public Sprite CardArt;
        public ECardType CardType = ECardType.Attack;
        public ECardRarity Rarity = ECardRarity.Common;
        public ECardTarget TargetType = ECardTarget.SingleEnemy;

        [Header("2. 마나 코스트")]
        public int BaseCost = 1;
        public int UpgradedCost = 1;
        [Tooltip("남은 마나를 전부 소모하고 그 수치(X)만큼 효과를 발동하는 X-코스트 카드 여부 (BaseCost가 -1일 때도 자동 활성)")]
        public bool IsXCost = false;

        [Header("3. 키워드 플래그")]
        public bool IsExhaust;          // 소멸 (사용 시 소멸 더미로 이동)
        public bool IsUpgradedExhaust;  // 강화 시 소멸 여부
        public bool IsEthereal;         // 휘발성 (턴 종료 시 손패에 있으면 자동 소멸)
        public bool IsRetain;           // 보존 (턴 종료 시 버려지지 않음)
        public bool IsInnate;           // 선천성 (전투 시작 첫 턴 확정 드로우)

        [Header("4. 연출 데이터 (선택)")]
        public string AnimTriggerName = "Attack";
        public GameObject HitVFXPrefab;
        public AudioClip CastSFX;
        public float CameraShakeIntensity = 0.2f;

        [Header("5. 조립형 카드 효과 블록 리스트 (확장 핵심)")]
        [SerializeReference]
        public List<ICardEffect> Effects = new();
    }
}
