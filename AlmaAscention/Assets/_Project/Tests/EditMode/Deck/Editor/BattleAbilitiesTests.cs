using System.Collections.Generic;
using AlmaAscention.Combat;
using AlmaAscention.Deck;
using CNF.Data;
using NUnit.Framework;
using UnityEngine;

public sealed class BattleAbilitiesTests
{
    [Test]
    public void DeckSnapshotRestoresCardZonesStateAndRandomSequence()
    {
        var data = ScriptableObject.CreateInstance<CardDataSO>();
        data.CardID = "snapshot_test";
        try
        {
            var master = new MasterDeck();
            master.CreateAndAddCardOrNull(data);
            master.CreateAndAddCardOrNull(data);
            var deck = new BattleDeckManager(master, new CombatRandom(42));
            deck.Draw(1);
            var card = deck.HandCards[0];
            string id = card.InstanceGUID;
            var snapshot = deck.CaptureSnapshot();

            card.Upgrade();
            card.BonusDamage = 9;
            deck.DiscardFromHand(id);
            deck.Draw(2);
            string expectedNext = deck.HandCards[0].InstanceGUID;

            deck.RestoreSnapshot(snapshot);
            Assert.That(deck.HandCards.Count, Is.EqualTo(1));
            Assert.That(deck.HandCards[0].InstanceGUID, Is.EqualTo(id));
            Assert.That(deck.HandCards[0].IsUpgraded, Is.False);
            Assert.That(deck.HandCards[0].BonusDamage, Is.Zero);
            Assert.That(deck.DiscardPile.Count, Is.Zero);
            deck.DiscardFromHand(id);
            deck.Draw(2);
            Assert.That(deck.HandCards[0].InstanceGUID, Is.EqualTo(expectedNext));
        }
        finally { Object.DestroyImmediate(data); }
    }

    [Test]
    public void EnemyBrainRestoresPhaseAndIntentSequence()
    {
        var data = ScriptableObject.CreateInstance<MonsterDataSO>();
        try
        {
            data.Phases = new List<MonsterPhaseData>
            {
                new MonsterPhaseData { PhaseID = "P1", HPRatioThreshold = 1f,
                    SelectionMode = EPatternSelectionMode.Sequential,
                    Patterns = new List<MonsterSkillPatternData> { new MonsterSkillPatternData { PatternID = "A" } } },
                new MonsterPhaseData { PhaseID = "P2", HPRatioThreshold = .5f,
                    SelectionMode = EPatternSelectionMode.Sequential,
                    Patterns = new List<MonsterSkillPatternData>
                    {
                        new MonsterSkillPatternData { PatternID = "B" },
                        new MonsterSkillPatternData { PatternID = "C" }
                    } }
            };
            Assert.That(data.ValidateConfiguration(), Is.Empty);
            var brain = new EnemyBrain(data, new CombatRandom(7));
            Assert.That(brain.SelectNextPattern(1f).PatternID, Is.EqualTo("A"));
            var snapshot = brain.CaptureSnapshot();
            Assert.That(brain.SelectNextPattern(.4f).PatternID, Is.EqualTo("B"));
            Assert.That(brain.CurrentPhase.PhaseID, Is.EqualTo("P2"));

            brain.RestoreSnapshot(snapshot);
            Assert.That(brain.CurrentPhase.PhaseID, Is.EqualTo("P1"));
            Assert.That(brain.SelectNextPattern(.4f).PatternID, Is.EqualTo("B"));
        }
        finally { Object.DestroyImmediate(data); }
    }

    [Test]
    public void MonsterConfigurationReportsInvalidPhaseAndDuplicatePattern()
    {
        var data = ScriptableObject.CreateInstance<MonsterDataSO>();
        try
        {
            data.Phases = new List<MonsterPhaseData>
            {
                new MonsterPhaseData { PhaseID = "P1", HPRatioThreshold = 1f,
                    Patterns = new List<MonsterSkillPatternData> { new MonsterSkillPatternData { PatternID = "Same" } } },
                new MonsterPhaseData { PhaseID = "P2", HPRatioThreshold = .5f,
                    Patterns = new List<MonsterSkillPatternData> { new MonsterSkillPatternData { PatternID = "Same" } } }
            };
            Assert.That(data.ValidateConfiguration(), Has.Count.EqualTo(1));
        }
        finally { Object.DestroyImmediate(data); }
    }
}
