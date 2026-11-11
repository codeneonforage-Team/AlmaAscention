#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using CNF.AbilitySystem;

namespace CNF.Data.Editor
{
    /// <summary>
    /// 1) 언리얼 DataTable CSV 5종 비파괴 스마트 동기화기
    /// 2) CardDataSO 효과 블록 순서 변경(▲/▼) 및 개별 삭제(✕) 인스펙터
    /// 3) StatusController 플레이 모드 실시간 상태이상(Dictionary) 모니터 & 원클릭 디버그 버튼 인스펙터
    /// 4) 정적 캐시 기반 렉 없는 [StatusIdSelector] 드롭다운 PropertyDrawer
    /// </summary>
    public class CardCsvImporterEditor : EditorWindow
    {
        private TextAsset _cardCsv;
        private TextAsset _relicCsv;
        private TextAsset _potionCsv;
        private TextAsset _monsterCsv;
        private TextAsset _eventCsv;

        private string _baseOutputFolder = "Assets/_Project/Resources/GameData";

        private const string PrefKeyOutputFolder = "CNF_CsvPipe_OutputFolder";
        private const string PrefKeyCardCsv      = "CNF_CsvPipe_CardCsv";
        private const string PrefKeyRelicCsv     = "CNF_CsvPipe_RelicCsv";
        private const string PrefKeyPotionCsv    = "CNF_CsvPipe_PotionCsv";
        private const string PrefKeyMonsterCsv   = "CNF_CsvPipe_MonsterCsv";
        private const string PrefKeyEventCsv     = "CNF_CsvPipe_EventCsv";

        [MenuItem("CNF/Data Pipeline/CSV 통합 데이터 동기화 창 (Non-Destructive)")]
        public static void ShowWindow()
        {
            GetWindow<CardCsvImporterEditor>("CNF Data Pipeline");
        }

        private void OnEnable()
        {
            _baseOutputFolder = EditorPrefs.GetString(PrefKeyOutputFolder, "Assets/_Project/Resources/GameData");
            _cardCsv    = LoadSavedCsv(PrefKeyCardCsv);
            _relicCsv   = LoadSavedCsv(PrefKeyRelicCsv);
            _potionCsv  = LoadSavedCsv(PrefKeyPotionCsv);
            _monsterCsv = LoadSavedCsv(PrefKeyMonsterCsv);
            _eventCsv   = LoadSavedCsv(PrefKeyEventCsv);

            if (_cardCsv == null)
                _cardCsv = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/Scripts/Data/SampleCards.csv");
        }

        private static TextAsset LoadSavedCsv(string prefKey)
        {
            string path = EditorPrefs.GetString(prefKey, string.Empty);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        }

        private static void SaveCsvPref(string prefKey, TextAsset asset)
        {
            string path = asset != null ? AssetDatabase.GetAssetPath(asset) : string.Empty;
            EditorPrefs.SetString(prefKey, path);
        }

        private void OnGUI()
        {
            GUILayout.Label("언리얼 CSV ➔ Unity ScriptableObject 통합 동기화 파이프라인", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _baseOutputFolder = EditorGUILayout.TextField("기본 저장 경로", _baseOutputFolder);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(PrefKeyOutputFolder, _baseOutputFolder);

            EditorGUILayout.HelpBox(
                "• 이미 존재하는 ID의 에셋은 삭제하지 않고 수치만 덮어씁니다 (GUID 및 아이콘/프리팹 참조 100% 보존).\n" +
                "• 인스펙터에서 수동으로 추가해 둔 특수 카드 기믹 블록도 삭제되지 않고 그대로 보존됩니다.\n" +
                "• 한 번 지정한 CSV 파일은 에디터 창을 닫았다 열어도 자동 유지됩니다.",
                MessageType.Info);

            EditorGUILayout.Space(6);

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.35f, 0.85f, 0.55f);
            if (GUILayout.Button("⚡ 등록된 CSV 전체 일괄 동기화 (Sync All)", GUILayout.Height(30)))
            {
                if (_cardCsv != null) SyncCardsFromCsv(_cardCsv.text, $"{_baseOutputFolder}/Cards");
                if (_relicCsv != null) SyncRelicsFromCsv(_relicCsv.text, $"{_baseOutputFolder}/Relics");
                if (_potionCsv != null) SyncPotionsFromCsv(_potionCsv.text, $"{_baseOutputFolder}/Potions");
                if (_monsterCsv != null) SyncMonstersFromCsv(_monsterCsv.text, $"{_baseOutputFolder}/Monsters");
                if (_eventCsv != null) SyncEventsFromCsv(_eventCsv.text, $"{_baseOutputFolder}/Events");
            }

            GUI.backgroundColor = new Color(0.45f, 0.75f, 1.0f);
            if (GUILayout.Button("🔍 전체 SO 데이터 무결성 검사 (Validator)", GUILayout.Height(30)))
            {
                ValidateAllGeneratedData();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            EditorGUI.BeginChangeCheck();
            _cardCsv = (TextAsset)EditorGUILayout.ObjectField("1. 카드 CSV (Card)", _cardCsv, typeof(TextAsset), false);
            if (EditorGUI.EndChangeCheck()) SaveCsvPref(PrefKeyCardCsv, _cardCsv);
            if (GUILayout.Button("카드(CardDataSO) 스마트 동기화 실행") && _cardCsv != null)
                SyncCardsFromCsv(_cardCsv.text, $"{_baseOutputFolder}/Cards");

            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _relicCsv = (TextAsset)EditorGUILayout.ObjectField("2. 성유물 CSV (Relic)", _relicCsv, typeof(TextAsset), false);
            if (EditorGUI.EndChangeCheck()) SaveCsvPref(PrefKeyRelicCsv, _relicCsv);
            if (GUILayout.Button("성유물(RelicDataSO) 스마트 동기화 실행") && _relicCsv != null)
                SyncRelicsFromCsv(_relicCsv.text, $"{_baseOutputFolder}/Relics");

            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _potionCsv = (TextAsset)EditorGUILayout.ObjectField("3. 물약 CSV (Potion)", _potionCsv, typeof(TextAsset), false);
            if (EditorGUI.EndChangeCheck()) SaveCsvPref(PrefKeyPotionCsv, _potionCsv);
            if (GUILayout.Button("물약(PotionDataSO) 스마트 동기화 실행") && _potionCsv != null)
                SyncPotionsFromCsv(_potionCsv.text, $"{_baseOutputFolder}/Potions");

            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _monsterCsv = (TextAsset)EditorGUILayout.ObjectField("4. 몬스터 CSV (Monster)", _monsterCsv, typeof(TextAsset), false);
            if (EditorGUI.EndChangeCheck()) SaveCsvPref(PrefKeyMonsterCsv, _monsterCsv);
            if (GUILayout.Button("몬스터(MonsterDataSO) 스마트 동기화 실행") && _monsterCsv != null)
                SyncMonstersFromCsv(_monsterCsv.text, $"{_baseOutputFolder}/Monsters");

            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _eventCsv = (TextAsset)EditorGUILayout.ObjectField("5. 이벤트 CSV (Event)", _eventCsv, typeof(TextAsset), false);
            if (EditorGUI.EndChangeCheck()) SaveCsvPref(PrefKeyEventCsv, _eventCsv);
            if (GUILayout.Button("이벤트(EventDataSO) 스마트 동기화 실행") && _eventCsv != null)
                SyncEventsFromCsv(_eventCsv.text, $"{_baseOutputFolder}/Events");
        }

        public static void SyncCardsFromCsv(string csvText, string outputFolder)
        {
            EnsureFolder(outputFolder);
            var rows = ParseCsvRows(csvText);
            int created = 0, updated = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                string id = GetId(r, "CardID");
                if (string.IsNullOrEmpty(id)) continue;

                string path = $"{outputFolder}/Card_{id}.asset";
                var so = LoadOrCreate<CardDataSO>(path, out bool isNew);

                so.CardID = id;
                so.CardName = GetVal(r, "CardName", id);
                so.UpgradedCardName = GetVal(r, "UpgradedCardName", $"{so.CardName}+");
                so.Description = GetVal(r, "Description", so.Description);
                so.UpgradedDescription = GetVal(r, "UpgradedDescription", so.UpgradedDescription);

                if (Enum.TryParse(GetVal(r, "CardType", "Attack"), true, out ECardType cType)) so.CardType = cType;
                if (Enum.TryParse(GetVal(r, "Rarity", "Common"), true, out ECardRarity rarity)) so.Rarity = rarity;
                if (Enum.TryParse(GetVal(r, "TargetType", "SingleEnemy"), true, out ECardTarget tType)) so.TargetType = tType;

                so.BaseCost = GetInt(r, "BaseCost", so.BaseCost);
                so.UpgradedCost = GetInt(r, "UpgradedCost", so.BaseCost);
                so.IsXCost = GetBool(r, "IsXCost", so.BaseCost < 0 || so.IsXCost);
                so.IsExhaust = GetBool(r, "IsExhaust", so.IsExhaust);
                so.IsUpgradedExhaust = GetBool(r, "IsUpgradedExhaust", so.IsExhaust);
                so.IsEthereal = GetBool(r, "IsEthereal", so.IsEthereal);
                so.IsRetain = GetBool(r, "IsRetain", so.IsRetain);
                so.IsInnate = GetBool(r, "IsInnate", so.IsInnate);

                int baseDmg = GetInt(r, "BaseDamage", 0);
                int upgDmg = GetInt(r, "UpgradedDamage", baseDmg);
                if (baseDmg > 0 || upgDmg > 0)
                {
                    var dmgEff = GetOrAddEffect<DealDamageEffect>(so.Effects);
                    dmgEff.BaseDamage = baseDmg;
                    dmgEff.UpgradedDamage = upgDmg;
                    dmgEff.HitCount = Mathf.Max(1, GetInt(r, "HitCount", 1));
                    dmgEff.OverrideTarget = so.TargetType;
                }

                int baseBlk = GetInt(r, "BaseBlock", 0);
                int upgBlk = GetInt(r, "UpgradedBlock", baseBlk);
                if (baseBlk > 0 || upgBlk > 0)
                {
                    var blkEff = GetOrAddEffect<GainBlockEffect>(so.Effects);
                    blkEff.BaseBlock = baseBlk;
                    blkEff.UpgradedBlock = upgBlk;
                }

                string statusId = GetVal(r, "StatusID", "");
                int baseStacks = GetInt(r, "BaseStatusStacks", 0);
                int upgStacks = GetInt(r, "UpgradedStatusStacks", baseStacks);
                // 양수뿐 아니라 음수 디버프(예: 적의 힘 -2 감소)도 정상 임포트
                if (!string.IsNullOrEmpty(statusId) && (baseStacks != 0 || upgStacks != 0))
                {
                    var stEff = GetOrAddEffect<ApplyStatusEffect>(so.Effects);
                    stEff.StatusID = statusId;
                    stEff.BaseStacks = baseStacks;
                    stEff.UpgradedStacks = upgStacks;
                    stEff.ApplyTo = so.TargetType;
                }

                int draw = GetInt(r, "DrawCount", 0);
                int mana = GetInt(r, "ManaGain", 0);
                if (draw > 0 || mana > 0)
                {
                    var dmEff = GetOrAddEffect<DrawAndManaEffect>(so.Effects);
                    dmEff.BaseDraw = draw;
                    dmEff.UpgradedDraw = GetInt(r, "UpgradedDrawCount", draw);
                    dmEff.BaseManaGain = mana;
                    dmEff.UpgradedManaGain = GetInt(r, "UpgradedManaGain", mana);
                }

                int heal = GetInt(r, "HealAmount", 0);
                int selfDmg = GetInt(r, "SelfDamage", 0);
                int hpDelta = heal != 0 ? heal : -selfDmg;
                if (hpDelta != 0)
                {
                    var hpEff = GetOrAddEffect<ModifyHPEffect>(so.Effects);
                    hpEff.BaseHPDelta = hpDelta;
                    hpEff.UpgradedHPDelta = GetInt(r, "UpgradedHPDelta", hpDelta);
                    hpEff.ApplyTo = ECardTarget.Self;
                }

                string specialKey = GetVal(r, "SpecialEffect", GetVal(r, "SpecialActionKey", ""));
                if (!string.IsNullOrEmpty(specialKey))
                {
                    if (specialKey.Equals("BodySlam", StringComparison.OrdinalIgnoreCase))
                        GetOrAddEffect<BodySlamDamageEffect>(so.Effects);
                    else if (specialKey.Equals("Reaper", StringComparison.OrdinalIgnoreCase))
                        GetOrAddEffect<VampireAoEDamageEffect>(so.Effects);
                    else if (specialKey.Equals("FiendFire", StringComparison.OrdinalIgnoreCase))
                        GetOrAddEffect<ExhaustHandDamageEffect>(so.Effects);
                    else if (specialKey.Equals("Entrench", StringComparison.OrdinalIgnoreCase))
                        GetOrAddEffect<MultiplyBlockEffect>(so.Effects);
                    else if (specialKey.Equals("SpotWeakness", StringComparison.OrdinalIgnoreCase))
                        GetOrAddEffect<SpotWeaknessEffect>(so.Effects);
                    else if (specialKey.Equals("Armaments", StringComparison.OrdinalIgnoreCase) ||
                             specialKey.Equals("UpgradeHand", StringComparison.OrdinalIgnoreCase))
                        GetOrAddEffect<UpgradeHandEffect>(so.Effects);
                }

                SyncExtraParams(so, r, _knownCardCols);
                SaveOrMarkDirty(so, path, isNew, ref created, ref updated);
            }
            FinalizeSync("CardDataSO", created, updated);
        }

        private static readonly HashSet<string> _knownCardCols = new(StringComparer.OrdinalIgnoreCase)
        {
            "RowName", "Name", "ID", "CardID", "CardName", "UpgradedCardName", "Description", "UpgradedDescription",
            "CardType", "Rarity", "TargetType", "BaseCost", "UpgradedCost", "IsXCost",
            "IsExhaust", "IsUpgradedExhaust", "IsEthereal", "IsRetain", "IsInnate",
            "BaseDamage", "UpgradedDamage", "HitCount", "BaseBlock", "UpgradedBlock",
            "StatusID", "BaseStatusStacks", "UpgradedStatusStacks",
            "DrawCount", "UpgradedDrawCount", "ManaGain", "UpgradedManaGain",
            "HealAmount", "SelfDamage", "UpgradedHPDelta", "SpecialEffect", "SpecialActionKey"
        };

        private static readonly HashSet<string> _knownRelicCols = new(StringComparer.OrdinalIgnoreCase)
        {
            "RowName", "Name", "ID", "RelicID", "RelicName", "Description", "FlavorText",
            "Rarity", "TriggerType", "ShopPrice", "EffectKey", "EffectValue", "StatusID", "StatusStacks",
            "AcquireMaxHealthBonus", "AcquireHealBonus", "AcquireGoldBonus",
            "IsTimedRelic", "TimedDurationFloors", "IsRecipeExclusive", "FusionIngredients", "FusionIngredientIDs"
        };

        private static readonly HashSet<string> _knownPotionCols = new(StringComparer.OrdinalIgnoreCase)
        {
            "RowName", "Name", "ID", "PotionID", "PotionName", "Description",
            "Rarity", "TargetType", "EffectType", "ShopPrice", "EffectValue", "StatusID", "StatusStacks"
        };

        private static readonly HashSet<string> _knownMonsterCols = new(StringComparer.OrdinalIgnoreCase)
        {
            "RowName", "Name", "ID", "MonsterID", "MonsterName", "Grade", "StageAppearance", "MinHP", "MaxHP", "InitialBlock"
        };

        private static readonly HashSet<string> _knownEventCols = new(StringComparer.OrdinalIgnoreCase)
        {
            "RowName", "Name", "ID", "EventID", "EventTitle", "Title", "EventBodyText", "BodyText", "MinStage", "MaxStage"
        };

        /// <summary>
        /// 이미 SO의 정식 변수로 매핑된 기본 컬럼(knownCols)은 제외하고,
        /// 정말로 새로 추가된 확장 컬럼만 CustomParams에 저장합니다 (기존 중복 저장된 기본 컬럼도 자동 청소).
        /// </summary>
        private static void SyncExtraParams(CustomParamContainerSO so, Dictionary<string, string> row, HashSet<string> knownCols)
        {
            foreach (var kvp in row)
            {
                string key = kvp.Key;
                if (knownCols.Contains(key) ||
                    key.StartsWith("Pattern", StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith("Choice", StringComparison.OrdinalIgnoreCase))
                {
                    so.RemoveParam(key);
                }
                else
                {
                    so.SetOrUpdateParam(key, kvp.Value);
                }
            }
        }

        private static T GetOrAddEffect<T>(List<ICardEffect> list) where T : class, ICardEffect, new()
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is T existing)
                    return existing;
            }
            var created = new T();
            list.Add(created);
            return created;
        }

        public static void SyncRelicsFromCsv(string csvText, string outputFolder)
        {
            EnsureFolder(outputFolder);
            var rows = ParseCsvRows(csvText);
            int created = 0, updated = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                string id = GetId(r, "RelicID");
                if (string.IsNullOrEmpty(id)) continue;

                string path = $"{outputFolder}/Relic_{id}.asset";
                var so = LoadOrCreate<RelicDataSO>(path, out bool isNew);

                so.RelicID = id;
                so.RelicName = GetVal(r, "RelicName", id);
                so.Description = GetVal(r, "Description", so.Description);
                so.FlavorText = GetVal(r, "FlavorText", so.FlavorText);
                if (Enum.TryParse(GetVal(r, "Rarity", "Common"), true, out ERelicRarity rarity)) so.Rarity = rarity;
                if (Enum.TryParse(GetVal(r, "TriggerType", "Passive"), true, out ERelicTriggerType trig)) so.TriggerType = trig;

                so.ShopPrice = GetInt(r, "ShopPrice", so.ShopPrice);
                so.EffectKey = GetVal(r, "EffectKey", so.EffectKey);
                so.EffectValue = GetInt(r, "EffectValue", so.EffectValue);
                so.StatusID = GetVal(r, "StatusID", so.StatusID);
                so.StatusStacks = GetInt(r, "StatusStacks", so.StatusStacks);
                so.AcquireMaxHealthBonus = GetInt(r, "AcquireMaxHealthBonus", so.AcquireMaxHealthBonus);
                so.AcquireHealBonus = GetInt(r, "AcquireHealBonus", so.AcquireHealBonus);
                so.AcquireGoldBonus = GetInt(r, "AcquireGoldBonus", so.AcquireGoldBonus);
                so.IsTimedRelic = GetBool(r, "IsTimedRelic", so.IsTimedRelic);
                so.TimedDurationFloors = GetInt(r, "TimedDurationFloors", so.TimedDurationFloors);
                so.IsRecipeExclusive = GetBool(r, "IsRecipeExclusive", so.IsRecipeExclusive);

                string rawIngredients = GetVal(r, "FusionIngredients", GetVal(r, "FusionIngredientIDs", ""));
                if (!string.IsNullOrEmpty(rawIngredients))
                {
                    so.FusionIngredientIDs.Clear();
                    string[] parts = rawIngredients.Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int p = 0; p < parts.Length; p++)
                    {
                        string trimmed = parts[p].Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                            so.FusionIngredientIDs.Add(trimmed);
                    }
                }

                SyncExtraParams(so, r, _knownRelicCols);
                SaveOrMarkDirty(so, path, isNew, ref created, ref updated);
            }
            FinalizeSync("RelicDataSO", created, updated);
        }

        public static void SyncPotionsFromCsv(string csvText, string outputFolder)
        {
            EnsureFolder(outputFolder);
            var rows = ParseCsvRows(csvText);
            int created = 0, updated = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                string id = GetId(r, "PotionID");
                if (string.IsNullOrEmpty(id)) continue;

                string path = $"{outputFolder}/Potion_{id}.asset";
                var so = LoadOrCreate<PotionDataSO>(path, out bool isNew);

                so.PotionID = id;
                so.PotionName = GetVal(r, "PotionName", id);
                so.Description = GetVal(r, "Description", so.Description);
                if (Enum.TryParse(GetVal(r, "Rarity", "Common"), true, out EPotionRarity rarity)) so.Rarity = rarity;
                if (Enum.TryParse(GetVal(r, "TargetType", "Self"), true, out ECardTarget target)) so.TargetType = target;
                if (Enum.TryParse(GetVal(r, "EffectType", "Heal"), true, out EPotionEffectType effType)) so.EffectType = effType;

                so.ShopPrice = GetInt(r, "ShopPrice", so.ShopPrice);
                so.EffectValue = GetInt(r, "EffectValue", so.EffectValue);
                so.StatusID = GetVal(r, "StatusID", so.StatusID);
                so.StatusStacks = GetInt(r, "StatusStacks", so.StatusStacks);

                SyncExtraParams(so, r, _knownPotionCols);
                SaveOrMarkDirty(so, path, isNew, ref created, ref updated);
            }
            FinalizeSync("PotionDataSO", created, updated);
        }

        public static void SyncMonstersFromCsv(string csvText, string outputFolder)
        {
            EnsureFolder(outputFolder);
            var rows = ParseCsvRows(csvText);
            int created = 0, updated = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                string id = GetId(r, "MonsterID");
                if (string.IsNullOrEmpty(id)) continue;

                string path = $"{outputFolder}/Monster_{id}.asset";
                var so = LoadOrCreate<MonsterDataSO>(path, out bool isNew);

                so.MonsterID = id;
                so.MonsterName = GetVal(r, "MonsterName", id);
                if (Enum.TryParse(GetVal(r, "Grade", "Normal"), true, out EMonsterGrade grade)) so.Grade = grade;
                so.StageAppearance = GetInt(r, "StageAppearance", so.StageAppearance);
                so.MinHP = GetInt(r, "MinHP", GetInt(r, "MaxHP", so.MinHP));
                so.MaxHP = GetInt(r, "MaxHP", so.MaxHP);
                so.InitialBlock = GetInt(r, "InitialBlock", so.InitialBlock);

                // CSV에 Pattern1~4 컬럼이 존재할 경우 비파괴 방식으로 AI 행동 패턴 자동 매핑
                for (int pIdx = 1; pIdx <= 4; pIdx++)
                {
                    string prefix = $"Pattern{pIdx}_";
                    string pName = GetVal(r, $"{prefix}Name", "");
                    string pIntentStr = GetVal(r, $"{prefix}Intent", "");
                    int pDmg = GetInt(r, $"{prefix}Damage", -1);

                    if (string.IsNullOrEmpty(pName) && string.IsNullOrEmpty(pIntentStr) && pDmg < 0)
                        continue;

                    while (so.Patterns.Count < pIdx)
                        so.Patterns.Add(new MonsterSkillPatternData { PatternID = $"P{so.Patterns.Count + 1}" });

                    var pat = so.Patterns[pIdx - 1];
                    if (string.IsNullOrEmpty(pat.PatternID)) pat.PatternID = $"P{pIdx}";
                    if (!string.IsNullOrEmpty(pName)) pat.PatternName = pName;
                    if (Enum.TryParse(pIntentStr, true, out EMonsterIntentType intentType)) pat.IntentType = intentType;
                    if (pDmg >= 0) pat.BaseDamage = pDmg;
                    pat.HitCount = GetInt(r, $"{prefix}HitCount", Mathf.Max(1, pat.HitCount));
                    pat.BlockGain = GetInt(r, $"{prefix}Block", pat.BlockGain);
                    pat.InflictStatusID = GetVal(r, $"{prefix}StatusID", pat.InflictStatusID);
                    pat.StatusStacks = GetInt(r, $"{prefix}StatusStacks", pat.StatusStacks);
                    pat.ApplyStatusToSelf = GetBool(r, $"{prefix}ApplyToSelf", GetBool(r, $"{prefix}ApplyStatusToSelf", pat.ApplyStatusToSelf));
                    pat.SummonMonsterID = GetVal(r, $"{prefix}SummonMonsterID", pat.SummonMonsterID);
                    pat.SelectionWeight = GetFloat(r, $"{prefix}Weight", GetFloat(r, $"{prefix}SelectionWeight", pat.SelectionWeight));
                    pat.CooldownTurns = GetInt(r, $"{prefix}Cooldown", GetInt(r, $"{prefix}CooldownTurns", pat.CooldownTurns));
                    pat.MaxConsecutiveUses = GetInt(r, $"{prefix}MaxConsecutive", GetInt(r, $"{prefix}MaxConsecutiveUses", pat.MaxConsecutiveUses));
                    pat.TurnModulo = GetInt(r, $"{prefix}TurnModulo", pat.TurnModulo);
                    pat.MaxHPRatioCondition = GetFloat(r, $"{prefix}MaxHPRatio", GetFloat(r, $"{prefix}MaxHPRatioCondition", pat.MaxHPRatioCondition));
                    pat.IsSpecialPattern = GetBool(r, $"{prefix}IsSpecial", GetBool(r, $"{prefix}IsSpecialPattern", pat.IsSpecialPattern));
                    pat.SpecialPriority = GetInt(r, $"{prefix}SpecialPriority", pat.SpecialPriority);
                    pat.AnimTriggerName = GetVal(r, $"{prefix}AnimTrigger", GetVal(r, $"{prefix}AnimTriggerName", pat.AnimTriggerName));
                }

                SyncExtraParams(so, r, _knownMonsterCols);
                SaveOrMarkDirty(so, path, isNew, ref created, ref updated);
            }
            FinalizeSync("MonsterDataSO", created, updated);
        }

        public static void SyncEventsFromCsv(string csvText, string outputFolder)
        {
            EnsureFolder(outputFolder);
            var rows = ParseCsvRows(csvText);
            int created = 0, updated = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                string id = GetId(r, "EventID");
                if (string.IsNullOrEmpty(id)) continue;

                string path = $"{outputFolder}/Event_{id}.asset";
                var so = LoadOrCreate<EventDataSO>(path, out bool isNew);

                so.EventID = id;
                so.EventTitle = GetVal(r, "EventTitle", GetVal(r, "Title", id));
                so.EventBodyText = GetVal(r, "EventBodyText", GetVal(r, "BodyText", so.EventBodyText));
                so.MinStage = GetInt(r, "MinStage", so.MinStage);
                so.MaxStage = GetInt(r, "MaxStage", so.MaxStage);

                // CSV에 Choice1~4 컬럼이 존재할 경우 비파괴 방식으로 이벤트 선택지 자동 매핑
                for (int cIdx = 1; cIdx <= 4; cIdx++)
                {
                    string prefix = $"Choice{cIdx}_";
                    string cText = GetVal(r, $"{prefix}Text", "");
                    if (string.IsNullOrEmpty(cText)) continue;

                    while (so.Choices.Count < cIdx)
                        so.Choices.Add(new EventChoiceData());

                    var ch = so.Choices[cIdx - 1];
                    ch.ChoiceText = cText;
                    ch.ResultDescription = GetVal(r, $"{prefix}Result", ch.ResultDescription);
                    ch.HPDelta = GetInt(r, $"{prefix}HPDelta", ch.HPDelta);
                    ch.MaxHPDelta = GetInt(r, $"{prefix}MaxHPDelta", ch.MaxHPDelta);
                    ch.GoldDelta = GetInt(r, $"{prefix}GoldDelta", ch.GoldDelta);
                    ch.RewardRelicID = GetVal(r, $"{prefix}RewardRelicID", ch.RewardRelicID);
                    ch.RewardCardID = GetVal(r, $"{prefix}RewardCardID", ch.RewardCardID);
                }

                SyncExtraParams(so, r, _knownEventCols);
                SaveOrMarkDirty(so, path, isNew, ref created, ref updated);
            }
            FinalizeSync("EventDataSO", created, updated);
        }

        private static void EnsureFolder(string folder)
        {
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
        }

        private static T LoadOrCreate<T>(string assetPath, out bool isNew) where T : ScriptableObject
        {
            T so = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (so == null)
            {
                so = CreateInstance<T>();
                isNew = true;
            }
            else
            {
                isNew = false;
            }
            return so;
        }

        private static void SaveOrMarkDirty(ScriptableObject so, string path, bool isNew, ref int created, ref int updated)
        {
            if (isNew)
            {
                AssetDatabase.CreateAsset(so, path);
                created++;
            }
            else
            {
                EditorUtility.SetDirty(so);
                updated++;
            }
        }

        private static void FinalizeSync(string label, int created, int updated)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CNF Data Pipeline - {label}] 신규 생성: {created}개, 기존 업데이트: {updated}개 완료!");
        }

        /// <summary>
        /// 생성된 모든 CardDataSO의 설명 플레이스홀더({D}, {D2}, {B}, {M}, {M2}, {H})와 실제 효과 블록 일치 여부 및 StatusID 오타를 검사합니다.
        /// </summary>
        public static void ValidateAllGeneratedData()
        {
            string[] cardGuids = AssetDatabase.FindAssets("t:CardDataSO");
            var validStatusKeys = new HashSet<string>(StatusKeys.AllBuiltInKeys, StringComparer.OrdinalIgnoreCase);

            string[] statusSoGuids = AssetDatabase.FindAssets("t:StatusBehaviorSO");
            for (int i = 0; i < statusSoGuids.Length; i++)
            {
                var stSo = AssetDatabase.LoadAssetAtPath<StatusBehaviorSO>(AssetDatabase.GUIDToAssetPath(statusSoGuids[i]));
                if (stSo != null && !string.IsNullOrEmpty(stSo.StatusID))
                    validStatusKeys.Add(stSo.StatusID);
            }

            int warningCount = 0;
            var providedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < cardGuids.Length; i++)
            {
                var card = AssetDatabase.LoadAssetAtPath<CardDataSO>(AssetDatabase.GUIDToAssetPath(cardGuids[i]));
                if (card == null) continue;

                providedTags.Clear();
                if (card.Effects != null)
                {
                    for (int e = 0; e < card.Effects.Count; e++)
                    {
                        if (card.Effects[e] is CardEffectBase effBase)
                        {
                            string tag = effBase.GetAssignedPlaceholderTag();
                            if (!string.IsNullOrEmpty(tag))
                                providedTags.Add(tag);
                        }

                        if (card.Effects[e] is ApplyStatusEffect applySt &&
                            !string.IsNullOrEmpty(applySt.StatusID) &&
                            !validStatusKeys.Contains(applySt.StatusID))
                        {
                            Debug.LogWarning($"[데이터 검증 경고] 카드 '{card.CardID}' ({card.CardName})에 등록되지 않은 StatusID '{applySt.StatusID}'가 지정되어 있습니다.", card);
                            warningCount++;
                        }
                    }
                }

                string combinedDesc = $"{card.Description} {card.UpgradedDescription}";
                int openIdx = 0;
                while ((openIdx = combinedDesc.IndexOf('{', openIdx)) >= 0)
                {
                    int closeIdx = combinedDesc.IndexOf('}', openIdx + 1);
                    if (closeIdx < 0) break;

                    string token = combinedDesc.Substring(openIdx, closeIdx - openIdx + 1);
                    if (!providedTags.Contains(token))
                    {
                        Debug.LogWarning($"[데이터 검증 경고] 카드 '{card.CardID}' ({card.CardName}) 설명에 '{token}' 태그가 있지만 이를 치환할 효과 블록이 없습니다.", card);
                        warningCount++;
                    }
                    openIdx = closeIdx + 1;
                }
            }

            string[] monsterGuids = AssetDatabase.FindAssets("t:MonsterDataSO");
            for (int i = 0; i < monsterGuids.Length; i++)
            {
                var monster = AssetDatabase.LoadAssetAtPath<MonsterDataSO>(AssetDatabase.GUIDToAssetPath(monsterGuids[i]));
                if (monster == null) continue;

                var phases = monster.GetEffectivePhases();
                if (phases == null || phases.Count == 0)
                {
                    Debug.LogWarning($"[데이터 검증 경고] 몬스터 '{monster.MonsterID}' ({monster.MonsterName})에 유효한 페이즈가 없습니다.", monster);
                    warningCount++;
                    continue;
                }

                for (int p = 0; p < phases.Count; p++)
                {
                    var phase = phases[p];
                    if (phase == null || phase.Patterns == null || phase.Patterns.Count == 0)
                    {
                        Debug.LogWarning($"[데이터 검증 경고] 몬스터 '{monster.MonsterID}' ({monster.MonsterName})의 페이즈 #{p}에 등록된 행동 패턴이 없습니다.", monster);
                        warningCount++;
                        continue;
                    }

                    for (int k = 0; k < phase.Patterns.Count; k++)
                    {
                        var pat = phase.Patterns[k];
                        if (pat != null && !string.IsNullOrEmpty(pat.InflictStatusID) && !validStatusKeys.Contains(pat.InflictStatusID))
                        {
                            Debug.LogWarning($"[데이터 검증 경고] 몬스터 '{monster.MonsterID}' 패턴 '{pat.PatternName}'에 등록되지 않은 StatusID '{pat.InflictStatusID}'가 지정되어 있습니다.", monster);
                            warningCount++;
                        }
                    }
                }
            }

            if (warningCount == 0)
                Debug.Log($"<color=#44FF66>[CNF 데이터 검증 완료]</color> 카드 {cardGuids.Length}개 및 몬스터 {monsterGuids.Length}개 에셋에서 이상이 발견되지 않았습니다!");
            else
                Debug.LogWarning($"[CNF 데이터 검증 완료] 총 {warningCount}건의 확인 필요 항목이 발견되었습니다. (콘솔 메시지 클릭 시 해당 SO로 이동)");
        }

        /// <summary>
        /// RFC-4180 호환 상태 머신 CSV 파서.
        /// 엑셀/구글시트 셀 내부의 줄바꿈(\n) 및 이스케이프 따옴표("")가 있어도 행이 절대 깨지지 않습니다.
        /// </summary>
        private static List<Dictionary<string, string>> ParseCsvRows(string csvText)
        {
            var result = new List<Dictionary<string, string>>();
            var allRecords = ParseRawCsvRecords(csvText);

            if (allRecords.Count < 2)
                return result;

            var headers = allRecords[0];
            for (int r = 1; r < allRecords.Count; r++)
            {
                var cols = allRecords[r];
                if (cols.Count == 0 || (cols.Count == 1 && string.IsNullOrWhiteSpace(cols[0])))
                    continue;

                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < Mathf.Min(headers.Count, cols.Count); c++)
                {
                    string h = headers[c].Trim().Trim('\uFEFF');
                    if (!string.IsNullOrEmpty(h))
                        dict[h] = cols[c].Trim().Trim('\uFEFF');
                }
                result.Add(dict);
            }
            return result;
        }

        private static List<List<string>> ParseRawCsvRecords(string csvText)
        {
            var records = new List<List<string>>();
            if (string.IsNullOrEmpty(csvText)) return records;

            var currentRecord = new List<string>();
            var cellBuilder = new StringBuilder(128);
            bool inQuotes = false;

            for (int i = 0; i < csvText.Length; i++)
            {
                char c = csvText[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < csvText.Length && csvText[i + 1] == '"')
                        {
                            cellBuilder.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        cellBuilder.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == ',')
                    {
                        currentRecord.Add(cellBuilder.ToString());
                        cellBuilder.Clear();
                    }
                    else if (c == '\r' || c == '\n')
                    {
                        if (c == '\r' && i + 1 < csvText.Length && csvText[i + 1] == '\n')
                            i++;

                        currentRecord.Add(cellBuilder.ToString());
                        cellBuilder.Clear();

                        if (currentRecord.Count > 1 || !string.IsNullOrWhiteSpace(currentRecord[0]))
                            records.Add(currentRecord);

                        currentRecord = new List<string>();
                    }
                    else
                    {
                        cellBuilder.Append(c);
                    }
                }
            }

            if (cellBuilder.Length > 0 || currentRecord.Count > 0)
            {
                currentRecord.Add(cellBuilder.ToString());
                if (currentRecord.Count > 1 || !string.IsNullOrWhiteSpace(currentRecord[0]))
                    records.Add(currentRecord);
            }

            return records;
        }

        private static string GetId(Dictionary<string, string> d, string primaryKey)
            => GetVal(d, primaryKey, GetVal(d, "RowName", GetVal(d, "Name", GetVal(d, "ID", ""))));

        private static string GetVal(Dictionary<string, string> dict, string key, string fallback)
            => dict.TryGetValue(key, out string val) && !string.IsNullOrEmpty(val) ? val : fallback;

        private static int GetInt(Dictionary<string, string> dict, string key, int fallback)
            => dict.TryGetValue(key, out string val) && int.TryParse(val, out int res) ? res : fallback;

        private static float GetFloat(Dictionary<string, string> dict, string key, float fallback)
            => dict.TryGetValue(key, out string val) && float.TryParse(val, out float res) ? res : fallback;

        private static bool GetBool(Dictionary<string, string> dict, string key, bool fallback)
            => dict.TryGetValue(key, out string val) && bool.TryParse(val, out bool res) ? res : fallback;
    }

    /// <summary>
    /// CardDataSO 인스펙터에서 조립된 효과 블록의 구체 클래스 이름을 보여주고,
    /// 순서 이동(▲/▼), 개별 삭제(✕), 새 블록 추가 드롭다운을 제공하는 커스텀 에디터.
    /// </summary>
    [CustomEditor(typeof(CardDataSO))]
    public class CardDataSOEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            CardDataSO card = (CardDataSO)target;
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("조립된 효과 블록 빠른 관리", EditorStyles.boldLabel);

            if (card.Effects != null && card.Effects.Count > 0)
            {
                for (int i = 0; i < card.Effects.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    string typeName = card.Effects[i] != null ? card.Effects[i].GetType().Name : "(Null Effect)";
                    EditorGUILayout.LabelField($"[{i}] {typeName}", EditorStyles.miniBoldLabel);

                    GUI.enabled = i > 0;
                    if (GUILayout.Button("▲", GUILayout.Width(28)))
                    {
                        Undo.RecordObject(card, "Move Effect Up");
                        (card.Effects[i - 1], card.Effects[i]) = (card.Effects[i], card.Effects[i - 1]);
                        EditorUtility.SetDirty(card);
                    }

                    GUI.enabled = i < card.Effects.Count - 1;
                    if (GUILayout.Button("▼", GUILayout.Width(28)))
                    {
                        Undo.RecordObject(card, "Move Effect Down");
                        (card.Effects[i + 1], card.Effects[i]) = (card.Effects[i], card.Effects[i + 1]);
                        EditorUtility.SetDirty(card);
                    }

                    GUI.enabled = true;
                    if (GUILayout.Button("✕", GUILayout.Width(28)))
                    {
                        Undo.RecordObject(card, "Remove Card Effect");
                        card.Effects.RemoveAt(i);
                        EditorUtility.SetDirty(card);
                        EditorGUILayout.EndHorizontal();
                        break;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.Space(4);
            if (GUILayout.Button("+ 카드 효과 블록 추가 (Add ICardEffect Module)", GUILayout.Height(30)))
            {
                GenericMenu menu = new GenericMenu();
                var effectTypes = TypeCache.GetTypesDerivedFrom<ICardEffect>();
                foreach (Type t in effectTypes)
                {
                    if (t.IsAbstract || t.IsInterface) continue;
                    menu.AddItem(new GUIContent(t.Name), false, () =>
                    {
                        Undo.RecordObject(card, "Add Card Effect");
                        card.Effects.Add((ICardEffect)Activator.CreateInstance(t));
                        EditorUtility.SetDirty(card);
                    });
                }
                menu.ShowAsContext();
            }
        }
    }

    /// <summary>
    /// 플레이 모드 중 StatusController의 Dictionary 상태이상 목록을 인스펙터에 실시간으로 표시하고
    /// 원클릭 테스트 버튼을 제공하는 커스텀 인스펙터.
    /// </summary>
    [CustomEditor(typeof(StatusController))]
    public class StatusControllerEditor : UnityEditor.Editor
    {
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            StatusController ctrl = (StatusController)target;
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("실시간 상태이상(Dictionary) 모니터", EditorStyles.boldLabel);

            var statuses = ctrl.GetAllStatuses();
            if (statuses == null || statuses.Count == 0)
            {
                EditorGUILayout.HelpBox("현재 적용된 상태이상(버프/디버프)이 없습니다.", MessageType.None);
            }
            else
            {
                var richStyle = new GUIStyle(EditorStyles.boldLabel) { richText = true };
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                foreach (var kvp in statuses)
                {
                    EditorGUILayout.BeginHorizontal();
                    bool isDebuff = StatusKeys.IsBuiltInDebuff(kvp.Key, kvp.Value) || kvp.Value < 0;
                    string colorHex = isDebuff ? "#FF6666" : "#55FF77";
                    EditorGUILayout.LabelField($"<color={colorHex}>● {kvp.Key}</color>", richStyle);
                    EditorGUILayout.LabelField($"{kvp.Value} 스택", GUILayout.Width(65));

                    if (Application.isPlaying)
                    {
                        if (GUILayout.Button("+1", GUILayout.Width(28)))
                        {
                            ctrl.AddStatus(kvp.Key, 1);
                            break;
                        }
                        if (GUILayout.Button("-1", GUILayout.Width(28)))
                        {
                            ctrl.AddStatus(kvp.Key, -1);
                            break;
                        }
                        if (GUILayout.Button("✕", GUILayout.Width(24)))
                        {
                            ctrl.SetStatus(kvp.Key, 0);
                            break;
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("플레이 모드 빠른 테스트 버튼", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("피해 10")) ctrl.TakeDamage(10);
                if (GUILayout.Button("회복 10")) ctrl.Heal(10);
                if (GUILayout.Button("방어도 +8")) ctrl.AddBlock(8);
                if (GUILayout.Button("+인공물 1")) ctrl.AddStatus(StatusKeys.Artifact, 1);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("+힘 2")) ctrl.AddStatus(StatusKeys.Strength, 2);
                if (GUILayout.Button("힘 -2")) ctrl.AddStatus(StatusKeys.Strength, -2);
                if (GUILayout.Button("+약화 1")) ctrl.AddStatus(StatusKeys.Weak, 1);
                if (GUILayout.Button("+취약 1")) ctrl.AddStatus(StatusKeys.Vulnerable, 1);
                if (GUILayout.Button("+손상 1")) ctrl.AddStatus(StatusKeys.Frail, 1);
                if (GUILayout.Button("+감전 1")) ctrl.AddStatus(StatusKeys.Shock, 1);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("턴 시작 처리")) ctrl.ProcessTurnStartStatuses();
                if (GUILayout.Button("턴 종료 처리")) ctrl.ProcessTurnEndStatuses();
                if (GUILayout.Button("버프 전체 초기화")) ctrl.ClearBattleStatuses();
                EditorGUILayout.EndHorizontal();
            }
        }
    }

    [CustomPropertyDrawer(typeof(StatusIdSelectorAttribute))]
    public class StatusIdSelectorDrawer : PropertyDrawer
    {
        private static List<string> _cachedOptions;

        public static void InvalidateCache() => _cachedOptions = null;

        private static List<string> GetCachedOptions()
        {
            if (_cachedOptions != null) return _cachedOptions;

            _cachedOptions = new List<string> { "(None)" };
            _cachedOptions.AddRange(StatusKeys.AllBuiltInKeys);

            string[] guids = AssetDatabase.FindAssets("t:StatusBehaviorSO");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var so = AssetDatabase.LoadAssetAtPath<StatusBehaviorSO>(path);
                if (so != null && !string.IsNullOrEmpty(so.StatusID) && !_cachedOptions.Contains(so.StatusID))
                    _cachedOptions.Add(so.StatusID);
            }
            return _cachedOptions;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var baseOptions = GetCachedOptions();
            var options = new List<string>(baseOptions);

            string currentVal = property.stringValue;
            int selectedIndex = 0;
            if (!string.IsNullOrEmpty(currentVal))
            {
                selectedIndex = options.IndexOf(currentVal);
                if (selectedIndex < 0)
                {
                    options.Add(currentVal);
                    selectedIndex = options.Count - 1;
                }
            }

            float dropdownWidth = position.width * 0.68f;
            float textWidth = position.width - dropdownWidth - 4f;

            Rect dropRect = new Rect(position.x, position.y, dropdownWidth, position.height);
            Rect textRect = new Rect(position.x + dropdownWidth + 4f, position.y, textWidth, position.height);

            int newIndex = EditorGUI.Popup(dropRect, label.text, selectedIndex, options.ToArray());
            if (newIndex != selectedIndex)
            {
                property.stringValue = newIndex == 0 ? string.Empty : options[newIndex];
            }

            property.stringValue = EditorGUI.TextField(textRect, property.stringValue);
        }
    }

    public class StatusCacheInvalidator : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            StatusIdSelectorDrawer.InvalidateCache();
        }
    }
}
#endif
