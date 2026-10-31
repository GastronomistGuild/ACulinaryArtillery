using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ACulinaryArtillery
{
    public class ItemExpandedCheese : ItemExpandedFood
    {
        public virtual string Type => Variant["type"];
        public virtual string Part => Variant["part"];

        /// <summary>
        /// The block code to be placed when shift-clicking.
        /// </summary>
        public virtual string? PlaceBlock => Attributes["placeBlock"].AsString();

        /// <summary>
        /// The item code to be produced under vanilla's blue cheese conditions.
        /// (i.e. sunlight level less than 2 and not in an enclosed room)
        /// 
        /// This goes on the *raw* cheese, not the ripened cheese!
        /// </summary>
        public virtual string? BlueCheeseOutput => Attributes["blueCheeseOutput"].AsString();

        /// <summary>
        /// Configure what should be returned when slicing this cheese. Not used when
        /// shearing. See ReturnedShearingStacks.
        ///
        /// If the first code part matches the cheese currently being sliced, the
        /// attributes (including freshness) are carried over. This allows you to
        /// return the normal slicing output as normal when using this attribute.
        ///
        /// Defaults to this item's single slice variant.
        /// </summary>
        public virtual JsonItemStack[]? Outputs => Attributes["outputs"].AsObject<JsonItemStack[]>();

        /// <summary>
        /// If this variant can be sheared, the item code of the result.
        /// </summary>
        public virtual string? ShearsTo => Attributes["shearsTo"].AsString();

        /// <summary>
        /// The language key to use for this shearable variant's block help.
        /// </summary>
        public virtual string ShearHelpLangKey => Attributes["shearLangKey"].AsString("blockhelp-cheese-shear");

        /// <summary>
        /// Item stacks to be returned when shearing.
        /// </summary>
        public virtual JsonItemStack[]? ReturnedShearingStacks => Attributes["returnedShearingStacks"].AsObject<JsonItemStack[]>();

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (byEntity.Controls.ShiftKey && blockSel != null)
            {
                IPlayer? byPlayer = null;
                if (byEntity is EntityPlayer player) byPlayer = byEntity.World.PlayerByUid(player.PlayerUID);
                if (byPlayer == null) return;

                if (!byEntity.World.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
                {
                    return;
                }


                if (PlaceBlock == null || api.World.GetBlock(PlaceBlock) is not Block placeblock)
                {
                    return;
                }

                BlockPos targetPos = blockSel.Position.AddCopy(blockSel.Face);
                string failureCode = "";

                BlockSelection placeSel = blockSel.Clone();
                placeSel.Position.Add(blockSel.Face);

                if (placeblock?.TryPlaceBlock(api.World, byPlayer, slot.Itemstack, placeSel, ref failureCode) == true)
                {
                    if (api.World.BlockAccessor.GetBlockEntity(targetPos) is BlockEntityExpandedCheese)
                    {
                        slot.TakeOut(1);
                        slot.MarkDirty();
                    }

                    api.World.PlaySoundAt(placeblock.Sounds.Place, targetPos, -0.5, byPlayer);

                    handling = EnumHandHandling.PreventDefault;
                }
                else
                {
                    (api as ICoreClientAPI)?.TriggerIngameError(this, failureCode, Lang.Get("placefailure-" + failureCode));
                }

                return;
            }

            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
        }

        public override ItemStack? OnTransitionNow(ItemSlot slot, TransitionableProperties props)
        {
            if (props.Type == EnumTransitionType.Ripen
                && BlueCheeseOutput is string blueCheese
                && slot.Inventory.Pos is BlockPos pos)
            {
                Room room = api.ModLoader.GetModSystem<RoomRegistry>().GetRoomForPosition(pos);
                int lightlevel = api.World.BlockAccessor.GetLightLevel(pos, EnumLightLevelType.OnlySunLight);

                if (room.ExitCount > 0 && lightlevel < 2)
                {
                    if (api.World.GetItem(blueCheese) is Item cheeseItem)
                    {
                        return new ItemStack(cheeseItem);
                    }
                    else
                    {
                        return base.OnTransitionNow(slot, props);
                    }
                }
            }

            return base.OnTransitionNow(slot, props);
        }
    }
}
