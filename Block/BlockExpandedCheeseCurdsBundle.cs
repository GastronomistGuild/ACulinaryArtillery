using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ACulinaryArtillery;

public class BlockExpandedCheeseCurdsBundle : BlockCheeseCurdsBundle
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        BECheeseCurdsBundle? beccb = api.World.BlockAccessor.GetBlockEntity(blockSel.Position) as BECheeseCurdsBundle;

        if (beccb == null || beccb.State != EnumCurdsBundleState.OpenedSalted) return base.OnBlockInteractStart(world, byPlayer, blockSel);

        string cheeseCode = Attributes?["cheeseItem"]?.AsString("rawcheese-salted") ?? "rawcheese-salted";
        if (api.World.GetItem(cheeseCode) is not Item cheeseItem)
        {
            (api as ICoreClientAPI)?.TriggerIngameError(this, "nosuchcheeseitem", $"Cheese item {cheeseCode} does not exist");
            return false;
        }

        ItemStack cheese = new(cheeseItem);

        if (!byPlayer.InventoryManager.TryGiveItemstack(cheese, true))
        {
            api.World.SpawnItemEntity(cheese, byPlayer.Entity.Pos.XYZ.Add(0, 0.5, 0));
        }

        api.World.Logger.Audit("{0} Took 1x{1} at {2}.",
            byPlayer.PlayerName,
            cheese.Collectible.Code,
            blockSel.Position
        );

        string linenCode = Attributes?["linenBlock"]?.AsString("linen-normal-down") ?? "linen-normal-down";
        if (api.World.GetBlock(linenCode) is not Block linenBlock)
        {
            (api as ICoreClientAPI)?.TriggerIngameError(this, "nosuchlinenblock", $"Linen block {linenCode} is not a Block!");
            return false;
        }

        api.World.BlockAccessor.SetBlock(linenBlock.Id, blockSel.Position);
        return true;
    }
}
