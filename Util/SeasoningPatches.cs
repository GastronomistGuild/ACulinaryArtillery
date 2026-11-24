using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace ACulinaryArtillery;

public class SeasoningProperties
{
    public string Sound = "sounds/block/plant";

    public bool IsLiquid = false;
    public int PortionSize = 1;
    public float PortionSizeLitres = 0.1f;

    /// <summary>
    /// If present, only accept this seasoning on these recipe codes.
    /// </summary>
    public string[]? RecipeCodes;

    /// <summary>
    /// Whether or not RecipeCodes is a blacklist. Whitelist by default.
    ///
    /// Does nothing if RecipeCodes does not exist.
    /// </summary>
    public bool RecipeCodeBlacklist = false;

    /// <summary>
    /// Nutrition properties that override nutritionPropertiesWhenInMeal.
    /// </summary>
    public FoodNutritionProperties? nutritionProperties;

    public bool CanUseOnRecipe(string? code)
    {
        if (code == null) return false;
        if (RecipeCodes == null) return true;

        return RecipeCodeBlacklist ? !RecipeCodes.Contains(code) : RecipeCodes.Contains(code);
    }
}

public class SeasoningUtil
{
    public static string LangStringForStack(ItemStack stack)
    {
        return Lang.Get($"{stack.Collectible.Code.Domain}:{(stack.Class == EnumItemClass.Block ? "block" : "item")}-{stack.Collectible.Code.Path}");
    }

    public static ItemStack? ContentStackForSeasoning(ItemStack stack)
    {
        if (stack.Collectible.GetCollectibleInterface<ILiquidSource>() is ILiquidSource source)
        {
            return source.GetContent(stack);
        }

        return stack;
    }
}

// BlockMeal doesn't have its own overrides for these, so
// we just match on the class instead.
[HarmonyPatchCategory("seasoning")]
[HarmonyPatch(typeof(CollectibleObject))]
public static class CollectibleObjectPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(CollectibleObject.GetMergableQuantity))]
    public static bool AddMealSeasoningInventoryQuantity(CollectibleObject __instance, ref int __result, ItemStack sinkStack, ItemStack sourceStack, EnumMergePriority priority)
    {
        if (__instance is BlockMeal
            && priority != EnumMergePriority.AutoMerge
            && SeasoningUtil.ContentStackForSeasoning(sourceStack) is ItemStack seasoningStack
            && seasoningStack.ItemAttributes["seasoningProps"]?.AsObject<SeasoningProperties>() != null
            && !sinkStack.Attributes.HasAttribute("seasoning")
            && sinkStack.Attributes.GetDecimal("quantityServings") > 0
            && sinkStack.Attributes.HasAttribute("recipeCode"))
        {
            // Force merging here so we can avoid swapping the stacks in TryMergeStacks
            // by setting op.MovedQuantity to 0.
            __result = 1;
            return false;
        }

        return true;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CollectibleObject.TryMergeStacks))]
    public static bool AddMealSeasoningInventory(CollectibleObject __instance, ItemStackMergeOperation op)
    {
        ItemStack sourceStack = op.SourceSlot.Itemstack!;
        ItemStack sinkStack = op.SinkSlot.Itemstack!;

        if (__instance is BlockMeal
            && op.CurrentPriority == EnumMergePriority.DirectMerge
            && SeasoningUtil.ContentStackForSeasoning(sourceStack) is ItemStack seasoningStack
            && seasoningStack.ItemAttributes["seasoningProps"]?.AsObject<SeasoningProperties>() is SeasoningProperties seasoningProps
            && !sinkStack.Attributes.HasAttribute("seasoning")
            && sinkStack.Attributes.GetDecimal("quantityServings") > 0
            && sinkStack.Attributes.HasAttribute("recipeCode"))
        {
            // Don't merge anything until we know we've successfully added the seasoning
            op.MovedQuantity = 0;

            // Don't season sealed crocks
            if (sinkStack.Collectible is BlockCrock && sinkStack.Attributes.GetBool("sealed"))
            {
                (op.ActingPlayer.Entity.Api as ICoreClientAPI)?.TriggerIngameError(__instance, "cantseasonsealedcrock", Lang.Get("aculinaryartillery:cant-season-sealed-crock"));
                return false;
            }

            if (!seasoningProps.CanUseOnRecipe(sinkStack.Attributes.GetString("recipeCode")))
            {
                (op.ActingPlayer.Entity.Api as ICoreClientAPI)?.TriggerIngameError(
                    __instance,
                    "seasoningwrongrecipe",
                    Lang.Get("aculinaryartillery:seasoning-wrong-recipe",
                        Lang.Get($"mealrecipe-name-{sinkStack.Attributes.GetString("recipeCode")}").ToLowerInvariant(),
                        SeasoningUtil.LangStringForStack(seasoningStack).ToLowerInvariant()
                    )
                );
                return false;
            }

            int servings = (int)Math.Ceiling(sinkStack.Attributes.GetDecimal("quantityServings") * sinkStack.StackSize);

            int totalSeasoning = servings * seasoningProps.PortionSize;
            float totalSeasoningLitres = servings * seasoningProps.PortionSizeLitres;
            if (seasoningProps.IsLiquid)
            {
                ILiquidSource container = sourceStack.Collectible.GetCollectibleInterface<ILiquidSource>()!;
                if (!container.AllowHeldLiquidTransfer)
                {
                    (op.ActingPlayer.Entity.Api as ICoreClientAPI)?.TriggerIngameError(__instance, "seasoningcanttransferliquid", Lang.Get("aculinaryartillery:seasoning-cant-transfer-liquid"));
                    return false;
                }

                if (container.GetCurrentLitres(sourceStack) < totalSeasoningLitres)
                {
                    (op.ActingPlayer.Entity.Api as ICoreClientAPI)?.TriggerIngameError(__instance, "notenoughseasoning", Lang.Get(servings == 1 ? "aculinaryartillery:not-enough-seasoning-liquid-singular" : "deartibusrusticis-notenough-seasoning-liquid-plural", totalSeasoningLitres, servings));
                    return false;
                }

                int quantity = (int)(totalSeasoningLitres * container.GetContentProps(sourceStack)!.ItemsPerLitre);
                ItemStack liquidStack = container.TryTakeContent(op.SourceSlot.Itemstack!, quantity / op.SourceSlot.StackSize);
                liquidStack.StackSize = quantity;

                // Preserve the perish timer
                op.SinkSlot.Itemstack?.Attributes.SetItemstack("seasoning", liquidStack);
            }
            else
            {
                if (seasoningStack.StackSize < servings)
                {
                    (op.ActingPlayer.Entity.Api as ICoreClientAPI)?.TriggerIngameError(__instance, "notenoughseasoning", Lang.Get(servings == 1 ? "aculinaryartillery:not-enough-seasoning-singular" : "deartibusrusticis-notenough-seasoning-plural", totalSeasoning, servings));
                    return false;
                }

                op.SourceSlot.TakeOut(totalSeasoning);
                op.MovedQuantity = totalSeasoning;

                // Preserve the perish timer
                ItemStack stackForAttr = sourceStack.Clone();
                stackForAttr.StackSize = seasoningProps.PortionSize;
                op.SinkSlot.Itemstack?.Attributes.SetItemstack("seasoning", stackForAttr);
            }

            op.World.PlaySoundFor(seasoningProps.Sound, op.ActingPlayer);

            ItemStack seasoningStackForAudit = sinkStack.Attributes.GetItemstack("seasoning");
            seasoningStackForAudit.ResolveBlockOrItem(op.World);

            op.World.Logger.Audit(
                "{0} added {1}x{2} seasoning to {3} meals with recipe {4} in inventory",
                op.ActingPlayer.PlayerName,
                seasoningProps.IsLiquid ? $"{totalSeasoningLitres}L" : totalSeasoning,
                seasoningStackForAudit.Collectible.Code,
                servings,
                sinkStack.Attributes["recipeCode"]
            );

            return false;
        }

        return true;
    }
}

[HarmonyPatchCategory("seasoning")]
[HarmonyPatch(typeof(BlockMeal))]
public static class BlockMealPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(BlockCookedContainerBase.OnContainedInteractStart))]
    public static bool AddSeasoningInWorld(BlockCookedContainerBase __instance, ref bool __result, BlockEntityContainer be, ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel)
    {
        ItemSlot? heldSlot = byPlayer.InventoryManager.ActiveHotbarSlot;

        if (heldSlot == null) return true;

        ItemStack? heldStack = heldSlot.Itemstack;
        ItemStack? mealStack = slot.Itemstack;

        if (heldStack == null || mealStack == null) return true;

        if (SeasoningUtil.ContentStackForSeasoning(heldStack) is ItemStack seasoningStack
            && seasoningStack.ItemAttributes["seasoningProps"]?.AsObject<SeasoningProperties>() is SeasoningProperties seasoningProps
            && !mealStack.Attributes.HasAttribute("seasoning")
            && mealStack.Attributes.GetDecimal("quantityServings") > 0
            && mealStack.Attributes.HasAttribute("recipeCode"))
        {
            if (!seasoningProps.CanUseOnRecipe(mealStack.Attributes.GetString("recipeCode")))
            {
                (byPlayer.Entity.Api as ICoreClientAPI)?.TriggerIngameError(
                    __instance,
                    "seasoningwrongrecipe",
                    Lang.Get("aculinaryartillery:seasoning-wrong-recipe",
                        Lang.Get($"mealrecipe-name-{mealStack.Attributes.GetString("recipeCode")}").ToLowerInvariant(),
                        SeasoningUtil.LangStringForStack(heldStack).ToLowerInvariant()
                    )
                );
                __result = true;
                return false;
            }

            int servings = (int)Math.Ceiling(mealStack.Attributes.GetDecimal("quantityServings") * mealStack.StackSize);

            int totalSeasoning = servings * seasoningProps.PortionSize;
            float totalSeasoningLitres = servings * seasoningProps.PortionSizeLitres;
            if (seasoningProps.IsLiquid)
            {
                ILiquidSource container = heldStack.Collectible.GetCollectibleInterface<ILiquidSource>()!;
                if (!container.AllowHeldLiquidTransfer)
                {
                    (byPlayer.Entity.Api as ICoreClientAPI)?.TriggerIngameError(__instance, "seasoningcanttransferliquid", Lang.Get("aculinaryartillery:seasoning-cant-transfer-liquid"));
                    return false;
                }

                if (container.GetCurrentLitres(heldStack) < totalSeasoningLitres)
                {
                    (byPlayer.Entity.Api as ICoreClientAPI)?.TriggerIngameError(__instance, "notenoughseasoning", Lang.Get(servings == 1 ? "aculinaryartillery:not-enough-seasoning-liquid-singular" : "deartibusrusticis-notenough-seasoning-liquid-plural", totalSeasoningLitres, servings));
                    return false;
                }

                int quantity = (int)(totalSeasoningLitres * container.GetContentProps(heldStack)!.ItemsPerLitre);
                ItemStack liquidStack = container.TryTakeContent(heldStack, quantity / heldStack.StackSize);
                liquidStack.StackSize = quantity;

                // Preserve the perish timer
                mealStack.Attributes.SetItemstack("seasoning", liquidStack);
            }
            else
            {
                if (seasoningStack.StackSize < servings)
                {
                    (byPlayer.Entity.Api as ICoreClientAPI)?.TriggerIngameError(__instance, "notenoughseasoning", Lang.Get(servings == 1 ? "aculinaryartillery:not-enough-seasoning-singular" : "deartibusrusticis-notenough-seasoning-plural", totalSeasoning, servings));
                    __result = true;
                    return false;
                }

                heldSlot.TakeOut(totalSeasoning);

                // Preserve the perish timer
                ItemStack stackForAttr = heldStack.Clone();
                stackForAttr.StackSize = seasoningProps.PortionSize;
                mealStack.Attributes.SetItemstack("seasoning", stackForAttr);
            }

            be.Api.World.PlaySoundFor(seasoningProps.Sound, byPlayer);

            ItemStack seasoningStackForAudit = mealStack.Attributes.GetItemstack("seasoning");
            seasoningStackForAudit.ResolveBlockOrItem(be.Api.World);

            byPlayer.Entity.World.Logger.Audit(
                "{0} added {1}x{2} seasoning to {3} meals with recipe {4} at {5}",
                byPlayer.PlayerName,
                seasoningProps.IsLiquid ? $"{totalSeasoningLitres}L" : totalSeasoning,
                seasoningStackForAudit.Collectible.Code,
                servings,
                mealStack.Attributes["recipeCode"],
                blockSel.Position
            );

            slot?.MarkDirty();
            be.MarkDirty(true);
            __result = true;
            return false;
        }

        return true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(BlockMeal.GetContentNutritionProperties), [typeof(IWorldAccessor), typeof(ItemSlot), typeof(ItemStack?[]), typeof(EntityAgent), typeof(bool), typeof(float), typeof(float)])]
    public static void AddSeasoningNutrition(ref FoodNutritionProperties[] __result, IWorldAccessor world, ItemSlot inSlot, ItemStack?[]? contentStacks, EntityAgent? forEntity, bool mulWithStacksize = false, float nutritionMul = 1, float healthMul = 1)
    {
        if (contentStacks == null || inSlot.Itemstack is not ItemStack mealStack) return;
        if (mealStack.Attributes.GetItemstack("seasoning") is not ItemStack seasoningStack) return;
        seasoningStack.ResolveBlockOrItem(world);
        if (!mealStack.Attributes.HasAttribute("recipeCode")) return;
        if (seasoningStack?.ItemAttributes["seasoningProps"]?.AsObject<SeasoningProperties>() is not SeasoningProperties seasoningProps) return;
        if (!seasoningProps.CanUseOnRecipe(mealStack.Attributes.GetString("recipeCode"))) return;

        FoodNutritionProperties? seasoningNutritionProps = seasoningProps.nutritionProperties;
        seasoningNutritionProps ??= BlockMeal.GetIngredientStackNutritionProperties(world, seasoningStack, forEntity);
        if (seasoningNutritionProps == null) return;

        FoodNutritionProperties props = seasoningNutritionProps.Clone();

        float spoilState = 0;
        DummySlot slot = new DummySlot(seasoningStack, inSlot.Inventory);
        bool timeFrozen = mealStack.Attributes.GetBool("timeFrozen");
        if (!timeFrozen)
        {
            TransitionState? state = seasoningStack.Collectible.UpdateAndGetTransitionState(world, slot, EnumTransitionType.Perish);
            spoilState = state != null ? state.TransitionLevel : 0;
            mealStack.Attributes.SetItemstack("seasoning", seasoningStack);
        }

        float satLossMul = GlobalConstants.FoodSpoilageSatLossMul(spoilState, mealStack, forEntity);
        float healthLoss = GlobalConstants.FoodSpoilageHealthLossMul(spoilState, mealStack, forEntity);
        props.Satiety *= satLossMul * nutritionMul * (mulWithStacksize ? seasoningStack.StackSize : 1);
        props.Health *= healthLoss * healthMul * (mulWithStacksize ? seasoningStack.StackSize : 1);

        List<FoodNutritionProperties> propsList = [.. __result];
        propsList.Add(props);
        __result = [.. propsList];
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(BlockMeal.GetHeldItemInfo))]
    public static void AddInfoSeasonedText(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (inSlot.Itemstack?.Attributes.GetItemstack("seasoning") is ItemStack seasoning)
        {
            seasoning.ResolveBlockOrItem(world);
            dsc.AppendLine(Lang.Get("aculinaryartillery:meal-seasoned", SeasoningUtil.LangStringForStack(seasoning).ToLowerInvariant()));
        }
    }
}

[HarmonyPatchCategory("seasoning")]
[HarmonyPatch(typeof(BlockCookedContainerBase))]
public static class BlockCookedContainerBasePatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(BlockCookedContainerBase.OnContainedInteractStart))]
    public static bool AddSeasoningInWorld(BlockCookedContainerBase __instance, ref bool __result, BlockEntityContainer be, ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel)
    {
        ItemSlot? heldSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
        if (heldSlot == null) return true;

        ItemStack? heldStack = heldSlot.Itemstack;
        ItemStack? potStack = slot.Itemstack;

        if (heldStack == null || potStack == null) return true;

        if (SeasoningUtil.ContentStackForSeasoning(heldStack) is ItemStack seasoningStack
            && seasoningStack.ItemAttributes["seasoningProps"]?.AsObject<SeasoningProperties>() is SeasoningProperties seasoningProps
            && !potStack.Attributes.HasAttribute("seasoning")
            && potStack.Attributes.GetDecimal("quantityServings") > 0
            && potStack.Attributes.HasAttribute("recipeCode"))
        {
            if (!seasoningProps.CanUseOnRecipe(potStack.Attributes.GetString("recipeCode")))
            {
                (byPlayer.Entity.Api as ICoreClientAPI)?.TriggerIngameError(
                    __instance,
                    "seasoningwrongrecipe",
                    Lang.Get("aculinaryartillery:seasoning-wrong-recipe",
                        Lang.Get($"mealrecipe-name-{potStack.Attributes.GetString("recipeCode")}").ToLowerInvariant(),
                        SeasoningUtil.LangStringForStack(heldStack).ToLowerInvariant()
                    )
                );
                return false;
            }

            int servings = (int)Math.Ceiling(potStack.Attributes.GetDecimal("quantityServings") * potStack.StackSize);

            int totalSeasoning = servings * seasoningProps.PortionSize;
            float totalSeasoningLitres = servings * seasoningProps.PortionSizeLitres;
            if (seasoningProps.IsLiquid)
            {
                ILiquidSource container = heldStack.Collectible.GetCollectibleInterface<ILiquidSource>()!;
                if (!container.AllowHeldLiquidTransfer)
                {
                    (byPlayer.Entity.Api as ICoreClientAPI)?.TriggerIngameError(__instance, "seasoningcanttransferliquid", Lang.Get("aculinaryartillery:seasoning-cant-transfer-liquid"));
                    return false;
                }

                if (container.GetCurrentLitres(heldStack) < totalSeasoningLitres)
                {
                    (byPlayer.Entity.Api as ICoreClientAPI)?.TriggerIngameError(__instance, "notenoughseasoning", Lang.Get(servings == 1 ? "aculinaryartillery:not-enough-seasoning-liquid-singular" : "deartibusrusticis-notenough-seasoning-liquid-plural", totalSeasoningLitres, servings));
                    return false;
                }

                int quantity = (int)(totalSeasoningLitres * container.GetContentProps(heldStack)!.ItemsPerLitre);
                ItemStack liquidStack = container.TryTakeContent(heldStack, quantity / heldStack.StackSize);
                liquidStack.StackSize = quantity;

                // Preserve the perish timer
                potStack.Attributes.SetItemstack("seasoning", liquidStack);
            }
            else
            {
                if (seasoningStack.StackSize < servings)
                {
                    (byPlayer.Entity.Api as ICoreClientAPI)?.TriggerIngameError(__instance, "notenoughseasoning", Lang.Get(servings == 1 ? "aculinaryartillery:not-enough-seasoning-singular" : "deartibusrusticis-notenough-seasoning-plural", totalSeasoning, servings));
                    __result = true;
                    return false;
                }

                heldSlot.TakeOut(totalSeasoning);

                // Preserve the perish timer
                ItemStack stackForAttr = heldStack.Clone();
                stackForAttr.StackSize = seasoningProps.PortionSize;
                potStack.Attributes.SetItemstack("seasoning", stackForAttr);
            }


            be.Api.World.PlaySoundFor(seasoningProps.Sound, byPlayer);

            ItemStack seasoningStackForAudit = potStack.Attributes.GetItemstack("seasoning");
            seasoningStackForAudit.ResolveBlockOrItem(be.Api.World);

            byPlayer.Entity.World.Logger.Audit(
                "{0} added {1}x{2} seasoning to cooked container with {3} meals with recipe {4} at {5}",
                byPlayer.PlayerName,
                totalSeasoning,
                seasoningStackForAudit.Collectible.Code,
                servings,
                potStack.Attributes["recipeCode"],
                blockSel.Position
            );

            slot?.MarkDirty();
            be.MarkDirty(true);
            __result = true;
            return false;
        }

        return true;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(BlockCookedContainerBase.GetMergableQuantity))]
    public static bool AddSeasoningInventoryQuantity(BlockCookedContainerBase __instance, ref int __result, ItemStack sinkStack, ItemStack sourceStack, EnumMergePriority priority)
    {
        if (priority != EnumMergePriority.AutoMerge
            && sinkStack != null
            && sourceStack != null
            && SeasoningUtil.ContentStackForSeasoning(sourceStack) is ItemStack seasoningStack
            && seasoningStack.ItemAttributes["seasoningProps"]?.AsObject<SeasoningProperties>() is SeasoningProperties seasoningProps
            && sinkStack.Attributes.HasAttribute("seasoning") == false
            && sinkStack.Attributes.GetDecimal("quantityServings") > 0
            && sinkStack.Attributes.HasAttribute("recipeCode") == true)
        {
            // Don't season sealed crocks
            if (sinkStack.Collectible is BlockCrock && sinkStack.Attributes.GetBool("sealed"))
            {
                __result = 0;
                return false;
            }

            if (!seasoningProps.CanUseOnRecipe(sinkStack.Attributes.GetString("recipeCode")))
            {
                __result = 0;
                return false;
            }

            int servings = (int)Math.Ceiling(sinkStack!.Attributes.GetDecimal("quantityServings"));
            int totalSeasoning = servings * seasoningProps.PortionSize;
            __result = sourceStack.StackSize >= totalSeasoning ? totalSeasoning : 0;
            return false;
        }

        return true;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(BlockCookedContainerBase.TryMergeStacks))]
    public static bool AddSeasoningInventory(BlockCookedContainerBase __instance, ItemStackMergeOperation op)
    {
        ItemStack sourceStack = op.SourceSlot.Itemstack!;
        ItemStack sinkStack = op.SinkSlot.Itemstack!;

        if (op.CurrentPriority == EnumMergePriority.DirectMerge
            && SeasoningUtil.ContentStackForSeasoning(sourceStack) is ItemStack seasoningStack
            && seasoningStack.ItemAttributes["seasoningProps"]?.AsObject<SeasoningProperties>() is SeasoningProperties seasoningProps
            && !sinkStack.Attributes.HasAttribute("seasoning")
            && sinkStack.Attributes.GetDecimal("quantityServings") > 0)
        {
            // Don't season sealed crocks
            if (sinkStack.Collectible is BlockCrock && sinkStack.Attributes.GetBool("sealed")) return true;

            int servings = (int)Math.Ceiling(sinkStack.Attributes.GetDecimal("quantityServings") * sinkStack.StackSize);

            int totalSeasoning = servings * seasoningProps.PortionSize;
            float totalSeasoningLitres = servings * seasoningProps.PortionSizeLitres;
            if (seasoningProps.IsLiquid)
            {
                ILiquidSource container = sourceStack.Collectible.GetCollectibleInterface<ILiquidSource>()!;
                if (!container.AllowHeldLiquidTransfer)
                {
                    (op.ActingPlayer.Entity.Api as ICoreClientAPI)?.TriggerIngameError(__instance, "seasoningcanttransferliquid", Lang.Get("aculinaryartillery:seasoning-cant-transfer-liquid"));
                    return false;
                }

                if (container.GetCurrentLitres(sourceStack) < totalSeasoningLitres)
                {
                    (op.ActingPlayer.Entity.Api as ICoreClientAPI)?.TriggerIngameError(__instance, "notenoughseasoning", Lang.Get(servings == 1 ? "aculinaryartillery:not-enough-seasoning-liquid-singular" : "deartibusrusticis-notenough-seasoning-liquid-plural", totalSeasoningLitres, servings));
                    return false;
                }

                int quantity = (int)(totalSeasoningLitres * container.GetContentProps(sourceStack)!.ItemsPerLitre);
                ItemStack liquidStack = container.TryTakeContent(op.SourceSlot.Itemstack!, quantity / op.SourceSlot.StackSize);
                liquidStack.StackSize = quantity;

                // Preserve the perish timer
                op.SinkSlot.Itemstack?.Attributes.SetItemstack("seasoning", liquidStack);
            }
            else
            {
                if (seasoningStack.StackSize < servings)
                {
                    (op.ActingPlayer.Entity.Api as ICoreClientAPI)?.TriggerIngameError(__instance, "notenoughseasoning", Lang.Get(servings == 1 ? "aculinaryartillery:not-enough-seasoning-singular" : "deartibusrusticis-notenough-seasoning-plural", totalSeasoning, servings));
                    return false;
                }

                op.SourceSlot.TakeOut(totalSeasoning);
                op.MovedQuantity = totalSeasoning;

                // Preserve the perish timer
                ItemStack stackForAttr = sourceStack.Clone();
                stackForAttr.StackSize = seasoningProps.PortionSize;
                op.SinkSlot.Itemstack?.Attributes.SetItemstack("seasoning", stackForAttr);
            }

            op.World.PlaySoundFor(seasoningProps.Sound, op.ActingPlayer);

            ItemStack? seasoningStackForAudit = op.SinkSlot.Itemstack?.Attributes.GetItemstack("seasoning");
            seasoningStackForAudit?.ResolveBlockOrItem(op.World);

            op.World.Logger.Audit(
                "{0} added {1}x{2} seasoning to {3} meals with recipe {4} in inventory",
                op.ActingPlayer.PlayerName,
                totalSeasoning,
                seasoningStackForAudit?.Collectible.Code ?? "<seasoning code not found>",
                servings,
                sinkStack.Attributes["recipeCode"]
            );

            return false;
        }

        return true;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(BlockCookedContainerBase.ServeIntoStack))]
    // __state stores pot seasoning before serving
    public static bool RequireSameSeasoningOnServeIntoStack(BlockCookedContainerBase __instance, ref ItemStack? __state, ItemSlot bowlSlot, ItemSlot potslot, IWorldAccessor world)
    {
        ItemStack? potSeasoningStack = potslot.Itemstack?.Attributes.GetItemstack("seasoning");
        potSeasoningStack?.ResolveBlockOrItem(world);
        string? potSeasoning = potSeasoningStack?.Collectible.Code;

        ItemStack? bowlSeasoningStack = bowlSlot.Itemstack?.Attributes.GetItemstack("seasoning");
        bowlSeasoningStack?.ResolveBlockOrItem(world);
        string bowlSeasoning = bowlSeasoningStack?.Collectible.Code;

        float quantityServingsPot = (float)(potslot?.Itemstack?.Attributes.GetDecimal("quantityServings") ?? 0);
        float quantityServingsBowl = (float)(potslot?.Itemstack?.Attributes.GetDecimal("quantityServings") ?? 0);
        float servingsToTransfer = Math.Min(quantityServingsPot, bowlSlot.Itemstack?.Collectible.Attributes["servingCapacity"].AsFloat(1) ?? 1);

        if (servingsToTransfer == 0)
        {
            return true;
        }

        if (
            // Both seasonings present, but different
            (bowlSeasoning != null && potSeasoning != null && bowlSeasoning != potSeasoning)
            // Only bowl seasoned, but servings present in pot
            || (bowlSeasoning != null && quantityServingsPot > 0 && potSeasoning == null)
            // Only pot seasoned, but servings present in bowl
            || (quantityServingsBowl > 0 && bowlSeasoning == null && potSeasoning != null)
        )
        {
            (potslot?.Inventory.Api as ICoreClientAPI)?.TriggerIngameError(__instance, "mismatchedseasoning", Lang.Get("aculinaryartillery:cant-mix-seasonings"));
            return false;
        }

        __state = potslot?.Itemstack?.Attributes.GetItemstack("seasoning");
        __state?.ResolveBlockOrItem(world);
        return true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(BlockCookedContainerBase.ServeIntoStack))]
    // __state stores pot seasoning before serving
    public static void TransferSeasoningIntoStack(ref ItemStack? __state, ItemSlot bowlSlot, ItemSlot potslot, IWorldAccessor world)
    {
        float quantityServings = (float)(potslot?.Itemstack?.Attributes.GetDecimal("quantityServings") ?? 0);
        float servingsToTransfer = Math.Min(quantityServings, bowlSlot?.Itemstack?.Collectible.Attributes["servingCapacity"].AsFloat(1) ?? 1);

        if (servingsToTransfer > 0 && bowlSlot?.Itemstack?.Attributes.HasAttribute("seasoning") == false)
        {
            bowlSlot?.Itemstack?.Attributes.SetItemstack("seasoning", __state);
        }

        if ((float)(potslot?.Itemstack?.Attributes.GetDecimal("quantityServings") ?? 0) == 0)
        {
            potslot?.Itemstack?.Attributes.RemoveAttribute("seasoning");
        }
    }
}

[HarmonyPatchCategory("seasoning")]
[HarmonyPatch(typeof(BlockCookedContainer))]
public static class BlockCookedContainerPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(BlockCookedContainer.GetHeldItemInfo))]
    public static void AddInfoSeasonedText(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (inSlot.Itemstack?.Attributes.GetItemstack("seasoning") is ItemStack seasoning)
        {
            seasoning.ResolveBlockOrItem(world);
            dsc.AppendLine(Lang.Get("aculinaryartillery:meal-seasoned", SeasoningUtil.LangStringForStack(seasoning).ToLowerInvariant()));
        }
    }
}

[HarmonyPatchCategory("seasoning")]
[HarmonyPatch(typeof(BlockFirepit))]
public static class BlockFirepitPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(BlockFirepit.OnBlockInteractStart))]
    public static bool AddSeasoningFirepit(BlockFirepit __instance, ref bool __result, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        ItemSlot? heldSlot = byPlayer.InventoryManager.ActiveHotbarSlot;

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityFirepit bef) return true;
        ItemSlot? potSlot = bef.outputSlot?.Itemstack?.Collectible is BlockCookedContainer ? bef.outputSlot : null;

        if (heldSlot?.Itemstack == null || potSlot?.Itemstack == null) return true;

        if (SeasoningUtil.ContentStackForSeasoning(heldSlot.Itemstack) is ItemStack seasoningStack
            && seasoningStack.ItemAttributes["seasoningProps"]?.AsObject<SeasoningProperties>() is SeasoningProperties seasoningProps
            && potSlot.Itemstack?.Attributes.HasAttribute("seasoning") == false
            && potSlot.Itemstack.Attributes.GetDecimal("quantityServings") > 0)
        {
            if (!seasoningProps.CanUseOnRecipe(potSlot.Itemstack.Attributes.GetString("recipeCode")))
            {
                (byPlayer.Entity.Api as ICoreClientAPI)?.TriggerIngameError(
                    __instance,
                    "seasoningwrongrecipe",
                    Lang.Get("aculinaryartillery:seasoning-wrong-recipe",
                        Lang.Get($"mealrecipe-name-{potSlot.Itemstack.Attributes.GetString("recipeCode")}").ToLowerInvariant(),
                        SeasoningUtil.LangStringForStack(heldSlot.Itemstack).ToLowerInvariant()
                    )
                );
                return false;
            }

            int servings = (int)Math.Ceiling(potSlot.Itemstack.Attributes.GetDecimal("quantityServings") * potSlot.Itemstack.StackSize);

            int totalSeasoning = servings * seasoningProps.PortionSize;
            float totalSeasoningLitres = servings * seasoningProps.PortionSizeLitres;
            if (seasoningProps.IsLiquid)
            {
                ILiquidSource container = heldSlot.Itemstack.Collectible.GetCollectibleInterface<ILiquidSource>()!;
                if (!container.AllowHeldLiquidTransfer)
                {
                    (byPlayer.Entity.Api as ICoreClientAPI)?.TriggerIngameError(__instance, "seasoningcanttransferliquid", Lang.Get("aculinaryartillery:seasoning-cant-transfer-liquid"));
                    return false;
                }

                if (container.GetCurrentLitres(heldSlot.Itemstack) < totalSeasoningLitres)
                {
                    (byPlayer.Entity.Api as ICoreClientAPI)?.TriggerIngameError(__instance, "notenoughseasoning", Lang.Get(servings == 1 ? "aculinaryartillery:not-enough-seasoning-liquid-singular" : "deartibusrusticis-notenough-seasoning-liquid-plural", totalSeasoningLitres, servings));
                    return false;
                }

                int quantity = (int)(totalSeasoningLitres * container.GetContentProps(heldSlot.Itemstack)!.ItemsPerLitre);
                ItemStack liquidStack = container.TryTakeContent(heldSlot.Itemstack, quantity / heldSlot.Itemstack.StackSize);
                liquidStack.StackSize = quantity;

                // Preserve the perish timer
                potSlot.Itemstack.Attributes.SetItemstack("seasoning", liquidStack);
            }
            else
            {
                if (seasoningStack.StackSize < servings)
                {
                    (byPlayer.Entity.Api as ICoreClientAPI)?.TriggerIngameError(__instance, "notenoughseasoning", Lang.Get(servings == 1 ? "aculinaryartillery:not-enough-seasoning-singular" : "deartibusrusticis-notenough-seasoning-plural", totalSeasoning, servings));
                    __result = true;
                    return false;
                }

                heldSlot.TakeOut(totalSeasoning);

                // Preserve the perish timer
                ItemStack stackForAttr = heldSlot.Itemstack.Clone();
                stackForAttr.StackSize = seasoningProps.PortionSize;
                potSlot.Itemstack.Attributes.SetItemstack("seasoning", stackForAttr);
            }

            world.PlaySoundAt(seasoningProps.Sound, byPlayer);

            ItemStack? seasoningStackForAudit = bef.outputSlot?.Itemstack?.Attributes.GetItemstack("seasoning");
            seasoningStackForAudit?.ResolveBlockOrItem(world);

            world.Logger.Audit(
                "{0} added {1}x{2} seasoning to cooked container with {3} meals with recipe {4} in firepit at {5}",
                byPlayer.PlayerName,
                totalSeasoning,
                seasoningStackForAudit?.Collectible.Code ?? "<seasoning code not found>",
                servings,
                potSlot.Itemstack.Attributes["recipeCode"],
                bef.Pos
            );

            (byPlayer as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
            __result = true;

            return false;
        }

        return true;
    }
}
