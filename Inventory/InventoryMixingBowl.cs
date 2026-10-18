using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.ServerMods.WorldEdit;

namespace ACulinaryArtillery
{
    /// <summary>
    /// Inventory with one input slot and one output slot and six watertight slots
    /// </summary>
    public class InventoryMixingBowl : InventoryBase, ISlotProvider
    {
        ItemSlot[] slots;
        public ItemSlot[] Slots => slots;
        BlockEntityMixingBowl machine;
        public int InputSlotStackCapacity
        {
            get
            {
                return machine.StackCapcity;
            }
        }
        public override void LateInitialize(string inventoryID, ICoreAPI api)
        {
            base.LateInitialize(inventoryID, api);
            foreach (var slot in slots)
            {
                if (slot is ItemSlotMixingBowl)
                {
                    slot.MaxSlotStackSize = InputSlotStackCapacity;
                }
            }

        }
        public InventoryMixingBowl(string? inventoryID, ICoreAPI? api, BlockEntityMixingBowl bowl) : base(inventoryID, api)
        {
            // slot 0 = pot
            // slot 1 = output
            //slots 2-7 = ingredients
            machine = bowl;
            slots = GenEmptySlots(8);
        }

        public override int Count => 8;

        public override ItemSlot this[int slotId]
        {
            get
            {
                if (slotId < 0 || slotId >= Count) throw new ArgumentOutOfRangeException(nameof(slotId));
                return slots[slotId];
            }
            set
            {
                if (slotId < 0 || slotId >= Count) throw new ArgumentOutOfRangeException(nameof(slotId));
                if (value == null) throw new ArgumentNullException(nameof(value));
                slots[slotId] = value;
            }
        }


        public override void FromTreeAttributes(ITreeAttribute tree)
        {
            slots = SlotsFromTreeAttributes(tree, slots);

            if (Api != null)
            {
                for (int i = 2; i < Count; i++)
                {
                    this[i].MaxSlotStackSize = InputSlotStackCapacity;
                    (this[i] as ItemSlotMixingBowl)?.Set(machine, i - 2);
                }
            }

        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            SlotsToTreeAttributes(slots, tree);
        }

        protected override ItemSlot NewSlot(int i)
        {
            if (i == 0) return new ItemSlotPotInput(this);
            if (i == 1) return new ItemSlotWatertight(this);
            return new ItemSlotMixingBowl(this, machine, i - 2);
        }

        public override ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
        {
            return slots[1];
        }

        public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
        {
            ItemSlot goingTo = base.GetAutoPushIntoSlot(atBlockFace, fromSlot);

            return goingTo == slots[1] ? slots[0] : goingTo;
        }
    }

    public class ItemSlotMixingBowl : ItemSlotWatertight
    {
        BlockEntityMixingBowl machine;
        int stackNum;

        public ItemSlotMixingBowl(InventoryBase inventory, BlockEntityMixingBowl bowl, int itemNumber) : base(inventory)
        {
            machine = bowl;
            stackNum = itemNumber;
        }

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        {
            return base.CanTakeFrom(sourceSlot, priority) && locked(sourceSlot);
        }

        public override bool CanHold(ItemSlot sourceSlot)
        {
            return base.CanHold(sourceSlot) && locked(sourceSlot);
        }

        public override bool CanTake()
        {
            bool isLiquid = !Empty && itemstack.Collectible.IsLiquid();
            if (isLiquid) return false;

            return base.CanTake();
        }

        protected override void ActivateSlotLeftClick(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
        {
            if (sourceSlot.Empty)
            {
                base.ActivateSlotLeftClick(sourceSlot, ref op);
                return;
            }

            IWorldAccessor world = inventory.Api.World;

            if (sourceSlot.Itemstack.Collectible is ILiquidSource source && source.AllowHeldLiquidTransfer)
            {
                ItemStack? liquidcontainerbaseContents = source.GetContent(sourceSlot.Itemstack);
                bool stackable = !Empty && Itemstack.Equals(world, liquidcontainerbaseContents, GlobalConstants.IgnoredStackAttributes);

                if ((Empty || stackable) && liquidcontainerbaseContents != null && !machine.invLocked)
                {
                    ItemStack liquidcontainerbaseStack = sourceSlot.Itemstack;

                    var lprops = BlockLiquidContainerBase.GetContainableProps(liquidcontainerbaseContents);
                    float itemsPerLitre = lprops?.ItemsPerLitre ?? 1;

                    float toMoveLitres = op.ShiftDown ? source.TransferSizeLitres : source.CapacityLitres;
                    float curSourceLitres = liquidcontainerbaseContents.StackSize / itemsPerLitre * liquidcontainerbaseStack.StackSize;
                    float curDestLitres = this.StackSize / itemsPerLitre;
                    // Cap by source amount
                    toMoveLitres = Math.Min(toMoveLitres, curSourceLitres);
                    // Cap by target capacity
                    toMoveLitres = Math.Min(toMoveLitres, machine.CapacityLitres - curDestLitres);

                    if (toMoveLitres > 0)
                    {
                        int moveQuantity = (int)(toMoveLitres * itemsPerLitre);
                        ItemStack takenContentStack = source.TryTakeContent(liquidcontainerbaseStack, moveQuantity / liquidcontainerbaseStack.StackSize);

                        takenContentStack.StackSize *= liquidcontainerbaseStack.StackSize;
                        takenContentStack.StackSize += StackSize;

                        Itemstack = takenContentStack;
                        MarkDirty();
                        op.MovedQuantity = moveQuantity;

                        var pos = op.ActingPlayer?.Entity?.Pos;
                        if (pos != null) op.World.PlaySoundAt(lprops?.FillSound, pos.X, pos.Y, pos.Z);
                    }
                    MarkDirty();
                    return;
                }

                return;
            }

            string? contentItemCode = sourceSlot.Itemstack?.ItemAttributes?["contentItemCode"].AsString();
            if (contentItemCode != null && !machine.invLocked)
            {
                ItemSlot mixingSlot = inventory[1];
                ItemStack contentStack = new ItemStack(world.GetItem(AssetLocation.Create(contentItemCode, sourceSlot.Itemstack!.Collectible.Code.Domain)));
                bool stackable = !mixingSlot.Empty && mixingSlot.Itemstack.Equals(world, contentStack, GlobalConstants.IgnoredStackAttributes);

                if ((mixingSlot.Empty || stackable) && contentStack != null)
                {
                    if (stackable) mixingSlot.Itemstack.StackSize++;
                    else mixingSlot.Itemstack = contentStack;

                    mixingSlot.MarkDirty();
                    ItemStack bowlStack = new ItemStack(world.GetBlock(AssetLocation.Create(sourceSlot.Itemstack?.ItemAttributes["emptiedBlockCode"].AsString(), sourceSlot.Itemstack?.Collectible.Code.Domain)));
                    if (sourceSlot.StackSize == 1)
                    {
                        sourceSlot.Itemstack = bowlStack;
                    }
                    else
                    {
                        sourceSlot.Itemstack!.StackSize--;
                        if (!op.ActingPlayer.InventoryManager.TryGiveItemstack(bowlStack))
                        {
                            world.SpawnItemEntity(bowlStack, op.ActingPlayer.Entity.Pos.XYZ);
                        }
                    }
                    sourceSlot.MarkDirty();
                }

                return;
            }

            if (sourceSlot.Itemstack?.ItemAttributes?["contentItem2BlockCodes"].Exists == true) return;

            base.ActivateSlotLeftClick(sourceSlot, ref op);
        }

        protected override void ActivateSlotRightClick(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
        {
            ItemSlotMixingBowl? mixingSlot = inventory[1] as ItemSlotMixingBowl;
            IWorldAccessor world = inventory.Api.World;

            BlockLiquidContainerBase? liquidcontainerbaseblock = sourceSlot.Itemstack?.Block as BlockLiquidContainerBase;
            if (sourceSlot.Itemstack?.Collectible is ILiquidSink sink && !Empty && sink.AllowHeldLiquidTransfer)
            {
                ItemStack mixSlotStack = Itemstack;
                var curTargetLiquidStack = sink.GetContent(sourceSlot.Itemstack);

                bool liquidstackable = curTargetLiquidStack == null || mixSlotStack.Equals(world, curTargetLiquidStack, GlobalConstants.IgnoredStackAttributes);

                if (liquidstackable)
                {
                    var lprops = BlockLiquidContainerBase.GetContainableProps(mixSlotStack);

                    float curSourceLitres = mixSlotStack.StackSize / (lprops?.ItemsPerLitre ?? 1);
                    float curTargetLitres = sink.GetCurrentLitres(sourceSlot.Itemstack);

                    float toMoveLitres = op.CtrlDown ? sink.TransferSizeLitres : (sink.CapacityLitres - curTargetLitres);

                    toMoveLitres *= sourceSlot.StackSize;
                    toMoveLitres = Math.Min(curSourceLitres, toMoveLitres);

                    if (toMoveLitres > 0)
                    {
                        op.MovedQuantity = sink.TryPutLiquid(sourceSlot.Itemstack, mixSlotStack, toMoveLitres / sourceSlot.StackSize);

                        Itemstack.StackSize -= op.MovedQuantity * sourceSlot.StackSize;
                        if (Itemstack.StackSize <= 0) this.Itemstack = null;
                        MarkDirty();
                        sourceSlot.MarkDirty();

                        var pos = op.ActingPlayer?.Entity?.Pos;
                        if (pos != null) op.World.PlaySoundAt(lprops?.PourSound, pos.X, pos.Y, pos.Z);
                    }
                }

                return;
            }

            if (itemstack != null && sourceSlot.Itemstack?.ItemAttributes?["contentItem2BlockCodes"].Exists == true)
            {
                string outBlockCode = sourceSlot.Itemstack.ItemAttributes["contentItem2BlockCodes"][itemstack.Collectible.Code.ToShortString()].AsString();

                if (outBlockCode != null)
                {
                    ItemStack outBlockStack = new ItemStack(world.GetBlock(AssetLocation.Create(outBlockCode, sourceSlot.Itemstack.Collectible.Code.Domain)));

                    if (sourceSlot.StackSize == 1)
                    {
                        sourceSlot.Itemstack = outBlockStack;
                    }
                    else
                    {
                        sourceSlot.Itemstack.StackSize--;
                        if (!op.ActingPlayer.InventoryManager.TryGiveItemstack(outBlockStack))
                        {
                            world.SpawnItemEntity(outBlockStack, op.ActingPlayer.Entity.Pos.XYZ);
                        }
                    }

                    sourceSlot.MarkDirty();
                    TakeOut(1);
                }

                return;
            }

            base.ActivateSlotRightClick(sourceSlot, ref op);
        }

        bool locked(ItemSlot sourceSlot)
        {
            if (!machine.invLocked) return true;

            ItemStack? stack = machine.lockedInv[stackNum];
            if (stack == null) return false;

            return stack.Equals(machine.Api.World, sourceSlot.Itemstack, GlobalConstants.IgnoredStackAttributes);
        }

        public void Set(BlockEntityMixingBowl bowl, int num)
        {
            machine = bowl;
            stackNum = num;
        }
    }

    public class ItemSlotPotInput : ItemSlot
    {
        public ItemSlotPotInput(InventoryBase inventory) : base(inventory)
        {

        }

        public override bool CanHold(ItemSlot slot)
        {
            return slot.Itemstack?.Collectible is BlockCookingContainer;
        }

        public override bool CanTake()
        {
            return true;
        }

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        {
            return sourceSlot.Itemstack?.Collectible is BlockCookingContainer && base.CanTakeFrom(sourceSlot, priority);
        }
    }
}
