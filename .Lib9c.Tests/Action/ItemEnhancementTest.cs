namespace Lib9c.Tests.Action
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Globalization;
    using System.Linq;
    using Bencodex.Types;
    using Libplanet;
    using Libplanet.Action;
    using Libplanet.Assets;
    using Libplanet.Crypto;
    using Nekoyume;
    using Nekoyume.Action;
    using Nekoyume.Model.Item;
    using Nekoyume.Model.State;
    using Nekoyume.TableData;
    using Xunit;

    // FIXME: Should work without .csv files
    public class ItemEnhancementTest : IDisposable
    {
        private readonly IRandom _random;
        private TableSheetsState _tableSheetsState;

        public ItemEnhancementTest()
        {
            _tableSheetsState = TableSheetsImporter.ImportTableSheets();
            _random = new TestRandom();
        }

        public void Dispose()
        {
            _tableSheetsState = null;
        }

        [Theory]
        [InlineData(0, 1, 1000)]
        [InlineData(3, 4, 0)]
        public void Execute(int level, int expectedLevel, int expectedGold)
        {
            var privateKey = new PrivateKey();
            var agentAddress = privateKey.PublicKey.ToAddress();
            var agentState = new AgentState(agentAddress);

            var tableSheets = TableSheets.FromTableSheetsState(_tableSheetsState);
            var avatarAddress = agentAddress.Derive("avatar");
            var avatarState = new AvatarState(
                avatarAddress,
                agentAddress,
                0,
                tableSheets,
                new GameConfigState()
            );

            agentState.avatarAddresses.Add(0, avatarAddress);

            var row = tableSheets.EquipmentItemSheet.Values.First(r => r.Grade == 1);
            var equipment = (Equipment)ItemFactory.CreateItemUsable(row, default, 0, level);
            var materialId = Guid.NewGuid();
            var material = (Equipment)ItemFactory.CreateItemUsable(row, materialId, 0, level);

            avatarState.inventory.AddItem(equipment, 1);
            avatarState.inventory.AddItem(material, 1);

            avatarState.worldInformation.ClearStage(1, 1, 1, tableSheets.WorldSheet, tableSheets.WorldUnlockSheet);

            var slotAddress =
                avatarAddress.Derive(string.Format(CultureInfo.InvariantCulture, CombinationSlotState.DeriveFormat, 0));

            Assert.Equal(level, equipment.level);

            var gold = new GoldCurrencyState(new Currency("NCG", 2, minter: null));

            var state = new State(ImmutableDictionary<Address, IValue>.Empty
                .Add(agentAddress, agentState.Serialize())
                .Add(avatarAddress, avatarState.Serialize())
                .Add(slotAddress, new CombinationSlotState(slotAddress, 0).Serialize())
                .Add(_tableSheetsState.address, _tableSheetsState.Serialize()))
                .SetState(GoldCurrencyState.Address, gold.Serialize())
                .MintAsset(GoldCurrencyState.Address, gold.Currency * 100000000000)
                .TransferAsset(Addresses.GoldCurrency, agentAddress, gold.Currency * 1000);

            Assert.Equal(gold.Currency * 99999999000, state.GetBalance(Addresses.GoldCurrency, gold.Currency));
            Assert.Equal(gold.Currency * 1000, state.GetBalance(agentAddress, gold.Currency));

            var action = new ItemEnhancement()
            {
                itemId = default,
                materialIds = new[] { materialId },
                avatarAddress = avatarAddress,
                slotIndex = 0,
            };

            var nextState = action.Execute(new ActionContext()
            {
                PreviousStates = state,
                Signer = agentAddress,
                BlockIndex = 1,
                Random = _random,
            });

            var slotState = nextState.GetCombinationSlotState(avatarAddress, 0);
            var resultEquipment = (Equipment)slotState.Result.itemUsable;
            Assert.Equal(expectedLevel, resultEquipment.level);
            Assert.Equal(default, resultEquipment.ItemId);
            Assert.Equal(expectedGold * gold.Currency, nextState.GetBalance(agentAddress, gold.Currency));
            Assert.Equal(
                (1000 - expectedGold) * gold.Currency,
                nextState.GetBalance(Addresses.Blacksmith, gold.Currency)
            );
        }

        [Fact]
        public void Rehearsal()
        {
            var agentAddress = default(Address);
            var avatarAddress = agentAddress.Derive("avatar");
            var slotAddress =
                avatarAddress.Derive(string.Format(CultureInfo.InvariantCulture, CombinationSlotState.DeriveFormat, 0));

            var action = new ItemEnhancement()
            {
                itemId = default,
                materialIds = new[] { Guid.NewGuid() },
                avatarAddress = avatarAddress,
                slotIndex = 0,
            };

            var gold = new GoldCurrencyState(new Currency("NCG", 2, minter: null));

            var updatedAddresses = new List<Address>()
            {
                agentAddress,
                avatarAddress,
                slotAddress,
                Addresses.GoldCurrency,
                Addresses.Blacksmith,
            };

            var state = new State()
                .SetState(GoldCurrencyState.Address, gold.Serialize());

            var nextState = action.Execute(new ActionContext()
            {
                PreviousStates = state,
                Signer = agentAddress,
                BlockIndex = 0,
                Rehearsal = true,
            });

            Assert.Equal(updatedAddresses.ToImmutableHashSet(), nextState.UpdatedAddresses);
        }

        public class TestRandom : IRandom
        {
            private readonly System.Random _random = new System.Random();

            public int Next()
            {
                return _random.Next();
            }

            public int Next(int maxValue)
            {
                return _random.Next(maxValue);
            }

            public int Next(int minValue, int maxValue)
            {
                return _random.Next(minValue, maxValue);
            }

            public void NextBytes(byte[] buffer)
            {
                _random.NextBytes(buffer);
            }

            public double NextDouble()
            {
                return _random.NextDouble();
            }
        }
    }
}
