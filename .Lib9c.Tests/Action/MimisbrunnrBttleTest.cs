﻿namespace Lib9c.Tests.Action
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Bencodex.Types;
    using Libplanet;
    using Libplanet.Action;
    using Libplanet.Crypto;
    using Nekoyume;
    using Nekoyume.Action;
    using Nekoyume.Battle;
    using Nekoyume.Model;
    using Nekoyume.Model.BattleStatus;
    using Nekoyume.Model.Item;
    using Nekoyume.Model.Mail;
    using Nekoyume.Model.State;
    using Nekoyume.TableData;
    using Xunit;

    public class MimisbrunnrBttleTest
    {
        private readonly TableSheets _tableSheets;

        private readonly Address _agentAddress;

        private readonly Address _avatarAddress;
        private readonly AvatarState _avatarState;

        private readonly Address _rankingMapAddress;

        private readonly WeeklyArenaState _weeklyArenaState;
        private readonly IAccountStateDelta _initialState;

        public MimisbrunnrBttleTest()
        {
            var sheets = TableSheetsImporter.ImportSheets();
            _tableSheets = new TableSheets(sheets);

            var privateKey = new PrivateKey();
            _agentAddress = privateKey.PublicKey.ToAddress();
            var agentState = new AgentState(_agentAddress);

            _avatarAddress = _agentAddress.Derive("avatar");
            _rankingMapAddress = _avatarAddress.Derive("ranking_map");
            _avatarState = new AvatarState(
                _avatarAddress,
                _agentAddress,
                0,
                _tableSheets.GetAvatarSheets(),
                new GameConfigState(sheets[nameof(GameConfigSheet)]),
                _rankingMapAddress
            )
            {
                level = 100,
            };
            agentState.avatarAddresses.Add(0, _avatarAddress);

            _weeklyArenaState = new WeeklyArenaState(0);

            _initialState = new State()
                .SetState(_weeklyArenaState.address, _weeklyArenaState.Serialize())
                .SetState(_agentAddress, agentState.Serialize())
                .SetState(_avatarAddress, _avatarState.Serialize())
                .SetState(_rankingMapAddress, new RankingMapState(_rankingMapAddress).Serialize());

            foreach (var (key, value) in sheets)
            {
                _initialState = _initialState
                    .SetState(Addresses.TableSheet.Derive(key), value.Serialize());
            }
        }

        [Theory]
        [InlineData(200, 101, 10010, true)]
        public void Execute(int avatarLevel, int worldId, int stageId, bool contains)
        {
            Assert.True(_tableSheets.WorldSheet.TryGetValue(worldId, out var worldRow));
            Assert.True(stageId >= worldRow.StageBegin);
            Assert.True(stageId <= worldRow.StageEnd);
            Assert.True(_tableSheets.StageSheet.TryGetValue(stageId, out _));

            var previousAvatarState = _initialState.GetAvatarState(_avatarAddress);
            previousAvatarState.level = avatarLevel;
            previousAvatarState.worldInformation = new WorldInformation(
                0,
                _tableSheets.WorldSheet,
                Math.Max(_tableSheets.StageSheet.First?.Id ?? 1, stageId - 1));

            var costumeId = _tableSheets
                .CostumeItemSheet
                .Values
                .First(r => r.ItemSubType == ItemSubType.FullCostume)
                .Id;
            var costume =
                ItemFactory.CreateItem(_tableSheets.ItemSheet[costumeId], new ItemEnhancementTest.TestRandom());
            previousAvatarState.inventory.AddItem(costume);

            var mimisbrunnrSheet = _tableSheets.MimisbrunnrSheet;
            if (!mimisbrunnrSheet.TryGetValue(stageId, out var mimisbrunnrSheetRow))
            {
                throw new SheetRowNotFoundException("MimisbrunnrSheet", stageId);
            }

            var equipmentRow = _tableSheets.EquipmentItemSheet.Values.First();
            var equipment = ItemFactory.CreateItemUsable(equipmentRow, default, 0);
            previousAvatarState.inventory.AddItem(equipment);

            foreach (var equipmentId in previousAvatarState.inventory.Equipments)
            {
                if (previousAvatarState.inventory.TryGetNonFungibleItem(equipmentId, out ItemUsable itemUsable))
                {
                    var elementalType = ((Equipment)itemUsable).ElementalType;
                    Assert.True(mimisbrunnrSheetRow.ElementalTypes.Exists(x => x == elementalType));
                }
            }

            var result = new CombinationConsumable.ResultModel()
            {
                id = default,
                gold = 0,
                actionPoint = 0,
                recipeId = 1,
                materials = new Dictionary<Material, int>(),
                itemUsable = equipment,
            };
            for (var i = 0; i < 100; i++)
            {
                var mail = new CombinationMail(result, i, default, 0);
                previousAvatarState.Update(mail);
            }

            var state = _initialState.SetState(_avatarAddress, previousAvatarState.Serialize());

            var action = new MimisbrunnrBattle()
            {
                costumes = new List<int> { costumeId },
                equipments = new List<Guid>() { equipment.ItemId },
                foods = new List<Guid>(),
                worldId = worldId,
                stageId = stageId,
                avatarAddress = _avatarAddress,
                WeeklyArenaAddress = _weeklyArenaState.address,
                RankingMapAddress = _rankingMapAddress,
            };

            Assert.Null(action.Result);

            var nextState = action.Execute(new ActionContext()
            {
                PreviousStates = state,
                Signer = _agentAddress,
                Random = new ItemEnhancementTest.TestRandom(),
                Rehearsal = false,
            });

            var nextAvatarState = nextState.GetAvatarState(_avatarAddress);
            var newWeeklyState = nextState.GetWeeklyArenaState(0);

            Assert.NotNull(action.Result);

            Assert.NotEmpty(action.Result.OfType<GetReward>());
            Assert.Equal(BattleLog.Result.Win, action.Result.result);
            Assert.Equal(contains, newWeeklyState.ContainsKey(_avatarAddress));
            Assert.True(nextAvatarState.worldInformation.IsStageCleared(stageId));
            Assert.Equal(30, nextAvatarState.mailBox.Count);
            if (contains)
            {
                Assert.True(
                    newWeeklyState[_avatarAddress].CombatPoint >
                    CPHelper.GetCP(nextAvatarState, _tableSheets.CharacterSheet)
                );
            }

            var value = nextState.GetState(_rankingMapAddress);

            var rankingMapState = new RankingMapState((Dictionary)value);
            var info = rankingMapState.GetRankingInfos(null).First();

            Assert.Equal(info.AgentAddress, _agentAddress);
            Assert.Equal(info.AvatarAddress, _avatarAddress);
        }
    }
}