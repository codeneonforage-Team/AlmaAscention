using System;
using System.Collections.Generic;
using UnityEngine;
using CNF.Data;

namespace CNF.AbilitySystem
{
    /// <summary>
    /// 턴 되감기(재회/Regain 권능 등)를 위해 특정 시점의 캐릭터 체력, 방어도, 마나, 생존 여부 및 모든 버프/디버프 스택을 통째로 보존하는 런타임 스냅샷.
    /// </summary>
    public readonly struct StatusSnapshot
    {
        public readonly int CurrentHP;
        public readonly int MaxHP;
        public readonly int CurrentMana;
        public readonly int MaxMana;
        public readonly int Block;
        public readonly bool IsDeathHandled;
        public readonly Dictionary<string, int> Statuses;

        public StatusSnapshot(
            int currentHP,
            int maxHP,
            int currentMana,
            int maxMana,
            int block,
            bool isDeathHandled,
            Dictionary<string, int> statuses)
        {
            CurrentHP = currentHP;
            MaxHP = maxHP;
            CurrentMana = currentMana;
            MaxMana = maxMana;
            Block = block;
            IsDeathHandled = isDeathHandled;
            Statuses = statuses;
        }
    }

    /// <summary>
    /// 언리얼의 UCNF_AttributeSet + UAbilitySystemComponent를 대체하는 핵심 전투 스탯/상태이상 컴포넌트.
    /// • 가시(Thorns) 반사 피해, 바리케이드(Barricade)/잔상(Blur) 방어도 유지, 금속화(Metallicize)/의식(Ritual)을 기본 내장합니다.
    /// • GC(가비지 컬렉션) 할당이 0이며 전역 CombatEvents 버스와 자동 연동됩니다.
    /// </summary>
    public class StatusController : MonoBehaviour
    {
        [Header("소속 진영 (0 = 플레이어, 1 = 몬스터)")]
        public int TeamID = 0;

        [Header("기본 전투 자원")]
        [SerializeField] private int _currentHP = 80;
        [SerializeField] private int _maxHP = 80;
        [SerializeField] private int _currentMana = 3;
        [SerializeField] private int _maxMana = 3;
        [SerializeField] private int _block = 0;

        [Header("감전(Shock) 기본 설정 (SO 미등록 시 폴백)")]
        public int ShockExplosionThreshold = 4;
        public int ShockExplosionDamage = 10;

        public int CurrentHP => _currentHP;
        public int MaxHP => _maxHP;
        public int CurrentMana => _currentMana;
        public int MaxMana => _maxMana;
        public int Block => _block;
        public bool IsAlive => _currentHP > 0 && !_isDeathHandled;

        private bool _isDeathHandled;

        private readonly Dictionary<string, int> _statuses = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _keyBuffer = new(16);

        // 개별 캐릭터 UI 및 인텐트 갱신용 로컬 이벤트
        public event Action<int, int> OnHPChanged;                  // (currentHP, maxHP)
        public event Action<int> OnBlockChanged;                    // (currentBlock)
        public event Action<int, int> OnManaChanged;                // (currentMana, maxMana)
        public event Action<string, int> OnStatusChanged;           // (statusID, currentStacks)
        public event Action OnAnyStatusChanged;                     // 인텐트 재계산 트리거용
        public event Action<StatusController> OnDeath;

        // 치명상 부활 인터셉트 훅 (요정 물약, 부활 유물 등. true 반환 시 사망 취소)
        public Func<StatusController, bool> OnTryInterceptFatalDamage;

        // 영구 체력 동기화 콜백 (플레이어 체력/최대체력 변동 시 RunManager/GameInstance에 즉시 반영)
        public Action<int, int> OnSyncPersistentPlayerHP;

        public void InitializeStats(int currentHP, int maxHP, int maxMana = 3)
        {
            _maxHP = Mathf.Max(1, maxHP);
            _currentHP = Mathf.Clamp(currentHP, 0, _maxHP);
            _maxMana = maxMana;
            _currentMana = maxMana;
            _block = 0;
            _isDeathHandled = false;

            OnHPChanged?.Invoke(_currentHP, _maxHP);
            OnBlockChanged?.Invoke(_block);
            OnManaChanged?.Invoke(_currentMana, _maxMana);
        }

        /// <summary>
        /// MonsterDataSO의 체력 범위(MinHP~MaxHP)와 초기 방어도(InitialBlock)로 몬스터 스탯을 즉시 초기화합니다.
        /// (BattleFlow.Setup 호출 전 몬스터 StatusController 세팅용)
        /// </summary>
        public void InitializeFromMonsterData(MonsterDataSO data, System.Random random = null)
        {
            if (data == null) return;
            TeamID = 1;
            int minHp = Mathf.Max(1, data.MinHP);
            int maxHp = Mathf.Max(minHp, data.MaxHP);
            int rolledHp = random != null
                ? random.Next(minHp, maxHp + 1)
                : UnityEngine.Random.Range(minHp, maxHp + 1);

            InitializeStats(rolledHp, rolledHp, maxMana: 0);
            if (data.InitialBlock > 0)
                AddBlock(data.InitialBlock, applyDexterity: false);
        }

        /// <summary>
        /// [재회(턴 되감기) 권능 연동용]: 현재 시점의 체력, 방어도, 마나, 생존 상태 및 모든 상태이상(Dictionary)을 깊은 복사해 스냅샷으로 반환합니다.
        /// </summary>
        public StatusSnapshot CaptureSnapshot()
        {
            return new StatusSnapshot(
                _currentHP,
                _maxHP,
                _currentMana,
                _maxMana,
                _block,
                _isDeathHandled,
                new Dictionary<string, int>(_statuses, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// [재회(턴 되감기) 권능 연동용]: 저장해 둔 스냅샷 시점으로 캐릭터의 모든 스탯과 버프/디버프 상태를 즉시 복원합니다.
        /// </summary>
        public void RestoreSnapshot(StatusSnapshot snapshot)
        {
            _maxHP = Mathf.Max(1, snapshot.MaxHP);
            _currentHP = Mathf.Clamp(snapshot.CurrentHP, 0, _maxHP);
            _maxMana = snapshot.MaxMana;
            _currentMana = snapshot.CurrentMana;
            _block = Mathf.Max(0, snapshot.Block);
            _isDeathHandled = snapshot.IsDeathHandled;

            _statuses.Clear();
            if (snapshot.Statuses != null)
            {
                foreach (var kvp in snapshot.Statuses)
                {
                    if (!string.IsNullOrEmpty(kvp.Key) && kvp.Value != 0)
                        _statuses[kvp.Key] = kvp.Value;
                }
            }

            OnHPChanged?.Invoke(_currentHP, _maxHP);
            OnBlockChanged?.Invoke(_block);
            OnManaChanged?.Invoke(_currentMana, _maxMana);
            OnAnyStatusChanged?.Invoke();
            SyncPlayerHPIfNeeded();
        }

        // =====================================================================
        // 상태이상 (Status / Buff / Debuff / Power) 제어
        // =====================================================================

        public int GetStatus(string statusId)
        {
            if (string.IsNullOrEmpty(statusId)) return 0;
            return _statuses.TryGetValue(statusId, out int stacks) ? stacks : 0;
        }

        public IReadOnlyDictionary<string, int> GetAllStatuses() => _statuses;

        public void AddStatus(string statusId, int amount)
        {
            if (string.IsNullOrEmpty(statusId) || amount == 0 || !IsAlive) return;

            StatusBehaviorSO behavior = ResolveStatusRule(statusId, out bool allowNegative);

            bool isDebuffApplication = behavior != null
                ? (behavior.Polarity == EStatusPolarity.Debuff || (allowNegative && amount < 0))
                : StatusKeys.IsBuiltInDebuff(statusId, amount);

            // 인공물(Artifact) 버프가 있으면 디버프 부여를 1회 무효화하고 인공물 1스택 차감
            if (isDebuffApplication)
            {
                int artifactStacks = GetStatus(StatusKeys.Artifact);
                if (artifactStacks > 0)
                {
                    SetStatus(StatusKeys.Artifact, artifactStacks - 1);
                    return;
                }
            }

            int prev = GetStatus(statusId);
            int next = allowNegative ? (prev + amount) : Mathf.Max(0, prev + amount);
            _statuses[statusId] = next;

            if (behavior != null)
            {
                behavior.OnStackAdded(this, ref next, amount);
                _statuses[statusId] = allowNegative ? next : Mathf.Max(0, next);
            }
            else
            {
                // 기본 내장 감전(Shock) 4스택 폭발 기믹
                if (string.Equals(statusId, StatusKeys.Shock, StringComparison.OrdinalIgnoreCase) && next >= ShockExplosionThreshold)
                {
                    int remainingShock = next - ShockExplosionThreshold;
                    if (remainingShock <= 0) _statuses.Remove(statusId);
                    else _statuses[statusId] = remainingShock;

                    BroadcastStatusChange(statusId, amount, remainingShock);

                    AddStatus(StatusKeys.Stunned, 1);
                    TakeDirectDamage(ShockExplosionDamage);
                    return;
                }
            }

            if (_statuses.TryGetValue(statusId, out int finalVal))
            {
                if (allowNegative ? (finalVal == 0) : (finalVal <= 0))
                    _statuses.Remove(statusId);
            }

            BroadcastStatusChange(statusId, amount, GetStatus(statusId));
        }

        public void SetStatus(string statusId, int exactStacks)
        {
            if (string.IsNullOrEmpty(statusId)) return;

            ResolveStatusRule(statusId, out bool allowNegative);

            int prev = GetStatus(statusId);
            int clamped = allowNegative ? exactStacks : Mathf.Max(0, exactStacks);
            if (clamped == 0)
                _statuses.Remove(statusId);
            else
                _statuses[statusId] = clamped;

            BroadcastStatusChange(statusId, clamped - prev, clamped);
        }

        private static StatusBehaviorSO ResolveStatusRule(string statusId, out bool allowNegative)
        {
            StatusBehaviorSO behavior = DataManager.Instance != null
                ? DataManager.Instance.GetStatusBehavior(statusId)
                : null;

            allowNegative = (behavior != null && behavior.AllowNegativeStacks)
                || StatusKeys.IsNegativeStackAllowed(statusId);

            return behavior;
        }

        public void ClearBattleStatuses()
        {
            _statuses.Clear();
            _block = 0;
            OnBlockChanged?.Invoke(_block);
            OnAnyStatusChanged?.Invoke();
        }

        private void BroadcastStatusChange(string statusId, int delta, int currentStacks)
        {
            OnStatusChanged?.Invoke(statusId, currentStacks);
            OnAnyStatusChanged?.Invoke();
            CombatEvents.RaiseStatusApplied(this, statusId, delta, currentStacks);
        }

        // =====================================================================
        // 체력 / 방어도 / 마나 / 가시 반사 / 치명상 제어
        // =====================================================================

        public void AddBlock(int rawBlock, bool applyDexterity = true)
        {
            if (!IsAlive || rawBlock <= 0) return;

            int added = applyDexterity
                ? DamageCalculator.CalculatePreviewBlock(rawBlock, this)
                : rawBlock;

            if (added <= 0) return;

            _block += added;
            OnBlockChanged?.Invoke(_block);
            CombatEvents.RaiseBlockGained(this, added, _block);
        }

        public void MultiplyBlock(int multiplier = 2)
        {
            if (!IsAlive || _block <= 0) return;
            int prev = _block;
            _block *= multiplier;
            OnBlockChanged?.Invoke(_block);
            CombatEvents.RaiseBlockGained(this, _block - prev, _block);
        }

        public void ClearBlock()
        {
            if (_block == 0) return;
            _block = 0;
            OnBlockChanged?.Invoke(_block);
        }

        /// <summary>
        /// 방어도를 먼저 차감한 뒤 남은 피해를 체력에 적용하고 실제 깎인 HP량을 반환합니다.
        /// attacker가 지정되어 있고 내게 가시(Thorns) 버프가 있으면 공격자에게 즉시 반사 피해를 입힙니다.
        /// </summary>
        public int TakeDamage(int finalDamage, StatusController attacker = null, bool triggerThorns = true)
        {
            if (finalDamage <= 0 || !IsAlive) return 0;

            int remaining = finalDamage;
            int absorbed = 0;

            if (_block > 0)
            {
                absorbed = Mathf.Min(_block, remaining);
                _block -= absorbed;
                remaining -= absorbed;
                OnBlockChanged?.Invoke(_block);
            }

            int lostHP = remaining > 0
                ? TakeDirectDamageInternal(remaining, attacker, absorbed)
                : RaiseBlockedDamageOnly(attacker, absorbed);

            // 가시(Thorns) 반사 기믹 자동 처리 (무한 반사 루프 방지를 위해 TakeDirectDamage 사용)
            if (triggerThorns && attacker != null && attacker != this && attacker.IsAlive)
            {
                int thorns = GetStatus(StatusKeys.Thorns);
                if (thorns > 0)
                    attacker.TakeDirectDamage(thorns, this);
            }

            return lostHP;
        }

        private int RaiseBlockedDamageOnly(StatusController attacker, int absorbedBlock)
        {
            CombatEvents.RaiseDamageTaken(this, attacker, absorbedBlock, 0);
            return 0;
        }

        /// <summary>
        /// 방어도를 무시하고 체력을 직접 깎습니다 (중독, 감전 폭발, 가시 반사, 체력 소모 이벤트 등).
        /// </summary>
        public int TakeDirectDamage(int damage, StatusController attacker = null)
        {
            return TakeDirectDamageInternal(damage, attacker, absorbedBlock: 0);
        }

        private int TakeDirectDamageInternal(int damage, StatusController attacker, int absorbedBlock)
        {
            if (damage <= 0 || !IsAlive) return 0;

            int prevHP = _currentHP;
            _currentHP = Mathf.Max(0, _currentHP - damage);
            int actualLostHP = prevHP - _currentHP;

            OnHPChanged?.Invoke(_currentHP, _maxHP);
            SyncPlayerHPIfNeeded();

            // 항상 사망 판정(OnCharacterDied)보다 피격 이벤트(OnDamageTaken)가 시간순으로 먼저 발행되도록 보장
            CombatEvents.RaiseDamageTaken(this, attacker, absorbedBlock, actualLostHP);

            if (_currentHP == 0)
            {
                if (OnTryInterceptFatalDamage != null && OnTryInterceptFatalDamage.Invoke(this))
                {
                    SyncPlayerHPIfNeeded();
                    return actualLostHP;
                }

                _isDeathHandled = true;
                OnDeath?.Invoke(this);
                CombatEvents.RaiseCharacterDied(this, attacker);
            }

            return actualLostHP;
        }

        public void Heal(int amount)
        {
            if (amount <= 0 || (_currentHP <= 0 && _isDeathHandled)) return;
            int prev = _currentHP;
            _currentHP = Mathf.Clamp(_currentHP + amount, 1, _maxHP);
            int actualHealed = _currentHP - prev;

            OnHPChanged?.Invoke(_currentHP, _maxHP);
            SyncPlayerHPIfNeeded();

            if (actualHealed > 0)
                CombatEvents.RaiseHealed(this, actualHealed, _currentHP);
        }

        public void AddMaxHP(int amount, bool healAddedAmount = true)
        {
            if (amount == 0) return;
            _maxHP = Mathf.Max(1, _maxHP + amount);
            if (healAddedAmount && amount > 0)
                _currentHP = Mathf.Min(_maxHP, _currentHP + amount);
            else
                _currentHP = Mathf.Min(_currentHP, _maxHP);

            OnHPChanged?.Invoke(_currentHP, _maxHP);
            SyncPlayerHPIfNeeded();
        }

        public bool HasEnoughMana(int cost) => _currentMana >= cost;

        public bool ConsumeMana(int cost)
        {
            if (cost < 0 || _currentMana < cost) return false;
            _currentMana -= cost;
            OnManaChanged?.Invoke(_currentMana, _maxMana);
            if (cost > 0)
                CombatEvents.RaiseManaSpent(this, cost, _currentMana);
            return true;
        }

        public void GainMana(int amount)
        {
            if (amount == 0) return;
            _currentMana = Mathf.Max(0, _currentMana + amount);
            OnManaChanged?.Invoke(_currentMana, _maxMana);
        }

        public void RefillTurnMana()
        {
            _currentMana = _maxMana;
            OnManaChanged?.Invoke(_currentMana, _maxMana);
        }

        // =====================================================================
        // 턴 시작 / 턴 종료 상태이상 처리 루프 (바리케이드/잔상/금속화/의식 기본 내장)
        // =====================================================================

        public void ProcessTurnStartStatuses(bool forceRetainBlock = false)
        {
            bool hasBarricade = GetStatus(StatusKeys.Barricade) > 0;
            int blurStacks = GetStatus(StatusKeys.Blur);

            if (!forceRetainBlock && !hasBarricade && blurStacks <= 0)
                ClearBlock();

            if (blurStacks > 0)
                SetStatus(StatusKeys.Blur, blurStacks - 1);

            if (_statuses.Count > 0)
            {
                CopyKeysToBuffer();

                for (int i = 0; i < _keyBuffer.Count; i++)
                {
                    string key = _keyBuffer[i];
                    int stacks = GetStatus(key);
                    if (stacks == 0) continue;

                    var behavior = DataManager.Instance != null ? DataManager.Instance.GetStatusBehavior(key) : null;
                    if (behavior != null)
                    {
                        behavior.OnTurnStart(this, ref stacks);
                        SetStatus(key, stacks);
                    }
                }
            }

            CombatEvents.RaiseTurnStarted(this);
        }

        public void ProcessTurnEndStatuses()
        {
            if (_statuses.Count > 0)
            {
                CopyKeysToBuffer();

                for (int i = 0; i < _keyBuffer.Count; i++)
                {
                    string key = _keyBuffer[i];
                    int stacks = GetStatus(key);
                    if (stacks == 0) continue;

                    var behavior = DataManager.Instance != null ? DataManager.Instance.GetStatusBehavior(key) : null;
                    if (behavior != null)
                    {
                        behavior.OnTurnEnd(this, ref stacks);
                        SetStatus(key, stacks);
                        continue;
                    }

                    // 기본 내장 상태이상 & 파워 턴 종료 처리
                    if (string.Equals(key, StatusKeys.Poison, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, StatusKeys.Burn, StringComparison.OrdinalIgnoreCase))
                    {
                        TakeDirectDamage(stacks);
                        SetStatus(key, stacks - 1);
                    }
                    else if (string.Equals(key, StatusKeys.Weak, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(key, StatusKeys.Vulnerable, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(key, StatusKeys.Frail, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(key, StatusKeys.Stunned, StringComparison.OrdinalIgnoreCase))
                    {
                        SetStatus(key, stacks - 1);
                    }
                    else if (string.Equals(key, StatusKeys.Metallicize, StringComparison.OrdinalIgnoreCase))
                    {
                        AddBlock(stacks, applyDexterity: false);
                    }
                    else if (string.Equals(key, StatusKeys.Ritual, StringComparison.OrdinalIgnoreCase))
                    {
                        AddStatus(StatusKeys.Strength, stacks);
                    }
                    else if (string.Equals(key, StatusKeys.LoseStrengthAtTurnEnd, StringComparison.OrdinalIgnoreCase))
                    {
                        SetStatus(key, 0);
                        AddStatus(StatusKeys.Strength, -stacks);
                    }
                    else if (string.Equals(key, StatusKeys.LoseDexterityAtTurnEnd, StringComparison.OrdinalIgnoreCase))
                    {
                        SetStatus(key, 0);
                        AddStatus(StatusKeys.Dexterity, -stacks);
                    }
                }
            }

            CombatEvents.RaiseTurnEnded(this);
        }

        private void CopyKeysToBuffer()
        {
            _keyBuffer.Clear();
            foreach (var kvp in _statuses)
                _keyBuffer.Add(kvp.Key);
        }

        private void SyncPlayerHPIfNeeded()
        {
            if (TeamID == 0)
                OnSyncPersistentPlayerHP?.Invoke(_currentHP, _maxHP);
        }
    }
}
