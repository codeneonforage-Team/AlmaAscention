using System;
using System.Collections.Generic;
using UnityEngine;
using CNF.Data;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CNF.AbilitySystem
{
    /// <summary>
    /// 만든 카드(CardDataSO)들을 게임 화면(Game 뷰)에서 즉시 클릭해 사용해 보고,
    /// "한 턴에 카드를 몇 장 굴릴 수 있는지 / 한 턴에 총 몇 대미지를 뽑아낼 수 있는지" 실시간으로 측정하는 전투 샌드박스 테스터.
    /// • 아트 프리팹이나 UI Canvas가 없어도 이 컴포넌트 하나만 씬에 있으면 즉시 시각적 전투 테스트가 가능합니다.
    /// </summary>
    public class CombatSandboxTester : MonoBehaviour, IDeckOperations
    {
        [Header("1. 테스트할 카드 목록 (비워두면 프로젝트의 모든 카드를 자동 로드)")]
        [Tooltip("여기에 직접 만든 CardDataSO를 넣거나, 비워두면 Resources/GameData/Cards의 모든 카드를 자동으로 가져옵니다.")]
        public List<CardDataSO> TestDeckCards = new();

        [Header("2. 플레이어 시작 설정")]
        public int PlayerMaxHP = 80;
        public int PlayerMaxMana = 3;
        public int DrawPerTurn = 5;
        [Tooltip("켜두면 카드를 내도 버려지지 않고 계속 손패에 남아서 한 턴 무한 난사 테스트가 가능합니다.")]
        public bool KeepCardsInHandAfterPlay = false;

        [Header("3. 샌드백 몬스터 설정")]
        public int Dummy1MaxHP = 120;
        public int Dummy2MaxHP = 9999;

        private StatusController _player;
        private StatusController _enemy1;
        private StatusController _enemy2;
        private readonly List<StatusController> _enemies = new(2);
        private StatusController _selectedTarget;
        private CardExecutor _executor;

        private readonly List<CardInstance> _drawPile = new();
        private readonly List<CardInstance> _hand = new();
        private readonly List<CardInstance> _discardPile = new();
        private readonly List<CardInstance> _exhaustPile = new();
        private readonly List<CardDataSO> _availableCardPool = new();

        // 한 턴 딜량 / 콤보 측정용 통계
        private int _currentTurnNumber = 1;
        private int _turnCardsPlayed;
        private int _turnTotalDamageDealt;
        private int _turnUnblockedDamageDealt;
        private int _turnTotalBlockGained;
        private int _turnManaSpent;
        private int _bestTurnDamageRecord;
        private readonly List<string> _turnPlayedCardNames = new();
        private readonly List<string> _combatLogs = new();

        private Vector2 _handScrollPos;
        private Vector2 _poolScrollPos;
        private Vector2 _logScrollPos;
        private bool _showCardSpawnerModal;
        private int _pendingTargetCardIndex = -1; // 단일 대상(SingleEnemy) 카드 선택 후 적 클릭 대기 중인 손패 인덱스

        private GUIStyle _richBoxStyle;
        private GUIStyle _richBtnStyle;
        private GUIStyle _headerLabelStyle;
        private static readonly List<string> _statusFormatBuffer = new(16);

#if UNITY_EDITOR
        [MenuItem("CNF/Combat Sandbox/현재 씬에 카드 딜 측정 샌드박스 생성")]
        public static void CreateSandboxInScene()
        {
            var existing = FindAnyObjectByType<CombatSandboxTester>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[CNF Sandbox] 이미 씬에 CombatSandboxTester가 존재합니다. Play(▶)를 눌러 테스트하세요!");
                return;
            }

            var go = new GameObject("@CombatSandboxTester");
            go.AddComponent<CombatSandboxTester>();
            Undo.RegisterCreatedObjectUndo(go, "Create CombatSandboxTester");
            Selection.activeGameObject = go;
            Debug.Log("<color=#44FF66>[CNF Sandbox]</color> '@CombatSandboxTester' 오브젝트가 생성되었습니다! 바로 Play(▶) 버튼을 눌러 Game 화면에서 카드를 사용해 보세요.");
        }
#endif

        private void Awake()
        {
            SetupCombatActors();
            SubscribeCombatMetrics();
            BuildAvailableCardPool();
            ResetFullBattle();
        }

        private void OnDestroy()
        {
            UnsubscribeCombatMetrics();
        }

        private void SetupCombatActors()
        {
            _executor = gameObject.GetComponent<CardExecutor>();
            if (_executor == null)
                _executor = gameObject.AddComponent<CardExecutor>();

            _player = CreateActor("Player", teamId: 0, PlayerMaxHP, PlayerMaxMana);
            _enemy1 = CreateActor("슬라임 (일반 표적)", teamId: 1, Dummy1MaxHP, 0);
            _enemy2 = CreateActor("샌드백 골렘 (딜 측정용)", teamId: 1, Dummy2MaxHP, 0);

            _enemies.Clear();
            _enemies.Add(_enemy1);
            _enemies.Add(_enemy2);
            _selectedTarget = _enemy1;
        }

        private StatusController CreateActor(string actorName, int teamId, int maxHp, int maxMana)
        {
            var child = new GameObject(actorName);
            child.transform.SetParent(transform);
            var ctrl = child.AddComponent<StatusController>();
            ctrl.TeamID = teamId;
            ctrl.InitializeStats(maxHp, maxHp, maxMana);
            return ctrl;
        }

        private void SubscribeCombatMetrics()
        {
            CombatEvents.OnDamageTaken += HandleDamageTaken;
            CombatEvents.OnBlockGained += HandleBlockGained;
            CombatEvents.OnManaSpent += HandleManaSpent;
        }

        private void UnsubscribeCombatMetrics()
        {
            CombatEvents.OnDamageTaken -= HandleDamageTaken;
            CombatEvents.OnBlockGained -= HandleBlockGained;
            CombatEvents.OnManaSpent -= HandleManaSpent;
        }

        private void HandleDamageTaken(StatusController target, StatusController attacker, int blockedDmg, int lostHp)
        {
            if (target != null && target.TeamID == 1)
            {
                int totalHit = blockedDmg + lostHp;
                _turnTotalDamageDealt += totalHit;
                _turnUnblockedDamageDealt += lostHp;
                if (_turnTotalDamageDealt > _bestTurnDamageRecord)
                    _bestTurnDamageRecord = _turnTotalDamageDealt;

                AddLog($"💥 [{target.name}] 피해 {totalHit} (HP -{lostHp}, 방어도 차감 {blockedDmg})");
            }
        }

        private void HandleBlockGained(StatusController target, int gained, int totalBlock)
        {
            if (target == _player)
            {
                _turnTotalBlockGained += gained;
                AddLog($"🛡️ 플레이어 방어도 +{gained} (현재 {totalBlock})");
            }
        }

        private void HandleManaSpent(StatusController owner, int spent, int remaining)
        {
            if (owner == _player)
            {
                _turnManaSpent += spent;
            }
        }

        private void BuildAvailableCardPool()
        {
            _availableCardPool.Clear();

            if (TestDeckCards != null)
            {
                for (int i = 0; i < TestDeckCards.Count; i++)
                {
                    if (TestDeckCards[i] != null && !_availableCardPool.Contains(TestDeckCards[i]))
                        _availableCardPool.Add(TestDeckCards[i]);
                }
            }

            if (DataManager.Instance != null)
            {
                foreach (var kvp in DataManager.Instance.GetAllCards())
                {
                    if (kvp.Value != null && !_availableCardPool.Contains(kvp.Value))
                        _availableCardPool.Add(kvp.Value);
                }
            }

            // 프로젝트에 아직 만든 카드가 하나도 없다면 즉시 체험용 샘플 카드 5종을 런타임 생성
            if (_availableCardPool.Count == 0)
            {
                CreateRuntimeFallbackCards();
            }
        }

        private void CreateRuntimeFallbackCards()
        {
            var strike = ScriptableObject.CreateInstance<CardDataSO>();
            strike.CardID = "Sample_Strike";
            strike.CardName = "타격";
            strike.UpgradedCardName = "타격+";
            strike.CardType = ECardType.Attack;
            strike.TargetType = ECardTarget.SingleEnemy;
            strike.BaseCost = 1;
            strike.UpgradedCost = 1;
            strike.Description = "적에게 피해를 {D} 줍니다.";
            strike.UpgradedDescription = "적에게 피해를 {D} 줍니다.";
            strike.Effects.Add(new DealDamageEffect { BaseDamage = 6, UpgradedDamage = 9 });

            var defend = ScriptableObject.CreateInstance<CardDataSO>();
            defend.CardID = "Sample_Defend";
            defend.CardName = "수비";
            defend.UpgradedCardName = "수비+";
            defend.CardType = ECardType.Skill;
            defend.TargetType = ECardTarget.Self;
            defend.BaseCost = 1;
            defend.UpgradedCost = 1;
            defend.Description = "방어도를 {B} 얻습니다.";
            defend.UpgradedDescription = "방어도를 {B} 얻습니다.";
            defend.Effects.Add(new GainBlockEffect { BaseBlock = 5, UpgradedBlock = 8 });

            var bash = ScriptableObject.CreateInstance<CardDataSO>();
            bash.CardID = "Sample_Bash";
            bash.CardName = "강타";
            bash.UpgradedCardName = "강타+";
            bash.CardType = ECardType.Attack;
            bash.TargetType = ECardTarget.SingleEnemy;
            bash.BaseCost = 2;
            bash.UpgradedCost = 2;
            bash.Description = "피해를 {D} 주고 취약을 {M} 부여합니다.";
            bash.UpgradedDescription = "피해를 {D} 주고 취약을 {M} 부여합니다.";
            bash.Effects.Add(new DealDamageEffect { BaseDamage = 8, UpgradedDamage = 10 });
            bash.Effects.Add(new ApplyStatusEffect { StatusID = StatusKeys.Vulnerable, BaseStacks = 2, UpgradedStacks = 3, ApplyTo = ECardTarget.SingleEnemy });

            var whirlwind = ScriptableObject.CreateInstance<CardDataSO>();
            whirlwind.CardID = "Sample_Whirlwind";
            whirlwind.CardName = "소용돌이(X)";
            whirlwind.UpgradedCardName = "소용돌이+(X)";
            whirlwind.CardType = ECardType.Attack;
            whirlwind.TargetType = ECardTarget.AllEnemies;
            whirlwind.BaseCost = 0;
            whirlwind.UpgradedCost = 0;
            whirlwind.IsXCost = true;
            whirlwind.Description = "모든 적에게 피해를 {D}씩 X번 줍니다.";
            whirlwind.UpgradedDescription = "모든 적에게 피해를 {D}씩 X번 줍니다.";
            whirlwind.Effects.Add(new DealDamageEffect { BaseDamage = 5, UpgradedDamage = 8, MultiplyHitsByXCost = true, OverrideTarget = ECardTarget.AllEnemies });

            var offering = ScriptableObject.CreateInstance<CardDataSO>();
            offering.CardID = "Sample_Offering";
            offering.CardName = "봉헌";
            offering.UpgradedCardName = "봉헌+";
            offering.CardType = ECardType.Skill;
            offering.TargetType = ECardTarget.Self;
            offering.BaseCost = 0;
            offering.UpgradedCost = 0;
            offering.IsExhaust = true;
            offering.IsUpgradedExhaust = true;
            offering.Description = "체력을 {H} 잃고 마나 2와 카드 {M}장을 뽑습니다. (소멸)";
            offering.UpgradedDescription = "체력을 {H} 잃고 마나 2와 카드 {M}장을 뽑습니다. (소멸)";
            offering.Effects.Add(new ModifyHPEffect { BaseHPDelta = -6, UpgradedHPDelta = -6 });
            offering.Effects.Add(new DrawAndManaEffect { BaseManaGain = 2, UpgradedManaGain = 2, BaseDraw = 3, UpgradedDraw = 5 });

            _availableCardPool.Add(strike);
            _availableCardPool.Add(defend);
            _availableCardPool.Add(bash);
            _availableCardPool.Add(whirlwind);
            _availableCardPool.Add(offering);

            if (DataManager.Instance != null)
            {
                for (int i = 0; i < _availableCardPool.Count; i++)
                    DataManager.Instance.RegisterCard(_availableCardPool[i]);
            }
        }

        public void ResetFullBattle()
        {
            _player.InitializeStats(PlayerMaxHP, PlayerMaxHP, PlayerMaxMana);
            _player.ClearBattleStatuses();

            _enemy1.InitializeStats(Dummy1MaxHP, Dummy1MaxHP, 0);
            _enemy1.ClearBattleStatuses();

            _enemy2.InitializeStats(Dummy2MaxHP, Dummy2MaxHP, 0);
            _enemy2.ClearBattleStatuses();

            _selectedTarget = _enemy1;

            _drawPile.Clear();
            _hand.Clear();
            _discardPile.Clear();
            _exhaustPile.Clear();

            var sourceList = (TestDeckCards != null && TestDeckCards.Count > 0) ? TestDeckCards : _availableCardPool;
            for (int i = 0; i < sourceList.Count; i++)
            {
                if (sourceList[i] != null)
                    _drawPile.Add(new CardInstance(sourceList[i]));
            }

            // 카드가 너무 적으면 드로우 테스트가 가능하도록 최소 8장까지 채움
            while (_drawPile.Count < 8 && _availableCardPool.Count > 0)
            {
                var pick = _availableCardPool[_drawPile.Count % _availableCardPool.Count];
                _drawPile.Add(new CardInstance(pick));
            }

            ShuffleDrawPile();
            _currentTurnNumber = 1;
            ResetTurnMetrics();
            DrawCards(DrawPerTurn);
            AddLog("=== 🔄 전투 샌드박스 초기화 완료 (1턴 시작) ===");
        }

        public void StartNextTurn()
        {
            // 현재 턴 종료 상태이상 처리 (독/화상/약화/취약 감소 등)
            _player.ProcessTurnEndStatuses();
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i].IsAlive)
                {
                    _enemies[i].ProcessTurnStartStatuses();
                    _enemies[i].ProcessTurnEndStatuses();
                }
            }

            // 보존(Retain)이 아닌 손패 버리기 및 휘발성(Ethereal) 소멸 처리
            for (int i = _hand.Count - 1; i >= 0; i--)
            {
                var c = _hand[i];
                c.ResetTurnModifiers();

                if (c.Data != null && c.Data.IsEthereal)
                {
                    _hand.RemoveAt(i);
                    _exhaustPile.Add(c);
                    CardExecutor.TriggerCardExhaustPassives(c, BuildContext());
                }
                else if (c.Data == null || !c.Data.IsRetain)
                {
                    _hand.RemoveAt(i);
                    _discardPile.Add(c);
                }
            }

            _currentTurnNumber++;
            _player.ProcessTurnStartStatuses();
            _player.RefillTurnMana();
            ResetTurnMetrics();
            DrawCards(DrawPerTurn);
            AddLog($"=== ▶ {_currentTurnNumber}턴 시작 (마나 {_player.CurrentMana} 리필 & {DrawPerTurn}장 드로우) ===");
        }

        private void ResetTurnMetrics()
        {
            _pendingTargetCardIndex = -1;
            _turnCardsPlayed = 0;
            _turnTotalDamageDealt = 0;
            _turnUnblockedDamageDealt = 0;
            _turnTotalBlockGained = 0;
            _turnManaSpent = 0;
            _turnPlayedCardNames.Clear();
        }

        private CombatContext BuildContext(StatusController explicitTarget = null)
        {
            StatusController target = explicitTarget != null ? explicitTarget : _selectedTarget;
            if (target == null || !target.IsAlive)
                target = _enemy1.IsAlive ? _enemy1 : (_enemy2.IsAlive ? _enemy2 : null);

            return new CombatContext
            {
                Caster = _player,
                PrimaryTarget = target,
                AllEnemies = _enemies,
                DeckOps = this,
                IsTargetIntendingToAttack = _ => true
            };
        }

        private void OnHandCardClicked(int handIndex)
        {
            if (handIndex < 0 || handIndex >= _hand.Count) return;
            CardInstance card = _hand[handIndex];

            // 단일 대상(SingleEnemy) 카드는 먼저 카드를 선택한 뒤 공격할 적을 직접 클릭해서 시전하도록 처리
            if (card.Data != null && card.Data.TargetType == ECardTarget.SingleEnemy)
            {
                if (_pendingTargetCardIndex == handIndex)
                {
                    _pendingTargetCardIndex = -1;
                    AddLog($"↩️ [{card.DisplayName}] 타겟 선택을 취소했습니다.");
                    return;
                }

                if (!_player.HasEnoughMana(card.CurrentCost))
                {
                    AddLog($"⚠️ [{card.DisplayName}] 사용 불가: 마나가 부족합니다.");
                    return;
                }

                _pendingTargetCardIndex = handIndex;
                AddLog($"🎯 [{card.DisplayName}] 준비됨 ➔ 우측에서 공격할 몬스터를 클릭하세요! (우클릭 시 취소)");
                return;
            }

            // 단일 대상이 아닌 카드(자신/전체 적/무작위 적)는 클릭 즉시 발동
            _pendingTargetCardIndex = -1;
            PlayCardFromHand(handIndex, null);
        }

        private void PlayCardFromHand(int handIndex, StatusController explicitTarget)
        {
            if (handIndex < 0 || handIndex >= _hand.Count) return;

            _pendingTargetCardIndex = -1;
            if (explicitTarget != null)
                _selectedTarget = explicitTarget;

            CardInstance card = _hand[handIndex];
            CombatContext ctx = BuildContext(explicitTarget);

            if (!_executor.CanPlayCard(card, ctx, out string failReason))
            {
                AddLog($"⚠️ [{card.DisplayName}] 사용 불가: {failReason}");
                return;
            }

            // 먼저 손패에서 분리 (악마의 불꽃 등 손패 전체 소멸 기믹이 자기 자신을 중복 소멸시키지 않도록)
            _hand.RemoveAt(handIndex);

            if (_executor.TryPlayCard(card, ctx))
            {
                _turnCardsPlayed++;
                string targetSuffix = (card.Data != null && card.Data.TargetType == ECardTarget.SingleEnemy && ctx.PrimaryTarget != null)
                    ? $"({ctx.PrimaryTarget.name})"
                    : string.Empty;
                _turnPlayedCardNames.Add($"{card.DisplayName}{targetSuffix}");

                bool exhausted = card.WillExhaust || (card.Data != null && card.Data.CardType == ECardType.Power);
                if (KeepCardsInHandAfterPlay)
                {
                    _hand.Insert(Mathf.Min(handIndex, _hand.Count), card);
                }
                else if (exhausted)
                {
                    _exhaustPile.Add(card);
                }
                else
                {
                    _discardPile.Add(card);
                }

                AddLog($"🃏 카드 사용: [{card.DisplayName}]{targetSuffix} (이번 턴 {_turnCardsPlayed}번째 카드)");
            }
            else
            {
                _hand.Insert(Mathf.Min(handIndex, _hand.Count), card);
            }
        }

        // =====================================================================
        // IDeckOperations 구현 (카드 효과로 드로우/소멸/손패 강화가 일어날 때 자동 연동)
        // =====================================================================

        public void DrawCards(int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (_drawPile.Count == 0)
                {
                    if (_discardPile.Count == 0) break;
                    _drawPile.AddRange(_discardPile);
                    _discardPile.Clear();
                    ShuffleDrawPile();
                }

                int last = _drawPile.Count - 1;
                CardInstance drawn = _drawPile[last];
                _drawPile.RemoveAt(last);
                _hand.Add(drawn);
            }
        }

        public int ExhaustEntireHand()
        {
            int count = _hand.Count;
            if (count == 0) return 0;

            CombatContext ctx = BuildContext();
            var temp = new List<CardInstance>(_hand);
            _hand.Clear();

            for (int i = 0; i < temp.Count; i++)
            {
                _exhaustPile.Add(temp[i]);
                CardExecutor.TriggerCardExhaustPassives(temp[i], ctx);
            }
            AddLog($"🔥 손패 {count}장 전체 소멸!");
            return count;
        }

        public void UpgradeCardsInHand(bool upgradeAll)
        {
            if (_hand.Count == 0) return;

            if (upgradeAll)
            {
                for (int i = 0; i < _hand.Count; i++)
                    _hand[i].Upgrade();
                AddLog("⚒️ 손패의 모든 카드 강화 완료!");
            }
            else
            {
                for (int i = 0; i < _hand.Count; i++)
                {
                    if (_hand[i].Upgrade())
                    {
                        AddLog($"⚒️ [{_hand[i].DisplayName}] 강화 완료!");
                        break;
                    }
                }
            }
        }

        public void AddCardToHand(string cardId, bool isUpgraded = false)
        {
            CardDataSO so = DataManager.Instance != null ? DataManager.Instance.GetCard(cardId) : null;
            if (so == null)
                so = _availableCardPool.Find(c => string.Equals(c.CardID, cardId, StringComparison.OrdinalIgnoreCase));

            if (so != null)
            {
                var inst = new CardInstance(so, isUpgraded);
                _hand.Add(inst);
                AddLog($"➕ 손패에 [{inst.DisplayName}] 생성 추가");
            }
        }

        private void ShuffleDrawPile()
        {
            for (int i = _drawPile.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (_drawPile[i], _drawPile[j]) = (_drawPile[j], _drawPile[i]);
            }
        }

        private void AddLog(string msg)
        {
            _combatLogs.Insert(0, $"[T{_currentTurnNumber}] {msg}");
            if (_combatLogs.Count > 40)
                _combatLogs.RemoveAt(_combatLogs.Count - 1);
        }

        // =====================================================================
        // Game 뷰 실시간 전투 & 한 턴 딜량 측정 UI (OnGUI)
        // =====================================================================

        private void OnGUI()
        {
            // 마우스 우클릭 시 단일 대상 카드 타겟 선택 대기 해제
            if (_pendingTargetCardIndex >= 0 && Event.current != null &&
                Event.current.type == EventType.MouseDown && Event.current.button == 1)
            {
                _pendingTargetCardIndex = -1;
                Event.current.Use();
            }

            if (_pendingTargetCardIndex >= _hand.Count)
                _pendingTargetCardIndex = -1;

            _richBoxStyle ??= new GUIStyle(GUI.skin.box) { richText = true, fontSize = 13, alignment = TextAnchor.UpperLeft };
            _richBtnStyle ??= new GUIStyle(GUI.skin.button) { richText = true, fontSize = 12, wordWrap = true };
            _headerLabelStyle ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15, fontStyle = FontStyle.Bold };

            GUIStyle richBox = _richBoxStyle;
            GUIStyle richBtn = _richBtnStyle;
            GUIStyle headerLabel = _headerLabelStyle;

            float w = Screen.width;
            float h = Screen.height;

            // 1. 상단 전광판: 이번 턴 딜량 & 사용한 카드 수 측정기
            GUILayout.BeginArea(new Rect(12, 10, w - 24, 92), GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                $"<size=17><b>📊 [{_currentTurnNumber}턴 딜량 & 콤보 측정기]</b></size>   " +
                $"이번 턴 낸 카드: <color=#FFE066><b>{_turnCardsPlayed}장</b></color>   |   " +
                $"이번 턴 총 피해량: <color=#FF6666><b>{_turnTotalDamageDealt}</b></color> (실제 HP 차감: {_turnUnblockedDamageDealt})   |   " +
                $"이번 턴 획득 방어도: <color=#66CCFF><b>{_turnTotalBlockGained}</b></color>   |   " +
                $"소모 마나: <b>{_turnManaSpent}</b>   |   " +
                $"🏆 최고 한 턴 딜 기록: <color=#44FF66><b>{_bestTurnDamageRecord}</b></color>",
                headerLabel);

            if (GUILayout.Button("📥 내가 만든 카드 골라서 손패에 넣기", GUILayout.Width(230), GUILayout.Height(30)))
            {
                BuildAvailableCardPool();
                _showCardSpawnerModal = !_showCardSpawnerModal;
            }
            if (GUILayout.Button("🔄 전투 전체 초기화", GUILayout.Width(140), GUILayout.Height(30)))
            {
                BuildAvailableCardPool();
                ResetFullBattle();
            }
            GUILayout.EndHorizontal();

            string comboHistory = _turnPlayedCardNames.Count > 0
                ? string.Join(" ➔ ", _turnPlayedCardNames)
                : "(아직 이번 턴에 사용한 카드가 없습니다. 아래 손패 카드를 클릭해 보세요!)";
            GUILayout.Label($"<b>이번 턴 콤보 순서:</b> {comboHistory}", headerLabel);
            GUILayout.EndArea();

            // 2. 좌측: 플레이어 상태 & 치트 버튼
            float midY = 110f;
            float midH = h - 355f;
            GUILayout.BeginArea(new Rect(12, midY, (w * 0.44f) - 18, midH), GUI.skin.box);
            GUILayout.Label($"<size=15><b>🧙‍♂️ 플레이어 (Player)</b></size>", headerLabel);
            GUILayout.Label(
                $"❤️ 체력: <b>{_player.CurrentHP} / {_player.MaxHP}</b>   |   " +
                $"🛡️ 방어도: <color=#66CCFF><b>{_player.Block}</b></color>   |   " +
                $"💎 마나: <color=#FFE066><b>{_player.CurrentMana} / {_player.MaxMana}</b></color>",
                headerLabel);
            GUILayout.Label($"<b>현재 버프/디버프:</b> {FormatStatuses(_player)}", richBox);

            GUILayout.Space(6);
            GUILayout.Label("<b>⚡ 빠른 테스트 치트 버튼</b>", headerLabel);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("💎 마나 +1")) _player.GainMana(1);
            if (GUILayout.Button("💎 마나 99(무한)")) _player.GainMana(99);
            if (GUILayout.Button("🃏 1장 드로우")) DrawCards(1);
            if (GUILayout.Button("⚒️ 손패 전체 강화(+)")) UpgradeCardsInHand(true);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("💪 힘 +2")) _player.AddStatus(StatusKeys.Strength, 2);
            if (GUILayout.Button("🏃 민첩 +2")) _player.AddStatus(StatusKeys.Dexterity, 2);
            if (GUILayout.Button("🧱 무감각 +3")) _player.AddStatus(StatusKeys.FeelNoPain, 3);
            if (GUILayout.Button("🌑 어둠의포옹 +1")) _player.AddStatus(StatusKeys.DarkEmbrace, 1);
            if (GUILayout.Button("버프 초기화")) _player.ClearBattleStatuses();
            GUILayout.EndHorizontal();

            KeepCardsInHandAfterPlay = GUILayout.Toggle(
                KeepCardsInHandAfterPlay,
                " ♾️ 무한 손패 모드 (카드를 써도 버려지지 않고 손패에 계속 유지)");

            GUILayout.Space(4);
            GUILayout.Label($"📦 덱 현황 — 뽑을 더미: <b>{_drawPile.Count}</b>장 | 버린 더미: <b>{_discardPile.Count}</b>장 | 소멸 더미: <b>{_exhaustPile.Count}</b>장", headerLabel);
            GUILayout.EndArea();

            // 3. 우측: 타겟 몬스터 2마리 & 전투 로그
            GUILayout.BeginArea(new Rect(w * 0.44f, midY, (w * 0.56f) - 12, midH), GUI.skin.box);
            GUILayout.BeginHorizontal();
            DrawEnemyBox(_enemy1, richBox, headerLabel);
            DrawEnemyBox(_enemy2, richBox, headerLabel);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("<b>📜 실시간 전투/연산 로그</b>", headerLabel);
            _logScrollPos = GUILayout.BeginScrollView(_logScrollPos, GUI.skin.box);
            for (int i = 0; i < _combatLogs.Count; i++)
                GUILayout.Label(_combatLogs[i]);
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // 4. 하단: 내 손패 (단일 대상은 카드 클릭 후 적 선택, 비단일 대상은 즉시 시전) & 턴 종료 버튼
            float bottomY = h - 235f;
            GUILayout.BeginArea(new Rect(12, bottomY, w - 24, 225f), GUI.skin.box);
            GUILayout.BeginHorizontal();
            if (_pendingTargetCardIndex >= 0 && _pendingTargetCardIndex < _hand.Count)
            {
                string pendingName = _hand[_pendingTargetCardIndex].DisplayName;
                GUILayout.Label(
                    $"<size=15><color=#FFE066><b>🎯 [{pendingName}] 타겟 선택 중! ➔ 우측에서 공격할 몬스터의 [⚔️ 이 적에게 시전!] 버튼을 클릭하세요!</b></color></size>",
                    headerLabel);

                if (GUILayout.Button("↩️ 선택 취소 (우클릭)", GUILayout.Width(150), GUILayout.Height(32)))
                    _pendingTargetCardIndex = -1;
            }
            else
            {
                GUILayout.Label(
                    $"<size=15><b>🃏 내 손패 ({_hand.Count}장) — 단일 대상(SingleEnemy) 카드는 클릭 후 공격할 적을 고르고, 전체/자신 카드는 클릭 즉시 발동합니다!</b></size>",
                    headerLabel);
            }

            GUI.backgroundColor = new Color(1f, 0.75f, 0.3f);
            if (GUILayout.Button("⏭️ 턴 종료 & 다음 턴 딜 측정 시작 (마나/드로우 리필)", GUILayout.Width(320), GUILayout.Height(32)))
            {
                StartNextTurn();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            _handScrollPos = GUILayout.BeginScrollView(_handScrollPos, false, false, GUILayout.Height(175f));
            GUILayout.BeginHorizontal();
            CombatContext previewCtx = BuildContext();

            for (int i = 0; i < _hand.Count; i++)
            {
                CardInstance card = _hand[i];
                bool isPendingTarget = _pendingTargetCardIndex == i;
                bool playable = _executor.CanPlayCard(card, previewCtx, out _);

                if (isPendingTarget)
                    GUI.backgroundColor = new Color(1f, 0.85f, 0.25f);
                else
                    GUI.backgroundColor = playable ? new Color(0.85f, 0.95f, 1f) : new Color(0.6f, 0.6f, 0.6f);

                string costStr = card.IsXCost ? "X" : card.CurrentCost.ToString();
                string typeStr = card.Data != null ? card.Data.CardType.ToString() : "";
                string targetModeStr = card.Data != null ? card.Data.TargetType.ToString() : "";
                string desc = card.GetDynamicDescription(previewCtx);
                string topBadge = isPendingTarget ? "🎯 [타겟 선택 대기 중]\n" : string.Empty;

                string cardLabel =
                    $"{topBadge}<size=14><b>[{costStr}코스트] {card.DisplayName}</b></size>\n" +
                    $"<color=#CCCCCC>({typeStr} / {targetModeStr})</color>\n\n" +
                    $"{desc}";

                if (GUILayout.Button(cardLabel, richBtn, GUILayout.Width(195f), GUILayout.Height(150f)))
                {
                    OnHandCardClicked(i);
                    GUI.backgroundColor = Color.white;
                    break;
                }
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // 5. 모달: 내가 만든 카드 목록에서 원하는 카드 클릭해 손패에 즉시 추가
            if (_showCardSpawnerModal)
            {
                DrawCardSpawnerModal(w, h, richBtn, headerLabel);
            }
        }

        private void DrawEnemyBox(StatusController enemy, GUIStyle richBox, GUIStyle headerLabel)
        {
            bool isTargetingMode = _pendingTargetCardIndex >= 0 && _pendingTargetCardIndex < _hand.Count;
            bool isSelected = _selectedTarget == enemy;

            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width((Screen.width * 0.27f) - 14));

            if (isTargetingMode && enemy.IsAlive)
            {
                CardInstance pendingCard = _hand[_pendingTargetCardIndex];
                string previewDescForThisEnemy = pendingCard.GetDynamicDescription(BuildContext(enemy));

                GUI.backgroundColor = new Color(0.35f, 1f, 0.45f);
                if (GUILayout.Button($"⚔️ <b>[{enemy.name}]에게 '{pendingCard.DisplayName}' 시전! (클릭)</b>", GUILayout.Height(34)))
                {
                    GUI.backgroundColor = Color.white;
                    PlayCardFromHand(_pendingTargetCardIndex, enemy);
                    GUILayout.EndVertical();
                    return;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.Label($"<color=#FFE066>▶ 이 적 대상 프리뷰: {previewDescForThisEnemy}</color>", richBox);
            }
            else
            {
                GUI.backgroundColor = isSelected ? new Color(1f, 0.85f, 0.4f) : Color.white;
                string targetMark = isSelected ? "🎯 [기본 프리뷰 기준 타겟]" : "클릭하여 프리뷰 기준 변경";
                if (GUILayout.Button($"<b>{enemy.name}</b> — {targetMark}", GUILayout.Height(26)))
                {
                    _selectedTarget = enemy;
                }
                GUI.backgroundColor = Color.white;
            }

            string aliveStr = enemy.IsAlive ? $"❤️ HP: <b>{enemy.CurrentHP} / {enemy.MaxHP}</b>" : "<color=#FF4444><b>💀 사망 (완치 버튼을 누르세요)</b></color>";
            GUILayout.Label($"{aliveStr}   |   🛡️ 방어도: <b>{enemy.Block}</b>", headerLabel);
            GUILayout.Label($"<b>상태이상:</b> {FormatStatuses(enemy)}", richBox);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+취약 2")) enemy.AddStatus(StatusKeys.Vulnerable, 2);
            if (GUILayout.Button("+약화 2")) enemy.AddStatus(StatusKeys.Weak, 2);
            if (GUILayout.Button("+방어 15")) enemy.AddBlock(15, false);
            if (GUILayout.Button("체력 완치"))
            {
                enemy.InitializeStats(enemy.MaxHP, enemy.MaxHP, 0);
                enemy.ClearBattleStatuses();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawCardSpawnerModal(float w, float h, GUIStyle richBtn, GUIStyle headerLabel)
        {
            Rect modalRect = new Rect(w * 0.15f, h * 0.15f, w * 0.7f, h * 0.65f);
            GUI.Box(modalRect, "");
            GUILayout.BeginArea(modalRect, GUI.skin.window);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<size=15><b>📥 프로젝트 내 전체 카드 목록 ({_availableCardPool.Count}종) — 클릭하면 즉시 내 손패에 들어옵니다!</b></size>", headerLabel);
            if (GUILayout.Button("닫기 ✕", GUILayout.Width(80), GUILayout.Height(28)))
                _showCardSpawnerModal = false;
            GUILayout.EndHorizontal();

            _poolScrollPos = GUILayout.BeginScrollView(_poolScrollPos);
            for (int i = 0; i < _availableCardPool.Count; i++)
            {
                var data = _availableCardPool[i];
                if (data == null) continue;

                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"<b>[{data.CardID}] {data.CardName}</b> ({data.CardType}, {data.BaseCost}코스트) — {data.Description}", headerLabel);
                if (GUILayout.Button("+ 일반 추가", richBtn, GUILayout.Width(100)))
                    _hand.Add(new CardInstance(data, isUpgraded: false));
                if (GUILayout.Button("+ 강화(+) 추가", richBtn, GUILayout.Width(110)))
                    _hand.Add(new CardInstance(data, isUpgraded: true));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static string FormatStatuses(StatusController ctrl)
        {
            var dict = ctrl.GetAllStatuses();
            if (dict == null || dict.Count == 0) return "<color=#888888>(없음)</color>";

            _statusFormatBuffer.Clear();
            foreach (var kvp in dict)
            {
                bool isDebuff = StatusKeys.IsBuiltInDebuff(kvp.Key, kvp.Value) || kvp.Value < 0;
                string col = isDebuff ? "#FF7777" : "#66FF88";
                _statusFormatBuffer.Add($"<color={col}>{kvp.Key}({kvp.Value})</color>");
            }
            return string.Join(", ", _statusFormatBuffer);
        }
    }
}
