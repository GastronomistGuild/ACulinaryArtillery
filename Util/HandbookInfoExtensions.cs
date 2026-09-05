using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ACulinaryArtillery.Util
{
    public static class HandbookInfoExtensions
    {

        public static RichTextComponentBase? GetSubHeading(List<RichTextComponentBase> components, string code)
        {
            return components.FirstOrDefault(comp => (comp as RichTextComponent)?.DisplayText == "• " + Lang.Get(code) + "\n");
        }

        public static List<RichTextComponentBase> ACAHandbookIngredientForComponents(ICoreClientAPI capi, ItemStack[] allStacks, ActionConsumable<string> openDetailPageFor, ItemStack stack)
        {
            ItemStack maxstack = stack.Clone();
            maxstack.StackSize = maxstack.Collectible.MaxStackSize * 10; // because SatisfiesAsIngredient() tests for stacksize. Times 10 because liquid portion oddities

            ItemStack[] resolvedStacks = [.. capi.GetKneadingRecipes().Where(rec => rec.Ingredients.Any(ing => ing.GetMatch(maxstack) != null)).Select(rec => rec.Output.ResolvedItemStack),
                                          .. capi.GetSimmerRecipes().Where(rec => rec.Ingredients.Any(ing => ing.SatisfiesAsIngredient(maxstack))).Select(rec => rec.Simmering.SmeltedStack.ResolvedItemStack)];

            List<ItemStack> recipestacks = [.. allStacks.Where(stack => resolvedStacks.Where(rstack => rstack != null).Any(rstack => stack.Equals(capi.World, rstack, GlobalConstants.IgnoredStackAttributes)))];
            List<CookingRecipe> mixingrecipes = [.. capi.GetMixingRecipes().Where(recipe => recipe.CooksInto?.ResolvedItemStack == null && recipe.Ingredients!.Any(ingred => ingred.GetMatchingStack(stack) != null))];

            if (recipestacks.Count == 0 && mixingrecipes.Count == 0) return [];
            List<RichTextComponentBase> components = [];

            while (recipestacks.Count > 0)
            {
                ItemStack dstack = recipestacks[0];
                recipestacks.RemoveAt(0);
                if (dstack == null) continue;

                components.Add(new SlideshowItemstackTextComponent(capi, dstack, recipestacks, 40, EnumFloat.Inline, (cs) => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs))));
            }

            while (mixingrecipes.Count > 0)
            {
                CookingRecipe recipe;
                recipe = mixingrecipes[0];
                mixingrecipes.RemoveAt(0);
                if (recipe == null) continue;

                ItemStack mealBlock = new ItemStack(BlockMeal.RandomMealBowl(capi));
#nullable disable // This bit of the code will need to be changed once the proper caching is in vanilla
                var comp = new MealstackTextComponent(capi, mealBlock, recipe, 40, EnumFloat.Inline, (cs) => openDetailPageFor("handbook-mealrecipe-" + recipe.Code), 6, false, maxstack);
                components.Add(comp);
#nullable restore
            }

            return components;
        }

        public static List<RichTextComponentBase> ACAHandbookProcessesIntoComponents(ICoreClientAPI capi, ActionConsumable<string> openDetailPageFor, ItemStack stack, float marginBottom, List<ItemStack> fuels, bool haveText)
        {
            List<RichTextComponentBase> components = [];
            var collObj = stack.Collectible;

            // Simmers into
            if (collObj.CombustibleProps?.SmeltedStack?.ResolvedItemStack != null && !collObj.CombustibleProps.SmeltedStack.ResolvedItemStack.Equals(capi.World, stack, GlobalConstants.IgnoredStackAttributes))
            {
                if (getCanSimmer(fuels, stack) && collObj.CombustibleProps.SmeltedStack?.ResolvedItemStack is ItemStack smeltedStack)
                {
                    string smelttype = collObj.CombustibleProps.SmeltingType.ToString().ToLowerInvariant();
                    CollectibleBehaviorHandbookTextAndExtraInfo.AddHeading(components, capi, "aculinaryartillery:smeltdesc-simmer-title", ref haveText);


                    var cmp = new ItemstackTextComponent(capi, smeltedStack, 40, 10, EnumFloat.Inline, (cs) => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs)));
                    cmp.ShowStacksize = true;
                    cmp.PaddingLeft = 2;
                    components.Add(cmp);
                    components.Add(new ClearFloatTextComponent(capi, marginBottom));  //nice margin below the item graphic
                }
            }

            return components;
        }

        public static List<RichTextComponentBase> ACAHandbookCreatedByComponents(ICoreClientAPI capi, ItemStack[] allStacks, ActionConsumable<string> openDetailPageFor, ItemStack stack, List<ItemStack> fuels)
        {
            DoughRecipe[] kneadingRecipes = [.. capi.GetKneadingRecipes().Where(rec => rec.Output.ResolvedItemStack.Satisfies(stack))];
            SimmerRecipe[] simmeringRecipes = [.. capi.GetSimmerRecipes().Where(rec => rec.Simmering.SmeltedStack.ResolvedItemStack.Satisfies(stack))];
            List<ItemStack> simmeringStacks = [];

            foreach (var val in ObjectCacheUtil.TryGet<List<ItemStack>>(capi, "ACAhandbooksimmerStacks"))
            {
                if (val.Collectible.CombustibleProps.SmeltedStack?.ResolvedItemStack?.Equals(capi.World, stack, GlobalConstants.IgnoredStackAttributes) == true && !simmeringStacks.Any(s => s.Equals(capi.World, val, GlobalConstants.IgnoredStackAttributes)))
                {
                    var simmerStack = val.Clone();
                    simmerStack.StackSize = val.Collectible.CombustibleProps.SmeltedRatio;
                    simmeringStacks.Add(val);
                }
            }

            if (kneadingRecipes.Length == 0 && simmeringRecipes.Length == 0 && simmeringStacks.Count == 0) return [];

            List<RichTextComponentBase> components = [];
            var verticalSpace = new ClearFloatTextComponent(capi, 7);

            if (kneadingRecipes.Length > 0)
            {
                CollectibleBehaviorHandbookTextAndExtraInfo.AddSubHeading(components, capi, openDetailPageFor, "aculinaryartillery:handbook-createdby-mixingbowl", "kneadingmixingguide");

                bool firstRecipe = true;
                foreach (var recipe in kneadingRecipes)
                {
                    if (recipe.Ingredients == null) continue;
                    if (!firstRecipe) components.Add(verticalSpace);
                    firstRecipe = false;

                    bool firstItem = true;
                    foreach (DoughIngredient ing in recipe.Ingredients)
                    {
                        ItemStack[] inputs = [.. ing.Inputs.Where(input => input.IsWildCard)
                                                           .SelectMany(input => capi.World.SearchItems(input.Code).Select(item => new ItemStack(item, input.Quantity))
                                                                                                                  .Where(stack => stack != null && ing.GetMatch(stack, false) != null)),
                                              .. ing.Inputs.Where(input => !input.IsWildCard && input.ResolvedItemStack != null)
                                                           .Select(input => new ItemStack(input.ResolvedItemStack.Id, input.ResolvedItemStack.Class, input.Quantity, (TreeAttribute)input.ResolvedItemStack.Attributes, capi.World))
                                                           .Where(stack => stack != null)
                                             ];

                        if (inputs.Length > 0)
                        {
                            if (!firstItem) components.Add(new RichTextComponent(capi, " + ", CairoFont.WhiteMediumText()) { VerticalAlign = EnumVerticalAlign.Middle });
                            components.Add(new SlideshowItemstackTextComponent(capi, inputs, 40, EnumFloat.Inline, (ItemStack cs) => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs))) { ShowStackSize = true, PaddingRight = 0 });
                            firstItem = false;
                        }
                    }

                    components.Add(new RichTextComponent(capi, " = ", CairoFont.WhiteMediumText()) { VerticalAlign = EnumVerticalAlign.Middle });
                    components.Add(new ItemstackTextComponent(capi, recipe.Output.ResolvedItemStack, 40, 10, EnumFloat.Inline) { ShowStacksize = true });
                }

                components.Add(verticalSpace);
            }

            if (simmeringRecipes.Length > 0 || simmeringStacks.Count > 0)
            {
                CollectibleBehaviorHandbookTextAndExtraInfo.AddSubHeading(components, capi, openDetailPageFor, "aculinaryartillery:handbook-createdby-simmering", "simmeringguide");

                int firstPadding = 2;
                while (simmeringStacks.Count > 0)
                {
                    ItemStack dstack = simmeringStacks[0];
                    simmeringStacks.RemoveAt(0);
                    if (dstack == null) continue;

                    SlideshowItemstackTextComponent comp = new SlideshowItemstackTextComponent(capi, dstack, simmeringStacks, 40, EnumFloat.Inline, (cs) => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs)));
                    comp.ShowStackSize = true;
                    comp.PaddingLeft = firstPadding;
                    firstPadding = 0;
                    components.Add(comp);
                }

                bool firstRecipe = true;
                foreach (var recipe in simmeringRecipes)
                {
                    if (recipe.Ingredients == null) continue;
                    if (!firstRecipe) components.Add(verticalSpace);
                    firstRecipe = false;

                    bool firstItem = true;
                    foreach (CraftingRecipeIngredient ing in recipe.Ingredients.Where(ing => ing.IsWildCard || ing.ResolvedItemStack != null))
                    {
                        ItemStack[] inputs = [.. ing.IsWildCard ? capi.World.SearchItems(ing.Code).Select(item => new ItemStack(item, ing.Quantity))
                                                                                                  .Where(stack => stack != null && ing.SatisfiesAsIngredient(stack, false)) :
                                              (ing.ResolvedItemStack != null ? [new ItemStack(ing.ResolvedItemStack.Id, ing.ResolvedItemStack.Class, ing.Quantity, (TreeAttribute)ing.ResolvedItemStack.Attributes, capi.World)] : [])
                                             ];

                        if (inputs.Length > 0)
                        {
                            if (!firstItem) components.Add(new RichTextComponent(capi, " + ", CairoFont.WhiteMediumText()) { VerticalAlign = EnumVerticalAlign.Middle });
                            components.Add(new SlideshowItemstackTextComponent(capi, inputs, 40, EnumFloat.Inline, (ItemStack cs) => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs))) { ShowStackSize = true, PaddingRight = 0 });
                            firstItem = false;
                        }
                    }

                    components.Add(new RichTextComponent(capi, " = ", CairoFont.WhiteMediumText()) { VerticalAlign = EnumVerticalAlign.Middle });
                    components.Add(new ItemstackTextComponent(capi, recipe.Simmering.SmeltedStack.ResolvedItemStack, 40, 10, EnumFloat.Inline) { ShowStacksize = true });
                }

                components.Add(verticalSpace);
            }

            return components;
        }

        public static List<RichTextComponentBase> ACAHandbookStorableComponents(ICoreClientAPI capi, ItemStack[] allStacks, ActionConsumable<string> openDetailPageFor, ItemStack stack)
        {
            if (stack.ItemAttributes?["bottlerackable"].AsBool() != true) return [];
            List<ItemStack> displayStorables = [.. allStacks.Where(val => val.Collectible is BlockBottleRack)];

            if (displayStorables.Count == 0) return [];

            List<RichTextComponentBase> components = [];
            while (displayStorables.Count > 0)
            {
                ItemStack dstack = displayStorables[0];
                displayStorables.RemoveAt(0);
                if (dstack == null) continue;

                components.Add(new SlideshowItemstackTextComponent(capi, dstack, displayStorables, 40, EnumFloat.Inline, (cs) => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs))) { PaddingLeft = 0 });
            }

            return components;
        }

        public static List<RichTextComponentBase> ACAHandbookStoredInComponents(ICoreClientAPI capi, ItemStack[] allStacks, ActionConsumable<string> openDetailPageFor, ItemStack stack)
        {
            if (stack.Collectible is not BlockBottleRack) return [];
            List<ItemStack> storables = [.. allStacks.Where(val => val.ItemAttributes?["bottlerackable"].AsBool() == true)];

            if (storables.Count == 0) return [];

            List<RichTextComponentBase> components = [];
            while (storables.Count > 0)
            {
                ItemStack dstack = storables[0];
                storables.RemoveAt(0);
                if (dstack == null) continue;

                SlideshowItemstackTextComponent comp = new SlideshowItemstackTextComponent(capi, dstack, storables, 40, EnumFloat.Inline, (cs) => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs)));
                if (BlockLiquidContainerBase.GetContainableProps(dstack) is not WaterTightContainableProps) comp.ShowStackSize = true;
                comp.PaddingLeft = 0;
                components.Add(comp);
            }


            return components;
        }

        internal static bool getCanSimmer(List<ItemStack> fuels, ItemStack stack)
        {
            if (stack.Collectible.CombustibleProps is not CombustibleProperties combustProps) return false;
            int maxFuelTemp = fuels.OrderBy(fuel => fuel.Collectible.CombustibleProps.BurnTemperature).Select(fuel => fuel.Collectible.CombustibleProps.BurnTemperature).LastOrDefault();
            if (combustProps.MeltingPoint > maxFuelTemp) return false;
            if (combustProps.SmeltingType is not EnumSmeltType.Cook or EnumSmeltType.Convert) return false;
            if (combustProps.RequiresContainer == true) return true;

            return false;
        }

        public static Dictionary<CookingRecipeIngredient, HashSet<ItemStack?>>? CreateCachedMealRecipeStacks(ICoreClientAPI capi, CookingRecipe recipe)
        {
            ItemStack[]? allstacks = ObjectCacheUtil.TryGet<ItemStack[]>(capi, "handbookallstacks");
            if (allstacks == null) return null;

            return ObjectCacheUtil.GetOrCreate(capi, "valstacksbying-" + recipe.Code, () =>
            {
                Dictionary<CookingRecipeIngredient, HashSet<ItemStack?>>? valStacksByIng = [];

                foreach (var ingredient in recipe.Ingredients!)
                {
                    HashSet<ItemStack?> ingredientStacks = [];

                    CookingRecipeIngredientPatcher.Resolve(ingredient, capi.World, "handbook meal recipes");
                    foreach (var astack in allstacks)
                    {
                        if (ingredient.GetMatchingStack(astack) is not CookingRecipeStack vstack) continue;

                        ItemStack stack = astack.Clone();
                        stack.StackSize = vstack.StackSize;

                        if (BlockLiquidContainerBase.GetContainableProps(stack) is WaterTightContainableProps props)
                        {
                            stack.StackSize *= (int)(props.ItemsPerLitre * ingredient.PortionSizeLitres);
                        }

                        ingredientStacks.Add(stack);
                    }

                    if (ingredient.MinQuantity <= 0) ingredientStacks.Add(null);

                    valStacksByIng.Add(ingredient.Clone(), ingredientStacks);
                }

                return valStacksByIng;
            });
        }
    }
}
