using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ACulinaryArtillery
{
    public class BlockSpile : Block
    {
        // Using CanPlaceBlock causes the error message to be overriden by BlockBehaviorHorizontalAttachable
        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            Block? attachingTo = world.BlockAccessor.GetBlock(blockSel.Position.AddCopy(blockSel.Face, -1));
            if (blockSel.Face.IsHorizontal && SapProperties.ReadFrom(attachingTo) == null)
            {
                failureCode = "notspileable";
                return false;
            }

            if (blockSel.Face.IsHorizontal && HasSpile(world.BlockAccessor, blockSel.Position.Copy(), blockSel.Face))
            {
                failureCode = "alreadyhasspile";
                return false;
            }

            return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
        }

        bool HasSpile(IBlockAccessor blockAccess, BlockPos pos, BlockFacing face)
        {
            pos.Add(face, -2); // check the opposite side of the log
            if (blockAccess.GetBlock(pos) is BlockSpile) return true;

            pos.Add(face, 1); // move back into the log
            face = face.GetCW(); // turn 90° clockwise
            pos.Add(face, -1); // move to the next side
            if (blockAccess.GetBlock(pos) is BlockSpile) return true;

            pos.Add(face, 2); // and finally move back through the log to the third side
            if (blockAccess.GetBlock(pos) is BlockSpile) return true;

            return false;
        }

        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            StringBuilder sb = new();

            sb.AppendLine(Lang.Get("aculinaryartillery:blockdesc-spile"));

            if (GetBlockEntity<BlockEntitySpile>(pos) is BlockEntitySpile bes)
            {
                if (SapProperties.ReadFrom(world.BlockAccessor.GetBlock(bes.PosForward(1, 0, 0))) is SapProperties xylem)
                {
                    switch (bes.GetClimateStatus(xylem, (float)world.Calendar.TotalDays))
                    {
                        case BlockEntitySpile.EnumSpileClimateStatus.Boosted:
                            {
                                // Reflect that the xylem may be configured to have no seasonal bonus
                                if (xylem.boostedDripLitres > xylem.dripLitres)
                                {
                                    sb.AppendLine(Lang.Get("aculinaryartillery:spile-boosted"));
                                }
                                else
                                {
                                    sb.AppendLine(Lang.Get("aculinaryartillery:spile-inseason"));
                                }
                                break;
                            }
                        case BlockEntitySpile.EnumSpileClimateStatus.Active:
                            {
                                sb.AppendLine(Lang.Get("aculinaryartillery:spile-inseason"));
                                break;
                            }
                        case BlockEntitySpile.EnumSpileClimateStatus.Inactive:
                            {
                                sb.AppendLine(Lang.Get("aculinaryartillery:spile-outofseason"));
                                break;
                            }
                    }
                }
                else
                {
                    sb.AppendLine(Lang.Get("aculinaryartillery:spile-outofseason"));
                }
            }

            return sb.ToString();
        }
    }
}
