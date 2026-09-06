using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ACulinaryArtillery
{
    [DocumentAsJson]
    public class SapProperties
    {
        /// <summary>
        /// The chance (out of 1) for a drip to succeed on each tick.
        /// </summary>
        [DocumentAsJson("Optional")]
        public double dripChance = 1;

        /// <summary>
        /// The number of hours between each drip tick. (Checked once per day)
        /// </summary>
        [DocumentAsJson("Optional")]
        public double dripHours = 12;

        /// <summary>
        /// The liquid produced by this xylem.
        /// </summary>
        [DocumentAsJson("Recommended")]
        public string sap = "game:waterportion";

        /// <summary>
        /// The amount of liquid produced by this xylem per tick.
        /// </summary>
        [DocumentAsJson("Optional")]
        public float dripLitres = 0.01f;

        /// <summary>
        /// The amount of liquid produced by this xylem when stimulated by temperature shifts.
        ///
        /// Default is base litres * 2 so that there is always at least 1 extra item in the liquid stack.
        /// </summary>
        [DocumentAsJson("Optional", "dripLitres * 2")]
        public float boostedDripLitres = 0;

        /// <summary>
        /// The range of temperatures in which this xylem produces.
        /// </summary>
        [DocumentAsJson("Recommended")]
        public int[] temperatureRange = [-5, 40];

        public static SapProperties? ReadFrom(CollectibleObject obj)
        {
            if (obj.Attributes?["sapProperties"]?.AsObject<SapProperties>() is not SapProperties xylem) return null;

            if (xylem.boostedDripLitres == 0) xylem.boostedDripLitres = xylem.dripLitres * 2f;

            return xylem;
        }

        public static SapProperties? ReadFrom(ItemStack stack)
        {
            return ReadFrom(stack.Collectible);
        }
    }

    public class BlockEntitySpile : BlockEntity
    {
        public double dripTimerHours = -1000;
        public RoomRegistry? roomreg;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            RegisterGameTickListener(SapDrip, 5000);
            if (dripTimerHours == -1000) dripTimerHours = Api.World.Calendar.TotalHours;

            roomreg = api.ModLoader.GetModSystem<RoomRegistry>();
        }

        public override void OnBlockPlaced(ItemStack? byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);

            dripTimerHours = Api.World.Calendar.TotalHours;
        }

        protected float getGreenhouseTempBonus()
        {
            if (Api.World.BlockAccessor.GetRainMapHeightAt(Pos) > Pos.Y) // Fast pre-check
            {
                Room? room = roomreg?.GetRoomForPosition(Pos);
                int roomness = (room != null && room.SkylightCount > room.NonSkylightCount && room.ExitCount == 0) ? 1 : 0;
                if (roomness > 0) return 5;
            }
            return 0;
        }

        public enum EnumSpileClimateStatus
        {
            Inactive,
            Active,
            Boosted
        }

        public EnumSpileClimateStatus GetClimateStatus(SapProperties xylem, float baseDays)
        {
            // Can't read if region isn't loaded.
            if (Api.World.BlockAccessor.GetClimateAt(Pos, EnumGetClimateMode.WorldGenValues) is not ClimateCondition baseClimate) return EnumSpileClimateStatus.Inactive;

            // Check if we cross over a temperature range limit and back again using quarter-day increments.
            // If so, the spile is boosted for the day.
            // Otherwise, if the temps are in range, the spile is active for the day.
            List<float> tempsForToday = [];
            float greenhouseBonus = getGreenhouseTempBonus();
            for (int i = 0; i < 4; i++)
            {
                float checkDays = (int)baseDays + (0.25f * i);
                float temp = Api.World.BlockAccessor.GetClimateAt(Pos, baseClimate, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, checkDays).Temperature;
                temp += greenhouseBonus;
                tempsForToday.Add(temp);
            }

            bool lowerThanRange = false;
            bool higherThanRange = false;
            bool active = false;
            foreach (float temp in tempsForToday)
            {
                if (xylem.temperatureRange[0] < temp && temp < xylem.temperatureRange[1]) active = true;
                if (temp < xylem.temperatureRange[0]) lowerThanRange = true;
                if (xylem.temperatureRange[1] < temp) higherThanRange = true;
            }

            // Account for very small temperature ranges
            if (lowerThanRange && higherThanRange) active = true;

            if (active && (lowerThanRange || higherThanRange))
            {
                return EnumSpileClimateStatus.Boosted;
            }
            else if (active)
            {
                return EnumSpileClimateStatus.Active;
            }
            else
            {
                return EnumSpileClimateStatus.Inactive;
            }
        }

        public void SapDrip(float dt)
        {
            BlockPos containerPos = PosForward(0, -1, 0);
            if (Api.World.BlockAccessor.GetBlock(containerPos) is not BlockLiquidContainerBase container) return;
            if (SapProperties.ReadFrom(Api.World.BlockAccessor.GetBlock(PosForward(1, 0, 0))) is not SapProperties xylem) return;

            while (Api.World.Calendar.TotalHours - dripTimerHours >= xylem.dripHours)
            {
                dripTimerHours += xylem.dripHours;

                EnumSpileClimateStatus status = GetClimateStatus(xylem, (float)(dripTimerHours / Api.World.Calendar.HoursPerDay));
                bool active = status is EnumSpileClimateStatus.Active or EnumSpileClimateStatus.Boosted;
                bool boosted = status is EnumSpileClimateStatus.Boosted;

                if (Api.World.Rand.NextDouble() > xylem.dripChance || !active) continue;
                float dripLitres = boosted ? xylem.boostedDripLitres : xylem.dripLitres;

                container.TryPutLiquid(containerPos, new(Api.World.GetItem(xylem.sap), 999999), dripLitres);
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetDouble("timer", dripTimerHours);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            dripTimerHours = tree.GetDouble("timer", -1000);
        }

        public BlockPos PosForward(int offset, int height, int otheraxis)
        {
            return Block.Shape.rotateY switch
            {
                0 => Pos.AddCopy(otheraxis, height, -offset),
                90 => Pos.AddCopy(-offset, height, otheraxis),
                180 => Pos.AddCopy(otheraxis, height, offset),
                270 => Pos.AddCopy(offset, height, otheraxis),
                _ => Pos
            };
        }
    }
}
