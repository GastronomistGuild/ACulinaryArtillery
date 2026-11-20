using ACulinaryArtillery.Util;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using Vintagestory.API.Datastructures;

namespace ACulinaryArtillery
{
    [HarmonyPatch(typeof(BlockLiquidContainerBase))]
    public static class BlockLiquidContainerBasePatches
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(BlockLiquidContainerBase.SplitStackAndPerformAction))]
        public static bool StopperTransmutationFix(ref BlockLiquidContainerBase __instance, ref int __result, Entity byEntity, ItemSlot slot, System.Func<ItemStack, int> action)
        {
            __result = SplitStackAndPerformAction(byEntity, slot, action);
            return false;
        }
        public static int SplitStackAndPerformAction(Entity byEntity, ItemSlot slot, System.Func<ItemStack, int> action)
        {
            if (slot.Itemstack == null)
            {
                return 0;
            }

            if (slot.Itemstack.StackSize == 1)
            {
                int num = action(slot.Itemstack);
                if (num > 0)
                {
                    _ = slot.Itemstack.Collectible.MaxStackSize;
                    EntityPlayer? obj = byEntity as EntityPlayer;
                    if (obj == null)
                    {
                        return num;
                    }

                    obj.WalkInventory(delegate (ItemSlot pslot)
                    {
                        if (pslot.Empty || pslot is ItemSlotCreative || pslot.StackSize == pslot.Itemstack.Collectible.MaxStackSize || pslot.Itemstack.ItemAttributes?["isLiquidContainerStopper"]?.AsBool() == true || pslot.Itemstack.ItemAttributes?["isLiquidContainerSealant"]?.AsBool() == true)
                        {
                            return true;
                        }

                        int mergableQuantity = slot.Itemstack.Collectible.GetMergableQuantity(slot.Itemstack, pslot.Itemstack, EnumMergePriority.DirectMerge);
                        if (mergableQuantity == 0)
                        {
                            return true;
                        }

                        BlockLiquidContainerBase? obj3 = slot.Itemstack.Collectible as BlockLiquidContainerBase;
                        BlockLiquidContainerBase? blockLiquidContainerBase = pslot.Itemstack.Collectible as BlockLiquidContainerBase;
                        if ((obj3?.GetContent(slot.Itemstack)?.StackSize).GetValueOrDefault() != (blockLiquidContainerBase?.GetContent(pslot.Itemstack)?.StackSize).GetValueOrDefault())
                        {
                            return true;
                        }

                        slot.Itemstack.StackSize += mergableQuantity;
                        pslot.TakeOut(mergableQuantity);
                        slot.MarkDirty();
                        pslot.MarkDirty();
                        return true;
                    });
                }

                return num;
            }

            ItemStack itemStack = slot.Itemstack.Clone();
            itemStack.StackSize = 1;
            int num2 = action(itemStack);
            if (num2 > 0)
            {
                slot.TakeOut(1);
                EntityPlayer? obj2 = byEntity as EntityPlayer;
                if (obj2?.Player.InventoryManager.TryGiveItemstack(itemStack, slotNotifyEffect: true) != true)
                {
                    byEntity.World.SpawnItemEntity(itemStack, byEntity.SidedPos.XYZ);
                }

                slot.MarkDirty();
            }

            return num2;
        }

        [HarmonyTranspiler]
        [HarmonyPatch("tryEatStop")]
        public static IEnumerable<CodeInstruction> AddHealOverTimePatch(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var matcher = new CodeMatcher(instructions, generator);
            matcher.Start()
                .MatchEndForward(
                    CodeMatch.IsLdloc(),
                    CodeMatch.LoadsField(AccessTools.Field(typeof(FoodNutritionProperties), nameof(FoodNutritionProperties.Health))),
                    CodeMatch.LoadsConstant(0f),
                    new CodeMatch(code => code.opcode == OpCodes.Beq || code.opcode == OpCodes.Beq_S)
                );

            matcher.MatchEndForward(
                new CodeMatch(OpCodes.Newobj, AccessTools.Constructor(typeof(DamageSource)))
            );

            matcher.InsertAfter(
                new CodeInstruction(OpCodes.Dup), // the new damage source
                CodeInstruction.LoadLocal(1), // the stack
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BlockLiquidContainerBasePatches), nameof(SetHealingOverTimeOnSource)))
            );

            return matcher.InstructionEnumeration();
        }

        public static void SetHealingOverTimeOnSource(DamageSource source, ItemStack stack)
        {
            float durationSec = stack.Collectible?.Attributes?["eatHealthEffectDurationSec"].AsFloat(ACulinaryArtilleryConfig.Current.ExpandedFoodHealDurationSec) ?? ACulinaryArtilleryConfig.Current.ExpandedFoodHealDurationSec;
            int ticks = stack.Collectible?.Attributes?["eatHealthEffectTicks"].AsInt(ACulinaryArtilleryConfig.Current.ExpandedFoodHealTicks) ?? ACulinaryArtilleryConfig.Current.ExpandedFoodHealTicks;
            source.Duration = TimeSpan.FromSeconds(durationSec);
            source.TicksPerDuration = ticks;
        }
    }

    [HarmonyPatch(typeof(CollectibleObject))]
    public static class CollectibleObjectPatches
    {
        // This patch lets us shim in the healing timer and ticks
        // set in the mod config. Only uses them as a fallback
        // should they not be explicitly set in the attributes.
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(CollectibleObject), "tryEatStop")]
        public static IEnumerable<CodeInstruction> AddHealOverTimePatch(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var matcher = new CodeMatcher(instructions, generator);

            matcher.DeclareLocal(typeof(double), out var durationLocal);
            matcher.DeclareLocal(typeof(int), out var ticksLocal);

            // Load the ItemSlot and get the proper duration and ticks
            matcher.Start().Insert(
                CodeInstruction.LoadArgument(2),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CollectibleObjectPatches), nameof(GetHealDuration))),
                CodeInstruction.StoreLocal(durationLocal.LocalIndex),

                CodeInstruction.LoadArgument(2),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CollectibleObjectPatches), nameof(GetHealTicks))),
                CodeInstruction.StoreLocal(ticksLocal.LocalIndex)
            );

            // Navigate to loading the duration local
            matcher.MatchStartForward(
                CodeMatch.Calls(AccessTools.Method(typeof(TimeSpan), nameof(TimeSpan.FromSeconds), [typeof(double)])),
                CodeMatch.StoresField(AccessTools.Field(typeof(DamageSource), nameof(DamageSource.Duration)))
            );

            // Replace the duration with our own
            matcher.Insert(
                new CodeInstruction(OpCodes.Pop),
                new CodeInstruction(OpCodes.Ldloc, durationLocal.LocalIndex)
            );

            // Navigate to loading the ticks local
            matcher.MatchStartForward(
                CodeMatch.StoresField(AccessTools.Field(typeof(DamageSource), nameof(DamageSource.TicksPerDuration)))
            );

            // Replace the ticks with our own
            matcher.Insert(
                new CodeInstruction(OpCodes.Pop),
                new CodeInstruction(OpCodes.Ldloc, ticksLocal.LocalIndex)
            );

            return matcher.InstructionEnumeration();
        }

        public static double GetHealDuration(ItemSlot slot)
        {
            return slot.Itemstack?.ItemAttributes?["eatHealthEffectDurationSec"].AsDouble(ACulinaryArtilleryConfig.Current.ExpandedFoodHealDurationSec) ?? ACulinaryArtilleryConfig.Current.ExpandedFoodHealDurationSec;
        }

        public static int GetHealTicks(ItemSlot slot)
        {
            return slot.Itemstack?.ItemAttributes?["eatHealthEffectEffectTicks"].AsInt(ACulinaryArtilleryConfig.Current.ExpandedFoodHealTicks) ?? ACulinaryArtilleryConfig.Current.ExpandedFoodHealTicks;
        }
    }

    [HarmonyPatch(typeof(ItemSlotCreative))]
    public static class ItemSlotCreativePatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(ItemSlotCreative.TakeOut))]
        public static void TakeOutPostfix(ItemSlotCreative __instance, ItemStack __result, int quantity)
        {
            float[] sat = new float[6];
            List<string> ingredients = [];

            if (__result.Collectible is ItemExpandedRawFood erf)
            {
                if ((__result.Attributes["madeWith"] as StringArrayAttribute)?.value is string[] addIngs)
                {
                    for (int i = 0; i < addIngs.Length; i++)
                    {
                        bool hasCount = false;
                        // Look ahead for the quantity
                        if (i + 1 < addIngs.Length && int.TryParse(addIngs[i + 1], out int count))
                        {
                            hasCount = true;
                        }
                        else
                        {
                            count = 1;
                        }

                        CollectibleObject? obj = __instance.Inventory.Api.World.GetItem(addIngs[i]);
                        obj ??= __instance.Inventory.Api.World.GetBlock(addIngs[i]);

                        if (obj != null)
                        {
                            erf.GetNutrientsFromIngredient(ref sat, obj, count);
                            ingredients.Add(addIngs[i]);
                        }

                        if (hasCount) i++;
                    }
                }

                __result.Attributes.RemoveAttribute("madeWith");
                __result.Attributes.RemoveAttribute("expandedSats");

                __result.Attributes["madeWith"] = new StringArrayAttribute([.. ingredients]);
                __result.Attributes["expandedSats"] = new FloatArrayAttribute([.. sat]);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(ItemSlotCreative.TakeOutWhole))]
        public static void TakeOutWholePostfix(ItemSlotCreative __instance, ItemStack __result)
        {
            float[] sat = new float[6];
            List<string> ingredients = [];

            if (__result.Collectible is ItemExpandedRawFood erf)
            {
                if ((__result.Attributes["madeWith"] as StringArrayAttribute)?.value is string[] addIngs)
                {
                    for (int i = 0; i < addIngs.Length; i++)
                    {
                        bool hasCount = false;
                        // Look ahead for the quantity
                        if (i + 1 < addIngs.Length && int.TryParse(addIngs[i + 1], out int count))
                        {
                            hasCount = true;
                        }
                        else
                        {
                            count = 1;
                        }

                        CollectibleObject? obj = __instance.Inventory.Api.World.GetItem(addIngs[i]);
                        obj ??= __instance.Inventory.Api.World.GetBlock(addIngs[i]);

                        if (obj != null)
                        {
                            erf.GetNutrientsFromIngredient(ref sat, obj, count);
                            ingredients.Add(addIngs[i]);
                        }

                        if (hasCount) i++;
                    }
                }

                __result.Attributes.RemoveAttribute("madeWith");
                __result.Attributes.RemoveAttribute("expandedSats");

                __result.Attributes["madeWith"] = new StringArrayAttribute([.. ingredients]);
                __result.Attributes["expandedSats"] = new FloatArrayAttribute([.. sat]);
            }
        }
    }

    [HarmonyPatch(typeof(BlockPie))]
    public static class BlockPiePatches
    {
        [HarmonyReversePatch]
        [HarmonyPatch(typeof(BlockMeal), nameof(BlockMeal.OnLoaded))]
        public static void MealOnLoaded(object instance, ICoreAPI api) => throw new NotImplementedException("Stub is replaced by method");



        [HarmonyPostfix]
        [HarmonyPatch(nameof(BlockPie.GetPlacedBlockInteractionHelp))]
        public static void GetPlacedBlockInteractionHelpPostfix(ref BlockPie __instance, ref WorldInteraction[] __result, IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            __result = [.. __result.Where(bi => bi.ActionLangCode != "blockhelp-meal-eat" && bi.ActionLangCode != "blockhelp-meal-pickup")];
        }
    }

    [HarmonyPatch(typeof(CookingRecipeIngredient))]
    public static class CookingRecipeIngredientPatcher
    {
        [HarmonyReversePatch]
        [HarmonyPatch("Resolve")]
        public static void Resolve(object instance, IWorldAccessor world, string sourceForErrorLogging) => throw new NotImplementedException("Method replaces stub");

        [HarmonyPrefix]
        [HarmonyPatch("GetMatchingStack")]
        public static bool displayFix(ItemStack inputStack, ref CookingRecipeStack? __result, CookingRecipeIngredient __instance)
        {
            if (inputStack == null)
            {
                __result = null;
                return false;
            }

            string[] ignoredStackAttributes = [.. GlobalConstants.IgnoredStackAttributes, "madeWith", "expandedSats", "timeFrozen"];
            for (int i = 0; i < __instance.ValidStacks.Length; i++)
            {
                bool isWildCard = __instance.ValidStacks[i].Code.Path.Contains("*");
                bool found =
                    (isWildCard && inputStack.Collectible.WildCardMatch(__instance.ValidStacks[i].Code))
                    || (!isWildCard && inputStack.Equals(__instance.world, __instance.ValidStacks[i].ResolvedItemStack, ignoredStackAttributes))
                    || (__instance.ValidStacks[i].CookedStack?.ResolvedItemStack is ItemStack cookedStack && inputStack.Equals(__instance.world, cookedStack, ignoredStackAttributes))
                ;

                if (found)
                {
                    __result = __instance.ValidStacks[i];
                    return false;
                }
            }

            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(CollectibleBehaviorHandbookTextAndExtraInfo), "addIngredientForInfo")]
    public static class GetHandbookIngredientForPatch
    {
        public static void Postfix(ref bool __result, ICoreClientAPI capi, ItemStack[] allStacks, ActionConsumable<string> openDetailPageFor, ItemStack stack, List<RichTextComponentBase> components, float marginTop, List<ItemStack> containers, List<ItemStack> fuels, List<ItemStack> molds, bool haveText)
        {
            int pieRecipeCount = 0;
            List<RichTextComponentBase> excessivePieRecipeComponents = [];

            // Toppings need to have the default crust type because they have only one shape.
            foreach (MealstackTextComponent? comp in components.OfType<MealstackTextComponent>())
            {
                if (comp == null) continue;

                ItemStack? mealBlock = Traverse.Create(comp).Field("dummySlot").GetValue<DummySlot>()?.Itemstack;
                if (mealBlock?.Block is not BlockPie pieBlock) continue;

                ItemStack? topCrustStack = pieBlock.GetContents(capi.World, mealBlock).ElementAtOrDefault(5);

                if (InPieProperties.ReadFrom(topCrustStack)?.PartType == EnumPiePartType.Topping)
                {
                    mealBlock.Attributes.SetString("topCrustType", "full");
                }


                pieRecipeCount++;
                if (pieRecipeCount > 48)
                {
                    excessivePieRecipeComponents.Add(comp);
                }
            }

            // Rendering too many new meals in one frame overburdens MealMeshCache.
            foreach (RichTextComponentBase comp in excessivePieRecipeComponents)
            {
                components.Remove(comp);
            }

            var newComponents = HandbookInfoExtensions.ACAHandbookIngredientForComponents(capi, allStacks, openDetailPageFor, stack);
            if (newComponents.Count == 0) return;

            if (!components.Any(comp => (comp as RichTextComponent)?.DisplayText == Lang.Get("Ingredient for") + "\n"))
            {
                CollectibleBehaviorHandbookTextAndExtraInfo.AddHeading(components, capi, "Ingredient for", ref __result);
                components.Add(new ClearFloatTextComponent(capi, 2));
                components.AddRange(newComponents);
                components.Add(new ClearFloatTextComponent(capi, 3));
            }
            else
            {
                var firstMealstack = components.FirstOrDefault(comp => comp is MealstackTextComponent);
                int insertIndex = components.Count - 1;
                if (firstMealstack != null) insertIndex = components.IndexOf(firstMealstack);
                components.InsertRange(insertIndex, newComponents);
            }
        }
    }

    [HarmonyPatch(typeof(CollectibleBehaviorHandbookTextAndExtraInfo), "addProcessesIntoInfo")]
    public static class GetHandbookProcessesIntoPatch
    {
        public static bool Prefix(ref bool __result, ICoreClientAPI capi, ActionConsumable<string> openDetailPageFor, ItemStack stack, List<RichTextComponentBase> components, float marginTop, float marginBottom, List<ItemStack> containers, List<ItemStack> fuels, bool haveText)
        {
            components.AddRange(HandbookInfoExtensions.ACAHandbookProcessesIntoComponents(capi, openDetailPageFor, stack, marginBottom, fuels, haveText));
            return true;
        }
    }

    [HarmonyPatch(typeof(CollectibleBehaviorHandbookTextAndExtraInfo), "addCreatedByInfo")]
    public static class GetHandbookCreatedByPatch
    {
        public static void Postfix(ref bool __result, ICoreClientAPI capi, ItemStack[] allStacks, ActionConsumable<string> openDetailPageFor, ItemStack stack, List<RichTextComponentBase> components, float marginTop, List<ItemStack> containers, List<ItemStack> fuels, List<ItemStack> molds, bool haveText)
        {
            ObjectCacheUtil.GetOrCreate(capi, "ACAhandbooksimmerStacks", () =>
            {
                List<ItemStack> validStacks = [];

                foreach (var val in allStacks)
                {
                    if (HandbookInfoExtensions.getCanSimmer(fuels, val) && !validStacks.Any(s => s.Equals(capi.World, val, GlobalConstants.IgnoredStackAttributes)))
                    {
                        validStacks.Add(val);
                    }
                }

                return validStacks;
            });

            var newComponents = HandbookInfoExtensions.ACAHandbookCreatedByComponents(capi, allStacks, openDetailPageFor, stack, fuels);
            if (newComponents.Count == 0) return;

            if (!components.Any(comp => (comp as RichTextComponent)?.DisplayText == Lang.Get("Created by") + "\n"))
            {
                CollectibleBehaviorHandbookTextAndExtraInfo.AddHeading(components, capi, "Created by", ref __result);
                components.Add(new ClearFloatTextComponent(capi, 3));
                newComponents.RemoveAt(newComponents.Count - 1);
                components.AddRange(newComponents);
            }
            else
            {
                var beforeSubheading = HandbookInfoExtensions.GetSubHeading(components, "Baking (in oven)");
                beforeSubheading ??= HandbookInfoExtensions.GetSubHeading(components, "handbook-createdby-potcooking");
                beforeSubheading ??= HandbookInfoExtensions.GetSubHeading(components, "Crafting");
                int insertIndex = components.Count;
                if (beforeSubheading != null) insertIndex = components.IndexOf(beforeSubheading);
                components.InsertRange(insertIndex, newComponents);
            }
        }
    }

    [HarmonyPatch(typeof(CollectibleBehaviorHandbookTextAndExtraInfo), "addStorableInfo")]
    public static class GetHandbookStorablePatch
    {
        public static void Postfix(ICoreClientAPI capi, ItemStack[] allStacks, ActionConsumable<string> openDetailPageFor, ItemStack stack, List<RichTextComponentBase> components, float marginTop)
        {
            var newComponents = HandbookInfoExtensions.ACAHandbookStorableComponents(capi, allStacks, openDetailPageFor, stack);
            if (newComponents.Count == 0) return;

            if (!components.Any(comp => (comp as RichTextComponent)?.DisplayText == Lang.Get("Storable in/on") + "\n"))
            {
                bool haveText = components.Count > 0;
                CollectibleBehaviorHandbookTextAndExtraInfo.AddHeading(components, capi, "Storable in/on", ref haveText);
                components.Add(new ClearFloatTextComponent(capi, 3));
                CollectibleBehaviorHandbookTextAndExtraInfo.AddSubHeading(components, capi, openDetailPageFor, "handbook-storable-displaycontainers", null);
                newComponents[0].PaddingLeft = 5;
                components.AddRange(newComponents);
                components.Add(new RichTextComponent(capi, "\n", CairoFont.WhiteSmallText()));
            }
            else
            {
                var displaySubheading = HandbookInfoExtensions.GetSubHeading(components, "handbook-storable-displaycontainers");
                var beforeSubheading = HandbookInfoExtensions.GetSubHeading(components, "handbook-storable-liquidcontainers");
                beforeSubheading ??= HandbookInfoExtensions.GetSubHeading(components, "handbook-storable-foodcontainers");
                beforeSubheading ??= HandbookInfoExtensions.GetSubHeading(components, "handbook-storable-animalhusbandry");
                int insertIndex = components.Count - 1;
                if (beforeSubheading != null) insertIndex = components.IndexOf(beforeSubheading);
                if (displaySubheading != null) components.InsertRange(insertIndex, newComponents);
                else
                {
                    List<RichTextComponentBase> subheadingComponents = [];
                    CollectibleBehaviorHandbookTextAndExtraInfo.AddSubHeading(subheadingComponents, capi, openDetailPageFor, "handbook-storable-displaycontainers", null);
                    newComponents[0].PaddingLeft = 5;
                    components.InsertRange(insertIndex, [.. subheadingComponents, .. newComponents]);
                    components.Insert(insertIndex, new RichTextComponent(capi, "\n", CairoFont.WhiteSmallText()));
                }
            }
        }
    }

    [HarmonyPatch(typeof(CollectibleBehaviorHandbookTextAndExtraInfo), "addStoredInInfo")]
    public static class GetHandbookStoredInPatch
    {
        public static void Postfix(ICoreClientAPI capi, ItemStack[] allStacks, ActionConsumable<string> openDetailPageFor, ItemStack stack, List<RichTextComponentBase> components, float marginTop)
        {
            var newComponents = HandbookInfoExtensions.ACAHandbookStoredInComponents(capi, allStacks, openDetailPageFor, stack);
            if (newComponents.Count == 0) return;

            if (!components.Any(comp => (comp as RichTextComponent)?.DisplayText == Lang.Get("handbook-storedin") + "\n"))
            {
                bool haveText = components.Count > 0;
                components.Add(new ClearFloatTextComponent(capi, 7));
                CollectibleBehaviorHandbookTextAndExtraInfo.AddHeading(components, capi, "handbook-storedin", ref haveText);
                newComponents[0].PaddingLeft = 5;
                components.AddRange(newComponents);
                components.Add(new RichTextComponent(capi, "\n", CairoFont.WhiteSmallText()));
            }
            else components.InsertRange(components.Count - 1, newComponents);

            // Toppings need to have the default crust type because they have only one shape.
            // foreach (MealstackTextComponent? comp in components.OfType<MealstackTextComponent>())
            // {
            //     ItemStack? mealBlock = Traverse.Create(comp).Field("dummySlot").GetValue<DummySlot>()?.Itemstack;
            //     if (mealBlock?.Block is not BlockPie pieBlock) continue;

            //     ItemStack? topCrustStack = pieBlock.GetContents(capi.World, mealBlock).ElementAtOrDefault(5);

            //     if (InPieProperties.ReadFrom(topCrustStack)?.PartType == EnumPiePartType.Topping)
            //     {
            //         mealBlock.Attributes.SetString("topCrustType", "full");
            //     }
            // }
        }
    }

    [HarmonyPatch(typeof(ModSystemSurvivalHandbook), "onCreatePagesAsync")]
    public static class HandbookCreatePagesPatch
    {
        public static void Postfix(ref List<IFlatListItem> __result, ref ICoreClientAPI ___capi)
        {
            var firstPie = __result.FirstOrDefault(comp => (comp as GuiHandbookMealRecipePage)?.PageCode.Contains("handbook-mealrecipe-") == true && (comp as GuiHandbookMealRecipePage)?.PageCode.Contains("-pie") == true);
            int insertIndex = __result.Count;
            if (firstPie != null) insertIndex = __result.IndexOf(firstPie);

            var allstacks = ObjectCacheUtil.TryGet<ItemStack[]>(___capi, "handbookallstacks");

            foreach (var recipe in ___capi.GetMixingRecipes())
            {
                if (___capi.IsShuttingDown) break;
                if (recipe.CooksInto == null)
                {
                    GuiHandbookMealRecipePage elem = new GuiHandbookMealRecipePage(___capi, recipe, 6, false)
                    {
                        Visible = true
                    };

                    HandbookInfoExtensions.CreateCachedMealRecipeStacks(___capi, recipe);

                    __result.Insert(insertIndex, elem);
                    insertIndex++;
                }
            }

            foreach (var recipe in ___capi.GetCookingRecipes())
            {
                HandbookInfoExtensions.CreateCachedMealRecipeStacks(___capi, recipe);
            }

            foreach (var recipe in BlockPie.GetHandbookRecipes(___capi, allstacks))
            {
                HandbookInfoExtensions.CreateCachedMealRecipeStacks(___capi, recipe);
            }
        }
    }

    // [HarmonyPatch(typeof(GuiHandbookMealRecipePage), MethodType.Constructor)]
    // [HarmonyPatch([typeof(ICoreClientAPI), typeof(CookingRecipe), typeof(int), typeof(bool)])]
    // public static class GuiHandbookMealRecipePagePatch
    // {
    //     public static void Postfix(ref GuiHandbookMealRecipePage __instance, ICoreClientAPI capi, CookingRecipe recipe, int slots = 4, bool isPie = false)
    //     {
    //         if (!isPie) return;

    //         ItemStack? mealBlock = __instance.dummySlot.Itemstack;
    //         if (mealBlock?.Block is not BlockPie pieBlock) return;

    //         ItemStack? topCrustStack = pieBlock.GetContents(capi.World, mealBlock).ElementAtOrDefault(5);

    //         if (InPieProperties.ReadFrom(topCrustStack)?.PartType == EnumPiePartType.Topping)
    //         {
    //             mealBlock.Attributes.SetString("topCrustType", "full");
    //         }
    //     }
    // }


    [HarmonyPatch(typeof(CookingRecipe), "GenerateRandomMeal")]
    public static class CookingRecipeRandomPatch
    {
        public static bool Prefix(ref CookingRecipe __instance, ref ICoreClientAPI api, ref Dictionary<CookingRecipeIngredient, HashSet<ItemStack?>>? cachedValidStacksByIngredient)
        {
            if (__instance.Ingredients == null || __instance.Ingredients.Length == 0) return false;
            cachedValidStacksByIngredient ??= HandbookInfoExtensions.CreateCachedMealRecipeStacks(api, __instance);
            return true;
        }
    }

    [HarmonyPatch(typeof(BlockFirepit))]
    public class BlockFirepitPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("OnBlockInteractStart")]
        public static bool AllowPlacingSaucepanOnInteract(ref BlockFirepit __instance, ref bool __result, ref ICoreAPI ___api, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (__instance.Stage != 5) return true;

            BlockEntityFirepit? bef = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityFirepit;
            if (bef == null) return true;

            ItemStack? stack = byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack;
            if (stack?.Collectible is not BlockSaucepan) return true;

            bool activated = false;

            if (byPlayer.InventoryManager.ActiveHotbarSlot.TryPutInto(___api.World, bef.inputSlot, 1) > 0)
            {
                activated = true;
            }

            if (activated)
            {
                (byPlayer as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);

                var loc = stack.ItemAttributes?["placeSound"].Exists == true ? AssetLocation.Create(stack.ItemAttributes["placeSound"].AsString(), stack.Collectible.Code.Domain) : null;

                if (loc != null)
                {
                    ___api.World.PlaySoundAt(loc.WithPathPrefixOnce("sounds/"), blockSel.Position.X, blockSel.Position.InternalY, blockSel.Position.Z, byPlayer, 0.88f + (float)___api.World.Rand.NextDouble() * 0.24f, 16);
                }

                __result = true;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(InventorySmelting))]
    public class SmeltingInvPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch("GetOutputText")]
        public static void displayFix(ref string? __result, InventorySmelting __instance)
        {
            if (__instance[1].Itemstack?.Collectible is BlockSaucepan)
            {
                __result = (__instance[1].Itemstack.Collectible as BlockSaucepan)?.GetOutputText(__instance.Api.World, __instance);
            }
        }


        /// <summary>
        /// Turns the
        /// <code>
        ///     ...
        ///	    if (targetSlot == this.slots[1] && (stack.Collectible is BlockSmeltingContainer || stack.Collectible is BlockCookingContainer))
        ///	    {
        ///	        ...
        ///	    }  
        ///	    ...
        /// </code>
        /// block
        /// into
        /// <code>
        ///     ...
        ///	    if (targetSlot == this.slots[1] && (stack.Collectible is BlockSmeltingContainer || stack.Collectible is BlockSaucePan || stack.Collectible is BlockCookingContainer))
        ///	    {
        ///	        ...
        ///	    }  
        ///	    ...
        /// </code>
        /// to make saucepans/cauldrons prefer a firepit's input slot.
        /// </summary>
        /// 

        [HarmonyPrefix]
        [HarmonyPatch(typeof(InventorySmelting), nameof(InventorySmelting.GetSuitability))]
        public static bool Harmony_InventorySmelting_GetSuitability_Prefix(
            ItemSlot sourceSlot, ItemSlot targetSlot, ItemSlot[] ___slots, ref float __result)
        {
            var stack = sourceSlot.Itemstack;
            if (targetSlot == ___slots[1] && stack.Collectible is BlockSaucepan)
            {
                __result = 2.2f;
                return false;
            }
            return true;
        }
        // Thanks Apache!!!
    }

    [HarmonyPatch(typeof(CookingRecipe))]
    public class CookingRecipePatches
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetOutputName")]
        public static bool recipeNameFix(IWorldAccessor worldForResolve, ItemStack[] inputStacks, ref string __result, CookingRecipe __instance)
        {
            bool rotten = inputStacks.Any((stack) => stack?.Collectible.Code.Path == "rot");
            if (rotten)
            {
                __result = Lang.Get("Rotten Food");
                return false;
            }

            if (CookingRecipe.NamingRegistry.TryGetValue(__instance.Code!, out ICookingRecipeNamingHelper? namer))
            {
                __result = namer.GetNameForIngredients(worldForResolve, __instance.Code!, inputStacks);
                return false;
            }

            __result = new ACACookingRecipeNames().GetNameForIngredients(worldForResolve, __instance.Code, inputStacks);
            return false;
        }
    }

    [HarmonyPatch(typeof(BlockCookedContainerBase))]
    public class BlockMealContainerBasePatches
    {
        [HarmonyPostfix]
        [HarmonyPatch("GetCookingRecipe")]
        public static void recipeFix(ref CookingRecipe? __result, ItemStack containerStack, IWorldAccessor world, BlockCookedContainerBase __instance)
        {
            __result ??= world.Api.GetMixingRecipes().FirstOrDefault(rec => rec.Code == __instance.GetRecipeCode(world, containerStack));
        }

        [HarmonyPostfix]
        [HarmonyPatch("GetMealRecipe")]
        public static void mealFix(ref CookingRecipe? __result, ItemStack containerStack, IWorldAccessor world, BlockCookedContainerBase __instance)
        {
            __result ??= world.Api.GetMixingRecipes().FirstOrDefault(rec => rec.Code == __instance.GetRecipeCode(world, containerStack));
        }

        // Fixes a vanilla bug.
        // Prevent pies from disappearing and sealed crocks from accepting servings.
        [HarmonyPrefix]
        [HarmonyPatch("GetMergableQuantity")]
        public static bool mergeQuantityFix(BlockCookedContainerBase __instance, ref int __result, ItemStack sinkStack, ItemStack sourceStack, EnumMergePriority priority)
        {
            if (priority == EnumMergePriority.AutoMerge) return true;

            if (sourceStack.Collectible is BlockPie)
            {
                __result = 0;
                return false;
            }

            if (sourceStack.Collectible is BlockCrock && sourceStack.Attributes?.GetBool("sealed") == true)
            {
                __result = 0;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(BlockMeal))]
    public class BlockMealPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch("GetCookingRecipe")]
        public static void recipeFix(ref CookingRecipe? __result, ItemStack containerStack, IWorldAccessor world, BlockCookedContainerBase __instance)
        {
            __result ??= world.Api.GetMixingRecipes().FirstOrDefault(rec => rec.Code == __instance.GetRecipeCode(world, containerStack));
        }


        [HarmonyPrefix]
        [HarmonyPatch("GetContentNutritionProperties", typeof(IWorldAccessor), typeof(ItemSlot), typeof(ItemStack[]), typeof(EntityAgent), typeof(bool), typeof(float), typeof(float))]
        public static bool nutriFix(IWorldAccessor world, ItemSlot inSlot, ItemStack[] contentStacks, EntityAgent forEntity, ref FoodNutritionProperties[] __result, bool mulWithStacksize = false, float nutritionMul = 1, float healthMul = 1)
        {
            List<FoodNutritionProperties> props = new List<FoodNutritionProperties>();
            for (int i = 0; i < contentStacks.Length; i++)
            {
                if (contentStacks[i] == null)
                    continue;
                props.AddRange(ItemExpandedRawFood.GetExpandedContentNutritionProperties(
                                                                                            world,
                                                                                            inSlot,
                                                                                            contentStacks[i],
                                                                                            forEntity,
                                                                                            mulWithStacksize,
                                                                                            nutritionMul,
                                                                                            healthMul
                                                                                            ));
            }

            __result = [.. props];
            return false;
        }


        [HarmonyPrefix]
        [HarmonyPatch("GetContentNutritionFacts", typeof(IWorldAccessor), typeof(ItemSlot), typeof(ItemStack[]), typeof(EntityAgent), typeof(bool), typeof(float), typeof(float))]
        public static bool nutriFactsFix(IWorldAccessor world, ItemSlot inSlotorFirstSlot, ItemStack[] contentStacks, EntityAgent forEntity, ref string __result, bool mulWithStacksize = false, float nutritionMul = 1, float healthMul = 1)
        {
            FoodNutritionProperties[] props;

            Dictionary<EnumFoodCategory, float> totalSaturation = new Dictionary<EnumFoodCategory, float>();
            float totalHealth = 0;
            float satLossMul = 1;
            float healthLossMul = 1;

            for (int i = 0; i < contentStacks.Length; i++)
            {
                if (contentStacks[i] == null)
                    continue;
                DummySlot slot = new DummySlot(contentStacks[i], inSlotorFirstSlot.Inventory);
                TransitionState state = contentStacks[i].Collectible.UpdateAndGetTransitionState(world, slot, EnumTransitionType.Perish);
                float spoilState = state != null ? state.TransitionLevel : 0;

                satLossMul = GlobalConstants.FoodSpoilageSatLossMul(spoilState, slot.Itemstack, forEntity);
                healthLossMul = GlobalConstants.FoodSpoilageHealthLossMul(spoilState, slot.Itemstack, forEntity);

                props = ItemExpandedRawFood.GetExpandedContentNutritionProperties(world, inSlotorFirstSlot, contentStacks[i], forEntity, mulWithStacksize, nutritionMul, healthMul);
                for (int j = 0; j < props.Length; j++)
                {
                    FoodNutritionProperties prop = props[j];
                    if (prop == null)
                        continue;
                    float sat = 0;
                    totalSaturation.TryGetValue(prop.FoodCategory, out sat);
                    totalHealth += prop.Health * healthLossMul;
                    totalSaturation[prop.FoodCategory] = sat + prop.Satiety * satLossMul;
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Lang.Get("When eaten: {0} sat", Math.Round(totalSaturation.Values.Sum())));
            sb.AppendLine(Lang.Get("Nutrition Facts"));

            foreach (var val in totalSaturation)
            {
                sb.AppendLine(Lang.Get("efrecipes:- {0} {1} sat", Math.Round(val.Value, 1), ItemExpandedFood.GetLocalizedFoodCategory(val.Key)));
            }
            double roundedHealth = Math.Round(totalHealth, 1);
            if (roundedHealth != 0)
            {
                sb.AppendLine("- " + Lang.Get("Health: {0}{1} hp", roundedHealth > 0 ? "+" : "", roundedHealth));
            }

            __result = sb.ToString();
            return false;
        }

        [HarmonyTranspiler]
        [HarmonyPatch("Consume")]
        public static IEnumerable<CodeInstruction> AddHealOverTimePatch(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            CodeMatcher matcher = new(instructions, generator);

            matcher.DeclareLocal(typeof(ItemStack), out var stack);

            // Get the ItemStack inside the ItemSlot argument
            matcher.Start().MatchEndForward(
                CodeMatch.IsLdarg(3),
                CodeMatch.Calls(AccessTools.PropertyGetter(typeof(ItemSlot), nameof(ItemSlot.Itemstack)))
            );

            matcher.InsertAfter(
                CodeInstruction.StoreLocal(stack.LocalIndex),
                CodeInstruction.LoadLocal(stack.LocalIndex)
            );

            // Advance to the total health != 0 check
            matcher.MatchEndForward(
                CodeMatch.IsLdloc(),
                CodeMatch.LoadsConstant(0f),
                CodeMatch.Branches()
            );

            matcher.MatchEndForward(
                new CodeMatch(OpCodes.Newobj, AccessTools.Constructor(typeof(DamageSource)))
            );

            matcher.InsertAfter(
                new CodeInstruction(OpCodes.Dup), // Create a new DamageSource
                CodeInstruction.LoadLocal(stack.LocalIndex),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BlockMealPatches), nameof(SetHealingOverTimeOnSource)))
            );

            return matcher.InstructionEnumeration();
        }

        public static void SetHealingOverTimeOnSource(DamageSource source, ItemStack stack)
        {
            source.Duration = TimeSpan.FromSeconds(ACulinaryArtilleryConfig.Current.MealHealDurationSec);
            source.TicksPerDuration = ACulinaryArtilleryConfig.Current.MealHealTicks;
        }
    }


    [HarmonyPatch(typeof(BlockEntityQuern))]
    public class BlockEntityQuernPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("grindInput")]
        public static bool grindInputWIthInheritedAttributes(ref int ___nowOutputFace, BlockEntityQuern __instance)
        {

            ItemStack grindedStack = __instance.InputGrindProps.GroundStack.ResolvedItemstack.Clone();
            if (grindedStack.Collectible is IExpandedFood food) food.OnCreatedByGrinding(__instance.InputStack, grindedStack);
            else return true;

            if (__instance.OutputSlot.Itemstack == null)
            {
                __instance.OutputSlot.Itemstack = grindedStack;
            }
            else
            {
                if (__instance.OutputSlot.Itemstack.Collectible.GetMergableQuantity(__instance.OutputSlot.Itemstack, grindedStack, EnumMergePriority.AutoMerge) > 0)
                {
                    __instance.OutputSlot.Itemstack.StackSize += grindedStack.StackSize;
                }
                else
                {
                    BlockFacing face = BlockFacing.HORIZONTALS[___nowOutputFace];
                    ___nowOutputFace = (___nowOutputFace + 1) % 4;

                    Block block = __instance.Api.World.BlockAccessor.GetBlock(__instance.Pos.AddCopy(face));
                    if (block.Replaceable < 6000) return false;
                    __instance.Api.World.SpawnItemEntity(grindedStack, __instance.Pos.ToVec3d().Add(0.5 + face.Normalf.X * 0.7, 0.75, 0.5 + face.Normalf.Z * 0.7), new Vec3d(face.Normalf.X * 0.02f, 0, face.Normalf.Z * 0.02f));
                }
            }

            __instance.InputSlot.TakeOut(1);
            __instance.InputSlot.MarkDirty();
            __instance.OutputSlot.MarkDirty();
            return false;
        }
    }

    [HarmonyPatch(typeof(InventoryQuern))]
    public class InventoryQuernPatch
    {
        // Require a liquid container to take liquid outputs.
        [HarmonyPrefix]
        [HarmonyPatch("NewSlot")]
        public static bool WaterTightOutputSlot(ref InventoryQuern __instance, ref ItemSlot __result, int i)
        {
            __result = i == 1 ? new ItemSlotWatertight(__instance) : new ItemSlotSurvival(__instance);
            return false;
        }
    }

    // Giant patch to improve clay oven interaction
    [HarmonyPatch(typeof(BlockClayOven))]
    public class BlockClayOvenPatch
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "ovenInv")]
        extern static ref InventoryOven BEOvenInv(BlockEntityOven instance);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "interactions")]
        extern static ref WorldInteraction[] Interactions(BlockClayOven instance);


        [HarmonyPrefix]
        [HarmonyPatch("OnLoaded")]
        public static bool OnLoadedPatch(BlockClayOven __instance, ICoreAPI api)
        {
            __instance.PlacedPriorityInteract = true;

            if (api is not ICoreClientAPI capi) return true;

            Interactions(__instance) = ObjectCacheUtil.GetOrCreate(api, "ovenInteractions", () =>
            {
                List<ItemStack> bakeableStacklist = [];
                List<ItemStack> fuelStacklist = [];
                List<ItemStack> canIgniteStacks = BlockBehaviorCanIgnite.CanIgniteStacks(api, true);

                foreach (CollectibleObject obj in api.World.Collectibles)
                {
                    // we test firewood first because LazyWarlock's mod adds a wood baking recipe, which we don't want to be treated as a bakeable item here
                    if (obj.Attributes?.IsTrue("isClayOvenFuel") == true)
                    {
                        List<ItemStack> stacks = obj.GetHandBookStacks(capi);
                        if (stacks != null) fuelStacklist.AddRange(stacks);
                    }
                    else if (obj.Attributes?["bakingProperties"]?.AsObject<BakingProperties>() != null || obj.CombustibleProps?.SmeltingType == EnumSmeltType.Bake && obj.CombustibleProps.SmeltedStack != null && obj.CombustibleProps.MeltingPoint < BlockEntityOven.maxBakingTemperatureAccepted)
                    {
                        List<ItemStack> stacks = obj.GetHandBookStacks(capi);
                        if (stacks != null) bakeableStacklist.AddRange(stacks);
                    }
                }

                foreach (var stack in bakeableStacklist)
                {
                    if (stack.Collectible is not BlockPie pieBlock) continue;

                    stack.Attributes.SetInt("pieSize", 4);
                    stack.Attributes.SetString("topCrustType", "square");
                    stack.Attributes.SetInt("bakeLevel", 0);

                    ItemStack doughStack = new(api.World.GetItem("dough-spelt"), 2);
                    ItemStack fillingStack = new(api.World.GetItem("fruit-redapple"), 2);
                    pieBlock.SetContents(stack, [doughStack, fillingStack, fillingStack, fillingStack, fillingStack, doughStack]);
                    stack.Attributes.SetFloat("quantityServings", 1);
                }

                return new WorldInteraction[] {
                    new ()
                    {
                        ActionLangCode = "blockhelp-oven-take",
                        MouseButton = EnumMouseButton.Right,
                        ShouldApply = (wi, bs, es) => {
                            return api.World.BlockAccessor.GetBlockEntity<BlockEntityOven>(bs.Position) is BlockEntityOven beo && !BEOvenInv(beo).Empty && !beo.IsBurning;
                        }
                    },
                    new ()
                    {
                        ActionLangCode = "blockhelp-oven-fuel",
                        HotKeyCode = "shift",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = [.. fuelStacklist],
                        GetMatchingStacks = (wi, bs, es) => {
                            return wi.Itemstacks.Where(stack => api.World.BlockAccessor.GetBlockEntity<BlockEntityOven>(bs.Position) is BlockEntityOven beo && BlockEntityOvenPatch.CanAddFuel(beo, stack) == true).ToArray();
                        }
                    },
                    new (){
                        ActionLangCode = "blockhelp-oven-bakeable",
                        HotKeyCode = "shift",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = [.. bakeableStacklist],
                        GetMatchingStacks = (wi, bs, es) => {
                            return wi.Itemstacks.Where(stack => api.World.BlockAccessor.GetBlockEntity<BlockEntityOven>(bs.Position) is BlockEntityOven beo && BlockEntityOvenPatch.CanAddBakeable(beo, stack) == true).ToArray();
                        }
                    },
                    new ()
                    {
                        ActionLangCode = "blockhelp-oven-ignite",
                        MouseButton = EnumMouseButton.Right,
                        HotKeyCode = "shift",
                        Itemstacks = [.. canIgniteStacks],
                        GetMatchingStacks = (wi, bs, es) => {
                            if (wi.Itemstacks.Length == 0) return null;
                            return api.World.BlockAccessor.GetBlockEntity<BlockEntityOven>(bs.Position) is BlockEntityOven beo && beo.CanIgnite() ? wi.Itemstacks : null;
                        }
                    }
                };
            });

            return true;
        }
    }

    [HarmonyPatch(typeof(BlockEntityOven))]
    public class BlockEntityOvenPatch
    {
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "TryTake")]
        extern static bool TryTake(BlockEntityOven instance, IPlayer byPlayer);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "TryAddFuel")]
        extern static bool TryAddFuel(BlockEntityOven instance, ItemSlot slot);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "TryPut")]
        extern static bool TryPut(BlockEntityOven instance, ItemSlot slot);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "ovenInv")]
        extern static ref InventoryOven OvenInv(BlockEntityOven instance);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "EnvironmentTemperature")]
        extern static int EnvironmentTemperature(BlockEntityOven instance);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "bakingData")]
        extern static ref OvenItemData[] BakingData(BlockEntityOven instance);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "updateMesh")]
        extern static void UpdateMesh(BlockEntityDisplay instance, int index);

        [HarmonyPrefix]
        [HarmonyPatch("TryPut")]
        public static bool TryPutPatch(BlockEntityOven __instance, ref bool __result, ItemSlot slot)
        {
            __result = TryPut(__instance, slot, out _, out _);
            return false;
        }

        public static bool TryPut(BlockEntityOven beo, ItemSlot slot, out string? errCode, out string? errMessage)
        {
            // CanAddBakeable checks for large item requirements
            if (!CanAddBakeable(beo, slot.Itemstack, out errCode, out errMessage)) return false;

            int empty = Array.FindIndex(OvenInv(beo).Take(4).ToArray(), slot => slot.Empty);
            if (empty == -1) return false;

            int moved = slot.TryPutInto(beo.Api.World, OvenInv(beo)[empty]);

            if (moved > 0)
            {
                // We store the baked level data into the BlockEntity itself, for continuity and to avoid adding unwanted attributes to the ItemStacks (which e.g. could cause them not to stack)
                BakingData(beo)[empty] = new OvenItemData(OvenInv(beo)[empty].Itemstack);
                UpdateMesh(beo, empty);

                beo.MarkDirty();
            }

            return moved > 0;
        }

        public static bool CanAddBakeable(BlockEntityOven beo, ItemStack stack)
        {
            return CanAddBakeable(beo, stack, out _, out _);
        }

        /// <summary>
        /// Whether or not the oven can currently accept a given bakeable item.
        /// </summary>
        /// <returns>True if either the oven is empty or there is enough space to accept the given item. Oven must not be burning or contain fuel.</returns>
        public static bool CanAddBakeable(BlockEntityOven beo, ItemStack? stack, out string? errCode, out string? errMessage)
        {
            errCode = null;
            errMessage = null;

            if (beo.IsBurning)
            {
                errCode = "fuelburning";
                errMessage = Lang.Get("Wait until the fire is out");
                return false;
            }

            if (beo.HasFuel)
            {
                errCode = "fuelpresent";
                errMessage = Lang.Get("ovenerror-notfuel");
                return false;
            }

            // Don't invite player to insert bakeable items in a cold oven - 25 degrees allows some hysteresis if SEASONS causes changes in enviro temperature
            if (beo.ovenTemperature <= EnvironmentTemperature(beo) + 25)
            {
                errCode = "toocold";
                errMessage = Lang.Get("ovenerror-toocold");
                return false;
            }


            if (OvenInv(beo)[0].Empty) return true;


            if (stack?.ItemAttributes?.KeyExists("bakingProperties") == false)
            {
                errCode = "notbakeable";
                errMessage = Lang.Get("This item is not bakeable.");
                return false;
            }

            // Large items take up all slots
            if (BakingProperties.ReadFrom(OvenInv(beo)[0].Itemstack)?.LargeItem ?? false)
            {
                errCode = "ovenfull";
                errMessage = Lang.Get("Oven is full");
                return false;
            }

            if (OvenInv(beo).Take(4).All(slot => !slot.Empty))
            {
                errCode = "ovenfull";
                errMessage = Lang.Get("Oven is full");
                return false;
            }

            // Handle held large item separately if not all slots are full
            if (BakingProperties.ReadFrom(stack)?.LargeItem ?? false && !OvenInv(beo)[0].Empty)
            {
                errCode = "notenoughspace";
                errMessage = Lang.Get("ovenerror-notenoughspace");
                return false;
            }

            return true;
        }

        public static bool CanAddFuel(BlockEntityOven beo, ItemStack? stack)
        {
            return CanAddFuel(beo, stack, out _, out _);
        }

        /// <summary>
        /// Whether or not the oven can currently accept a given fuel item.
        /// </summary>
        /// <returns>True if either the oven is empty or the fuel slot is of the same type and has space. Oven must not be burning or contain bakeables.</returns>
        public static bool CanAddFuel(BlockEntityOven beo, ItemStack? stack, out string? errCode, out string? errMessage)
        {
            errCode = null;
            errMessage = null;

            if (beo.FuelSlot.Empty) return true;

            if (beo.IsBurning)
            {
                errCode = "fuelburning";
                errMessage = Lang.Get("Wait until the fire is out");
                return false;
            }

            if (stack?.ItemAttributes?.IsTrue("isClayOvenFuel") == false)
            {
                errCode = "notfuel";
                errMessage = Lang.Get("ovenerror-notfuel");
                return false;
            }

            if (beo.HasFuel && beo.FuelSlot.StackSize < beo.fuelitemCapacity && !beo.FuelSlot.Itemstack.Satisfies(stack))
            {
                errCode = "nonmatchingfuel";
                errMessage = Lang.Get("ovenerror-nonmatchingfuel");
                return false;
            }

            // Bakeables are already present
            if (!beo.FuelSlot.Empty && !beo.HasFuel)
            {
                errCode = "notbakeable";
                errMessage = Lang.Get("This item is not bakeable.");
                return false;
            }

            if (beo.FuelSlot.StackSize >= beo.fuelitemCapacity)
            {
                errCode = "ovenfull";
                errMessage = Lang.Get("Oven is full");
                return false;
            }

            return true;
        }

        public static bool TryAddFuel(BlockEntityOven beo, ItemSlot slot, out string? errCode, out string? errMessage)
        {
            errCode = null;
            errMessage = null;

            if (!CanAddFuel(beo, slot.Itemstack, out errCode, out errMessage)) return false;

            int moved = slot.TryPutInto(beo.Api.World, beo.FuelSlot);

            if (moved > 0)
            {
                UpdateMesh(beo, 0);
                beo.MarkDirty();
            }

            return moved > 0;
        }

        [HarmonyPrefix]
        [HarmonyPatch("OnInteract")]
        public static bool OnInteractPatch(BlockEntityOven __instance, ref bool __result, IPlayer byPlayer, BlockSelection bs)
        {
            ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;

            if (!byPlayer.Entity.Controls.ShiftKey)
            {
                if (TryTake(__instance, byPlayer))
                {
                    byPlayer.InventoryManager.BroadcastHotbarSlot();
                    __result = true;
                    return false;
                }

                __result = false;
                return false;
            }

            if (slot.Itemstack?.Collectible is not CollectibleObject co) { __result = false; return false; }
            AssetLocation stackName = co.Code;
            CombustibleProperties? combustibleProperties = co.GetCombustibleProperties(__instance.Api.World, slot.Itemstack, null);

            string? errCode;
            string? errMessage;

            if (co.Attributes?.IsTrue("isClayOvenFuel") == true)
            {
                if (TryAddFuel(__instance, slot, out errCode, out errMessage))
                {
                    SoundAttributes? sound = slot.Itemstack?.Block?.Sounds?.Place;
                    __instance.Api.World.PlaySoundAt(sound ?? GlobalConstants.DefaultBuildSound, byPlayer.Entity, byPlayer);
                    byPlayer.InventoryManager.BroadcastHotbarSlot();
                    __instance.Api.World.Logger.Audit("{0} Put 1x{1} into Clay oven at {2}.",
                        byPlayer.PlayerName,
                        stackName,
                        __instance.Pos
                    );
                    (byPlayer as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);

                    __result = true;
                    return false;
                }

                (__instance.Api as ICoreClientAPI)?.TriggerIngameError(__instance, errCode, errMessage);

                __result = false;
                return false;
            }


            // Can't meaningfully bake anything requiring heat over 260 in the basic clay oven
            if (co?.Attributes?.KeyExists("bakingProperties") == true || combustibleProperties?.SmeltingType == EnumSmeltType.Bake && combustibleProperties.MeltingPoint < BlockEntityOven.maxBakingTemperatureAccepted)
            {
                if (TryPut(__instance, slot, out errCode, out errMessage))
                {
                    SoundAttributes? sound = slot.Itemstack?.Block?.Sounds?.Place;
                    __instance.Api.World.PlaySoundAt(sound ?? new SoundAttributes(new AssetLocation("sounds/player/buildhigh"), true) { Range = 16 }, byPlayer.Entity, byPlayer);
                    byPlayer.InventoryManager.BroadcastHotbarSlot();
                    __instance.Api.World.Logger.Audit("{0} Put 1x{1} into Clay oven at {2}.",
                        byPlayer.PlayerName,
                        stackName,
                        __instance.Pos
                    );
                    (byPlayer as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
                }
                else if (slot.Itemstack.Block?.GetBehavior<BlockBehaviorCanIgnite>() == null)
                {
                    (__instance.Api as ICoreClientAPI)?.TriggerIngameError(__instance, errCode, errMessage);
                }

                __result = true;
                return false;
            }

            __result = false;
            return false;
        }
    }
}
