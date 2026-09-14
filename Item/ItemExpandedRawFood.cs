using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ACulinaryArtillery
{

    public class ItemExpandedRawFood : Item, IExpandedFood, ITexPositionSource, IContainedMeshSource, IBakeableCallback, IHandBookPageCodeProvider
    {
        public Size2i AtlasSize => targetAtlas?.Size ?? throw new Exception("ItemExpandedRawFood has no defined targetAtlas");
        public TextureAtlasPosition this[string textureCode] => GetOrCreateTexPos(GetTexturePath(textureCode));
        protected ITextureAtlasAPI? targetAtlas;
        protected Shape? nowTesselatingShape;

        public override bool Satisfies(ItemStack thisStack, ItemStack otherStack)
        {
            return (thisStack.Class == otherStack.Class && thisStack.Id == otherStack.Id &&
                    !otherStack.Attributes.HasAttribute("madeWith") && thisStack.Attributes.HasAttribute("madeWith"))
                    || base.Satisfies(thisStack, otherStack);
        }

        public float SatMult => Attributes?["satMult"].AsFloat(1f) ?? 1f;

        protected AssetLocation GetTexturePath(string textureCode)
        {
            AssetLocation? texturePath = null;
            CompositeTexture? tex;

            if (Textures.TryGetValue(textureCode, out tex)) texturePath = tex.Baked.BakedName; // Prio 1: Get from collectible textures
            else if (Textures.TryGetValue("all", out tex)) texturePath = tex.Baked.BakedName;  // Prio 2: Get from collectible textures, use "all" code
            else nowTesselatingShape?.Textures.TryGetValue(textureCode, out texturePath);      // Prio 3: Get from currently tesselating shape

            return texturePath ??= new AssetLocation(textureCode);                             // Prio 4: The code is the path
        }

        protected TextureAtlasPosition GetOrCreateTexPos(AssetLocation texturePath)
        {
            if (targetAtlas == null) throw new Exception("ItemExpandedRawFood has no defined targetAtlas");
            TextureAtlasPosition? texpos = targetAtlas[texturePath];
            if (texpos != null) return texpos;

            IAsset? texAsset = (api as ICoreClientAPI)?.Assets.TryGet(texturePath.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
            if (targetAtlas.GetOrInsertTexture(texturePath, out _, out texpos, () => texAsset?.ToBitmap(api as ICoreClientAPI))) return texpos;

            (api as ICoreClientAPI)?.World.Logger.Warning("Item {0} defined texture {1}, but no such texture was found.", Code, texturePath);
            return targetAtlas.UnknownTexturePosition;
        }

        public override void OnCreatedByCrafting(ItemSlot[] allInputslots, ItemSlot outputSlot, IRecipeBase byRecipe)
        {
            base.OnCreatedByCrafting(allInputslots, outputSlot, byRecipe);

            HashSet<string> ingredients = [];
            float[] sat = new float[6];

            foreach (ItemSlot slot in allInputslots)
            {
                if (slot.Itemstack == null) continue;

                IRecipeIngredient? match = byRecipe?.RecipeIngredients?.FirstOrDefault(ing => ing.SatisfiesAsIngredient(slot.Itemstack));

                if (slot.Itemstack.Collectible is ItemExpandedRawFood)
                {
                    string[]? addIngs = (slot.Itemstack.Attributes["madeWith"] as StringArrayAttribute)?.value;
                    float[]? addSat = (slot.Itemstack.Attributes["expandedSats"] as FloatArrayAttribute)?.value;

                    if (addSat?.Length == 6) sat = [.. sat.Zip(addSat, (x, y) => x + (y * match?.Quantity ?? 1))];
                    if (addIngs?.Length > 0) ingredients.AddRange(addIngs);
                }
                else
                {
                    var collObj = slot.Itemstack.Collectible;
                    GetNutrientsFromIngredient(ref sat, collObj, match?.Quantity ?? 1);
                    ingredients.Add(collObj.Code.Domain + ":" + collObj.Code.Path);
                }
            }

            outputSlot.Itemstack.Attributes["madeWith"] = new StringArrayAttribute([.. ingredients.Order()]);
            if (outputSlot.Itemstack.Collectible is not ItemExpandedLiquid)
            {
                sat = Array.ConvertAll(sat, i => i / outputSlot.StackSize);
                outputSlot.Itemstack.Attributes.RemoveAttribute("waterTightContainerProps");
            }

            outputSlot.Itemstack.Attributes["expandedSats"] = new FloatArrayAttribute([.. sat]);
        }

        public void OnCreatedByKneading(Dictionary<ItemSlot, CraftingRecipeIngredient> input, ItemStack output)
        {
            HashSet<string> ingredients = [];
            float[] sat = new float[6];

            foreach (var val in input)
            {
                if (val.Key.Itemstack.Collectible is ItemExpandedRawFood)
                {
                    var stack = val.Key.Itemstack;
                    string[]? addIngs = (stack.Attributes["madeWith"] as StringArrayAttribute)?.value;
                    float[]? addSat = (stack.Attributes["expandedSats"] as FloatArrayAttribute)?.value;

                    if (addSat?.Length == 6) sat = [.. sat.Zip(addSat, (x, y) => x + (y * (val.Value.Quantity / (stack.Collectible is ItemExpandedLiquid ? 10 : 1))))];
                    if (addIngs?.Length > 0) ingredients.AddRange(addIngs);
                }
                else
                {
                    var collObj = val.Key.Itemstack.Collectible;
                    GetNutrientsFromIngredient(ref sat, collObj, val.Value.Quantity);
                    ingredients.Add(collObj.Code.Domain + ":" + collObj.Code.Path);
                }
            }

            output.Attributes["madeWith"] = new StringArrayAttribute([.. ingredients.Order()]);
            if (output.Collectible is not ItemExpandedLiquid)
            {
                sat = Array.ConvertAll(sat, i => i / output.StackSize);
                output.Attributes.RemoveAttribute("waterTightContainerProps");
            }
            output.Attributes["expandedSats"] = new FloatArrayAttribute([.. sat]);
        }
        public void OnCreatedBySimmering(Dictionary<ItemSlot, CraftingRecipeIngredient> input, ItemStack output)
        {
            HashSet<string> ingredients = [];
            float[] sat = new float[6];

            foreach (var val in input)
            {
                if (val.Key.Itemstack.Collectible is ItemExpandedRawFood)
                {
                    var stack = val.Key.Itemstack;
                    string[]? addIngs = (stack.Attributes["madeWith"] as StringArrayAttribute)?.value;
                    float[]? addSat = (stack.Attributes["expandedSats"] as FloatArrayAttribute)?.value;

                    if (addSat?.Length == 6) sat = [.. sat.Zip(addSat, (x, y) => x + (y * (stack.StackSize / (stack.Collectible is ItemExpandedLiquid ? 10 : 1))))];
                    if (addIngs?.Length > 0) ingredients.AddRange(addIngs);
                }
                else
                {
                    var collObj = val.Key.Itemstack.Collectible;
                    GetNutrientsFromIngredient(ref sat, collObj, val.Value.Quantity);
                    ingredients.Add(collObj.Code.Domain + ":" + collObj.Code.Path);
                }
            }

            output.Attributes["madeWith"] = new StringArrayAttribute([.. ingredients.Order()]);
            if (output.Collectible is not ItemExpandedLiquid)
            {
                output.Attributes.RemoveAttribute("waterTightContainerProps");
            }
            output.Attributes["expandedSats"] = new FloatArrayAttribute([.. sat]);
        }

        public override bool CanSmelt(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemStack inputStack, ItemStack outputStack)
        {
            if (inputStack.Collectible.CombustibleProps == null) return false;
            if (outputStack == null) return true;

            if (!inputStack.Collectible.CombustibleProps.SmeltedStack.ResolvedItemstack.Equals(world, outputStack, [.. GlobalConstants.IgnoredStackAttributes, "madeWith", "expandedSats"])) return false;
            if (outputStack.StackSize >= outputStack.Collectible.MaxStackSize) return false;

            if (outputStack.Attributes["madeWith"] == null || !inputStack.Attributes["madeWith"].Equals(world, outputStack.Attributes["madeWith"])) return false;
            if (outputStack.Attributes["expandedSats"] == null || !inputStack.Attributes["expandedSats"].Equals(world, outputStack.Attributes["expandedSats"])) return false;

            return true;
        }

        public override void DoSmelt(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot, ItemSlot outputSlot)
        {
            if (!CanSmelt(world, cookingSlotsProvider, inputSlot.Itemstack, outputSlot.Itemstack)) return;

            ItemStack smeltedStack = inputSlot.Itemstack.Collectible.CombustibleProps.SmeltedStack.ResolvedItemstack.Clone();

            // Copy over spoilage values but reduce them by a bit
            TransitionState? state = UpdateAndGetTransitionState(world, new DummySlot(inputSlot.Itemstack), EnumTransitionType.Perish);

            if (state != null)
            {
                TransitionState smeltedState = smeltedStack.Collectible.UpdateAndGetTransitionState(world, new DummySlot(smeltedStack), EnumTransitionType.Perish);

                float nowTransitionedHours = state.TransitionedHours / (state.TransitionHours + state.FreshHours) * 0.8f * (smeltedState.TransitionHours + smeltedState.FreshHours) - 1;

                smeltedStack.Collectible.SetTransitionState(smeltedStack, EnumTransitionType.Perish, Math.Max(0, nowTransitionedHours));
            }

            string[]? ingredients = (inputSlot.Itemstack.Attributes["madeWith"] as StringArrayAttribute)?.value;
            float[]? satieties = (inputSlot.Itemstack.Attributes["expandedSats"] as FloatArrayAttribute)?.value;


            if (ingredients != null) smeltedStack.Attributes["madeWith"] = new StringArrayAttribute(ingredients);
            if (satieties != null) smeltedStack.Attributes["expandedSats"] = new FloatArrayAttribute(satieties);

            // If the output slot isn't empty use TryMergeStacks to average spoilage rate and temperature
            outputSlot.Itemstack?.Collectible.TryMergeStacks(new(world, EnumMouseButton.Left, 0, EnumMergePriority.ConfirmedMerge, smeltedStack.StackSize)
            {
                SourceSlot = new DummySlot(smeltedStack),
                SinkSlot = new DummySlot(outputSlot.Itemstack)
            });
            outputSlot.Itemstack ??= smeltedStack; // Otherwise put the smelted stack into the output slot

            inputSlot.TakeOut(CombustibleProps.SmeltedRatio);
            outputSlot.MarkDirty();
        }

        public override ItemStack? OnTransitionNow(ItemSlot slot, TransitionableProperties props)
        {
            string[]? ings = (slot.Itemstack.Attributes["madeWith"] as StringArrayAttribute)?.value;
            float[]? xNutr = (slot.Itemstack.Attributes["expandedSats"] as FloatArrayAttribute)?.value;

            ItemStack org = base.OnTransitionNow(slot, props);
            if (org?.Collectible is null or not ItemExpandedRawFood) return org;

            if (ings != null) org.Attributes["madeWith"] = new StringArrayAttribute(ings);
            if (xNutr?.Length > 0) org.Attributes["expandedSats"] = new FloatArrayAttribute(xNutr);

            return org;
        }

        public void GetNutrientsFromIngredient(ref float[] satHolder, CollectibleObject ing, int mult)
        {
            Dictionary<string, FoodNutritionProperties>? expProps = Attributes?["expandedNutritionProps"]?.AsObject<Dictionary<string, FoodNutritionProperties>>();

            FoodNutritionProperties? ingProps = null;
            expProps?.TryGetValue(FindMatch(ing.Code.Domain + ":" + ing.Code.Path, [.. expProps.Keys]), out ingProps);
            ingProps ??= ing.Attributes?["nutritionPropsWhenInMeal"].AsObject<FoodNutritionProperties>();
            ingProps ??= ing.NutritionProps;

            if (ingProps == null) return;

            if (ingProps.Health != 0) satHolder[(int)EnumNutritionMatch.Hp] += ingProps.Health * mult;

            int foodCat = (int)ingProps.FoodCategory + 1;
            if (foodCat >= 1 && foodCat <= 5) satHolder[foodCat] += ingProps.Satiety * mult;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            string[] ings = [.. (inSlot.Itemstack.Attributes["madeWith"] as StringArrayAttribute)?.value
                                .Select(ing => new AssetLocation(ing))?
                                .Select(ing => Lang.GetIfExists("recipeingredient-block-" + ing.Path) ??
                                               Lang.GetIfExists("recipeingredient-item-" + ing.Path))?
                                .Where(ing => ing != null)
                                .Distinct() ?? []];

            if (ings?.Length > 0)
            {
                if (ings.Length == 1) dsc.AppendLine(Lang.Get("efrecipes:Made with ") + ings[0]);
                else dsc.AppendLine(Lang.Get("efrecipes:Made with ") + string.Join(", ", ings.Take(ings.Length - 1)) + Lang.Get("efrecipes:and ") + ings.Last());
            }
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        }

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            RenderAlphaTest = 0.5f;
            string[]? ings = (itemstack.Attributes?["madeWith"] as StringArrayAttribute)?.value;
            if (ings == null || ings.Length <= 0) return;

            Dictionary<string, MultiTextureMeshRef> meshrefs = ObjectCacheUtil.GetOrCreate(capi, "expandedFoodGuiMeshRefs", () => new Dictionary<string, MultiTextureMeshRef>());

            string key = Code.ToShortString() + string.Join("|", ings);
            if (!meshrefs.TryGetValue(key, out var meshref))
            {
                if (GenMesh(capi.ItemTextureAtlas, ings, new Vec3f(0, 0, 0)) is not MeshData mesh) return;
                meshrefs[key] = meshref = capi.Render.UploadMultiTextureMesh(mesh);
            }

            if (meshref != null) renderinfo.ModelRef = meshref;
        }

        public virtual MeshData? GenMesh(ITextureAtlasAPI targetAtlas, string[] ings, Vec3f? rot = null, ITesselatorAPI? tesselator = null)
        {
            if (api is not ICoreClientAPI capi) return null;

            this.targetAtlas = targetAtlas;
            nowTesselatingShape = null;
            tesselator ??= capi.Tesselator;

            TreeAttribute? renderIngs = Attributes?["renderIngredients"].ToAttribute() as TreeAttribute;
            //TreeAttribute? renderIngs = Attributes?["renderIngredients"]?.AsObject<TreeAttribute>();
            if (renderIngs == null) return null;

            List<AssetLocation> addShapes = [];
            List<Dictionary<string, string>> texureMappingsPerShape = [];
            for (int i = 0; i < ings.Length; i++)
            {
                Dictionary<string, string> textureMap = [];
                string match = FindMatch(ings[i], [.. renderIngs.Keys]);
                string path = renderIngs.GetAsString(match);
                if (renderIngs.GetTreeAttribute(match) is TreeAttribute keyValuePairs)
                {
                    string wildCard = WildcardUtil.GetWildcardValue(new AssetLocation(match), new AssetLocation(ings[i]));
                    path = keyValuePairs.GetAsString("shape");
                    TreeAttribute? textureMappings = keyValuePairs.GetTreeAttribute("textureMap") as TreeAttribute;
                    if (textureMappings?.Keys is string[] texKeys)
                    {
                        foreach (var key in texKeys)
                        {
                            textureMap[key] = textureMappings.GetString(key).Replace("{" + keyValuePairs.GetAsString("name") + "}", wildCard);
                        }
                    }
                }

                if (path == null) continue;
                AssetLocation shape = new AssetLocation(path);
                if (shape != null)
                {
                    addShapes.Add(shape);
                    texureMappingsPerShape.Add(textureMap);
                }
            }

            if (addShapes.Count <= 0) return null;

            // Render first added ingredient before everything else to avoid transparent bread
            AssetLocation baseIngredient = addShapes.Last();
            Dictionary<string, string> baseMapping = texureMappingsPerShape.Last();

            MeshData? mesh = null;
            float uvoffset = 0;
            for (int i = 0; i < addShapes.Count; i++)
            {
                if (!addShapes[i].Valid) continue;
                Shape? addShape = capi.Assets.TryGet(addShapes[i]).ToObject<Shape>();
                if (addShape == null) continue;

                Shape clonedAddShape = addShape.Clone();
                if (addShape.Textures != null) clonedAddShape.Textures = new(addShape.Textures);

                MeshData addIng;
                if (addShape.Textures?.Keys is not null && texureMappingsPerShape[i].Count > 0)
                {
                    foreach (var key in texureMappingsPerShape[i].Keys)
                    {
                        string clonedKey = key;
                        if (clonedAddShape.Textures.ContainsKey(key)) clonedKey = texureMappingsPerShape[i][key];
                        clonedAddShape.Textures[key] = GetTexturePath(clonedKey); // path to desired texture
                    }

                    ShapeTextureSource textureSource = new(capi, clonedAddShape, addShapes[i].ToString());

                    if (addShapes.Where(x => x != null && x == addShapes[i]).Count() > 1)
                    {
                        int texHeight = clonedAddShape.TextureHeight;
                        int texWidth = clonedAddShape.TextureWidth;
                        foreach (ShapeElement elm in clonedAddShape.Elements)
                        {
                            foreach (ShapeElementFace face in elm.FacesResolved)
                            {
                                if (face != null)
                                {
                                    float faceWidth = Math.Abs(face.Uv[2] - face.Uv[0]);
                                    float faceHeight = Math.Abs(face.Uv[3] - face.Uv[1]);
                                    float ustart = (float)Math.Floor(uvoffset * (texWidth - faceWidth));
                                    float vstart = (float)Math.Floor(uvoffset * (texHeight - faceHeight));
                                    face.Uv[0] = ustart;
                                    face.Uv[1] = vstart;
                                    face.Uv[2] = ustart + faceWidth;
                                    face.Uv[3] = vstart + faceHeight;
                                }
                            }
                        }
                        uvoffset += 0.0625f;
                    }

                    tesselator.TesselateShape("ACA", clonedAddShape, out addIng, textureSource, rot);
                }
                else tesselator.TesselateShape("ACA", clonedAddShape, out addIng, this, rot);

                mesh?.AddMeshData(addIng);
                mesh ??= addIng;
            }

            mesh?.RenderPassesAndExtraBits.Fill((short)EnumChunkRenderPass.BlendNoCull);
            return mesh;
        }

        public virtual MeshData? GenMesh(ItemSlot slot, ITextureAtlasAPI targetAtlas, BlockPos? atBlockPos = null)
        {
            if (api is not ICoreClientAPI capi) return null;

            ItemStack stack = slot.Itemstack;
            this.targetAtlas = targetAtlas;
            nowTesselatingShape = null;
            var be = api.World.BlockAccessor.GetBlockEntity(atBlockPos);

            string[]? ings = (stack.Attributes?["madeWith"] as StringArrayAttribute)?.value;
            if (ings?.Length > 0) return GenMesh(targetAtlas, ings, new Vec3f(0, be.Block.Shape.rotateY, 0), capi.Tesselator);

            if (stack.Item?.Shape?.Base is AssetLocation loc) nowTesselatingShape = (api as ICoreClientAPI)?.TesselatorManager.GetCachedShape(loc);
            capi.Tesselator.TesselateItem(stack.Item, out MeshData mesh, this);
            mesh.RenderPassesAndExtraBits.Fill((short)EnumChunkRenderPass.BlendNoCull);
            return mesh;
        }

        public string GetMeshCacheKey(ItemSlot slot)
        {
            ItemStack stack = slot.Itemstack;
            string[]? ings = (stack.Attributes?["madeWith"] as StringArrayAttribute)?.value;
            if (ings == null) return Code.ToShortString();

            return Code.ToShortString() + string.Join(",", ings);
        }

        public MeshData? GenMesh(ICoreClientAPI capi, string[] ings, ItemStack stack, Vec3f? rot = null, ITesselatorAPI? tesselator = null)
        {
            tesselator ??= capi.Tesselator;

            TreeAttribute? renderIngs = Attributes?["renderIngredients"]?.AsObject<TreeAttribute>();
            if (renderIngs == null) return null;

            List<AssetLocation> addShapes = [];
            for (int i = 0; i < ings.Length; i++)
            {
                if (renderIngs.GetAsString(FindMatch(ings[i], [.. renderIngs.Keys])) is not string path) continue;
                AssetLocation shape = new AssetLocation(path);
                if (shape != null) addShapes.Add(shape);
            }

            if (addShapes.Count <= 0) return new MeshData();

            MeshData? mesh = null;
            for (int i = 0; i < addShapes.Count; i++)
            {
                Shape addShape;
                if (!addShapes[i].Valid || (addShape = capi.Assets.TryGet(addShapes[i]).ToObject<Shape>()) == null)
                    continue;

                if (addShape.Textures != null)
                {
                    if (stack.Item.Textures.TryGetValue(addShape.Textures.Keys.First(), out CompositeTexture? tex2))
                    {
                        tesselator.TesselateShape("expandedfood", addShape, out var addIng, new EFTextureSource(capi, stack, tex2), rot);
                        mesh?.AddMeshData(addIng);
                        mesh ??= addIng;
                    }
                }
            }

            return mesh;
        }

        public FoodNutritionProperties[]? GetPropsFromArray(float[]? satieties)
        {
            if (satieties == null || satieties.Length < 6) return null;

            List<FoodNutritionProperties> props = [];
            for (int i = 1; i <= 5; i++)
            {
                if (satieties[i] != 0) props.Add(new() { FoodCategory = (EnumFoodCategory)(i - 1), Satiety = satieties[i] * SatMult });
            }

            if (satieties[0] != 0 && props.Count > 0) props[0].Health = satieties[0] * SatMult;

            return [.. props];
        }

        public string FindMatch(string needle, string[] haystack)
        {
            if (needle != null && haystack?.Length > 0)
            {
                foreach (string hay in haystack) if (hay == needle || WildcardUtil.Match(hay, needle)) return hay;
            }

            return "";
        }

        // Get Nutrition Properties for a SINGLE STACK
        // SPANG - March 13, 2022
        public static FoodNutritionProperties[] GetExpandedContentNutritionProperties(IWorldAccessor world, ItemSlot inSlot, ItemStack contentStack, EntityAgent? forEntity, bool mulWithStacksize = false, float nutritionMul = 1f, float healthMul = 1f)
        {
            List<FoodNutritionProperties> foodProps = [];

            CollectibleObject obj = contentStack.Collectible;
            CombustibleProperties? cprops = obj.GetCombustibleProperties(world, contentStack, null);
            WaterTightContainableProps? liquidProps = BlockLiquidContainerBase.GetContainableProps(contentStack);
            FoodNutritionProperties? nutriProps;

            nutriProps = obj.Attributes?["nutritionPropsWhenInMeal"].AsObject<FoodNutritionProperties>();
            nutriProps ??= cprops?.SmeltedStack?.ResolvedItemstack?.Collectible.GetNutritionProperties(world, cprops.SmeltedStack.ResolvedItemstack, forEntity);
            nutriProps ??= liquidProps?.NutritionPropsPerLitreWhenInMeal;

            if (liquidProps != null && nutriProps == null)
            {
                nutriProps = liquidProps.NutritionPropsPerLitre?.Clone();
                if (nutriProps != null)
                {
                    nutriProps.Satiety *= 1.3f;
                    float litre = contentStack.StackSize / liquidProps.ItemsPerLitre;
                    nutriProps.Health *= litre;
                    nutriProps.Satiety *= litre;
                    nutriProps.Intoxication *= litre;
                    nutriProps.Psychedelic *= litre;
                }
            }

            nutriProps ??= obj.GetNutritionProperties(world, contentStack, forEntity);

            float satLossMul = 1.0f;
            float healthLoss = 1.0f;
            float mul = contentStack.StackSize;
            // Create a stack that we can test for the proper amount of nutrition regardless of the actual stack size in the container
            ItemStack nutriStack = contentStack.Clone();
            nutriStack.StackSize = 1;
            if (!mulWithStacksize)
            {
                nutriStack.StackSize = (int)(BlockLiquidContainerBase.GetContainableProps(nutriStack)?.ItemsPerLitre ?? 1);
                var cookingIngreds = world.Api.GetCookingRecipe(inSlot.Itemstack!.Attributes.GetString("recipeCode"))?.Ingredients?.Select(ingred => ingred.Clone()).ToList();
                if (cookingIngreds?.FirstOrDefault(ing => ing.Matches(nutriStack)) is CookingRecipeIngredient ingred)
                {
                    mul = ingred.GetMatchingStack(nutriStack)?.StackSize ?? 1;
                    nutriStack.StackSize = (int)(nutriStack.StackSize * ingred.PortionSizeLitres);
                }
                else mul = 1;
            }

            if (BlockMeal.GetIngredientStackNutritionProperties(world, nutriStack, forEntity) is not FoodNutritionProperties stackProps) return [];

            nutriProps = stackProps.Clone();

            if (obj is ItemExpandedRawFood && (contentStack.Attributes["expandedSats"] as FloatArrayAttribute)?.value?.Length == 6)
            {
                FoodNutritionProperties[]? exProps = (obj as ItemExpandedRawFood)?.GetPropsFromArray((contentStack.Attributes["expandedSats"] as FloatArrayAttribute)?.value);

                if (exProps?.Length > 0)
                {
                    foreach (FoodNutritionProperties exProp in exProps)
                    {
                        exProp.Satiety *= satLossMul * nutritionMul * mul;
                        exProp.Health *= healthLoss * healthMul * mul;
                        exProp.Intoxication *= mul;
                        exProp.Psychedelic *= mul;

                        foodProps.Add(exProp);
                    }
                }
                if (nutriProps != null)
                {
                    FoodNutritionProperties props = nutriProps.Clone();
                    props.Satiety *= satLossMul * nutritionMul * mul;
                    props.Health *= healthLoss * healthMul * mul;
                    props.Intoxication *= mul;
                    props.Psychedelic *= mul;
                    foodProps.Add(props);
                }
            }
            else if (nutriProps != null)
            {
                FoodNutritionProperties props = nutriProps.Clone();

                DummySlot slot = new DummySlot(contentStack, inSlot.Inventory);
                TransitionState state = contentStack.Collectible.UpdateAndGetTransitionState(world, slot, EnumTransitionType.Perish);
                float spoilState = state != null ? state.TransitionLevel : 0;

                satLossMul = GlobalConstants.FoodSpoilageSatLossMul(spoilState, slot.Itemstack, forEntity);
                healthLoss = GlobalConstants.FoodSpoilageHealthLossMul(spoilState, slot.Itemstack, forEntity);
                props.Satiety *= satLossMul * nutritionMul * mul;
                props.Health *= healthLoss * healthMul * mul;
                props.Intoxication *= mul;
                props.Psychedelic *= mul;
                foodProps.Add(props);
            }
            return [.. foodProps];
        }

        public void OnBaked(ItemStack oldStack, ItemStack newStack)
        {
            string[]? ings = (oldStack?.Attributes["madeWith"] as StringArrayAttribute)?.value;
            float[]? sats = (oldStack?.Attributes["expandedSats"] as FloatArrayAttribute)?.value;
            if (ings != null) newStack.Attributes["madeWith"] = new StringArrayAttribute(ings);
            if (sats != null) newStack.Attributes["expandedSats"] = new FloatArrayAttribute(sats);
        }
        public void OnCreatedByGrinding(ItemStack input, ItemStack output)
        {
            string[]? ings = (input?.Attributes["madeWith"] as StringArrayAttribute)?.value;
            float[]? sats = (input?.Attributes["expandedSats"] as FloatArrayAttribute)?.value;
            if (ings != null) output.Attributes["madeWith"] = new StringArrayAttribute(ings);
            if (sats != null) output.Attributes["expandedSats"] = new FloatArrayAttribute(Array.ConvertAll(sats, i => i / output.StackSize));
        }

        public string HandbookPageCodeForStack(IWorldAccessor world, ItemStack stack)
        {
            return stack.Class.Name() + "-" + stack.Collectible.Code.ToShortString();
        }
    }

    public interface IExpandedFood
    {
        void OnCreatedByKneading(Dictionary<ItemSlot, CraftingRecipeIngredient> input, ItemStack output);
        void OnCreatedBySimmering(Dictionary<ItemSlot, CraftingRecipeIngredient> input, ItemStack output);
        void OnCreatedByGrinding(ItemStack input, ItemStack output);
    }

    public enum EnumNutritionMatch
    {
        Hp,
        Fruit,
        Grain,
        Vegetable,
        Protein,
        Dairy
    }

    /*************************************************************************************************************/
    public class EFTextureSource : ITexPositionSource
    {
        public ItemStack forContents;
        private readonly ICoreClientAPI capi;
        private TextureAtlasPosition? contentTextPos;
        private readonly CompositeTexture contentTexture;

        public EFTextureSource(ICoreClientAPI capi, ItemStack forContents, CompositeTexture contentTexture)
        {
            this.capi = capi;
            this.forContents = forContents;
            this.contentTexture = contentTexture;
        }

        public Size2i AtlasSize => capi.BlockTextureAtlas.Size;
        public virtual TextureAtlasPosition this[string textureCode]
        {
            get
            {
                if (contentTextPos == null)
                {
                    int textureSubId;
                    textureSubId = ObjectCacheUtil.GetOrCreate(capi, "efcontenttexture-" + contentTexture.ToString(), () =>
                    {
                        var id = 0;
                        var bmp = capi.Assets.TryGet(contentTexture.Base.CopyWithPathPrefixAndAppendixOnce("textures/", ".png"))?.ToBitmap(capi);

                        if (bmp != null)
                        {
                            if (contentTexture != null && contentTexture.Alpha != 255)
                            {
                                bmp.MulAlpha(contentTexture.Alpha);
                            }

                            capi.BlockTextureAtlas.InsertTexture(bmp, out id, out var texPos);
                            bmp.Dispose();
                        }
                        return id;
                    });
                    contentTextPos = capi.BlockTextureAtlas.Positions[textureSubId];
                }
                return contentTextPos;
            }
        }
    }
}
