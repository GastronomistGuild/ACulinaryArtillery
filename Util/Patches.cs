using ACulinaryArtillery.Util;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Vintagestory.API.Datastructures;

namespace ACulinaryArtillery
{
    [HarmonyPatch(typeof(BlockLiquidContainerBase))]
    public static class AntiCorkTransmutationPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(BlockLiquidContainerBase.SplitStackAndPerformAction))]
        public static bool TransmutationFix(ref BlockLiquidContainerBase __instance, ref int __result, Entity byEntity, ItemSlot slot, System.Func<ItemStack, int> action)
        {
            __result = BottleSplitStackAndPerformAction(byEntity, slot, action);
            return false;
        }
        public static int BottleSplitStackAndPerformAction(Entity byEntity, ItemSlot slot, System.Func<ItemStack, int> action)
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
                        if (pslot.Empty || pslot is ItemSlotCreative || pslot.StackSize == pslot.Itemstack.Collectible.MaxStackSize || pslot.Itemstack.Collectible.GetTags(pslot.Itemstack).Overlaps(BlockBottle.bottleStopperTag) || pslot.Itemstack.Collectible.GetTags(pslot.Itemstack).Overlaps(BlockBottle.bottleSealantTag))
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
    }


    [HarmonyPatch(typeof(BlockPie))]
    public static class BlockPiePatches
    {
        [HarmonyReversePatch]
        [HarmonyPatch(typeof(BlockMeal), nameof(BlockMeal.OnLoaded))]
        public static void MealOnLoaded(object instance, ICoreAPI api) => throw new NotImplementedException("Stub is replaced by method");

        [HarmonyPrefix]
        [HarmonyPatch(nameof(BlockPie.OnLoaded))]
        public static bool OnLoadedPrefix(
            ref BlockPie __instance,
            ref float ___InteractionHelpYOffset,
            ref WorldInteraction[]? ___interactions,
            ref MealMeshCache? ___ms,
            ref bool ___displayContentsInfo,
            ICoreAPI api
        )
        {
            MealOnLoaded(__instance, api);

            BlockPie.TopCrustTypes ??= api.Assets.Get("config/pietopcrusttypes.json").ToObject<PieTopCrustType[]>();

            ___InteractionHelpYOffset = 0.375f;

            BlockPie pie = __instance;

            ___interactions = ObjectCacheUtil.GetOrCreate(api, "pieInteractions-", () =>
            {
                ItemStack[] knifeStacks = ObjectCacheUtil.GetToolStacks(api, EnumTool.Knife);
                List<ItemStack> doughStacks = [];
                List<ItemStack> fillStacks = [];
                List<ItemStack> toppingStacks = [];

                foreach (CollectibleObject obj in api.World.Collectibles)
                {
                    string? piePropsErr = null;
                    if (ExpandedInPieProperties.ReadFrom(obj, out piePropsErr) is not ExpandedInPieProperties pieProps)
                    {
                        if (piePropsErr != null) api.World.Logger.Error(piePropsErr);
                        continue;
                    }

                    EnumPiePartType partType = pieProps.PartType;

                    if (obj is ItemDough || partType == EnumPiePartType.Crust)
                    {
                        doughStacks.Add(new ItemStack(obj, pieProps.ItemsPerPortion()));
                    }

                    switch (partType)
                    {
                        case EnumPiePartType.Filling:
                            fillStacks.Add(new ItemStack(obj, pieProps.ItemsPerPortion()));
                            break;
                        case EnumPiePartType.Topping:
                            toppingStacks.Add(new ItemStack(obj, pieProps.ItemsPerPortion()));
                            break;
                        case EnumPiePartType.Crust:
                            toppingStacks.Add(new ItemStack(obj, pieProps.ItemsPerPortion()));
                            break;
                    }

                    if (pieProps.PartType == EnumPiePartType.Filling || pieProps.PartType == EnumPiePartType.Topping)
                    {
                        int nonFoodCatCodes = pieProps.MixingCodes.Where(code => code != pieProps.FoodCategory.ToString().ToLowerInvariant()).ToArray().Length;
                        if (nonFoodCatCodes == 0 && pieProps.FoodCategory == EnumFoodCategory.NoNutrition)
                        {
                            api.World.Logger.Error($"InPieProperties for filling {obj.Code} has no mixing codes and food category NoNutrition. It cannot be added to pies! See the documentation.");
                        }

                        if (!pieProps.AllowMixing && nonFoodCatCodes > 0)
                        {
                            api.World.Logger.Error($"InPieProperties for filling {obj.Code} has explicit mixingCodes, but allowMixing is disabled. Mixing codes will be ignored. Don't do this intentionally. Allow mixing or remove the mixing codes to suppress this error.");
                        }
                    }
                }

                return new WorldInteraction[]
                {
                    new() {
                        ActionLangCode = "blockhelp-pie-cut",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = knifeStacks,
                        GetMatchingStacks = (wi, bs, _) => {
                            if (pie.GetBlockEntity<BlockEntityPie>(bs.Position) is not BlockEntityPie bep) return null;
                            return pie.State != "raw" && bep.SlicesLeft > 1 ? wi.Itemstacks : null;
                        }
                    },
                    new() {
                        ActionLangCode = "blockhelp-pie-addfilling",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = fillStacks.ToArray(),
                        GetMatchingStacks = (wi, bs, _) => {
                            if (pie.GetBlockEntity<BlockEntityPie>(bs.Position) is not BlockEntityPie bep) return null;
                            return bep.State == "raw" && !bep.HasAllFilling ? wi.Itemstacks.Where(stack => ExpandedPieUtil.BEPieCanAddIngredient(bep, stack)).ToArray() : null;
                        }
                    },
                    new() {
                        ActionLangCode = "game:blockhelp-pie-addcrustortopping",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = toppingStacks.ToArray(),
                        GetMatchingStacks = (wi, bs, _) => {
                            if (pie.GetBlockEntity<BlockEntityPie>(bs.Position) is not BlockEntityPie bep) return null;
                            return bep.State == "raw" && bep.HasAllFilling && ExpandedPieUtil.BEPieGetToppingType(bep) == null ? wi.Itemstacks.Where(stack => ExpandedPieUtil.BEPieCanAddIngredient(bep, stack)).ToArray() : null;
                        }
                    },
                    new() {
                        ActionLangCode = "blockhelp-pie-changecruststyle",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = knifeStacks,
                        GetMatchingStacks = (wi, bs, _) => {
                            if (pie.GetBlockEntity<BlockEntityPie>(bs.Position) is not BlockEntityPie bep) return null;
                            return bep.State == "raw" && ExpandedPieUtil.BEPieGetToppingType(bep) == EnumPiePartType.Crust ? wi.Itemstacks : null;
                        }
                    }
                };
            });

            ___ms = api.ModLoader.GetModSystem<MealMeshCache>();

            ___displayContentsInfo = false;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(BlockPie.GetHeldItemName))]
        public static bool GetHeldItemNamePrefix(
            BlockPie __instance,
            ref string __result,
            ItemStack? itemStack,
            ref ICoreAPI ___api
        )
        {
            ItemStack?[] cStacks = __instance.GetContents(___api.World, itemStack);
            if (cStacks.Length <= 1 || cStacks[1] == null)
            {
                __result = Lang.Get("pie-empty");
                return false;
            }

            // Use null-forgiving in this method in case the pie is malformed,
            // e.g. an ingredient lost its pie props. Report it because its
            // a content error.
            for (int i = 0; i < 6; i++)
            {
                if (cStacks[i] != null && ExpandedInPieProperties.ReadFrom(cStacks[i]) == null)
                {
                    ExpandedPieUtil.ReportMissingPieProps(___api.Logger, cStacks[i]!.Collectible.Code);
                }
            }

            bool singleIngredient = true;
            IEnumerable<string> mixCodes = ExpandedInPieProperties.ReadFrom(cStacks[1])?.MixingCodes ?? [];
            for (int i = 2; i < cStacks.Length - 1; i++)
            {
                if (cStacks[i] == null) continue;

                singleIngredient &= cStacks[i]?.Equals(___api.World, cStacks[1], GlobalConstants.IgnoredStackAttributes) ?? true;
                mixCodes = ExpandedInPieProperties.ReadFrom(cStacks[i])?.MixingCodes.Intersect(mixCodes) ?? [];

                if (!singleIngredient && !mixCodes.Any()) break;
            }

            string state = __instance.Variant["state"];

            string mixCode = mixCodes.FirstOrDefault("missing");

            if (!singleIngredient && mixCode == "missing")
            {
                ___api.Logger.Error($"Pie does not have any valid mixing codes! They were likely removed from one of the ingredients: [\n    {string.Join("\n    ", (object?[])cStacks)}\n]");
            }

            string pieName = Lang.Get(singleIngredient
                ? "pie-single-" + cStacks[1]!.Collectible.Code.ToShortString() + "-" + state
                : "pie-mixed-" + mixCode + "-" + state);

            if (cStacks[5] != null && ExpandedInPieProperties.ReadFrom(cStacks[5])?.PartType != EnumPiePartType.Crust)
            {
                pieName = Lang.Get("meal-topping-ingredient-format", cStacks[5]?.Collectible.GetHeldItemName(cStacks[5]), pieName.ToLowerInvariant());
            }

            __result = pieName;
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(BlockPie.GetPlacedBlockInteractionHelp))]
        public static void GetPlacedBlockInteractionHelpPostfix(ref BlockPie __instance, ref WorldInteraction[] __result, IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            __result = [.. __result.Where(bi => bi.ActionLangCode != "blockhelp-meal-eat" && bi.ActionLangCode != "blockhelp-meal-pickup")];
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(BlockPie.GetHandbookRecipes))]
        public static bool GetHandbookRecipesPrefix(ref List<CookingRecipe> __result, ICoreAPI api, ItemStack[] allStacks)
        {
            List<ItemStack> crusts = [];
            List<ItemStack> noMixFillings = [];
            Dictionary<string, List<ItemStack>> mixedFillings = [];
            Dictionary<string, List<ItemStack>> toppingsByCode = [];

            foreach (ItemStack s in allStacks)
            {
                if (ExpandedInPieProperties.ReadFrom(s) is not ExpandedInPieProperties pieProps) continue;
                ItemStack stack = s.Clone();

                stack.StackSize = pieProps.ItemsPerPortion();

                switch (pieProps.PartType)
                {
                    case EnumPiePartType.Crust:
                        crusts.Add(stack);

                        break;
                    case EnumPiePartType.Filling:
                        if (pieProps.AllowMixing)
                        {
                            foreach (string mixCode in pieProps.MixingCodes)
                            {
                                if (mixedFillings.TryGetValue(mixCode, out List<ItemStack>? value)) value.Add(stack);
                                else mixedFillings.Add(mixCode, [stack]);
                            }
                        }
                        else
                        {
                            noMixFillings.Add(stack);
                        }
                        break;
                    case EnumPiePartType.Topping:
                        foreach (string mixCode in pieProps.MixingCodes)
                        {
                            if (toppingsByCode.TryGetValue(mixCode, out List<ItemStack>? value)) value.Add(stack);
                            else toppingsByCode.Add(mixCode, [stack]);
                        }
                        break;
                }
            }

            __result =
            [
                .. mixedFillings.Select(entry =>
                    {
                        List<ItemStack> toppings = toppingsByCode
                            .Where(topping => topping.Key == entry.Key)
                            .SelectMany(topping => topping.Value)
                            .ToList();

                        // Crusts apply to all mixing codes, so always add them
                        toppings.AddRange(crusts);

                        return CreateRecipe(
                            api.World,
                            "mixed-" + entry.Key.ToLowerInvariant(),
                            crusts,
                            [.. entry.Value],
                            toppings);
                    }
                ),
                .. noMixFillings.Select(stack =>
                {
                    List<ItemStack> toppings = toppingsByCode
                        .Where(topping => ExpandedInPieProperties.ReadFrom(stack)!.MixingCodes.Contains(topping.Key))
                        .SelectMany(topping => topping.Value)
                        .ToList();

                    // Crusts apply to all mixing codes, so always add them
                    toppings.AddRange(crusts);

                    return CreateRecipe(
                        api.World,
                        "single-" + stack.Collectible.Code.ToShortString(),
                        crusts,
                        [stack],
                        toppings
                    );
                })
            ];

            return false;
        }

        // Modified copy of private method
        public static CookingRecipe CreateRecipe(IWorldAccessor world, string code, List<ItemStack> crusts, List<ItemStack> fillings, List<ItemStack> toppings, bool mixedRecipe = false)
        {
            static CookingRecipeStack getCookingStack(ItemStack stack)
            {
                ExpandedInPieProperties pieProps = ExpandedInPieProperties.ReadFrom(stack)!;

                return new()
                {
                    Code = stack.Collectible.Code,
                    Type = stack.Collectible.ItemClass,
                    StackSize = pieProps.ItemsPerPortion(),
                    ResolvedItemStack = stack.Clone()
                };
            }


            return new()
            {
                Code = code,
                Ingredients =
                [
                    new ()
                    {
                        Code = "dough",
                        TypeName = "bottomcrust",
                        MinQuantity = 1,
                        MaxQuantity = 1,
                        PortionSizeLitres = 0.01f,
                        ValidStacks = [.. crusts.Select(getCookingStack)]
                    },
                    new ()
                    {
                        Code = "filling",
                        TypeName = "piefilling",
                        MinQuantity = 4,
                        MaxQuantity = 4,
                        PortionSizeLitres = 0.01f,
                        ValidStacks = [.. fillings.Select(getCookingStack)]
                    },
                    new ()
                    {
                        Code = "crust",
                        TypeName = "topcrust",
                        MinQuantity = 0,
                        MaxQuantity = 1,
                        PortionSizeLitres = 0.01f,
                        ValidStacks = [.. toppings.Select(getCookingStack)]
                    }
                ],
                PerishableProps = new()
            };
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(BlockPie.GenerateRandomPie))]
        public static bool GenerateRandomPie(ref ItemStack?[] __result, ICoreAPI api, ref Dictionary<CookingRecipeIngredient, HashSet<ItemStack?>>? cachedValidStacksByIngredient, CookingRecipe recipe, ItemStack? ingredientStack = null)
        {
            if (recipe.Ingredients == null)
            {
                __result = new ItemStack?[6];
                return false;
            }

            Dictionary<CookingRecipeIngredient, HashSet<ItemStack?>>? validStacksByIngredient = cachedValidStacksByIngredient;

            if (validStacksByIngredient == null)
            {
                validStacksByIngredient = [];

                foreach (CookingRecipeIngredient? ingredient in recipe.Ingredients)
                {
                    HashSet<ItemStack?> ingredientStacks = [];

                    CookingRecipeIngredientPatcher.Resolve(ingredient, api.World, "handbook meal recipes");
                    foreach (ItemStack? stack in ingredient.ValidStacks.Select(stack => stack.ResolvedItemstack))
                    {
                        if (ingredient.GetMatchingStack(stack)?.Clone() is not CookingRecipeStack recipeStack) continue;

                        if (stack != null && BlockLiquidContainerBase.GetContainableProps(stack) is WaterTightContainableProps props)
                        {
                            stack.StackSize = recipeStack.StackSize * (int)(props.ItemsPerLitre * ingredient.PortionSizeLitres);
                            ingredientStacks.Add(stack);
                        }
                        else
                        {
                            ingredientStacks.Add(null);
                        }
                    }

                    if (ingredient.MinQuantity <= 0) ingredientStacks.Add(null);

                    validStacksByIngredient.Add(ingredient.Clone(), ingredientStacks);
                }

                cachedValidStacksByIngredient = validStacksByIngredient;
            }


            void addIngredient(ref List<ItemStack?> pie, string code, ref Dictionary<CookingRecipeIngredient, List<ItemStack?>> valIngStacks, ref CookingRecipeIngredient? requestedIngredient)
            {
                (CookingRecipeIngredient ingredient, List<ItemStack?> validStacks) = valIngStacks.FirstOrDefault(entry => entry.Key.Code == code);

                // Try to fulfill the ingredient request
                if (ingredient.Code == requestedIngredient?.Code)
                {
                    if (validStacks.First(stack => stack?.Collectible.Code == ingredientStack?.Collectible.Code) is ItemStack stack)
                    {
                        pie.Add(stack.Clone());

                        ingredient.MinQuantity--;
                        ingredient.MaxQuantity--;
                    }

                    requestedIngredient = null;

                    // Without this, we could add a requested dough/topping and
                    // then another, which breaks the pie.
                    if (ingredient.MaxQuantity <= 0) return;
                }

                // Only fillings need the code below here for filtering, so we skip the
                // list copying for crusts and toppings.
                if (code != "filling")
                {
                    pie.Add(validStacks.ElementAtOrDefault(api.World.Rand.Next(validStacks.Count))?.Clone());
                    return;
                }

                List<ItemStack?> filteredValidStacks = validStacks;
                string recipeCode = recipe.Code?.Split("-").ElementAtOrDefault(1) ?? "";
                while (ingredient.MinQuantity > 0)
                {
                    if (filteredValidStacks.Count > 0)
                    {
                        ItemStack? stack = filteredValidStacks[api.World.Rand.Next(filteredValidStacks.Count)]?.Clone();
                        // Get the list of codes for this ingredient that can be filtered out
                        string[] ingredientCodes = ExpandedInPieProperties.ReadFrom(stack)!.MixingCodes.Where(code => code != recipeCode)?.ToArray() ?? [];
                        // Remove all the other ingredients that share any codes
                        filteredValidStacks = filteredValidStacks.Where(stack => ExpandedInPieProperties.ReadFrom(stack)!.MixingCodes.Intersect(ingredientCodes).Count() == 0).ToList();
                        pie.Add(stack);
                    }
                    else
                    {
                        pie.Add(validStacks[api.World.Rand.Next(validStacks.Count)]?.Clone());
                    }

                    ingredient.MinQuantity--;
                    ingredient.MaxQuantity--;
                }
            }

            Dictionary<CookingRecipeIngredient, List<ItemStack?>> valIngStacks = [];
            foreach (var entry in validStacksByIngredient) valIngStacks.Add(entry.Key.Clone(), [.. entry.Value]);
            valIngStacks = valIngStacks.OrderBy(x => api.World.Rand.Next()).ToDictionary(item => item.Key, item => item.Value);
            CookingRecipeIngredient? requestedIngredient = null;
            if (ingredientStack != null)
            {
                List<CookingRecipeIngredient> validIngredients = [.. recipe.Ingredients.Where(ingredient => ingredient.Matches(ingredientStack))];
                requestedIngredient = validIngredients[api.World.Rand.Next(validIngredients.Count)].Clone();
            }

            List<ItemStack?> randomPie = [];
            addIngredient(ref randomPie, "dough", ref valIngStacks, ref requestedIngredient);
            addIngredient(ref randomPie, "filling", ref valIngStacks, ref requestedIngredient);
            addIngredient(ref randomPie, "crust", ref valIngStacks, ref requestedIngredient);

            if (randomPie.Count != 6)
            {
                api.Logger.Error($"Random pie [ {string.Join(", ", randomPie)} ] has a length that is not 6. Making it completely empty to prevent worse issues. This is a coding error, so please report it.");
                __result = [null, null, null, null, null, null];
                return false;
            }

            if (!recipe.Matches([.. randomPie]))
            {
                api.Logger.Error($"Random pie [ {string.Join(", ", randomPie)} ] is invalid or does not match recipe {recipe.Code}. Making it completely empty to prevent worse issues. This is a coding error, so please report it.");
                __result = [null, null, null, null, null, null];
                return false;
            }

            __result = [.. randomPie];
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(BlockPie.HandbookPageCodeForStack))]
        public static bool HandbookPageCodeForStackPrefix(ref BlockPie __instance, ref string __result, ref ICoreAPI ___api, IWorldAccessor world, ItemStack stack)
        {
            ItemStack[] cStacks = __instance.GetContents(world, stack);
            if (cStacks.Length <= 1 || cStacks[1] == null)
            {
                __result = "craftinginfo-pie";
                return false;
            }

            // Use null-forgiving in this method in case the pie is malformed,
            // e.g. an ingredient lost its pie props. Report it because it's
            // a content error.
            for (int i = 0; i < 6; i++)
            {
                if (cStacks[i] != null && ExpandedInPieProperties.ReadFrom(cStacks[i]) == null)
                {
                    ExpandedPieUtil.ReportMissingPieProps(___api.Logger, cStacks[i].Collectible.Code);
                }
            }

            bool singleIngredient = true;
            IEnumerable<string> mixCodes = ExpandedInPieProperties.ReadFrom(cStacks[1])?.MixingCodes ?? [];
            for (int i = 2; i < cStacks.Length - 1; i++)
            {
                if (cStacks[i] == null) continue;

                singleIngredient &= cStacks[i].Equals(world, cStacks[1], GlobalConstants.IgnoredStackAttributes);
                mixCodes = ExpandedInPieProperties.ReadFrom(cStacks[i])?.MixingCodes.Intersect(mixCodes) ?? [];

                if (!singleIngredient && !mixCodes.Any()) break;
            }

            string pieType = singleIngredient
                ? "single-" + cStacks[1].Collectible.Code.ToShortString()
                : "mixed-" + mixCodes.FirstOrDefault("unknown").ToLowerInvariant();

            __result = $"handbook-mealrecipe-{pieType}-pie";
            return false;
        }
    }

    [HarmonyPatch(typeof(BlockEntityPie))]
    public static class BlockEntityPiePatches
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(BlockEntityPie.OnPlaced))]
        public static bool OnPlacedPrefix(ref BlockEntityPie __instance, ref InventoryGeneric ___inv, ref MealMeshCache ___ms, ref MeshData ___mesh, IPlayer? byPlayer)
        {
            if (byPlayer?.InventoryManager.ActiveHotbarSlot.Itemstack?.Clone() is not ItemStack doughStack
                || ExpandedInPieProperties.ReadFrom(doughStack) is not ExpandedInPieProperties pieProps
                || pieProps.PartType != EnumPiePartType.Crust)
            {
                return false;
            }

            doughStack.StackSize = pieProps.PortionSize;

            if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
            {
                byPlayer.InventoryManager.ActiveHotbarSlot.TakeOut(pieProps.PortionSize);
            }

            ItemStack pie = new(__instance.Block);
            (pie.Block as BlockPie)?.SetContents(pie, [doughStack, null, null, null, null, null]);

            // Copy over the transition states so that we don't make a completely fresh pie from spoiling dough
            if (doughStack.Collectible.UpdateAndGetTransitionStates(byPlayer?.Entity.World, new DummySlot(doughStack)) is TransitionState[] doughStates
                && pie.Collectible.UpdateAndGetTransitionStates(byPlayer?.Entity.World, new DummySlot(pie)) is TransitionState[] pieStates)
            {

                for (int i = 0; i < doughStates.Length; i++)
                {
                    float scaledHours;
                    if (doughStates[i].TransitionLevel > 0)
                    {
                        scaledHours = pieStates[i].FreshHours + pieStates[i].TransitionHours * doughStates[i].TransitionLevel;
                        //if (__instance.Api.Side.IsServer()) __instance.Api.Logger.Debug($"Scaled spoiling dough lifetime to pie; {pieStates[i].FreshHours} + {pieStates[i].TransitionHours} * {doughStates[i].TransitionLevel} = {scaledHours}");
                    }
                    else
                    {
                        scaledHours = doughStates[i].TransitionedHours / (pieStates[i].TransitionHours / doughStates[i].TransitionHours);
                        //if (__instance.Api.Side.IsServer()) __instance.Api.Logger.Debug($"Scaled fresh dough lifetime to pie; {doughStates[i].TransitionedHours} / ({pieStates[i].TransitionHours} / {doughStates[i].TransitionHours}) = {scaledHours}");
                    }

                    pie.Collectible.SetTransitionState(pie, doughStates[i].Props.Type, scaledHours);
                }
            }

            pie.Attributes.SetInt("pieSize", 4);
            pie.Attributes.SetBool("bakeable", false);
            if (__instance.State != "raw" && !pie.Attributes.HasAttribute("quantityServings"))
            {
                pie.Attributes.SetFloat("quantityServings", pie.Attributes.GetAsInt("pieSize") * 0.25f);
            }
            ___inv[0].Itemstack = pie;

            if (__instance.Api == null || __instance.Api.Side == EnumAppSide.Server || ___inv[0].Empty) return false;
            ___mesh = ___ms.GetPieMesh(___inv[0].Itemstack)!;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(BlockEntityPie.OnInteract))]
        public static bool OnInteractPrefix(ref BlockEntityPie __instance, ref bool __result, ref InventoryGeneric ___inv, ref MealMeshCache ___ms, ref MeshData ___mesh, IPlayer byPlayer)
        {
            if (___inv[0].Itemstack?.Block is not BlockPie pieBlock) return false;

            ItemSlot? hotbarSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
            EnumTool? tool = byPlayer.InventoryManager.ActiveTool;

            if (tool is EnumTool.Knife || tool is EnumTool.Sword)
            {
                if (pieBlock.State != "raw")
                {
                    if (__instance.Api.Side == EnumAppSide.Server && __instance.TakeSlice() is ItemStack slicestack)
                    {
                        hotbarSlot.Itemstack?.Collectible.DamageItem(byPlayer.Entity.World, byPlayer.Entity, hotbarSlot);
                        if (!byPlayer.InventoryManager.TryGiveItemstack(slicestack))
                        {
                            __instance.Api.World.SpawnItemEntity(slicestack, __instance.Pos);
                        }
                        __instance.Api.World.Logger.Audit("{0} Took 1x{1} slice from Pie at {2}.",
                            byPlayer.PlayerName,
                            slicestack.Collectible.Code,
                            __instance.Pos
                        );
                    }

                    __instance.MarkDirty(true);
                }
                else if (ExpandedPieUtil.BEPieGetToppingType(__instance) == EnumPiePartType.Crust)
                {
                    // Cycle top crust type
                    ItemStack?[] cStacks = pieBlock.GetContents(__instance.Api.World, ___inv[0].Itemstack);
                    if (!__instance.HasAnyFilling || cStacks[5] == null) return true;

                    ___inv[0].Itemstack = BlockPie.CycleTopCrustType(___inv[0].Itemstack);
                    __instance.MarkDirty(true);
                }

                __result = true;
                return false;
            }

            // Filling rules:
            // 1. Get inPieProperties
            // 2. If pie is empty, add it.
            // 3. If pie is full, stop.
            // 3. If partially full, must
            //    a.) Have props.AllowMixing set to true
            //    b.) Have at least one matching mixing code

            // If the pie can be picked up into the current hotbar slot,
            // skip trying to add the held stack as filling. Prevents
            // the "cannot be added to pies" error message.
            bool canPickUpIntoHand = hotbarSlot?.Empty == false && ___inv[0].Itemstack?.Collectible.GetMergableQuantity(hotbarSlot.Itemstack, ___inv[0].Itemstack, EnumMergePriority.DirectMerge) > 0;

            if (hotbarSlot?.Empty == false && !canPickUpIntoHand && pieBlock.State == "raw")
            {
                bool added = ExpandedPieUtil.BEPieTryAddIngredientFrom(ref __instance, ref ___inv, hotbarSlot, byPlayer);
                if (added)
                {
                    if (__instance.Api == null || __instance.Api.Side == EnumAppSide.Server || ___inv[0].Empty) return false;
                    ___mesh = ___ms.GetPieMesh(___inv[0].Itemstack)!;
                    __instance.MarkDirty(true);
                }

                ___inv[0].Itemstack?.Attributes.SetBool("bakeable", __instance.HasAllFilling);

                __result = added;
                return false;
            }

            if (__instance.SlicesLeft == 1 && ___inv[0].Itemstack?.Attributes.HasAttribute("quantityServings") != true)
            {
                ___inv[0].Itemstack?.Attributes.SetBool("bakeable", false);
                ___inv[0].Itemstack?.Attributes.SetFloat("quantityServings", 0.25f);
            }

            if (byPlayer.Entity.Controls.ShiftKey)
            {
                __result = false;
                return false;
            }

            if (__instance.Api.Side == EnumAppSide.Server)
            {
                ___inv[0].Itemstack?.Attributes.SetBool("bakeable", __instance.HasAllFilling);
                if (!byPlayer.InventoryManager.TryGiveItemstack(___inv[0].Itemstack))
                {
                    __instance.Api.World.SpawnItemEntity(___inv[0].Itemstack, __instance.Pos.ToVec3d().Add(0.5, 0.25, 0.5));
                }
                __instance.Api.World.Logger.Audit("{0} Took 1x{1} at {2}.",
                    byPlayer.PlayerName,
                    ___inv[0].Itemstack?.Collectible.Code,
                    __instance.Pos
                );
                ___inv[0].Itemstack = null;
            }

            __instance.Api.World.BlockAccessor.SetBlock(0, __instance.Pos);

            __result = true;
            return false;
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

            // Toppings need to have the default crust type because they have only one shape.
            foreach (MealstackTextComponent? comp in components.OfType<MealstackTextComponent>())
            {
                if (comp == null) continue;

                ItemStack? mealBlock = Traverse.Create(comp).Field("dummySlot").GetValue<DummySlot>()?.Itemstack;
                if (mealBlock?.Block is not BlockPie pieBlock) continue;

                ItemStack? topCrustStack = pieBlock.GetContents(capi.World, mealBlock).ElementAtOrDefault(5);

                if (ExpandedInPieProperties.ReadFrom(topCrustStack)?.PartType == EnumPiePartType.Topping)
                {
                    mealBlock.Attributes.SetString("topCrustType", "full");
                }
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
            foreach (MealstackTextComponent? comp in components.OfType<MealstackTextComponent>())
            {
                ItemStack? mealBlock = Traverse.Create(comp).Field("dummySlot").GetValue<DummySlot>()?.Itemstack;
                if (mealBlock?.Block is not BlockPie pieBlock) continue;

                ItemStack? topCrustStack = pieBlock.GetContents(capi.World, mealBlock).ElementAtOrDefault(5);

                if (ExpandedInPieProperties.ReadFrom(topCrustStack)?.PartType == EnumPiePartType.Topping)
                {
                    mealBlock.Attributes.SetString("topCrustType", "full");
                }
            }
        }
    }

    [HarmonyPatch(typeof(ModSystemSurvivalHandbook), "onCreatePagesAsync")]
    public static class HandbookCreatePagesPatch
    {
        public static void Postfix(ref List<GuiHandbookPage> __result, ModSystemSurvivalHandbook __instance, ref ICoreClientAPI ___capi)
        {
            var firstPie = __result.FirstOrDefault(comp => comp.PageCode.Contains("handbook-mealrecipe-") && comp.PageCode.Contains("-pie"));
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

    [HarmonyPatch(typeof(GuiHandbookMealRecipePage), MethodType.Constructor)]
    [HarmonyPatch([typeof(ICoreClientAPI), typeof(CookingRecipe), typeof(int), typeof(bool)])]
    public static class GuiHandbookMealRecipePagePatch
    {
        public static void Postfix(ref GuiHandbookMealRecipePage __instance, ICoreClientAPI capi, CookingRecipe recipe, int slots = 4, bool isPie = false)
        {
            if (!isPie) return;

            ItemStack? mealBlock = __instance.dummySlot.Itemstack;
            if (mealBlock?.Block is not BlockPie pieBlock) return;

            ItemStack? topCrustStack = pieBlock.GetContents(capi.World, mealBlock).ElementAtOrDefault(5);

            if (ExpandedInPieProperties.ReadFrom(topCrustStack)?.PartType == EnumPiePartType.Topping)
            {
                mealBlock.Attributes.SetString("topCrustType", "full");
            }
        }
    }


    [HarmonyPatch(typeof(CookingRecipe), "GenerateRandomMeal")]
    public static class CookingRecipeRandomPatch
    {
        public static bool Prefix(ref CookingRecipe __instance, ref ICoreClientAPI api, ref Dictionary<CookingRecipeIngredient, HashSet<ItemStack?>>? cachedValidStacksByIngredient)
        {
            if (__instance.Ingredients == null) return false;
            cachedValidStacksByIngredient ??= HandbookInfoExtensions.CreateCachedMealRecipeStacks(api, __instance);
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
    }

    [HarmonyPatch(typeof(BlockMeal))]
    public class BlockMealBowlBasePatches
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


    [HarmonyPatch(typeof(MealMeshCache))]
    public class MealMeshCachePatch
    {
        // Patch adds handling for toppings
        [HarmonyPrefix]
        [HarmonyPatch("GetPieMesh")]
        public static bool GetPieMeshPrefix(
            ref MealMeshCache __instance,
            ref MeshData? __result,
            ref ICoreClientAPI ___capi,
            ref BlockPie? ___nowTesselatingBlock,
            ref ItemStack?[] ___contentStacks,
            ref AssetLocation? ___crustTextureLoc,
            ref AssetLocation? ___fillingTextureLoc,
            ref AssetLocation? ___topCrustTextureLoc,
            ref AssetLocation[] ___pieShapeBySize,
            ref AssetLocation[] ___pieShapeLocByFillLevel,
            ItemStack? pieStack,
            ModelTransform? transform = null
        )
        {
            Dictionary<int, AssetLocation> pieMixingCodeFillingTextures = [];
            AssetLocation? getPieFillingTexture(ICoreClientAPI capi, ExpandedInPieProperties?[] pieProps, string? mixingCode, bool singleIngredient)
            {
                // Correct for the actual file name
                if (mixingCode == "protein") mixingCode = "meat";
                if (mixingCode == "dairy") mixingCode = "cheese";

                if (singleIngredient) return pieProps[1]?.Texture;
                if (mixingCode == null)
                {
                    capi!.Logger.Error("Pie does not have any mixing codes. Using default unknown texture.");
                    return new("block/food/pie/fill-unknown");
                }

                if (!pieMixingCodeFillingTextures.TryGetValue(mixingCode.GetHashCode(), out AssetLocation? loc))
                {
                    loc = new("block/food/pie/fill-mixed" + mixingCode);
                    pieMixingCodeFillingTextures.Add(mixingCode.GetHashCode(), loc);
                }

                if (loc == null)
                {
                    capi!.Logger.Error($"No pie texture found for mixing code {mixingCode}.");
                }

                return loc;
            }

            ___nowTesselatingBlock = pieStack?.Block as BlockPie;

            if (___nowTesselatingBlock == null)
            {
                // This will occur if the pieStack changed to rot.
                __result = null;
                return false;
            }


            ___contentStacks = ___nowTesselatingBlock!.GetContents(___capi!.World, pieStack);
            if (___contentStacks.Length < 6)
            {
                // Pies are supposed to always have 6 stacks.
                __result = null;
                return false;
            }

            int pieSize = pieStack?.Attributes.GetAsInt("pieSize") ?? 0;
            if (pieSize <= 0 || pieSize > 4)
            {
                // Prevent bad array access crash for pies with invalid sizes.
                __result = null;
                return false;
            }

            // This is where we find the dough and filling textures.
            //
            // For dough, the texture is taken from the attributes. Vanilla doughs
            // use the path "block/food/pie/{type}{bakeLevel}"
            //
            // For filling, we need to check whether the pie is single-ingredient. If not,
            // we look for the texture named after the first mixing code.
            //
            // Single-ingredient: "block/food/pie/fill-<name>.png"
            // By mixing code: "block/food/pie/fill-mixed<tag>.png"

            int bakeLevel = pieStack?.Attributes.GetAsInt("bakeLevel", 0) ?? 0;
            ExpandedInPieProperties?[] stackPieProps = ___contentStacks.Select(ExpandedInPieProperties.ReadFrom).ToArray();

            bool singleIngredient = true;
            IEnumerable<string> mixCodes = stackPieProps[1]?.MixingCodes ?? [];
            for (int i = 2; i < ___contentStacks.Length - 1; i++)
            {
                if (___contentStacks[i] == null) continue;

                singleIngredient &= ___contentStacks[1]!.Equals(___capi.World, ___contentStacks[i], GlobalConstants.IgnoredStackAttributes);
                mixCodes = stackPieProps[i]?.MixingCodes.Intersect(mixCodes) ?? [];

                if (!singleIngredient && !mixCodes.Any()) break;
            }

            if (stackPieProps[0]?.Texture is AssetLocation crustTexture)
            {
                ___crustTextureLoc = crustTexture.Clone();
                ___crustTextureLoc.Path = ___crustTextureLoc.Path.Replace("{bakelevel}", "" + (bakeLevel + 1));
                ___fillingTextureLoc = new AssetLocation("block/transparent");
            }
            else if (stackPieProps[0] != null)
            {
                ___capi!.Logger.Error($"Bottom crust {___contentStacks[0]!.Collectible.Code} does not have a texture!");
            }

            ___topCrustTextureLoc = new AssetLocation("block/transparent");
            if (stackPieProps[5]?.Texture is AssetLocation topCrustTexture)
            {
                ___topCrustTextureLoc = topCrustTexture.Clone();
                ___topCrustTextureLoc.Path = ___topCrustTextureLoc.Path.Replace("{bakelevel}", "" + (bakeLevel + 1));
            }
            else if (stackPieProps[5] != null)
            {
                ___capi!.Logger.Error($"Topping {___contentStacks[5]!.Collectible.Code} does not have a texture!");
            }

            if (___contentStacks[1] != null)
            {
                ___fillingTextureLoc = getPieFillingTexture(___capi, stackPieProps, mixCodes.FirstOrDefault(), singleIngredient);
            }


            int fillLevel = (___contentStacks[1] != null ? 1 : 0) + (___contentStacks[2] != null ? 1 : 0) + (___contentStacks[3] != null ? 1 : 0) + (___contentStacks[4] != null ? 1 : 0);
            bool isComplete = fillLevel == 4;

            AssetLocation shapeloc = isComplete ? ___pieShapeBySize[pieSize - 1] : ___pieShapeLocByFillLevel[fillLevel];

            shapeloc.WithPathAppendixOnce(".json").WithPathPrefixOnce("shapes/");
            Shape shape = Shape.TryGet(___capi, shapeloc);

            string topCrustShapeElement;
            if (stackPieProps[5]?.PartType == EnumPiePartType.Topping)
            {
                topCrustShapeElement = stackPieProps[5]!.ToppingShapeElement;
            }
            else
            {
                topCrustShapeElement = BlockPie.TopCrustTypes.First(type => type.Code.EqualsFast(BlockPie.GetTopCrustType(pieStack) ?? "full")).ShapeElement;
            }
            string[] selectiveElements = ["origin/base/crust regular/*", "origin/base/filling/*", "origin/base/base-quarter/*", "origin/base/fillingquarter/*", topCrustShapeElement];

            ___capi.Tesselator.TesselateShape("pie", shape, out MeshData mesh, __instance, null, 0, 0, 0, null, selectiveElements);
            if (transform != null) mesh.ModelTransform(transform);

            __result = mesh;
            return false;
        }
    }
}
