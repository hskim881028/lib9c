namespace Lib9c.Tests.Model
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Lib9c.Tests.Action;
    using Libplanet.Action;
    using Nekoyume.Battle;
    using Nekoyume.Model;
    using Nekoyume.Model.BattleStatus;
    using Nekoyume.Model.Item;
    using Nekoyume.Model.Skill;
    using Nekoyume.Model.Stat;
    using Nekoyume.Model.State;
    using Priority_Queue;
    using Xunit;

    public class PlayerTest
    {
        private readonly IRandom _random;
        private readonly AvatarState _avatarState;
        private readonly TableSheets _tableSheets;

        public PlayerTest()
        {
            _random = new ItemEnhancementTest.TestRandom();
            _tableSheets = new TableSheets(TableSheetsImporter.ImportSheets());
            _avatarState = new AvatarState(
                default,
                default,
                0,
                _tableSheets.GetAvatarSheets(),
                new GameConfigState(),
                default
            );
        }

        [Fact]
        public void TickAlive()
        {
            var simulator = new StageSimulator(
                _random,
                _avatarState,
                new List<Guid>(),
                1,
                1,
                _tableSheets.GetStageSimulatorSheets()
            );
            var player = simulator.Player;
            var enemy = new Enemy(player, _tableSheets.CharacterSheet.Values.First(), 1);
            player.Targets.Add(enemy);
            player.InitAI();
            player.Tick();

            Assert.NotEmpty(simulator.Log);
            Assert.Equal(nameof(WaveTurnEnd), simulator.Log.Last().GetType().Name);
        }

        [Fact]
        public void TickDead()
        {
            var simulator = new StageSimulator(
                _random,
                _avatarState,
                new List<Guid>(),
                1,
                1,
                _tableSheets.GetStageSimulatorSheets()
            );
            var player = simulator.Player;
            var enemy = new Enemy(player, _tableSheets.CharacterSheet.Values.First(), 1);
            player.Targets.Add(enemy);
            player.InitAI();
            player.CurrentHP = -1;

            Assert.True(player.IsDead);

            player.Tick();

            Assert.NotEmpty(simulator.Log);
            Assert.Equal(nameof(WaveTurnEnd), simulator.Log.Last().GetType().Name);
        }

        [Theory]
        [InlineData(SkillCategory.DoubleAttack)]
        [InlineData(SkillCategory.AreaAttack)]
        public void UseDoubleAttack(SkillCategory skillCategory)
        {
            var skill = SkillFactory.Get(
                _tableSheets.SkillSheet.Values.First(r => r.SkillCategory == skillCategory),
                100,
                100
            );
            var simulator = new StageSimulator(
                _random,
                _avatarState,
                new List<Guid>(),
                1,
                1,
                _tableSheets.GetStageSimulatorSheets()
            );
            var player = simulator.Player;

            var enemy = new Enemy(player, _tableSheets.CharacterSheet.Values.First(), 1)
            {
                CurrentHP = 1,
            };
            player.Targets.Add(enemy);
            simulator.Characters = new SimplePriorityQueue<CharacterBase, decimal>();
            simulator.Characters.Enqueue(enemy, 0);
            player.InitAI();
            player.OverrideSkill(skill);
            Assert.Single(player.Skills);

            player.Tick();

            Assert.Single(simulator.Log.OfType<Dead>());
        }

        [Fact]
        public void SetCostumeStat()
        {
            var row = _tableSheets.CostumeStatSheet.Values.First(r => r.StatType == StatType.ATK);
            var costume = (Costume)ItemFactory.CreateItem(_tableSheets.ItemSheet[row.CostumeId], _random);
            costume.equipped = true;
            _avatarState.inventory.AddItem(costume);

            var player = new Player(
                _avatarState,
                _tableSheets.CharacterSheet,
                _tableSheets.CharacterLevelSheet,
                _tableSheets.EquipmentItemSetEffectSheet
            );
            player.SetCostumeStat(_tableSheets.CostumeStatSheet);

            Assert.Equal(row.Stat, player.Stats.OptionalStats.ATK);

            var copy = (Player)player.Clone();

            Assert.Equal(row.Stat, copy.Stats.OptionalStats.ATK);
        }

        [Fact]
        public void GetExp()
        {
            var row = _tableSheets.CostumeStatSheet.Values.First(r => r.StatType == StatType.HP);
            var costume = (Costume)ItemFactory.CreateItem(_tableSheets.ItemSheet[row.CostumeId], _random);
            costume.equipped = true;
            _avatarState.inventory.AddItem(costume);

            var player = new Player(
                _avatarState,
                _tableSheets.CharacterSheet,
                _tableSheets.CharacterLevelSheet,
                _tableSheets.EquipmentItemSetEffectSheet
            );
            player.SetCostumeStat(_tableSheets.CostumeStatSheet);

            Assert.Equal(600, player.HP);
            Assert.Equal(600, player.CurrentHP);

            Assert.Equal(1, player.Level);

            player.CurrentHP -= 10;

            Assert.Equal(590, player.CurrentHP);

            var requiredExp = _tableSheets.CharacterLevelSheet[1].ExpNeed;
            player.GetExp(requiredExp);

            Assert.Equal(2, player.Level);
            Assert.Equal(612, player.HP);
            Assert.Equal(590, player.CurrentHP);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 3)]
        [InlineData(5, 5)]
        public void GetExpV2(int level, int count = 1)
        {
            var player = new Player(
                level,
                _tableSheets.CharacterSheet,
                _tableSheets.CharacterLevelSheet,
                _tableSheets.EquipmentItemSetEffectSheet);

            for (int i = 0; i < count; ++i)
            {
                var requiredExp = _tableSheets.CharacterLevelSheet[level].ExpNeed;
                player.GetExpV2(requiredExp);

                Assert.Equal(level + 1, player.Level);
                ++level;
            }
        }

        [Fact]
        public void MaxLevelTest()
        {
            var maxLevel = _tableSheets.CharacterLevelSheet.Max(row => row.Value.Level);
            var player = new Player(
                maxLevel,
                _tableSheets.CharacterSheet,
                _tableSheets.CharacterLevelSheet,
                _tableSheets.EquipmentItemSetEffectSheet);

            var expRow = _tableSheets.CharacterLevelSheet[maxLevel];
            var maxLevelExp = expRow.Exp;
            var requiredExp = expRow.ExpNeed;
            player.GetExpV2(requiredExp);

            Assert.Equal(maxLevel, player.Level);
            Assert.Equal(requiredExp - 1, player.Exp.Current - expRow.Exp);
        }
    }
}
