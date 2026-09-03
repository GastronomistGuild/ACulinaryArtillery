using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ACulinaryArtillery
{
    public class SapProperties
    {
        public double dripChance = 1;
        public double dripTime = 12;
        public int[] seasons = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
        public string sap = "game:waterportion";
        public int dripCount = 1;
    }

    public class BlockEntitySpile : BlockEntity
    {
        public static HashSet<BlockPos> CachedSpiledTreeBlocks = [];
        public static Dictionary<BlockPos, Stack<BlockPos>> CachedTreesBySpilePos = [];
        public double timer;

        public BlockFacing Facing()
        {
            string[] parts = Block.Code.Path.Split('-');
            return BlockFacing.FromCode(parts[parts.Length - 1]);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            RegisterGameTickListener(SapDrip, 5000);
            if (timer == -1000) timer = Api.World.Calendar.TotalHours;

            if ((Block as BlockSpile)?.FindTree(Api.World.BlockAccessor, Pos.AddCopy(Facing())) is Stack<BlockPos> tree)
            {
                CachedSpiledTreeBlocks.AddRange(tree);
                CachedTreesBySpilePos.TryAdd(Pos, tree);
            }
        }

        public override void OnBlockPlaced(ItemStack? byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);

            timer = Api.World.Calendar.TotalHours;
        }

        public override void OnBlockRemoved()
        {
            if (CachedTreesBySpilePos.TryGetValue(Pos) is Stack<BlockPos> tree)
            {
                CachedTreesBySpilePos.Remove(Pos);
                foreach (BlockPos pos in tree)
                {
                    CachedSpiledTreeBlocks.Remove(pos);
                }
            }
        }

        public void SapDrip(float dt)
        {
            BlockPos containerpos = posForward(0, -1, 0);
            if (Api.World.BlockAccessor.GetBlock(containerpos) is not BlockLiquidContainerBase container) return;
            if (Api.World.BlockAccessor.GetBlock(posForward(1, 0, 0))?.Attributes?["sapProperties"]?.AsObject<SapProperties>() is not SapProperties xylem) return;

            while (Api.World.Calendar.TotalHours - timer >= xylem.dripTime)
            {
                timer += xylem.dripTime;

                if (Api.World.Rand.NextDouble() > xylem.dripChance || !xylem.seasons.Contains(GetMonth(timer))) return;

                container.TryPutLiquid(containerpos, new(Api.World.GetItem(xylem.sap)), xylem.dripCount);
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetDouble("timer", timer);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            timer = tree.GetDouble("timer", -1000);
        }

        public int GetMonth(double pastTime)
        {
            int pastDay = Api.World.Calendar.DayOfYear - (int)((Api.World.Calendar.TotalHours - pastTime) / Api.World.Calendar.HoursPerDay);
            if (pastDay < 0) pastDay += Api.World.Calendar.DaysPerYear;

            int month = (pastDay / Api.World.Calendar.DaysPerMonth);
            return (Api.World.Calendar.GetHemisphere(Pos) == EnumHemisphere.North ? month : (month + 6) % 12) + 1;
        }

        public BlockPos posForward(int offset, int height, int otheraxis)
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
