using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ACulinaryArtillery
{
    public class BlockEntityExpandedCheese : BECheese
    {
        public override string InventoryClassName => "expandedcheese";

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            Inventory.LateInitialize("expandedcheese-" + Pos, api);
        }

        /// <summary>
        /// Finds the number of slices based on the first number in
        /// the collectible name, e.g. "cheese-4slice-cheddar" -> 4
        /// </summary>
        public new int SlicesLeft
        {
            get
            {
                if (Inventory[0].Itemstack?.Collectible is not ItemExpandedCheese cheese) return 0;

                string numSlicesStr = Regex.Match(cheese.Part, @"\d+").Value;
                if (int.TryParse(numSlicesStr, out int numSlices))
                {
                    return numSlices;
                }

                return 0;
            }
        }

        public new ItemStack[] TakeSlice()
        {
            if (Inventory[0].Empty) return [];
            ITreeAttribute? attr = Inventory[0].Itemstack!.Attributes;
            if (Inventory[0].Itemstack!.Collectible is not ItemExpandedCheese cheese) return [];
            MarkDirty(true);

            List<ItemStack> outputStacks = [];
            if (cheese.Outputs != null)
            {
                foreach (JsonItemStack jstack in cheese.Outputs)
                {
                    jstack.Resolve(Api.World, "expandedcheese output", true);
                    ItemStack? stack = jstack.ResolvedItemstack;

                    if (stack?.Collectible.FirstCodePart() == cheese.FirstCodePart())
                    {
                        stack.Attributes = attr;
                    }

                    if (stack != null)
                    {
                        outputStacks.Add(stack);
                    }
                }
            }
            else
            {
                outputStacks.Add(new ItemStack(Api.World.GetItem(cheese.CodeWithVariant("part", "1slice"))) { Attributes = attr });
            }

            int slicesLeft = SlicesLeft - 1;
            if (slicesLeft == 0)
            {
                Inventory[0].Itemstack = null;
                Api.World.BlockAccessor.SetBlock(0, Pos);
            }
            else
            {
                Inventory[0].Itemstack = new(Api.World.GetItem(cheese.CodeWithVariant("part", slicesLeft.ToString() + "slice"))) { Attributes = attr };
            }

            return [.. outputStacks];
        }
    }
}
