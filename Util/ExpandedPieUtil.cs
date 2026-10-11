using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

using System;
using Vintagestory.API.Util;
using Vintagestory.API;
using Vintagestory.GameContent;
using HarmonyLib;

/// NOTE(Lazula)
///
/// This entire thing is just a carbon copy of the system that
/// I implemented for 1.23. There are no compromises; The entire
/// thing functions fine with just patching. Any content made for
/// this will work exactly the same in 1.23 and I don't plan to
/// change anything else about the ingredient JSON.
///
/// The new version of InPieProperties has some additional
/// information to support liquids and toppings. The attributes are
/// still pulled from "inPieProperties". I named the new type with
/// Expanded to avoid shadowing the existing InPieProperties.
///
/// Please ask me if you have any questions.

namespace ACulinaryArtillery.Util
{
    /// <summary>
    /// Defines the type of ingredient (crust, filling, topping), food category
    /// for mixing, and what mixing codes it can be used with, if any.
    /// </summary>
    /// <!--<jsonalias>inPieProperties</jsonalias>-->
    [DocumentAsJson]
    public class ExpandedInPieProperties
    {
        [DocumentAsJson("Required")]
        public required AssetLocation Texture;

        /// <summary>
        /// The shape to use if this is a topping.
        /// </summary>
        [DocumentAsJson("Optional", "origin/base/top crust full/*")]
        public string ToppingShapeElement = "origin/base/top crust full/*";

        /// <summary>
        /// Is this filling allowed to mix with other ingredients?
        /// Crusts and toppings ignore this, but toppings always
        /// require a matching mixing code.
        /// <br/><br/>
        /// If false, MixingCodes has no effect because this ingredient
        /// cannot be combined with anything else. Don't add mixing codes
        /// if this is disabled.
        /// </summary>
        [DocumentAsJson("Optional", "true")]
        public bool AllowMixing = true;

        /// <summary>
        /// Is this a crust, filling, or topping?
        /// <br/><br/>
        /// * Crust is used as the pie's base. The ingredient used to
        ///   create a pie must be a crust. Crust can also be used
        ///   as a topping and is not restricted by mixing codes.
        /// <br/>
        /// * Filling is what's inside the pie. AllowMixing determines
        ///   if it can only be used for single-ingredient pies. If it
        ///   can be mixed, other ingredients must have a matching
        ///   mixing code.
        /// <br/>
        /// * Topping is the pie's last layer. Like filling, it is
        ///   restricted by mixing codes. Any crust may be used in place
        ///   of a topping without being restricted by mixing codes.
        /// </summary>
        [DocumentAsJson("Required")]
        public required EnumPiePartType PartType;

        /// <summary>
        /// The food category of the ingredient when used in a pie. This does
        /// not affect the actual nutrition when consumed. It is only used
        /// to determine which category mixing code to prepend in the case
        /// that the code is not already present.
        /// <br/><br/>
        /// An ingredient of the NoNutrition category cannot be added to pies unless
        /// it has at least one explicit mixing code. A NoNutrition ingredient
        /// with no mixing codes is an error unless it is non-mixable. NoNutrition
        /// may be used explicitly, skipping the process below.
        /// <br/><br/>
        /// Checks in order, stopping once a value is found:
        /// <br/><br/>
        ///   1. inPieProperties["foodCategory"]
        /// <br/>
        ///   2. If stack has WaterTightContainableProps:
        /// <br/>
        ///     2a. NutritionPropsPerLitreWhenInMeal.FoodCategory
        /// <br/>
        ///     2b. NutritionPropsPerLitre.FoodCategory
        /// <br/>
        ///   3. If stack has NutritionProps:
        /// <br/>
        ///     3a. NutritionPropsWhenInMeal.FoodCategory
        /// <br/>
        ///     3b. NutritionProps.FoodCategory
        /// <br/>
        ///   4. Return the properties as null, because the item does not have.
        /// </summary>
        [DocumentAsJson("Optional", "See process in docstring")]
        public EnumFoodCategory FoodCategory = EnumFoodCategory.NoNutrition;

        /// <summary>
        /// A list of mixing codes that are allowed for this ingredient. When
        /// checking for mixing codes, the first mixing code present in all
        /// ingredients is used for the pie type.
        /// <br/><br/>
        /// If the ingredient's food category is not NoNutrition and the category
        /// code is not already present, it will be prepended. This means that by
        /// default, mixing codes are of a lower priority than the food category:
        /// <br/>
        /// [ "potpie" ] -> [ "vegetable", "potpie" ]
        /// <br/><br/>
        /// By including the food category in the list of mixing codes, other codes
        /// can be given a higher priority. For example, the following would create
        /// a "mushroom" mixing code that would take precedence over "vegetable".
        /// <br/>
        /// [ "mushroom", "vegetable", "potpie" ]
        /// <br/><br/>
        /// An ingredient may have multiple food category mixing codes. It will be
        /// allowed in any of those mixed pies and will not affect mixing codes.
        /// If it is the only ingredient, the pie will be named after it instead
        /// of a mixing code.
        /// <br/><br/>
        /// If MixingCodes is empty, the food category code will always be added.
        /// It is an error for MixingCodes to be empty with NoNutrition, unless
        /// the ingredient cannot be mixed.
        /// </summary>
        [DocumentAsJson("Optional", "[]")]
        public string[] MixingCodes = [];

        /// <summary>
        /// For items; The number of items in the stack used for one layer.
        /// </summary>
        [DocumentAsJson("Optional", "2")]
        public int PortionSize = 2;

        /// <summary>
        /// For liquids; The number of liters used for one layer.
        /// </summary>
        [DocumentAsJson("Optional", "0.4")]
        public float PortionSizeLitres = 0.4f;

        public bool IsLiquid = false;

        /// <summary>
        /// The appropriate portion size based on whether the ingredient is an item or liquid.
        /// </summary>
        public float GetPortionSize() => IsLiquid ? PortionSizeLitres : PortionSize;

        /// <summary>
        /// Cached from WaterTightContainableProperties
        /// </summary>
        public float ItemsPerLitre = 100f;

        /// <summary>
        /// The number of actual content items per portion.
        /// </summary>
        /// <returns></returns>
        public int ItemsPerPortion() => IsLiquid ? (int)(PortionSizeLitres * ItemsPerLitre) : PortionSize;

        /// <summary>
        /// Read pie properties from Attributes.
        /// </summary>
        /// <returns>Null if the ingredient is unusable or "inPieProperties" does not exist.</returns>
        public static ExpandedInPieProperties? ReadFrom(CollectibleObject obj, out string? errMessage)
        {
            errMessage = null;

            if (obj?.Attributes?["inPieProperties"]?.AsObject<ExpandedInPieProperties>(null, obj.Code.Domain) is not ExpandedInPieProperties props)
            {
                return null;
            }

            WaterTightContainableProps? liquidProps = BlockLiquidContainerBase.GetContainableProps(new ItemStack(obj));
            FoodNutritionProperties? nutriProps = obj.GetNutritionProperties(null, null, null);
            FoodNutritionProperties? nutriPropsInMeal = obj.Attributes["nutritionPropsWhenInMeal"]?.AsObject<FoodNutritionProperties>();

            // This ingredient has no nutrition properties at all, so it would do nothing in a pie.
            if (nutriProps == null && nutriPropsInMeal == null && (liquidProps == null || (liquidProps.NutritionPropsPerLitre == null && liquidProps.NutritionPropsPerLitreWhenInMeal == null)))
            {
                errMessage = $"{obj.Code} has inPieProperties, but no nutrition properties. It cannot be used in pies.";
                return null;
            }

            // Do not attempt to overwrite NoNutrition if it was set manually, only if it was defaulted to.
            if (!obj.Attributes["inPieProperties"]["foodCategory"].Exists)
            {
                // Get the food category manually. It doesn't need to be present in the pie properties.
                // We do this handling here to avoid making the field unnecessarily nullable.

                EnumFoodCategory? foodCat = null;

                if (liquidProps != null)
                {
                    foodCat ??= liquidProps.NutritionPropsPerLitreWhenInMeal?.FoodCategory;
                    foodCat ??= liquidProps.NutritionPropsPerLitre?.FoodCategory;
                }

                foodCat ??= nutriPropsInMeal?.FoodCategory;
                foodCat ??= nutriProps?.FoodCategory;

                // This ingredient has no food category at all, so it would do nothing in a pie.
                if (foodCat == null)
                {
                    errMessage = $"{obj.Code} has inPieProperties and nutrition properties, but no food category. It cannot be used in pies.";
                    return null;
                }

                props.FoodCategory = foodCat.Value;
            }

            if (liquidProps != null)
            {
                props.IsLiquid = true;
                props.ItemsPerLitre = liquidProps.ItemsPerLitre;
            }

            // Never add the code for NoNutrition.
            if (props.FoodCategory != EnumFoodCategory.NoNutrition)
            {
                // Add the food category code if there are no mixing codes or if it wasn't explicitly added.
                string foodCatCode = props.FoodCategory.ToString().ToLowerInvariant();
                if (props.MixingCodes.Length == 0 || !props.MixingCodes.Contains(foodCatCode))
                {
                    props.MixingCodes = props.MixingCodes.Prepend(foodCatCode).ToArray();
                }
            }

            // Nonsensical quantity.
            if (props.GetPortionSize() <= 0)
            {
                errMessage = $"inPieProperties for {obj.Code} has a portion size of 0 or less. It cannot be used in pies.";
                return null;
            }

            // Ingredient can be mixed, but nothing is compatible.
            if ((props.PartType == EnumPiePartType.Filling || props.PartType == EnumPiePartType.Topping)
                && props.AllowMixing && props.MixingCodes.Length == 0 && props.FoodCategory == EnumFoodCategory.NoNutrition)
            {
                errMessage = $"InPieProperties for {(props.PartType == EnumPiePartType.Filling ? "filling" : "topping")} {obj.Code} has no mixing codes and the food category NoNutrition. It cannot be added to pies unless you explicitly disable mixing.";
                return null;
            }

            return props;
        }

        /// <summary>
        /// Read pie properties from Attributes.
        /// </summary>
        /// <returns>Null if the ingredient is unusable or "inPieProperties" does not exist.</returns>
        public static ExpandedInPieProperties? ReadFrom(CollectibleObject? obj)
        {
            return obj != null ? ReadFrom(obj, out _) : null;
        }

        /// <summary>
        /// Read pie properties from ItemAttributes.
        /// </summary>
        /// <returns>Null if the ingredient is unusable or "inPieProperties" does not exist.</returns>
        public static ExpandedInPieProperties? ReadFrom(ItemStack? stack)
        {
            return ReadFrom(stack?.Collectible);
        }
    }

    public class ExpandedPieUtil
    {
        protected static HashSet<string> reportedMissingPieProps = [];

        /// <summary>
        /// Report that an object in a pie is missing its InPieProperties.
        /// Use this to prevent repeated error spam.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="code">The collectible code.</param>
        /// <returns>true if the element is added to the HashSet object; false if the element is already present.</returns>
        public static bool ReportMissingPieProps(ILogger logger, string code)
        {
            if (!reportedMissingPieProps.Contains(code))
            {
                logger.Error($"{code} is already in a pie, but it no longer has inPieProperties. It's still edible, but some things will break.");
            }

            return reportedMissingPieProps.Add(code);
        }

        public static bool BEPieCanAddIngredient(BlockEntityPie bep, ItemStack stack)
        {
            return BEPieCanAddIngredient(bep, stack, out _, out _, out _);
        }

        public static bool BEPieCanAddIngredient(BlockEntityPie bep, ItemStack stack, out int? emptySlotIndex, out string? errCode, out string? errMessage)
        {
            InventoryGeneric inv = Traverse.Create(bep).Field("inv").GetValue<InventoryGeneric>();

            errCode = null;
            errMessage = null;
            emptySlotIndex = null;

            ILiquidSource? container = stack.Collectible.GetCollectibleInterface<ILiquidSource>();
            if (container?.AllowHeldLiquidTransfer == false)
            {
                errCode = "notpieable";
                errMessage = Lang.Get("This item can not be added to pies");
                return false;
            }

            ItemStack contentStack = stack;
            if (container != null)
            {
                if (container.GetContent(stack) is ItemStack cStack)
                {
                    contentStack = cStack;
                }
                else
                {
                    errCode = "notpieable";
                    errMessage = Lang.Get("This item can not be added to pies");
                    return false;
                }
            }

            if (ExpandedInPieProperties.ReadFrom(contentStack) is not ExpandedInPieProperties pieProps)
            {
                errCode = "notpieable";
                errMessage = Lang.Get("This item can not be added to pies");
                return false;
            }

            float totalPortions = contentStack.StackSize / pieProps.ItemsPerPortion();
            if (totalPortions < 1)
            {
                errCode = "notenoughingredients";
                errMessage = Lang.Get(container != null ? "piemaking-notenoughliquid" : "piemaking-notenoughitems", pieProps.GetPortionSize());
                return false;
            }

            if (inv[0].Itemstack?.Block is not BlockPie pieBlock) return false;

            ItemStack?[] cStacks = pieBlock.GetContents(bep.Api.World, inv[0].Itemstack);

            // Special case:
            // Using a knife or crust on a pie with a crust topping should succeed without an error,
            // but the emptySlotIndex is still null because we aren't actually adding anything.
            if (BEPieGetToppingType(bep) is EnumPiePartType toppingType)
            {
                bool addingCrust = pieProps.PartType == EnumPiePartType.Crust;
                EnumTool? tool = stack?.Collectible.GetTool(new DummySlot(stack));
                bool usingCuttingTool = tool == EnumTool.Knife || tool == EnumTool.Sword;

                if (toppingType == EnumPiePartType.Crust && (addingCrust || usingCuttingTool))
                {
                    return true;
                }
                else
                {
                    errCode = "piefinished";
                    errMessage = Lang.Get("piemaking-alreadycomplete");
                    return false;
                }
            }

            if (bep.HasAllFilling)
            {
                if (pieProps.PartType == EnumPiePartType.Filling)
                {
                    errCode = "piefullfilling";
                    errMessage = Lang.Get("piemaking-alreadycomplete");
                    return false;
                }
                else if (pieProps.PartType == EnumPiePartType.Crust)
                {
                    emptySlotIndex = 5;
                    return true;
                }
            }

            if (!bep.HasAllFilling && pieProps.PartType != EnumPiePartType.Filling)
            {
                errCode = "pieneedsfilling";
                errMessage = Lang.Get("Need to add a filling next");
                return false;
            }

            if (!bep.HasAnyFilling)
            {
                emptySlotIndex = 1;
                return true;
            }

            ExpandedInPieProperties?[] stackPieProps = cStacks.Select(stack =>
            {
                ExpandedInPieProperties? pieProps = ExpandedInPieProperties.ReadFrom(stack);
                if (stack != null && pieProps == null)
                {
                    // An ingredient already in a pie is missing its inPieProperties
                    ReportMissingPieProps(bep.Api.Logger, stack.Collectible.Code);
                }
                return pieProps;
            }).ToArray();

            bool singleIngredient = true;
            bool allowMixing = pieProps.AllowMixing;
            IEnumerable<string> mixCodes = pieProps.MixingCodes;

            // Note that we check the topping slot here because non-crust toppings are restricted by mixing codes.
            for (int i = 1; i < cStacks.Length; i++)
            {
                if (cStacks[i] == null) break;

                singleIngredient &= cStacks[i]!.Equals(bep.Api.World, stack, GlobalConstants.IgnoredStackAttributes);

                if (stackPieProps[i] != null)
                {
                    allowMixing &= stackPieProps[i]!.AllowMixing == true || pieProps.PartType == EnumPiePartType.Topping;
                    mixCodes = stackPieProps[i]!.MixingCodes.Intersect(mixCodes) ?? [];
                }

                if (!singleIngredient && !mixCodes.Any()) break;
            }


            if (!singleIngredient && !allowMixing)
            {
                errCode = "pienonmixable";
                errMessage = Lang.Get("piemaking-mixingnotallowed");
                return false;
            }
            else if (!singleIngredient && !mixCodes.Any())
            {
                if (pieProps.PartType == EnumPiePartType.Filling)
                {
                    errCode = "piemismatchedmix";
                    errMessage = Lang.Get("piemaking-unabletomixingredient");
                }
                else
                {
                    errCode = "piemismatchedtopping";
                    errMessage = Lang.Get("piemaking-unabletoaddtopping");
                }
                return false;
            }

            if (cStacks[4] != null) emptySlotIndex = 5;
            else if (cStacks[3] != null) emptySlotIndex = 4;
            else if (cStacks[2] != null) emptySlotIndex = 3;
            else emptySlotIndex = 2;

            return true;
        }

        public static EnumPiePartType? BEPieGetToppingType(BlockEntityPie bep)
        {
            return (bep.Inventory[0].Itemstack?.Block as BlockPie)?
                .GetContents(bep.Api.World, bep.Inventory[0].Itemstack)?
                [5]?
                .ItemAttributes
                ["inPieProperties"]
                ["partType"]
                .AsObject<EnumPiePartType>();
        }

        /// <summary>
        /// The food category mixing code for the ingredient when used in a pie.
        /// This is not the actual food category when consumed.
        ///
        /// See ExpandedInPieProperties.FoodCategory documentation
        /// </summary>
        public static EnumFoodCategory IngredientFoodCategory(ItemStack? stack)
        {
            if (ExpandedInPieProperties.ReadFrom(stack) is ExpandedInPieProperties pieProps) return pieProps.FoodCategory;

            return EnumFoodCategory.NoNutrition;
        }

        public static bool BEPieTryAddIngredientFrom(ref BlockEntityPie bep, ref InventoryGeneric inv, ItemSlot slot, IPlayer byPlayer)
        {
            ICoreClientAPI? capi = bep.Api as ICoreClientAPI;

            if ((inv[0].Itemstack?.Block as BlockPie) == null || slot.Itemstack == null) return false;

            if (!BEPieCanAddIngredient(bep, slot.Itemstack, out int? emptySlotIndex, out string? errCode, out string? errMessage))
            {
                capi?.TriggerIngameError(bep, errCode, errMessage);
                return false;
            }

            ILiquidSource? container = slot.Itemstack.Collectible.GetCollectibleInterface<ILiquidSource>();
            ItemStack contentStack = slot.Itemstack;
            if (container?.GetContent(slot.Itemstack) is ItemStack cStack)
            {
                contentStack = cStack;
            }

            // CanAddIngredient already made sure the pie props are valid
            ExpandedInPieProperties pieProps = ExpandedInPieProperties.ReadFrom(contentStack)!;
            if (pieProps.PartType == EnumPiePartType.Crust)
            {
                if (emptySlotIndex == null)
                {
                    // Using a knife to cycle crust type
                    inv[0].Itemstack = BlockPie.CycleTopCrustType(inv[0].Itemstack);
                    return true;
                }

                if (emptySlotIndex == 5)
                {
                    // Crust attribute must exist to stack together
                    inv[0].Itemstack?.Attributes.SetString("topCrustType", "full");
                }
            }

            ItemStack ingStack;

            if (byPlayer?.WorldData.CurrentGameMode == EnumGameMode.Creative)
            {
                DummySlot dummySlot = new(slot.Itemstack.Clone());
                ILiquidSource? dummySource = dummySlot.Itemstack!.Collectible.GetCollectibleInterface<ILiquidSource>();

                if (dummySource != null)
                {
                    if (dummySource.TryTakeContent(dummySlot.Itemstack, pieProps.ItemsPerPortion()) is ItemStack taken && taken.StackSize >= pieProps.ItemsPerPortion())
                    {
                        ingStack = taken;
                    }
                    else
                    {
                        bep.Api.Logger.Error($"BEPie.TryAddIngredientFrom expected at least {pieProps.ItemsPerPortion()} liquid items, but there were only {dummySlot.Itemstack.StackSize}. There is likely a bug either here or in CanAddIngredient.");
                        return false;
                    }
                }
                else
                {
                    ingStack = dummySlot.TakeOut(pieProps.ItemsPerPortion());
                }
            }
            else
            {
                if (container != null)
                {
                    if (container.TryTakeContent(slot.Itemstack, pieProps.ItemsPerPortion()) is ItemStack taken && taken.StackSize >= pieProps.ItemsPerPortion())
                    {
                        ingStack = taken;
                        slot.MarkDirty();
                    }
                    else
                    {
                        bep.Api.Logger.Error($"BEPie.TryAddIngredientFrom expected at least {pieProps.ItemsPerPortion()} liquid items, but there were only {slot.Itemstack.StackSize}. There is likely a bug either here or in CanAddIngredient.");
                        return false;
                    }
                }
                else
                {
                    ingStack = slot.TakeOut(pieProps.ItemsPerPortion());
                }
            }

            ItemStack[] cStacks = (inv[0].Itemstack!.Block as BlockPie)!.GetContents(bep.Api.World, inv[0].Itemstack);
            int ingredientCount = cStacks.Where(stack => stack != null).Count();

            // Average perish timers before adding
            float t = (float)1 / (1 + ingredientCount);
            if (byPlayer?.Entity.World != null
                && ingStack.Collectible.UpdateAndGetTransitionState(byPlayer.Entity.World, new DummySlot(ingStack), EnumTransitionType.Perish) is TransitionState ingState
                && (inv[0].Itemstack!.Block as BlockPie)!.UpdateAndGetTransitionState(byPlayer.Entity.World, inv[0], EnumTransitionType.Perish) is TransitionState pieState)
            {
                float totalIngHours = ingState.FreshHours + ingState.TransitionHours;
                float totalPieHours = pieState.FreshHours + pieState.TransitionHours;
                float scaledIngTransitionedHours = ingState.TransitionedHours / (totalIngHours / totalPieHours);

                var avgTransitionedHours = scaledIngTransitionedHours * t + pieState.TransitionedHours * (1 - t);
                //if (bep.Api.Side.IsServer()) bep.Api.Logger.Debug($"Averaged new ingredient perish time: {ingState.TransitionedHours / (totalIngHours / totalPieHours)} * {t} + {pieState.TransitionedHours} * {1 - t}");
                (inv[0].Itemstack!.Block as BlockPie)!.SetTransitionState(inv[0].Itemstack, ingState.Props.Type, avgTransitionedHours);
            }

            cStacks[(int)emptySlotIndex!] = ingStack;
            (inv[0].Itemstack?.Block as BlockPie)!.SetContents(inv[0].Itemstack, cStacks);

            return true;
        }
    }
}
