using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ACulinaryArtillery
{
    public class BlockSpile : Block
    {
        // Using CanPlaceBlock causes the error message to be overriden by BlockBehaviorHorizontalAttachable
        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            if (blockSel.Position.AddCopy(blockSel.Face, -1) is not BlockPos attachingTo)
            {
                failureCode = "notattached";
                return false;
            }

            if (blockSel.Face.IsHorizontal)
            {
                if (world.BlockAccessor.GetBlock(attachingTo).Attributes?["sapProperties"]?.AsObject<SapProperties>() == null)
                {
                    failureCode = "notspileable";
                    return false;
                }

                if (!BlockEntitySpile.CachedTreesBySpilePos.ContainsKey(blockSel.Position) && treeHasSpile(world.BlockAccessor, attachingTo.Copy()))
                {
                    failureCode = "alreadyhasspile";
                    return false;
                }
            }

            return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
        }

        // We need to prevent the HorizontalAttachable behavior of defaulting to
        // any available face (e.g. when placed while looking at a block's underside)
        // to prevent cheesing the one per tree rule.
        public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
        {
            if (blockSel.Position.AddCopy(blockSel.Face, -1) is not BlockPos attachingTo)
            {
                return false;
            }

            if (blockSel.Face.IsHorizontal)
            {
                if (world.BlockAccessor.GetBlock(attachingTo)?.Attributes?["sapProperties"]?.AsObject<SapProperties>() == null)
                {
                    return false;
                }

                if (!BlockEntitySpile.CachedTreesBySpilePos.ContainsKey(blockSel.Position) && treeHasSpile(world.BlockAccessor, attachingTo.Copy()))
                {
                    return false;
                }
            }

            return base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode);
        }

        public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(world, blockPos, byItemStack);
        }

        /// <summary>
        /// Check if there is a spile anywhere on the tree. Searches in all directions.
        /// </summary>
        private bool treeHasSpile(IBlockAccessor blockAccess, BlockPos startPos)
        {
            if (BlockEntitySpile.CachedSpiledTreeBlocks.Contains(startPos))
            {
                return true;
            }

            Stack<BlockPos> tree = FindTree(blockAccess, startPos);
            return tree.Any(pos => blockHasSpile(blockAccess, pos));
        }

        // Adapted with slight modification from ItemAxe.FindTree, can probably be trimmed down more but it works
        public Stack<BlockPos> FindTree(IBlockAccessor blockAccess, BlockPos startPos)
        {
            Queue<Vec4i> queue = new Queue<Vec4i>();
            Queue<Vec4i> leafqueue = new Queue<Vec4i>();
            HashSet<BlockPos> checkedPositions = new HashSet<BlockPos>();
            Stack<BlockPos> foundPositions = new Stack<BlockPos>();

            Block block = blockAccess.GetBlock(startPos);
            if (block.Code == null) return foundPositions;

            string treeFellingGroupCode = block.Attributes?["treeFellingGroupCode"].AsString() ?? "";
            int spreadIndex = block.Attributes?["treeFellingGroupSpreadIndex"].AsInt(0) ?? 0;
            if (block.Attributes?["treeFellingCanChop"].AsBool(true) == false) return foundPositions;

            EnumTreeFellingBehavior bh = EnumTreeFellingBehavior.Chop;

            if (block is ICustomTreeFellingBehavior ctfbh)
            {
                bh = ctfbh.GetTreeFellingBehavior(startPos, null, spreadIndex);
                if (bh == EnumTreeFellingBehavior.NoChop)
                {
                    return foundPositions;
                }
            }


            // Must start with a log
            if (spreadIndex < 2) return foundPositions;
            if (treeFellingGroupCode == null) return foundPositions;

            queue.Enqueue(new Vec4i(startPos, spreadIndex));
            checkedPositions.Add(startPos);
            const int LeafGroups = 7;
            int[] adjacentLeafGroupsCounts = new int[LeafGroups];

            while (queue.Count > 0)
            {
                Vec4i pos = queue.Dequeue();
                foundPositions.Push(new BlockPos(pos.X, pos.Y, pos.Z));   // dimension-correct because pos.Y contains the dimension
                if (foundPositions.Count > 2500) break;

                block = blockAccess.GetBlockRaw(pos.X, pos.Y, pos.Z, BlockLayersAccess.Solid);
                if (block is ICustomTreeFellingBehavior ctfbhh)
                {
                    bh = ctfbhh.GetTreeFellingBehavior(startPos, null, spreadIndex);
                }
                if (bh == EnumTreeFellingBehavior.NoChop) continue;

                onTreeBlock(pos, blockAccess, checkedPositions, startPos, bh == EnumTreeFellingBehavior.ChopSpreadVertical, treeFellingGroupCode, queue, leafqueue, adjacentLeafGroupsCounts);
            }

            // Find which is the most prevalent of the 7 possible adjacentLeafGroups
            int maxCount = 0;
            int maxI = -1;
            for (int i = 0; i < adjacentLeafGroupsCounts.Length; i++)
            {
                if (adjacentLeafGroupsCounts[i] > maxCount)
                {
                    maxCount = adjacentLeafGroupsCounts[i];
                    maxI = i;
                }
            }
            // If we found adjacentleaves using the leafgroup system, update the treeFellingGroupCode for the leaves search, using the most commonly found group
            // The purpose of this is to avoid chopping the "wrong" leaf in those cases where trees are growing close together and one of tree 2's leaves is the first leaf found when chopping tree 1
            if (maxI >= 0) treeFellingGroupCode = (maxI + 1) + treeFellingGroupCode;

            while (leafqueue.Count > 0)
            {
                Vec4i pos = leafqueue.Dequeue();
                foundPositions.Push(new BlockPos(pos.X, pos.Y, pos.Z));   // dimension-correct because pos.Y contains the dimension
                if (foundPositions.Count > 2500) break;

                onTreeBlock(pos, blockAccess, checkedPositions, startPos, bh == EnumTreeFellingBehavior.ChopSpreadVertical, treeFellingGroupCode, leafqueue, null, null);
            }

            return foundPositions;
        }

        private void onTreeBlock(Vec4i pos, IBlockAccessor blockAccessor, HashSet<BlockPos> checkedPositions, BlockPos startPos, bool chopSpreadVertical, string treeFellingGroupCode, Queue<Vec4i> queue, Queue<Vec4i>? leafqueue, int[]? adjacentLeaves)
        {
            Queue<Vec4i> outqueue;
            for (int i = 0; i < Vec3i.DirectAndIndirectNeighbours.Length; i++)
            {
                Vec3i facing = Vec3i.DirectAndIndirectNeighbours[i];
                BlockPos neibPos = new BlockPos(pos.X + facing.X, pos.Y + facing.Y, pos.Z + facing.Z);

                float hordist = GameMath.Sqrt(neibPos.HorDistanceSqTo(startPos.X, startPos.Z));
                float vertdist = (neibPos.Y - startPos.Y);

                // Removed this segment so that we check the entire tree.
                //
                // "only breaks blocks inside an upside down square base pyramid"
                // float f = chopSpreadVertical ? 0.5f : 2;
                // if (hordist - 1 >= f * vertdist) continue;
                if (checkedPositions.Contains(neibPos)) continue;

                Block block = blockAccessor.GetBlock(neibPos, BlockLayersAccess.Solid);
                if (block.Code == null || block.Id == 0) continue;   // Skip air blocks

                string ngcode = block.Attributes?["treeFellingGroupCode"].AsString() ?? "";

                // Only break the same type tree blocks
                if (ngcode != treeFellingGroupCode)
                {
                    if (ngcode == null || leafqueue == null) continue;
                    // Leaves now can carry treeSubType value of 1-7 therefore do a separate check for the leaves
                    if (block.BlockMaterial == EnumBlockMaterial.Leaves && ngcode.Length == treeFellingGroupCode.Length + 1 && ngcode.EndsWithOrdinal(treeFellingGroupCode))
                    {
                        outqueue = leafqueue;
                        int leafGroup = GameMath.Clamp(ngcode[0] - '0', 1, 7);
                        if (adjacentLeaves != null) adjacentLeaves[leafGroup - 1]++;
                    }
                    else continue;
                }
                else outqueue = queue;

                // Only spread from "high to low". i.e. spread from log to leaves, but not from leaves to logs
                int nspreadIndex = block.Attributes?["treeFellingGroupSpreadIndex"].AsInt(0) ?? 0;
                if (pos.W < nspreadIndex) continue;

                checkedPositions.Add(neibPos);

                if (chopSpreadVertical && !facing.Equals(0, 1, 0) && nspreadIndex > 0) continue;

                outqueue.Enqueue(new Vec4i(neibPos, nspreadIndex));
            }
        }

        /// <summary>
        /// Check if a block has a spile attached to it.
        /// </summary>
        /// <param name="pos">The block to check, *not* the spile block location.</param>
        /// <returns></returns>
        private bool blockHasSpile(IBlockAccessor blockAccess, BlockPos pos)
        {
            foreach (BlockFacing face in BlockFacing.HORIZONTALS)
            {
                if (blockAccess.GetBlockOnSide(pos, face) is BlockSpile spile)
                {
                    // Make sure the spile is actually oriented how it should be if it's
                    // attached to this tree.
                    string[] parts = spile.Code.Path.Split('-');
                    if (BlockFacing.FromCode(parts[parts.Length - 1]).Opposite == face)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
