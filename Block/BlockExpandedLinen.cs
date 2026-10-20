using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace ACulinaryArtillery;

public class BlockExpandedLinen : BlockLinen
{
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        if (api.World.BlockAccessor.GetBlockEntity(blockSel.Position.AddCopy(blockSel.Face.Opposite)) is BlockEntityBarrel) return false;

        return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
    {
        if (blockSel != null && api.World.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityBarrel beba)
        {
            ItemSlot? liqslot = beba?.Inventory[1];
            string curdBundleCode = liqslot?.Itemstack?.ItemAttributes?["curdBundleBlock"]?.AsString("curdbundle") ?? "curdbundle";
            if (byEntity.World.GetBlock(curdBundleCode) is not BlockCheeseCurdsBundle bundle)
            {
                (api as ICoreClientAPI)?.TriggerIngameError(this, "nosuchbundleitem", $"Curd bundle item {curdBundleCode} does not exist or is not a curd bundle");
                return;
            }

            if (beba != null && liqslot?.Empty == false)
            {
                var props = BlockLiquidContainerBase.GetContainableProps(liqslot.Itemstack);
                float itemsPerLitre = props?.ItemsPerLitre ?? 1;
                float curLitres = liqslot.Itemstack.StackSize / itemsPerLitre;

                int litresNeeded = liqslot.Itemstack.ItemAttributes?["curdBundleLitres"]?.AsInt(25) ?? 25;

                if (curLitres < litresNeeded)
                {
                    (api as ICoreClientAPI)?.TriggerIngameError(this, "notenough", Lang.Get("aculinaryartillery:cheesemaking-notenoughliquid", litresNeeded));
                    handHandling = EnumHandHandling.PreventDefault;
                    return;
                }

                if (api.World.Side == EnumAppSide.Server)
                {
                    ItemStack ccStack = beba.Inventory[1].TakeOut((int)(litresNeeded * itemsPerLitre));

                    ItemStack bundleStack = new(bundle);
                    bundle?.SetContents(bundleStack, ccStack);

                    slot.TakeOut(1);
                    slot.MarkDirty();

                    beba.MarkDirty(true);

                    if (!byEntity.TryGiveItemStack(bundleStack))
                    {
                        api.World.SpawnItemEntity(bundleStack, byEntity.Pos.XYZ.AddCopy(0, 0.5, 0));
                    }
                    api.World.Logger.Audit("{0} Took 1x{1} from Barrel at {2}.",
                        byEntity.GetName(),
                        bundleStack.Collectible.Code,
                        blockSel.Position
                    );
                }

                handHandling = EnumHandHandling.PreventDefault;
                return;
            }
        }

        base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);
    }
}
