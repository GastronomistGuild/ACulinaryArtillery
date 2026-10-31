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
    public class BlockExpandedCheese : BlockCheese
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            EnumTool? tool = byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible?.GetTool(byPlayer.InventoryManager.ActiveHotbarSlot);
            if (tool == EnumTool.Knife || tool == EnumTool.Sword)
            {
                BlockEntityExpandedCheese? bec = world.BlockAccessor.GetBlockEntity<BlockEntityExpandedCheese>(blockSel.Position);
                if (bec?.Inventory[0].Itemstack?.Collectible is not ItemExpandedCheese cheese) return true;

                if (bec != null && cheese.ShearsTo != null)
                {
                    if (api.World.GetItem(cheese.ShearsTo) is not Item shearedItem)
                    {
                        api.Logger.Error($"ExpandedCheese shearsTo \"{cheese.ShearsTo}\" does not exist!");
                        (api as ICoreClientAPI)?.TriggerIngameError(this, "noshearstoitem", $"ExpandedCheese shearsTo item \"{cheese.ShearsTo}\" does not exist!");
                        return true;
                    }

                    ItemStack shearedStack = new(shearedItem);
                    TransitionableProperties? perishProps = shearedStack.Collectible
                        .GetTransitionableProperties(api.World, shearedStack, null)?
                        .FirstOrDefault(p => p.Type == EnumTransitionType.Perish);

                    if (perishProps != null)
                    {
                        perishProps.TransitionedStack.Resolve(api.World, "expandedcheese perished stack");
                        CarryOverFreshness(api, bec.Inventory[0], shearedStack, perishProps);
                    }

                    bec.Inventory[0].Itemstack = shearedStack;
                    bec.Inventory[0].MarkDirty();
                    bec.MarkDirty(true);

                    if (cheese.ReturnedShearingStacks != null)
                    {
                        foreach (JsonItemStack jstack in cheese.ReturnedShearingStacks)
                        {
                            jstack.Resolve(bec.Api.World, "expandedcheese shearing output");
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

                        world.Logger.Audit("{0} took {1} from ExpandedCheese at {2}.",
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
                BlockEntityExpandedCheese? bec = world.BlockAccessor.GetBlockEntity<BlockEntityExpandedCheese>(blockSel.Position);
                if (bec?.Inventory[0].Itemstack is ItemStack cheeseStack)
                {
                    if (!byPlayer.InventoryManager.TryGiveItemstack(cheeseStack, true))
                    {
                        world.SpawnItemEntity(cheeseStack, blockSel.Position);
                    }

                    world.Logger.Audit("{0} took 1x{1} from ExpandedCheese at {2}.",
                        byPlayer.PlayerName,
                        cheeseStack.Collectible.Code,
                        blockSel.Position
                    );
                }

                world.BlockAccessor.SetBlock(0, blockSel.Position);
                return true;
            }
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            BlockEntityExpandedCheese? bec = world.BlockAccessor.GetBlockEntity(selection.Position) as BlockEntityExpandedCheese;
            if (bec?.Inventory[0].Itemstack?.Collectible is not ItemExpandedCheese cheese) return [];

            return [
                new ()
                {
                    ActionLangCode = cheese.ShearHelpLangKey,
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = ObjectCacheUtil.GetToolStacks(api, EnumTool.Knife),
                    GetMatchingStacks = (wi, bs, es) => {
                        BlockEntityExpandedCheese? bec = world.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityExpandedCheese;
                        if (bec?.Inventory[0].Itemstack?.Collectible is not ItemExpandedCheese obj) return null;

                        if (bec != null && cheese.ShearsTo != null)
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
                        BlockEntityExpandedCheese? bec = api.World.BlockAccessor.GetBlockEntity(bs.Position) as BlockEntityExpandedCheese;
                        ItemExpandedCheese? obj = bec?.Inventory[0].Itemstack?.Collectible as ItemExpandedCheese;

                        if (bec != null && cheese.ShearsTo == null && bec.SlicesLeft > 1)
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
