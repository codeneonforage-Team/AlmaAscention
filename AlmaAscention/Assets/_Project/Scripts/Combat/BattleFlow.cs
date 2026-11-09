using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CNF.Data;
using CNF.AbilitySystem;
using AlmaAscention.Deck;

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
        public EBattleState State { get; private set; } = EBattleState.Initializing;
        public int RoundNumber { get; private set; }
        public int HandSize = 5;

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
            int handSize = 5)
        {
            TeardownSubscriptions();

            Player = player;
            CardExecutor = cardExecutor;
            HandSize = handSize;
            RoundNumber = 0;
            _isBattleOver = false;
            _isEndingPlayerTurn = false;

            System.Random setupRandom = new System.Random(seed);
            DeckManager = new BattleDeckManager(masterDeck, new System.Random(seed));
            DeckManager.CardExhausted += HandleCardExhausted;

            if (CardExecutor != null)
                CardExecutor.OnAfterCardPlayed += HandleAfterCardPlayed;

            _enemies.Clear();
            _enemyStatusCache.Clear();
            foreach (var (data, status) in enemySetups)
            {
                EnemyRuntime runtime = status.GetComponent<EnemyRuntime>();
                if (runtime == null) runtime = status.gameObject.AddComponent<EnemyRuntime>();
                runtime.Initialize(data, status, player, new System.Random(setupRandom.Next()));
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

            SetState(EBattleState.PlayerTurn);
            CheckBattleEnd();
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

        private void FinishBattle(bool playerWon)
        {
            if (_isBattleOver) return;
            _isBattleOver = true;

            if (CardExecutor != null)
                CardExecutor.ClearQueue();
            StopAllCoroutines();

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
            if (willExhaust)
                DeckManager.ExhaustFromHand(card.InstanceGUID);
            else
                DeckManager.DiscardFromHand(card.InstanceGUID);
        }

        private void HandleCardExhausted(CardInstance card)
        {
            if (CardExecutor == null) return;
            CardExecutor.TriggerCardExhaustPassives(card, BuildContext(null));
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
            if (DeckManager != null)
                DeckManager.CardExhausted -= HandleCardExhausted;
            if (CardExecutor != null)
                CardExecutor.OnAfterCardPlayed -= HandleAfterCardPlayed;
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
