using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CNF.Data;
using CNF.AbilitySystem;
using AlmaAscention.Deck;
using AlmaAscention.Relic;
using AlmaAscention.Potion;

namespace AlmaAscention.Combat
{
    public enum EBattleState
    {
        Initializing,
        PlayerTurn,
        ResolvingPlayerActions,
        EnemyTurn,
        Victory,
        Defeat
    }

    // 전투 상태(턴/행동 순서/승패)만 관리하는 오케스트레이터임.
    // 몬스터별 패턴 "선택" 규칙은 EnemyBrain/EnemyRuntime 몫이고, 여긴 그 결과(CurrentIntent)를
    // 언제 실행하고 언제 다음 의도 다시 물어볼지만 정함.
    public class BattleFlow : MonoBehaviour
    {
        public StatusController Player { get; private set; }
        public BattleDeckManager DeckManager { get; private set; }
        public CardExecutor CardExecutor { get; private set; }
        public RelicManager RelicManager { get; private set; }
        public PotionManager PotionManager { get; private set; }
        public EBattleState State { get; private set; } = EBattleState.Initializing;
        public int RoundNumber { get; private set; }
        public int HandSize = 5;
        [Min(1)] public int MaxTurnsPerRegain = 2;
        [Min(0)] public int RegainsPerBattle = 2;
        public int RemainingBattleRegains => Math.Max(0, RegainsPerBattle - _regainsUsed);
        public int RemainingChapterRegains => _getChapterRegains?.Invoke() ?? 0;
        public int AvailableRegainTurns => Math.Min(MaxTurnsPerRegain, _history.Count);
        public bool IsDefeatPending => _pendingDefeat;

        private sealed class BattleSnapshot
        {
            public int Round;
            public StatusSnapshot Player;
            public StatusSnapshot[] Enemies;
            public EnemyRuntime.Snapshot[] EnemyBrains;
            public BattleDeckManager.Snapshot Deck;
            public RelicManager.Snapshot Relics;
            public PotionDataSO[] Potions;
            public UnityEngine.Random.State UnityRandom;
        }

        private readonly List<BattleSnapshot> _history = new();
        private Func<int> _getChapterRegains;
        private Action _spendChapterRegain;
        private int _regainsUsed;
        private bool _pendingDefeat;

        private readonly List<EnemyRuntime> _enemies = new();
        private readonly List<StatusController> _enemyStatusCache = new();
        public IReadOnlyList<EnemyRuntime> Enemies => _enemies;

        private bool _isBattleOver;
        private bool _isEndingPlayerTurn;

        public event Action<EBattleState> OnStateChanged;
        public event Action<int> OnRoundChanged;
        public event Action<string> OnLogMessage;
        // true면 플레이어 승리, false면 패배임.
        public event Action<bool> OnBattleEnded;

        public bool CanEndTurn =>
            !_isBattleOver && State == EBattleState.PlayerTurn && !_isEndingPlayerTurn &&
            (CardExecutor == null || !CardExecutor.IsBusy);

        public void Setup(
            StatusController player,
            MasterDeck masterDeck,
            IReadOnlyList<(MonsterDataSO data, StatusController status)> enemySetups,
            CardExecutor cardExecutor,
            int seed,
            int handSize = 5,
            RelicManager relicManager = null,
            PotionManager potionManager = null,
            Func<int> getChapterRegains = null,
            Action spendChapterRegain = null)
        {
            TeardownSubscriptions();

            Player = player;
            CardExecutor = cardExecutor;
            RelicManager = relicManager;
            PotionManager = potionManager;
            _getChapterRegains = getChapterRegains;
            _spendChapterRegain = spendChapterRegain;
            _history.Clear();
            _regainsUsed = 0;
            _pendingDefeat = false;
            HandSize = handSize;
            RoundNumber = 0;
            _isBattleOver = false;
            _isEndingPlayerTurn = false;

            if (Player != null)
            {
                if (PotionManager != null)
                    Player.OnTryInterceptFatalDamage = PotionManager.TryConsumeResurrectPotion;
                Player.OnManaChanged += HandlePlayerManaChanged;
            }

            CombatRandom setupRandom = new CombatRandom(seed);
            DeckManager = new BattleDeckManager(masterDeck, new CombatRandom(seed));
            DeckManager.CardExhausted += HandleCardExhausted;
            DeckManager.Reshuffled += HandleDeckReshuffled;

            if (CardExecutor != null)
                CardExecutor.OnAfterCardPlayed += HandleAfterCardPlayed;

            CombatEvents.OnDamageTaken += HandleCombatDamageTaken;
            CombatEvents.OnCharacterDied += HandleCombatCharacterDied;

            _enemies.Clear();
            _enemyStatusCache.Clear();
            foreach (var (data, status) in enemySetups)
            {
                EnemyRuntime runtime = status.GetComponent<EnemyRuntime>();
                if (runtime == null) runtime = status.gameObject.AddComponent<EnemyRuntime>();
                runtime.Initialize(data, status, player, new CombatRandom(setupRandom.Next()));
                _enemies.Add(runtime);
                _enemyStatusCache.Add(status);
            }

            SetState(EBattleState.Initializing);
        }

        public void StartBattle()
        {
            if (State != EBattleState.Initializing || _isBattleOver) return;

            for (int i = 0; i < _enemies.Count; i++)
                _enemies[i].SelectAndAnnounceNextIntent();

            TriggerRelics(ERelicTriggerType.OnBattleStart);
            BeginPlayerTurn();
        }

        private void BeginPlayerTurn()
        {
            if (_isBattleOver) return;

            RoundNumber++;
            OnRoundChanged?.Invoke(RoundNumber);
            Log($"--- {RoundNumber}라운드: 플레이어 턴 시작 ---");

            Player.ProcessTurnStartStatuses();
            Player.RefillTurnMana();
            DeckManager.Draw(HandSize);
            TriggerRelics(ERelicTriggerType.OnTurnStart);

            SetState(EBattleState.PlayerTurn);
            if (!CheckBattleEnd()) CaptureTurnStart();
        }

        private void CaptureTurnStart()
        {
            var snapshot = new BattleSnapshot
            {
                Round = RoundNumber,
                Player = Player.CaptureSnapshot(),
                Enemies = new StatusSnapshot[_enemies.Count],
                EnemyBrains = new EnemyRuntime.Snapshot[_enemies.Count],
                Deck = DeckManager.CaptureSnapshot(),
                Relics = RelicManager?.CaptureSnapshot(),
                Potions = PotionManager?.CaptureSnapshot(),
                UnityRandom = UnityEngine.Random.state
            };
            for (int i = 0; i < _enemies.Count; i++)
            {
                snapshot.Enemies[i] = _enemies[i].Status.CaptureSnapshot();
                snapshot.EnemyBrains[i] = _enemies[i].CaptureSnapshot();
            }
            _history.Add(snapshot);
            while (_history.Count > MaxTurnsPerRegain)
                _history.RemoveAt(0);
        }

        // turnsBack=1은 현재 턴 시작, 2는 이전 턴 시작으로 복원한다.
        public bool CanRegain(int turnsBack) =>
            turnsBack >= 1 && turnsBack <= AvailableRegainTurns &&
            RemainingBattleRegains > 0 && RemainingChapterRegains > 0 &&
            (State == EBattleState.PlayerTurn || _pendingDefeat) &&
            (CardExecutor == null || !CardExecutor.IsBusy);

        public bool TryRegain(int turnsBack)
        {
            if (!CanRegain(turnsBack)) return false;
            int index = _history.Count - turnsBack;
            BattleSnapshot snapshot = _history[index];
            CardExecutor?.ClearQueue();
            StopAllCoroutines();
            _isBattleOver = false;
            _pendingDefeat = false;
            _isEndingPlayerTurn = false;
            RoundNumber = snapshot.Round;
            UnityEngine.Random.state = snapshot.UnityRandom;
            RelicManager?.RestoreSnapshot(snapshot.Relics);
            PotionManager?.RestoreSnapshot(snapshot.Potions);
            Player.RestoreSnapshot(snapshot.Player);
            for (int i = 0; i < _enemies.Count; i++)
            {
                _enemies[i].Status.RestoreSnapshot(snapshot.Enemies[i]);
                _enemies[i].RestoreSnapshot(snapshot.EnemyBrains[i]);
            }
            DeckManager.RestoreSnapshot(snapshot.Deck);
            _history.RemoveRange(index + 1, _history.Count - index - 1);
            _regainsUsed++;
            _spendChapterRegain?.Invoke();
            SetState(EBattleState.PlayerTurn);
            OnRoundChanged?.Invoke(RoundNumber);
            Log($"재회: {RoundNumber}턴 시작으로 돌아왔습니다.");
            return true;
        }

        public void ConfirmDefeat()
        {
            if (_pendingDefeat) FinishBattle(false, force: true);
        }

        public bool TryPlayCard(CardInstance card, StatusController target)
        {
            if (_isBattleOver || State != EBattleState.PlayerTurn || card == null)
                return false;

            CombatContext ctx = BuildContext(target);
            return CardExecutor.EnqueuePlayCard(card, ctx, success =>
            {
                if (success)
                    Log($"플레이어가 [{card.DisplayName}] 카드를 사용했습니다.");
                CheckBattleEnd();
            });
        }

        public bool TryEndPlayerTurn()
        {
            if (!CanEndTurn) return false;

            _isEndingPlayerTurn = true;
            StartCoroutine(EndPlayerTurnRoutine());
            return true;
        }

        private IEnumerator EndPlayerTurnRoutine()
        {
            SetState(EBattleState.ResolvingPlayerActions);

            while (CardExecutor != null && CardExecutor.IsBusy)
                yield return null;

            if (_isBattleOver)
            {
                _isEndingPlayerTurn = false;
                yield break;
            }

            TriggerRelics(ERelicTriggerType.OnTurnEnd);
            CleanupHandAtTurnEnd();
            Player.ProcessTurnEndStatuses();

            _isEndingPlayerTurn = false;

            if (CheckBattleEnd()) yield break;

            yield return StartCoroutine(RunEnemyTurnRoutine());
        }

        private void CleanupHandAtTurnEnd()
        {
            List<CardInstance> handSnapshot = new List<CardInstance>(DeckManager.HandCards);
            for (int i = 0; i < handSnapshot.Count; i++)
            {
                CardInstance card = handSnapshot[i];
                if (card?.Data == null) continue;

                card.ResetTurnModifiers();

                if (card.Data.IsEthereal)
                    DeckManager.ExhaustFromHand(card.InstanceGUID);
                else if (card.Data.IsRetain)
                    continue;
                else
                    DeckManager.DiscardFromHand(card.InstanceGUID);
            }
        }

        private IEnumerator RunEnemyTurnRoutine()
        {
            if (_isBattleOver) yield break;

            SetState(EBattleState.EnemyTurn);
            Log("--- 적 턴 시작 ---");

            List<EnemyRuntime> turnOrder = new List<EnemyRuntime>(_enemies);
            for (int i = 0; i < turnOrder.Count; i++)
            {
                if (_isBattleOver) yield break;

                EnemyRuntime enemy = turnOrder[i];
                StatusController status = enemy.Status;
                if (status == null || !status.IsAlive) continue;

                status.ProcessTurnStartStatuses();

                bool isStunned = status.GetStatus(StatusKeys.Stunned) > 0;

                if (isStunned)
                {
                    Log($"{enemy.Data.MonsterName}은 기절 상태라 행동을 건너뜁니다.");
                }
                else if (enemy.CurrentIntent != null)
                {
                    yield return StartCoroutine(EnemyActionExecutor.ExecutePatternRoutine(
                        status, Player, enemy.CurrentIntent, () => _isBattleOver, Log));
                }

                if (_isBattleOver) yield break;
                if (CheckBattleEnd()) yield break;

                if (status.IsAlive)
                    status.ProcessTurnEndStatuses();

                if (CheckBattleEnd()) yield break;

                if (status.IsAlive)
                    enemy.SelectAndAnnounceNextIntent();
            }

            if (CheckBattleEnd()) yield break;

            BeginPlayerTurn();
        }

        private bool CheckBattleEnd()
        {
            if (_isBattleOver) return true;

            if (Player == null || !Player.IsAlive)
            {
                FinishBattle(false);
                return true;
            }

            bool anyEnemyAlive = false;
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i].Status != null && _enemies[i].Status.IsAlive)
                {
                    anyEnemyAlive = true;
                    break;
                }
            }

            if (!anyEnemyAlive)
            {
                FinishBattle(true);
                return true;
            }

            return false;
        }

        public void SkipBattleForDevelopment()
        {
            if (!Debug.isDebugBuild || State == EBattleState.Initializing || Player == null || !Player.IsAlive)
                return;

            FinishBattle(true);
        }

        private void FinishBattle(bool playerWon, bool force = false)
        {
            if (_isBattleOver && !_pendingDefeat) return;
            if (!playerWon && !force && _history.Count > 0 &&
                RemainingBattleRegains > 0 && RemainingChapterRegains > 0)
            {
                _isBattleOver = true;
                _pendingDefeat = true;
                CardExecutor?.ClearQueue();
                StopAllCoroutines();
                SetState(EBattleState.Defeat);
                Log("패배했습니다. 재회를 사용하거나 결과를 확정하세요.");
                return;
            }
            _isBattleOver = true;
            _pendingDefeat = false;

            if (CardExecutor != null)
                CardExecutor.ClearQueue();
            StopAllCoroutines();

            if (playerWon)
                TriggerRelics(ERelicTriggerType.OnBattleWin);
            RelicManager?.OnBattleFinished();

            SetState(playerWon ? EBattleState.Victory : EBattleState.Defeat);
            Log(playerWon ? "전투 승리!" : "전투 패배...");
            OnBattleEnded?.Invoke(playerWon);
        }

        private CombatContext BuildContext(StatusController primaryTarget)
        {
            return new CombatContext
            {
                Caster = Player,
                PrimaryTarget = primaryTarget,
                AllEnemies = _enemyStatusCache,
                DeckOps = DeckManager,
                IsTargetIntendingToAttack = IsTargetIntendingToAttack
            };
        }

        private bool IsTargetIntendingToAttack(StatusController target)
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i].Status == target)
                    return _enemies[i].IsIntentAttacking();
            }
            return false;
        }

        private void HandleAfterCardPlayed(CardInstance card, CombatContext context, bool willExhaust)
        {
            bool isPower = card?.Data != null && card.Data.CardType == ECardType.Power;
            bool recycledToHand = CardExecutor != null && CardExecutor.ShouldKeepCardInHandAfterPlay(card);

            if (isPower)
            {
                DeckManager.RemoveFromHand(card.InstanceGUID);
            }
            else if (recycledToHand)
            {
                Log($"[우주 재활용 통] [{card.DisplayName}] 카드가 손패로 되돌아왔습니다.");
            }
            else if (willExhaust)
            {
                DeckManager.ExhaustFromHand(card.InstanceGUID);
            }
            else
            {
                DeckManager.DiscardFromHand(card.InstanceGUID);
            }

            TriggerRelics(ERelicTriggerType.OnCardPlayed, context.PrimaryTarget);

            if (DeckManager.HandCards.Count == 0)
            {
                TriggerRelics(ERelicTriggerType.OnHandEmptied, context.PrimaryTarget);
            }
        }

        private void HandleCardExhausted(CardInstance card)
        {
            if (CardExecutor == null) return;
            CombatContext ctx = BuildContext(null);
            CardExecutor.TriggerCardExhaustPassives(card, ctx);
            RelicManager?.TriggerRelics(ERelicTriggerType.OnCardExhausted, ctx);
        }

        private void HandleDeckReshuffled()
        {
            TriggerRelics(ERelicTriggerType.OnDeckReshuffled);
        }

        private void HandlePlayerManaChanged(int currentMana, int maxMana)
        {
            if (!_isBattleOver && State == EBattleState.PlayerTurn && currentMana == 0)
            {
                TriggerRelics(ERelicTriggerType.OnManaDepleted);
            }
        }

        private void HandleCombatDamageTaken(StatusController target, StatusController attacker, int blockedDmg, int lostHp)
        {
            if (!_isBattleOver && target == Player && lostHp > 0 && Player.IsAlive)
            {
                TriggerRelics(ERelicTriggerType.OnDamaged);
            }
        }

        private void HandleCombatCharacterDied(StatusController victim, StatusController killer)
        {
            if (!_isBattleOver && victim != null && victim.TeamID == 1)
            {
                TriggerRelics(ERelicTriggerType.OnEnemyKilled);
            }
        }

        private void TriggerRelics(ERelicTriggerType timing, StatusController primaryTarget = null)
        {
            if (RelicManager == null || Player == null || !Player.IsAlive) return;
            RelicManager.TriggerRelics(timing, BuildContext(primaryTarget));
        }

        private void Log(string message)
        {
            OnLogMessage?.Invoke(message);
        }

        private void SetState(EBattleState state)
        {
            State = state;
            OnStateChanged?.Invoke(state);
        }

        private void TeardownSubscriptions()
        {
            if (Player != null)
                Player.OnManaChanged -= HandlePlayerManaChanged;
            if (DeckManager != null)
            {
                DeckManager.CardExhausted -= HandleCardExhausted;
                DeckManager.Reshuffled -= HandleDeckReshuffled;
            }
            if (CardExecutor != null)
                CardExecutor.OnAfterCardPlayed -= HandleAfterCardPlayed;

            CombatEvents.OnDamageTaken -= HandleCombatDamageTaken;
            CombatEvents.OnCharacterDied -= HandleCombatCharacterDied;
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
            TeardownSubscriptions();
            OnStateChanged = null;
            OnRoundChanged = null;
            OnLogMessage = null;
            OnBattleEnded = null;
        }
    }
}
