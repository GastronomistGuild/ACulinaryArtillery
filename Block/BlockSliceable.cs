using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ACulinaryArtillery
{
    public class BlockSliceable : BlockCheese
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            EnumTool? tool = byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible?.GetTool(byPlayer.InventoryManager.ActiveHotbarSlot);
            if (tool == EnumTool.Knife || tool == EnumTool.Sword)
            {
                BlockEntitySliceable? bec = world.BlockAccessor.GetBlockEntity<BlockEntitySliceable>(blockSel.Position);
                if (bec?.Inventory[0].Itemstack?.Collectible is not ItemSliceable sliceable) return true;

                if (bec != null && sliceable.ShearsTo != null)
                {
                    if (api.World.GetItem(sliceable.ShearsTo) is not Item shearedItem)
                    {
                        api.Logger.Error($"Sliceable shearsTo \"{sliceable.ShearsTo}\" does not exist!");
                        (api as ICoreClientAPI)?.TriggerIngameError(this, "noshearstoitem", $"Sliceable shearsTo item \"{sliceable.ShearsTo}\" does not exist!");
                        return true;
                    }

                    ItemStack shearedStack = new(shearedItem);
                    TransitionableProperties? perishProps = shearedStack.Collectible
                        .GetTransitionableProperties(api.World, shearedStack, null)?
                        .FirstOrDefault(p => p.Type == EnumTransitionType.Perish);

                    if (perishProps != null)
                    {
                        perishProps.TransitionedStack.Resolve(api.World, "Sliceable perished stack");
                        CarryOverFreshness(api, bec.Inventory[0], shearedStack, perishProps);
                    }

                    bec.Inventory[0].Itemstack = shearedStack;
                    bec.Inventory[0].MarkDirty();
                    bec.MarkDirty(true);

                    if (sliceable.ReturnedShearingStacks != null)
                    {
                        foreach (JsonItemStack jstack in sliceable.ReturnedShearingStacks)
                        {
                            jstack.Resolve(bec.Api.World, "sliceable shearing output");
                            if (jstack.ResolvedItemStack is ItemStack stack && !byPlayer.InventoryManager.TryGiveItemstack(stack, true))
                            {
                                world.SpawnItemEntity(stack, blockSel.Position);
                            }
                        }
                    }

                    return true;
                }

                if (bec?.TakeSlice() is ItemStack[] stacks)
                {
                    foreach (ItemStack stack in stacks)
                    {
                        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
                        {
                            world.SpawnItemEntity(stack, blockSel.Position);
                        }

                        world.Logger.Audit("{0} took {1} from Sliceable at {2}.",
                            byPlayer.PlayerName,
                            stack.Collectible.Code,
                            blockSel.Position
                        );
                    }
                }

                return true;
            }
            else
            {
                BlockEntitySliceable? bec = world.BlockAccessor.GetBlockEntity<BlockEntitySliceable>(blockSel.Position);
                if (bec?.Inventory[0].Itemstack is ItemStack sliceableStack)
                {
                    if (!byPlayer.InventoryManager.TryGiveItemstack(sliceableStack, true))
                    {
                        world.SpawnItemEntity(sliceableStack, blockSel.Position);
                    }

                    world.Logger.Audit("{0} took 1x{1} from Sliceable at {2}.",
                        byPlayer.PlayerName,
                        sliceableStack.Collectible.Code,
                        blockSel.Position
                    );
                }

                world.BlockAccessor.SetBlock(0, blockSel.Position);
                return true;
            }
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            BlockEntitySliceable? bec = world.BlockAccessor.GetBlockEntity(selection.Position) as BlockEntitySliceable;
            if (bec?.Inventory[0].Itemstack?.Collectible is not ItemSliceable sliceable) return [];

            return [
                new ()
                {
                    ActionLangCode = sliceable.ShearHelpLangKey,
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = ObjectCacheUtil.GetToolStacks(api, EnumTool.Knife),
                    GetMatchingStacks = (wi, bs, es) => {
                        BlockEntitySliceable? bec = world.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntitySliceable;
                        if (bec?.Inventory[0].Itemstack?.Collectible is not ItemSliceable obj) return null;

                        if (bec != null && sliceable.ShearsTo != null)
                        {
                            return wi.Itemstacks;
                        }
                        return null;
                    }
                },
                new ()
                {
                    ActionLangCode = "blockhelp-cheese-cut",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = ObjectCacheUtil.GetToolStacks(api, EnumTool.Knife),
                    GetMatchingStacks = (wi, bs, es) => {
                        BlockEntitySliceable? bec = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntitySliceable;
                        ItemSliceable? sliceable = bec?.Inventory[0].Itemstack?.Collectible as ItemSliceable;

                        if (bec != null && sliceable.ShearsTo == null && sliceable.IsSliceable && bec.SlicesLeft > 1)
                        {
                            return wi.Itemstacks;
                        }
                        return null;
                    }
                }
            ];
        }
    }
}
