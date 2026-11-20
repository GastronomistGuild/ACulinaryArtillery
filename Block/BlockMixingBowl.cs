using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace ACulinaryArtillery
{
    public class BlockMixingBowl : BlockMPBase
    {
        public int CapacityLitres { get; set; }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            CapacityLitres = Attributes?["capacityLitres"]?.AsInt(CapacityLitres) ?? CapacityLitres;
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            if (base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode))
            {
                if (!tryConnect(world, byPlayer, blockSel.Position, BlockFacing.UP))
                {
                    tryConnect(world, byPlayer, blockSel.Position, BlockFacing.DOWN);
                }

                return true;
            }

            return false;
        }

        public override bool DoPartialSelection(IWorldAccessor world, BlockPos pos)
        {
            return true;
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityMixingBowl beBowl)
            {
                if (byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack?.Collectible is BlockCookingContainer
                    && byPlayer.InventoryManager.ActiveHotbarSlot.TryPutInto(world, beBowl.Inventory[0]) > 0)
                {
                    return true;
                }

                if (byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack?.Collectible.Attributes?.IsTrue("mealContainer") == true
                    && beBowl.Inventory[1].Itemstack?.Collectible is BlockCookedContainer pot)
                {
                    ItemSlot targetSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
                    if (byPlayer.InventoryManager.ActiveHotbarSlot.StackSize > 1)
                    {
                        targetSlot = new DummySlot(targetSlot.TakeOut(1));
                        byPlayer.InventoryManager.ActiveHotbarSlot.MarkDirty();
                        pot.ServeIntoStack(targetSlot, beBowl.Inventory[1], world);
                        if (!byPlayer.InventoryManager.TryGiveItemstack(targetSlot.Itemstack, true))
                        {
                            world.SpawnItemEntity(targetSlot.Itemstack, byPlayer.Entity.Pos.XYZ);
                        }
                    }
                    else
                    {
                        pot.ServeIntoStack(targetSlot, beBowl.Inventory[1], world);
                    }
                    return true;
                }

                if (byPlayer.Entity.Controls.CtrlKey && (blockSel.SelectionBoxIndex == 1 || beBowl.Inventory.openedByPlayerGUIds.Contains(byPlayer.PlayerUID)))
                {
                    beBowl.ToggleLock(byPlayer);
                    return true;
                }

                if (beBowl.CanMix && (blockSel.SelectionBoxIndex == 1 || beBowl.Inventory.openedByPlayerGUIds.Contains(byPlayer.PlayerUID)))
                {
                    beBowl.SetPlayerMixing(byPlayer, true);
                    return true;
                }
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityMixingBowl beBowl &&
                (blockSel.SelectionBoxIndex == 1 || beBowl.Inventory.openedByPlayerGUIds.Contains(byPlayer.PlayerUID)))
            {
                beBowl.SetPlayerMixing(byPlayer, true);
                return beBowl.CanMix;
            }

            return false;
        }

        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            GetBlockEntity<BlockEntityMixingBowl>(blockSel.Position)?.SetPlayerMixing(byPlayer, false);
        }

        public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, EnumItemUseCancelReason cancelReason)
        {
            GetBlockEntity<BlockEntityMixingBowl>(blockSel.Position)?.SetPlayerMixing(byPlayer, false);

            return true;
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return selection.SelectionBoxIndex switch
            {
                0 => [ new() {
                    ActionLangCode = "blockhelp-quern-addremoveitems",
                    MouseButton = EnumMouseButton.Right
                }, .. base.GetPlacedBlockInteractionHelp(world, selection, forPlayer)],
                _ => [ new() {
                    ActionLangCode = "aculinaryartillery:blockhelp-mixingbowl-mix",
                    MouseButton = EnumMouseButton.Right,
                    ShouldApply = (wi, bs, es) =>  GetBlockEntity<BlockEntityMixingBowl>(bs.Position)?.CanMix == true
                }, new() {
                    ActionLangCode = "aculinaryartillery:blockhelp-mixingbowl-autounlock",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "ctrl",
                    ShouldApply = (wi, bs, es) => GetBlockEntity<BlockEntityMixingBowl>(bs.Position)?.invLocked == true
                }, new() {
                    ActionLangCode = "aculinaryartillery:blockhelp-mixingbowl-autolock",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "ctrl",
                    ShouldApply = (wi, bs, es) => GetBlockEntity<BlockEntityMixingBowl>(bs.Position)?.invLocked == false
                }, .. base.GetPlacedBlockInteractionHelp(world, selection, forPlayer)]
            };
        }

        public override void DidConnectAt(IWorldAccessor world, BlockPos pos, BlockFacing face)
        {

        }

        public override bool HasMechPowerConnectorAt(IWorldAccessor world, BlockPos pos, BlockFacing face, BlockMPBase mpbase)
        {
            return face == BlockFacing.UP || face == BlockFacing.DOWN;
        }
    }
}
