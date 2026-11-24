using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace ACulinaryArtillery
{
    public class ACACookingRecipeNames : VanillaCookingRecipeNames, ICookingRecipeNamingHelper
    {
        public new string GetNameForIngredients(IWorldAccessor worldForResolve, string? recipeCode, ItemStack[] stacks)
        {
            Vintagestory.API.Datastructures.OrderedDictionary<ItemStack, int> quantitiesByStack = mergeStacks(worldForResolve, stacks);

            CookingRecipe? recipe = worldForResolve.Api.GetCookingRecipe(recipeCode) ?? worldForResolve.Api.GetMixingRecipes().FirstOrDefault((CookingRecipe rec) => recipeCode == rec.Code);

            if (recipeCode == null || recipe == null || quantitiesByStack.Count == 0) return Lang.Get("unknown");

            return GetNameForMergedIngredients(worldForResolve, recipe, quantitiesByStack);
        }

        protected override string GetNameForMergedIngredients(IWorldAccessor worldForResolve, CookingRecipe recipe, Vintagestory.API.Datastructures.OrderedDictionary<ItemStack, int> quantitiesByStack)
        {
            Vintagestory.API.Datastructures.OrderedDictionary<ItemStack, CookingRecipeIngredient> cookingRecipeIngredientsByStack = GetIngredientsFromRecipe(recipe, [.. quantitiesByStack.Keys]);
            string recipeCode = recipe.Code!;

            switch (recipeCode)
            {
                case "augratin":
                    {
                        string CheeseName = null!;
                        string? VegetableName = null;
                        string? MeatName = null;

                        List<string> FreshIngredientNames = [];

                        int max = 0;

                        foreach ((ItemStack stack, int quantity) in quantitiesByStack)
                        {
                            CookingRecipeIngredient? ingredient = cookingRecipeIngredientsByStack.TryGetValue(stack);
                            if (ingredient?.TypeName == "milk") continue;

                            if (ingredient?.TypeName == "cheese")
                            {
                                CheeseName = CheeseName == null
                                    ? CheeseName = getMainIngredientName(stack, recipeCode)
                                    : CheeseName = Lang.Get("meal-ingredient-augratin-primary-generic-cheese");

                                max += quantity;
                                continue;
                            }

                            if (ingredient?.TypeName == "vegetable")
                            {
                                VegetableName = VegetableName == null
                                    ? VegetableName = getMainIngredientName(stack, recipeCode, true)
                                    : VegetableName = Lang.Get("meal-ingredient-augratin-primary-generic-vegetable");

                                continue;
                            }

                            if (ingredient?.TypeName == "meat")
                            {
                                MeatName = MeatName == null
                                    ? MeatName = getMainIngredientName(stack, recipeCode, true)
                                    : MeatName = Lang.Get("meal-ingredient-augratin-primary-generic-meat");

                                continue;
                            }

                            FreshIngredientNames.Add(ingredientName(stack, EnumIngredientNameType.InsturmentalCase));
                        }

                        string MainIngredientsFormat = "{0}";
                        string FreshIngredientsFormat = "meal-adds-augratin-fresh";

                        string ExtraMeatVegetableFormat = "{0}";
                        List<string> ExtraMeatVegetableNames = [];
                        if (VegetableName != null) ExtraMeatVegetableNames.Add(VegetableName);
                        if (MeatName != null) ExtraMeatVegetableNames.Add(MeatName);

                        if (ExtraMeatVegetableNames.Count > 1)
                        {
                            ExtraMeatVegetableFormat = "multi-main-ingredients-format";
                        }

                        string MealFormat = getMaxMealFormat("meal", recipeCode, max);
                        if (ExtraMeatVegetableNames.Count > 0)
                        {
                            MealFormat += "-with-veggiemeat";
                        }

                        MealFormat = Lang.Get(MealFormat, getMainIngredientsString([CheeseName], MainIngredientsFormat), getMealAddsString(FreshIngredientsFormat, FreshIngredientNames), getMainIngredientsString(ExtraMeatVegetableNames, ExtraMeatVegetableFormat));

                        return MealFormat.Trim().UcFirst();
                    }
                case "compote":
                    {
                        string FruitName = null!;
                        string SyrupName = null!;

                        List<string> FreshIngredientNames = [];

                        int max = 0;

                        foreach ((ItemStack stack, int quantity) in quantitiesByStack)
                        {
                            CookingRecipeIngredient? ingredient = cookingRecipeIngredientsByStack.TryGetValue(stack);

                            if (ingredient?.TypeName == "fruit")
                            {
                                FruitName = FruitName == null
                                    ? FruitName = getMainIngredientName(stack, recipeCode)
                                    : FruitName = Lang.Get("meal-ingredient-compote-primary-generic-fruit");

                                max += quantity;
                                continue;
                            }

                            if (ingredient?.Code == "syrup-base")
                            {
                                SyrupName = getMainIngredientName(stack, recipeCode, true);
                                continue;
                            }

                            FreshIngredientNames.Add(ingredientName(stack, EnumIngredientNameType.InsturmentalCase));
                        }

                        string FreshIngredientsFormat = "meal-adds-compote-fresh";

                        string MealFormat = getMaxMealFormat("meal", recipeCode, max);
                        MealFormat = Lang.Get(MealFormat, FruitName, SyrupName, getMainIngredientsString(FreshIngredientNames, FreshIngredientsFormat));

                        return MealFormat.Trim().UcFirst();
                    }
            }

            return Lang.Get("unknown");
        }

        public static Vintagestory.API.Datastructures.OrderedDictionary<ItemStack, CookingRecipeIngredient> GetIngredientsFromRecipe(CookingRecipe recipe, ItemStack[] stacks)
        {
            Vintagestory.API.Datastructures.OrderedDictionary<ItemStack, CookingRecipeIngredient> ingredientsByStack = [];

            if (recipe.Ingredients == null || stacks.Length == 0) return ingredientsByStack;

            var ingredients = recipe.Ingredients;

            int[] curQuantities = new int[ingredients.Length];
            for (int i = 0; i < curQuantities.Length; i++) curQuantities[i] = 0;

            for (int i = 0; i < stacks.Length; i++)
            {
                ItemStack stack = stacks[i];

                for (int j = 0; j < ingredients.Length; j++)
                {
                    CookingRecipeIngredient ingred = ingredients[j];

                    if (ingred.GetMatchingStack(stack) is CookingRecipeStack jstack)
                    {
                        if (curQuantities[j] >= ingred.MaxQuantity) continue;

                        curQuantities[j]++;
                        ingredientsByStack[stack] = ingred;
                        break;
                    }
                }
            }

            return ingredientsByStack;
        }
    }

    public class DoughRecipe : IByteSerializable
    {
        public string Code = "something";
        public AssetLocation Name { get; set; } = null!;
        public bool Enabled { get; set; } = true;


        public DoughIngredient[] Ingredients = null!;

        public JsonItemStack Output = null!;

        public ItemStack? TryCraftNow(ICoreAPI api, ItemSlot[] inputslots)
        {
            ItemStack? mixedStack = TryGetOutput(inputslots);
            if (mixedStack == null) return null;

            ConsumeIngredients(inputslots, mixedStack);

            return mixedStack;
        }

        /// <summary>
        /// Builds what this recipe would produce from the given input WITHOUT consuming anything, so a
        /// caller can check it has somewhere to put the result before committing to the craft.
        /// </summary>
        public ItemStack? TryGetOutput(ItemSlot[] inputslots)
        {
            var matched = pairInput(inputslots);
            if (matched == null) return null;

            ItemStack mixedStack = Output.ResolvedItemstack.Clone();
            mixedStack.StackSize = getOutputSize(matched);

            if (mixedStack.StackSize <= 0) return null;

            if (mixedStack.Collectible is IExpandedFood food) food.OnCreatedByKneading(matched, mixedStack);

            return mixedStack;
        }

        /// <summary>
        /// Consumes the ingredients for a craft that has already been committed. <paramref name="craftedStack"/>
        /// must be the stack returned by <see cref="TryGetOutput"/>, so the amounts taken match its size.
        /// </summary>
        public void ConsumeIngredients(ItemSlot[] inputslots, ItemStack craftedStack)
        {
            var matched = pairInput(inputslots);
            if (matched == null) return;

            foreach (var val in matched)
            {
                val.Key.TakeOut(val.Value.Quantity * (craftedStack.StackSize / Output.StackSize));
                val.Key.MarkDirty();
            }
        }

        public bool Matches(IWorldAccessor worldForResolve, ItemSlot[] inputSlots)
        {
            if (pairInput(inputSlots) is not Dictionary<ItemSlot, CraftingRecipeIngredient> matched) return false;

            return getOutputSize(matched) >= 0;
        }

        Dictionary<ItemSlot, CraftingRecipeIngredient>? pairInput(ItemSlot[] inputStacks)
        {
            List<ItemSlot> inputSlots = [.. inputStacks.Where(slot => !slot.Empty)];
            if (inputSlots.Count != Ingredients.Length) return null;

            Dictionary<ItemSlot, CraftingRecipeIngredient> matched = [];
            foreach (var ingred in Ingredients)
            {
                bool found = false;

                for (int i = 0; i < inputSlots.Count; i++)
                {
                    if (ingred.GetMatch(inputSlots[i].Itemstack) is CraftingRecipeIngredient input)
                    {
                        matched[inputSlots[i]] = input;
                        inputSlots.RemoveAt(i);
                        found = true;
                        break;
                    }
                }

                if (!found) return null;
            }

            return matched;
        }

        int getOutputSize(Dictionary<ItemSlot, CraftingRecipeIngredient>? matched)
        {
            if (matched == null) return 0;
            int outQuantityMul = -1;

            foreach (var val in matched)
            {
                int posChange = val.Key.StackSize / val.Value.Quantity;

                if (posChange < outQuantityMul || outQuantityMul == -1) outQuantityMul = posChange;
            }

            if (outQuantityMul == -1) return -1;

            foreach (var val in matched)
            {
                // Must have same or more than the total crafted amount
                if (val.Key.StackSize < val.Value.Quantity * outQuantityMul) return -1;
            }

            outQuantityMul = 1;
            return Output.StackSize * outQuantityMul;
        }

        public string GetOutputName()
        {
            return Lang.Get("aculinaryartillery:Will make {0}", Output.ResolvedItemstack.GetName());
        }

        public bool Resolve(IWorldAccessor world, string sourceForErrorLogging)
        {
            bool ok = true;

            for (int i = 0; i < Ingredients.Length; i++)
            {
                ok &= Ingredients[i].Resolve(world, sourceForErrorLogging);
            }

            ok &= Output.Resolve(world, sourceForErrorLogging);

            return ok;
        }

        public void ToBytes(BinaryWriter writer)
        {
            writer.Write(Code);
            writer.Write(Ingredients.Length);
            foreach (var ingred in Ingredients) ingred.ToBytes(writer);

            Output.ToBytes(writer);
        }

        public void FromBytes(BinaryReader reader, IWorldAccessor resolver)
        {
            Code = reader.ReadString();
            Ingredients = new DoughIngredient[reader.ReadInt32()];

            for (int i = 0; i < Ingredients.Length; i++)
            {
                Ingredients[i] = new DoughIngredient();
                Ingredients[i].FromBytes(reader, resolver);
                Ingredients[i].Resolve(resolver, "Dough Recipe (FromBytes)");
            }

            Output = new JsonItemStack();
            Output.FromBytes(reader, resolver.ClassRegistry);
            Output.Resolve(resolver, "Dough Recipe (FromBytes)");
        }

        public DoughRecipe Clone()
        {
            return new()
            {
                Output = Output.Clone(),
                Code = Code,
                Enabled = Enabled,
                Name = Name,
                Ingredients = [.. Ingredients.Select(ing => ing.Clone())]
            };
        }

        public Dictionary<string, string[]> GetNameToCodeMapping(IWorldAccessor world)
        {
            Dictionary<string, string[]> mappings = [];

            if (Ingredients == null || Ingredients.Length == 0) return mappings;

            foreach (var ingreds in Ingredients)
            {
                if (ingreds.Inputs.Length <= 0) continue;
                CraftingRecipeIngredient ingred = ingreds.Inputs[0];
                if (ingred == null || !ingred.Code.Path.Contains('*') || ingred.Name == null) continue;

                int wildcardStartLen = ingred.Code.Path.IndexOf('*');
                int wildcardEndLen = ingred.Code.Path.Length - wildcardStartLen - 1;
                IEnumerable<CollectibleObject> collObjs = ingred.Type == EnumItemClass.Block ? world.Blocks : world.Items;

                mappings[ingred.Name] = [.. collObjs.Where(obj => obj.ItemClass == ingred.Type && obj.Code != null && !obj.IsMissing && WildcardUtil.Match(ingred.Code, obj.Code))
                                                    .Select(obj => obj.Code.Path[wildcardStartLen..])
                                                    .Select(code => code[..^wildcardEndLen])
                                                    .Where(codepart => ingred.AllowedVariants?.Contains(codepart) != false)];
            }

            return mappings;
        }
    }

    public class SimmerRecipe : IByteSerializable
    {
        public string Code = "something";
        public AssetLocation Name { get; set; } = null!;
        public bool Enabled { get; set; } = true;


        public CraftingRecipeIngredient[] Ingredients = null!;

        public CombustibleProperties Simmering = null!;

        public ItemStack? TryCraftNow(ICoreAPI api, ItemSlot[] inputslots)
        {
            var matched = pairInput(inputslots);
            if (matched == null) return null;

            ItemStack mixedStack = Simmering.SmeltedStack.ResolvedItemstack.Clone();
            mixedStack.StackSize = getOutputSize(matched);

            if (mixedStack.StackSize <= 0) return null;

            foreach (var val in matched)
            {
                val.Key.TakeOut(val.Value.Quantity * (mixedStack.StackSize / Simmering.SmeltedStack.StackSize));
                val.Key.MarkDirty();
            }

            return mixedStack;
        }

        public bool Matches(IWorldAccessor worldForResolve, ItemSlot[] inputSlots)
        {
            if (pairInput(inputSlots) is not Dictionary<ItemSlot, CraftingRecipeIngredient> matched) return false;

            return getOutputSize(matched) >= 0;
        }

        /// <summary>
        /// Match a list of ingredients against the recipe and give back the amount that can be made with what's given
        /// Will return 0 if the ingredients are NOT in the right proportions!
        /// </summary>
        /// <param name="Inputs">a list of item stacks</param>
        /// <returns>the amount of the recipe that can be made</returns>
        public int Match(List<ItemStack> Inputs)
        {
            if (Inputs.Count != Ingredients.Length) return 0; //not the correct amount of ingredients for that recipe

            var matched = new List<CraftingRecipeIngredient>();
            int amountForTheRecipe = -1;

            foreach (ItemStack input in Inputs)
            {
                // First check if we have a matching ingredient, and whether we've already matched that ingredient before

                var match = Ingredients.FirstOrDefault(ing => (ing.ResolvedItemStack != null || ing.IsWildCard) && !matched.Contains(ing) && ing.SatisfiesAsIngredient(input));

                if (match == null) return 0; // didn't find a match for the input in previous step
                if (input.StackSize % match.Quantity != 0) return 0; //this particular ingredient is not in enough quantity for full portions
                if (input.StackSize / match.Quantity % Simmering.SmeltedRatio != 0) return 0; //same but taking the smeltedRatio into account ? would love to see an example where that's needed

                int amountForThisIngredient = input.StackSize / match.Quantity / Simmering.SmeltedRatio;

                if (amountForThisIngredient > 0)    //the ingredient can at least produce a portion
                {
                    if (amountForTheRecipe == -1) amountForTheRecipe = amountForThisIngredient;   // on the first match we set the target amount of portions

                    if (amountForThisIngredient != amountForTheRecipe) return 0;   //we only want perfectly proportioned ingredients
                    else matched.Add(match); //this ingredient matches the target amount, add it 
                }
                else return 0;      //we need at least a full portion!
            }

            return amountForTheRecipe;
        }

        Dictionary<ItemSlot, CraftingRecipeIngredient>? pairInput(ItemSlot[] inputStacks)
        {
            var inputSlotsList = new Queue<ItemSlot>(inputStacks.Where(val => !val.Empty));

            if (inputSlotsList.Count != Ingredients.Length) return null;

            Dictionary<ItemSlot, CraftingRecipeIngredient> matched = [];
            while (inputSlotsList.Count > 0)
            {
                ItemSlot inputSlot = inputSlotsList.Dequeue();
                bool found = false;

                foreach (var ingred in Ingredients)
                {
                    if (ingred.SatisfiesAsIngredient(inputSlot.Itemstack) && !matched.ContainsValue(ingred))
                    {
                        matched[inputSlot] = ingred;
                        found = true;
                        break;
                    }
                }

                if (!found) return null;
            }

            if (matched.Count != Ingredients.Length) return null; // We're missing ingredients

            return matched;
        }

        int getOutputSize(Dictionary<ItemSlot, CraftingRecipeIngredient> matched)
        {
            int outQuantityMul = -1;

            foreach (var val in matched)
            {
                int posChange = val.Key.StackSize / val.Value.Quantity;

                if (posChange < outQuantityMul || outQuantityMul == -1) outQuantityMul = posChange;
            }

            if (outQuantityMul == -1) return -1;

            foreach (var val in matched)
            {
                // Must have same or more than the total crafted amount
                if (val.Key.StackSize < val.Value.Quantity * outQuantityMul) return -1;
            }

            return Simmering.SmeltedStack.StackSize;
        }

        public string GetOutputName()
        {
            return Lang.Get("aculinaryartillery:Will make {0}", Simmering.SmeltedStack.ResolvedItemstack.GetName());
        }

        public bool Resolve(IWorldAccessor world, string sourceForErrorLogging)
        {
            bool ok = true;

            for (int i = 0; i < Ingredients.Length; i++)
            {
                ok &= Ingredients[i].Resolve(world, sourceForErrorLogging);
            }

            ok &= Simmering.SmeltedStack.Resolve(world, sourceForErrorLogging);

            return ok;
        }

        public void ToBytes(BinaryWriter writer)
        {
            writer.Write(Code);
            writer.Write(Ingredients.Length);
            foreach (var ingred in Ingredients) ingred.ToBytes(writer);
            writer.Write(Simmering.MeltingPoint);
            writer.Write(Simmering.MeltingDuration);
            writer.Write(Simmering.SmeltedRatio);
            writer.Write((ushort)Simmering.SmeltingType);
            writer.Write(Simmering.SmeltedStack != null);
            Simmering.SmeltedStack?.ToBytes(writer);
            writer.Write(Simmering.RequiresContainer);
        }

        public void FromBytes(BinaryReader reader, IWorldAccessor resolver)
        {
            Code = reader.ReadString();
            Ingredients = new CraftingRecipeIngredient[reader.ReadInt32()];

            for (int i = 0; i < Ingredients.Length; i++)
            {
                Ingredients[i] = new CraftingRecipeIngredient();
                Ingredients[i].FromBytes(reader, resolver);
                Ingredients[i].Resolve(resolver, "Simmer Recipe (FromBytes)");
            }

            Simmering = new CombustibleProperties();
            Simmering.MeltingPoint = reader.ReadInt32();
            Simmering.MeltingDuration = reader.ReadSingle();
            Simmering.SmeltedRatio = reader.ReadInt32();
            Simmering.SmeltingType = (EnumSmeltType)reader.ReadUInt16();
            if (reader.ReadBoolean())
            {
                Simmering.SmeltedStack = new JsonItemStack();
                Simmering.SmeltedStack.FromBytes(reader, resolver.ClassRegistry);
                Simmering.SmeltedStack.Resolve(resolver, "Simmer Recipe (FromBytes)", true);
            }
            Simmering.RequiresContainer = reader.ReadBoolean();
        }

        public SimmerRecipe Clone()
        {
            return new()
            {
                Simmering = Simmering.Clone(),
                Code = Code,
                Enabled = Enabled,
                Name = Name,
                Ingredients = [.. Ingredients.Select(ingred => ingred.Clone())]
            };
        }

        public Dictionary<string, string[]> GetNameToCodeMapping(IWorldAccessor world)
        {
            var mappings = new Dictionary<string, string[]>();

            if (Ingredients == null || Ingredients.Length == 0) return mappings;

            foreach (var ingred in Ingredients)
            {
                if (ingred?.Name == null || !ingred.Code.Path.Contains('*')) continue;

                int wildcardStartLen = ingred.Code.Path.IndexOf('*');
                int wildcardEndLen = ingred.Code.Path.Length - wildcardStartLen - 1;
                IEnumerable<CollectibleObject> collObjs = ingred.Type == EnumItemClass.Block ? world.Blocks : world.Items;

                mappings[ingred.Name] = [.. collObjs.Where(obj => obj.ItemClass == ingred.Type && obj.Code != null && !obj.IsMissing && WildcardUtil.Match(ingred.Code, obj.Code))
                                                    .Select(obj => obj.Code.Path[wildcardStartLen..])
                                                    .Select(code => code[..^wildcardEndLen])
                                                    .Where(codepart => ingred.AllowedVariants?.Contains(codepart) != false)];
            }

            return mappings;
        }
    }

    public class DoughIngredient : IByteSerializable
    {
        public CraftingRecipeIngredient[] Inputs = null!;

        public CraftingRecipeIngredient? GetMatch(ItemStack stack, bool checkStackSize = true)
        {
            if (stack == null) return null;

            foreach (var input in Inputs)
            {
                if (input.SatisfiesAsIngredient(stack, checkStackSize)) return input;
            }

            return null;
        }

        public bool Resolve(IWorldAccessor world, string debug)
        {
            bool ok = true;

            for (int i = 0; i < Inputs.Length; i++)
            {
                ok &= Inputs[i].Resolve(world, debug);
            }

            return ok;
        }

        public void FromBytes(BinaryReader reader, IWorldAccessor resolver)
        {
            Inputs = new CraftingRecipeIngredient[reader.ReadInt32()];

            for (int i = 0; i < Inputs.Length; i++)
            {
                Inputs[i] = new CraftingRecipeIngredient();
                Inputs[i].FromBytes(reader, resolver);
                Inputs[i].Resolve(resolver, "Dough Ingredient (FromBytes)");
            }
        }

        public void ToBytes(BinaryWriter writer)
        {
            writer.Write(Inputs.Length);
            foreach (var input in Inputs) input.ToBytes(writer);
        }

        public DoughIngredient Clone()
        {
            return new() { Inputs = [.. Inputs.Select(input => input.Clone())] };
        }
    }

    public class ACARecipeRegistrySystem : ModSystem
    {
        public List<CookingRecipe> MixingRecipes = [];
        public List<DoughRecipe> DoughRecipes = [];
        public List<SimmerRecipe> SimmerRecipes = [];

        public override double ExecuteOrder()
        {
            return 1.0;
        }

        public override void Start(ICoreAPI api)
        {
            MixingRecipes = api.RegisterRecipeRegistry<RecipeRegistryGeneric<CookingRecipe>>("mixingrecipes").Recipes;
            DoughRecipes = api.RegisterRecipeRegistry<RecipeRegistryGeneric<DoughRecipe>>("doughrecipes").Recipes;
            SimmerRecipes = api.RegisterRecipeRegistry<RecipeRegistryGeneric<SimmerRecipe>>("simmerrecipes").Recipes;
        }

        public override void AssetsLoaded(ICoreAPI api)
        {
            if (api is not ICoreServerAPI coreServerAPI) return;
            loadMixingRecipes(coreServerAPI);
            loadDoughRecipes(coreServerAPI);
            loadSimmerRecipes(coreServerAPI);
        }

        void loadMixingRecipes(ICoreServerAPI coreServerAPI)
        {
            Dictionary<AssetLocation, JToken> files = coreServerAPI.Assets.GetMany<JToken>(coreServerAPI.Server.Logger, "recipes/mixing");

            foreach (var val in files)
            {
                if (val.Value is JObject)
                {
                    CookingRecipe? rec = val.Value.ToObject<CookingRecipe>();
                    if (rec?.Enabled != true) continue;

                    rec.Resolve(coreServerAPI.World, "mixing recipe " + val.Key);
                    RegisterCookingRecipe(rec);
                }
                if (val.Value is JArray jval)
                {
                    foreach (var token in jval)
                    {
                        CookingRecipe? rec = token.ToObject<CookingRecipe>();
                        if (rec?.Enabled != true) continue;

                        rec.Resolve(coreServerAPI.World, "mixing recipe " + val.Key);
                        RegisterCookingRecipe(rec);
                    }
                }
            }

            coreServerAPI.World.Logger.Event("{0} mixing recipes loaded", MixingRecipes.Count);
            coreServerAPI.World.Logger.StoryEvent(Lang.Get("aculinaryartillery:The chef and the apprentice..."));
        }

        void loadDoughRecipes(ICoreServerAPI coreServerAPI)
        {
            Dictionary<AssetLocation, JToken> files = coreServerAPI.Assets.GetMany<JToken>(coreServerAPI.Server.Logger, "recipes/kneading");

            foreach (var val in files)
            {
                if (val.Value is JObject)
                {
                    DoughRecipe? rec = val.Value.ToObject<DoughRecipe>();
                    if (rec?.Enabled != true) continue;

                    LoadKneadingRecipe(val.Key, rec, coreServerAPI);
                }
                if (val.Value is JArray jval)
                {
                    foreach (var token in jval)
                    {
                        DoughRecipe? rec = token.ToObject<DoughRecipe>();
                        if (rec?.Enabled != true) continue;

                        LoadKneadingRecipe(val.Key, rec, coreServerAPI);
                    }
                }
            }

            coreServerAPI.World.Logger.Event("{0} kneading recipes loaded", DoughRecipes.Count);
            coreServerAPI.World.Logger.StoryEvent(Lang.Get("aculinaryartillery:The butter and the bread..."));
        }

        void loadSimmerRecipes(ICoreServerAPI coreServerAPI)
        {
            Dictionary<AssetLocation, JToken> files = coreServerAPI.Assets.GetMany<JToken>(coreServerAPI.Server.Logger, "recipes/simmering");

            foreach (var val in files)
            {
                if (val.Value is JObject)
                {
                    SimmerRecipe? rec = val.Value.ToObject<SimmerRecipe>();
                    if (rec?.Enabled != true) continue;

                    LoadSimmeringRecipe(val.Key, rec, coreServerAPI);
                }
                if (val.Value is JArray jval)
                {
                    foreach (var token in jval)
                    {
                        SimmerRecipe? rec = token.ToObject<SimmerRecipe>();
                        if (rec?.Enabled != true) continue;

                        LoadSimmeringRecipe(val.Key, rec, coreServerAPI);
                    }
                }
            }
            coreServerAPI.World.Logger.Event("{0} simmer recipes loaded", SimmerRecipes.Count);
            coreServerAPI.World.Logger.StoryEvent(Lang.Get("aculinaryartillery:The syrup and lard..."));
        }

        void LoadSimmeringRecipe(AssetLocation path, SimmerRecipe recipe, ICoreServerAPI coreServerAPI)
        {
            if (!recipe.Enabled) return;
            if (recipe.Name == null) recipe.Name = path;

            Dictionary<string, string[]> nameToCodeMapping = recipe.GetNameToCodeMapping(coreServerAPI.World);

            if (nameToCodeMapping.Count > 0)
            {
                int qCombs = 1;
                foreach (var variant in nameToCodeMapping.Values) qCombs *= variant.Length;

                SimmerRecipe[] subRecipes = new SimmerRecipe[qCombs];
                for (int j = 0; j < qCombs; j++) subRecipes[j] = recipe.Clone();
                foreach (var val in nameToCodeMapping)
                {
                    string[] variants = val.Value;

                    for (int i = 0; i < qCombs; i++)
                    {
                        if (subRecipes[i].Ingredients != null)
                        {
                            foreach (var ingred in subRecipes[i].Ingredients)
                            {
                                if (ingred.Name == val.Key)
                                {
                                    ingred.Code = ingred.Code.CopyWithPath(ingred.Code.Path.Replace("*", variants[i % variants.Length]));
                                }
                            }
                        }

                        subRecipes[i].Simmering.SmeltedStack.FillPlaceHolder(val.Key, variants[i % variants.Length]);
                    }
                }

                if (subRecipes.Length == 0)
                {
                    coreServerAPI.World.Logger.Warning($"Simmer recipe file {path} make uses of wildcards, but no blocks or item matching those wildcards were found.");
                }

                foreach (SimmerRecipe subRecipe in subRecipes)
                {
                    if (!subRecipe.Resolve(coreServerAPI.World, "simmer recipe " + path)) continue;
                    RegisterSimmerRecipe(subRecipe);
                }

                return;
            }

            if (!recipe.Resolve(coreServerAPI.World, "simmer recipe " + path)) return;
            RegisterSimmerRecipe(recipe);
        }

        void LoadKneadingRecipe(AssetLocation path, DoughRecipe recipe, ICoreServerAPI coreServerAPI)
        {
            if (!recipe.Enabled) return;
            if (recipe.Name == null) recipe.Name = path;

            Dictionary<string, string[]> nameToCodeMapping = recipe.GetNameToCodeMapping(coreServerAPI.World);

            if (nameToCodeMapping.Count > 0)
            {
                int qCombs = 1;
                foreach (var variant in nameToCodeMapping.Values) qCombs *= variant.Length;

                DoughRecipe[] subRecipes = new DoughRecipe[qCombs];
                for (int j = 0; j < qCombs; j++) subRecipes[j] = recipe.Clone();
                foreach (var val in nameToCodeMapping)
                {
                    string[] variants = val.Value;

                    for (int i = 0; i < qCombs; i++)
                    {
                        if (subRecipes[i].Ingredients != null)
                        {
                            foreach (var ingreds in subRecipes[i].Ingredients)
                            {
                                if (ingreds.Inputs.Length <= 0) continue;
                                CraftingRecipeIngredient ingred = ingreds.Inputs[0];

                                if (ingred.Name == val.Key)
                                {
                                    ingred.Code = ingred.Code.CopyWithPath(ingred.Code.Path.Replace("*", variants[i % variants.Length]));
                                }
                            }
                        }

                        subRecipes[i].Output.FillPlaceHolder(val.Key, variants[i % variants.Length]);
                    }
                }

                if (subRecipes.Length == 0)
                {
                    coreServerAPI.World.Logger.Warning($"Kneading recipe file {path} make uses of wildcards, but no blocks or item matching those wildcards were found.");
                }

                foreach (DoughRecipe subRecipe in subRecipes)
                {
                    if (!subRecipe.Resolve(coreServerAPI.World, "kneading recipe " + path)) continue;
                    RegisterDoughRecipe(subRecipe);
                }

                return;
            }

            if (!recipe.Resolve(coreServerAPI.World, "kneading recipe " + path)) return;
            RegisterDoughRecipe(recipe);
        }

        public void RegisterCookingRecipe(CookingRecipe cookingrecipe)
        {
            MixingRecipes.Add(cookingrecipe);
        }

        public void RegisterDoughRecipe(DoughRecipe doughRecipe)
        {
            DoughRecipes.Add(doughRecipe);
        }

        public void RegisterSimmerRecipe(SimmerRecipe simmerRecipe)
        {
            SimmerRecipes.Add(simmerRecipe);
        }
    }
}
