using System;
using System.Collections.Generic;
using UnityEngine;
using CNF.AbilitySystem;

namespace CNF.Data
{
    /// <summary>
    /// 언리얼 UDataTable->FindRow를 대체하는 중앙 데이터 매니저.
    /// [Lazy Auto-Bootstrap 적용]: 어느 테스트 씬에서 바로 플레이를 눌러도 씬에 DataManager가 없으면
    /// 자동으로 생성 및 Resources/GameData 로드를 완료하여 NullReferenceException이 절대 발생하지 않습니다.
    /// </summary>
    public class DataManager : MonoBehaviour
    {
        private static DataManager _instance;
        private static bool _isQuitting;

        public static bool HasInstance => _instance != null && !_isQuitting;

        public static DataManager Instance
        {
            get
            {
                if (_isQuitting) return null;

                if (_instance == null && Application.isPlaying)
                {
                    _instance = FindAnyObjectByType<DataManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("@DataManager (Auto-Created)");
                        _instance = go.AddComponent<DataManager>();
                        _instance.InitializeDatabase();
                    }
                }
                return _instance;
            }
        }

        [Header("1. 등록된 데이터 목록 (Resources/GameData/* 자동 로드 지원)")]
        [SerializeField] private List<CardDataSO> _cardList = new();
        [SerializeField] private List<RelicDataSO> _relicList = new();
        [SerializeField] private List<PotionDataSO> _potionList = new();
        [SerializeField] private List<MonsterDataSO> _monsterList = new();
        [SerializeField] private List<EventDataSO> _eventList = new();

        [Header("2. 확장 상태이상 & 인챈트 규칙 목록")]
        [SerializeField] private List<StatusBehaviorSO> _statusBehaviorList = new();
        [SerializeField] private List<EnchantmentBehaviorSO> _enchantmentList = new();

        private readonly Dictionary<string, CardDataSO> _cardDict = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RelicDataSO> _relicDict = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PotionDataSO> _potionDict = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MonsterDataSO> _monsterDict = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, EventDataSO> _eventDict = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, StatusBehaviorSO> _statusDict = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, EnchantmentBehaviorSO> _enchantDict = new(StringComparer.OrdinalIgnoreCase);

        private bool _isInitialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instance = null;
            _isQuitting = false;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            if (Application.isPlaying)
                DontDestroyOnLoad(gameObject);

            InitializeDatabase();
        }

        private void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        public void InitializeDatabase()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            LoadCategory(_cardList, "GameData/Cards", _cardDict, x => x.CardID);
            LoadCategory(_relicList, "GameData/Relics", _relicDict, x => x.RelicID);
            LoadCategory(_potionList, "GameData/Potions", _potionDict, x => x.PotionID);
            LoadCategory(_monsterList, "GameData/Monsters", _monsterDict, x => x.MonsterID);
            LoadCategory(_eventList, "GameData/Events", _eventDict, x => x.EventID);
            LoadCategory(_statusBehaviorList, "GameData/Statuses", _statusDict, x => x.StatusID);
            LoadCategory(_enchantmentList, "GameData/Enchantments", _enchantDict, x => x.EnchantmentID);
        }

        private static void LoadCategory<T>(
            List<T> inspectorList,
            string resourceSubPath,
            Dictionary<string, T> dict,
            Func<T, string> keySelector) where T : UnityEngine.Object
        {
            dict.Clear();
            if (inspectorList != null)
            {
                for (int i = 0; i < inspectorList.Count; i++)
                    RegisterItem(dict, inspectorList[i], keySelector);
            }

            T[] loaded = Resources.LoadAll<T>(resourceSubPath);
            for (int i = 0; i < loaded.Length; i++)
                RegisterItem(dict, loaded[i], keySelector);
        }

        private static void RegisterItem<T>(Dictionary<string, T> dict, T item, Func<T, string> keySelector) where T : UnityEngine.Object
        {
            if (item == null) return;
            string key = keySelector(item);
            if (!string.IsNullOrEmpty(key))
                dict[key] = item;
        }

        // =====================================================================
        // 동적 등록 API
        // =====================================================================

        public void RegisterCard(CardDataSO data) => RegisterItem(_cardDict, data, x => x.CardID);
        public void RegisterRelic(RelicDataSO data) => RegisterItem(_relicDict, data, x => x.RelicID);
        public void RegisterPotion(PotionDataSO data) => RegisterItem(_potionDict, data, x => x.PotionID);
        public void RegisterMonster(MonsterDataSO data) => RegisterItem(_monsterDict, data, x => x.MonsterID);
        public void RegisterEvent(EventDataSO data) => RegisterItem(_eventDict, data, x => x.EventID);
        public void RegisterStatusBehavior(StatusBehaviorSO data) => RegisterItem(_statusDict, data, x => x.StatusID);
        public void RegisterEnchantment(EnchantmentBehaviorSO data) => RegisterItem(_enchantDict, data, x => x.EnchantmentID);

        // =====================================================================
        // 팀원 제공용 O(1) 조회 API
        // =====================================================================

        public CardDataSO GetCard(string id) => !string.IsNullOrEmpty(id) && _cardDict.TryGetValue(id, out var v) ? v : null;
        public RelicDataSO GetRelic(string id) => !string.IsNullOrEmpty(id) && _relicDict.TryGetValue(id, out var v) ? v : null;
        public PotionDataSO GetPotion(string id) => !string.IsNullOrEmpty(id) && _potionDict.TryGetValue(id, out var v) ? v : null;
        public MonsterDataSO GetMonster(string id) => !string.IsNullOrEmpty(id) && _monsterDict.TryGetValue(id, out var v) ? v : null;
        public EventDataSO GetEvent(string id) => !string.IsNullOrEmpty(id) && _eventDict.TryGetValue(id, out var v) ? v : null;
        public StatusBehaviorSO GetStatusBehavior(string id) => !string.IsNullOrEmpty(id) && _statusDict.TryGetValue(id, out var v) ? v : null;
        public EnchantmentBehaviorSO GetEnchantmentBehavior(string id) => !string.IsNullOrEmpty(id) && _enchantDict.TryGetValue(id, out var v) ? v : null;

        public bool IsValidCardID(string id) => !string.IsNullOrEmpty(id) && _cardDict.ContainsKey(id);
        public bool IsValidRelicID(string id) => !string.IsNullOrEmpty(id) && _relicDict.ContainsKey(id);
        public bool IsValidPotionID(string id) => !string.IsNullOrEmpty(id) && _potionDict.ContainsKey(id);

        public IReadOnlyDictionary<string, CardDataSO> GetAllCards() => _cardDict;
        public IReadOnlyDictionary<string, RelicDataSO> GetAllRelics() => _relicDict;
        public IReadOnlyDictionary<string, PotionDataSO> GetAllPotions() => _potionDict;
        public IReadOnlyDictionary<string, MonsterDataSO> GetAllMonsters() => _monsterDict;
        public IReadOnlyDictionary<string, EventDataSO> GetAllEvents() => _eventDict;

        // =====================================================================
        // 보상 화면 / 상점 / 스테이지 스폰 담당 팀원을 위한 조건부 필터링 & 랜덤 추출 API
        // =====================================================================

        /// <summary>
        /// 전투 보상이나 상점 진열용으로 중복 없는 랜덤 카드 목록을 반환합니다.
        /// 기본값으로 기본 카드(Starter), 특수(Special), 저주(Curse), 상태이상(Status) 카드는 보상 풀에서 자동 제외합니다.
        /// </summary>
        public List<CardDataSO> GetRandomCards(
            int count,
            ECardRarity? rarityFilter = null,
            bool excludeNonRewardCards = true)
        {
            var candidates = new List<CardDataSO>();
            foreach (var kvp in _cardDict)
            {
                var c = kvp.Value;
                if (c == null) continue;

                if (excludeNonRewardCards)
                {
                    if (c.Rarity == ECardRarity.Starter || c.Rarity == ECardRarity.Special || c.Rarity == ECardRarity.Curse)
                        continue;
                    if (c.CardType == ECardType.Status || c.CardType == ECardType.Curse)
                        continue;
                }

                if (rarityFilter.HasValue && c.Rarity != rarityFilter.Value)
                    continue;

                candidates.Add(c);
            }

            var picked = new List<CardDataSO>(Mathf.Min(count, candidates.Count));
            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                int idx = UnityEngine.Random.Range(0, candidates.Count);
                picked.Add(candidates[idx]);
                candidates.RemoveAt(idx);
            }
            return picked;
        }

        /// <summary>
        /// 이미 보유한 유물(ownedRelicIds)과 조합 전용 유물을 제외하고 미보유 유물 중 1개를 랜덤 추출합니다.
        /// </summary>
        public RelicDataSO GetRandomRelic(ERelicRarity? rarityFilter = null, ICollection<string> ownedRelicIds = null)
        {
            var candidates = new List<RelicDataSO>();
            foreach (var kvp in _relicDict)
            {
                var r = kvp.Value;
                if (r == null || r.IsRecipeExclusive || r.Rarity == ERelicRarity.Starter) continue;
                if (rarityFilter.HasValue && r.Rarity != rarityFilter.Value) continue;
                if (ownedRelicIds != null && ownedRelicIds.Contains(r.RelicID)) continue;
                candidates.Add(r);
            }

            if (candidates.Count == 0) return null;
            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        /// <summary>
        /// 현재 스테이지(1~3)와 몬스터 등급(Normal / Elite / Boss)에 부합하는 몬스터 데이터 목록을 반환합니다.
        /// </summary>
        public List<MonsterDataSO> GetMonstersByStage(int stage, EMonsterGrade? gradeFilter = null)
        {
            var result = new List<MonsterDataSO>();
            foreach (var kvp in _monsterDict)
            {
                var m = kvp.Value;
                if (m == null) continue;
                if (m.StageAppearance != stage) continue;
                if (gradeFilter.HasValue && m.Grade != gradeFilter.Value) continue;
                result.Add(m);
            }
            return result;
        }

        /// <summary>
        /// 현재 스테이지(MinStage ~ MaxStage)에서 등장 가능한 랜덤 이벤트 목록을 반환합니다.
        /// </summary>
        public List<EventDataSO> GetEventsByStage(int stage)
        {
            var result = new List<EventDataSO>();
            foreach (var kvp in _eventDict)
            {
                var e = kvp.Value;
                if (e == null) continue;
                if (stage >= e.MinStage && stage <= e.MaxStage)
                    result.Add(e);
            }
            return result;
        }
    }
}
