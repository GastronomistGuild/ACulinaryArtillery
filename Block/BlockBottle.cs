using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ACulinaryArtillery
{
    public class BlockBottle : BlockLiquidContainerBase, IContainedMeshSource, IContainedCustomName, IAttachableToEntity
    {
        private LiquidTopOpenContainerProps props = new();
        protected virtual string MeshRefsCacheKey => Code.ToShortString() + "meshRefs";
        protected virtual AssetLocation ContentShapeLoc => props.OpaqueContentShapeLoc;
        protected virtual AssetLocation LiquidContentShapeLoc => props.LiquidContentShapeLoc;
        public override float TransferSizeLitres => props.TransferSizeLitres;
        public override float CapacityLitres => props.CapacityLitres;
        public override bool CanDrinkFrom => Attributes["canDrinkFrom"].AsBool(true);
        public override bool IsTopOpened => Attributes["isTopOpened"].AsBool(true);
        public override bool AllowHeldLiquidTransfer => Attributes["allowHeldLiquidTransfer"].AsBool(true);
        protected virtual bool IsClear => Attributes["isClear"].AsBool();
        public virtual float MinFillY => Attributes["minFill"].AsFloat();
        public virtual float MaxFillY => Attributes["maxFill"].AsFloat();
        public virtual float MinFillZ => Attributes["minFillSideways"].AsFloat();
        public virtual float MaxFillZ => Attributes["maxFillSideways"].AsFloat();


        public static TagSet bottleStopperTag = TagSet.Empty;
        public static TagSet bottleSealantTag = TagSet.Empty;


        public virtual ItemStack DefaultStopper => new(api.World.GetItem("aculinaryartillery:stopper-bark-cork"));
        public bool HasTransparentStopper(ItemStack stack) => GetStopper(stack)?.ItemAttributes?["isTransparent"].AsBool() == true;
        public virtual ItemStack DefaultSealant => new(api.World.GetItem("game:beeswax"));


        public static ItemStack[] stopperStacks = null!;
        public static ItemStack[] sealantStacks = null!;


        protected virtual AssetLocation EmptyShapeLoc(ItemStack stack)
        {
            if (HasTransparentStopper(stack))
            {
                return stack.ItemAttributes["transparentStopperShapeLoc"].AsString("missing-transparentStopperShapeLoc");
            }

            return props.EmptyShapeLoc ?? Shape.Base;
        }

        public override byte[]? GetLightHsv(IBlockAccessor blockAccessor, BlockPos pos, ItemStack? stack = null)
        {
            return GetContent(stack)?.Item?.LightHsv ?? base.GetLightHsv(blockAccessor, pos, stack);
        }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            attrAtta = IAttachableToEntity.FromAttributes(this);
            props = Attributes?["liquidContainerProps"]?.AsObject(props, Code.Domain) ?? props;
            drinkPortionSizeFromAttributes = Attributes?["drinkPortionSize"].AsFloat(0.25f) ?? 0.25f; //base game reads this as an integer

            ReadOnlySpan<string> stopperTag = ["bottle-stopper"];
            if (bottleSealantTag.IsEmpty) api.CollectibleTagRegistry.TryCreateTagSet(out bottleStopperTag, stopperTag);

            ReadOnlySpan<string> sealantTag = ["bottle-sealant"];
            if (bottleSealantTag.IsEmpty) api.CollectibleTagRegistry.TryCreateTagSet(out bottleSealantTag, sealantTag);

            if (api.Side is EnumAppSide.Client && stopperStacks == null)
            {
                List<ItemStack> stopperStacks = [];
                foreach (CollectibleObject obj in api.World.Collectibles)
                {
                    if (obj.StorageFlags.HasFlag(EnumItemStorageFlags.Offhand) && obj.GetTags(new ItemStack(obj)).Overlaps(bottleStopperTag))
                    {
                        stopperStacks.Add(new ItemStack(obj));
                    }
                }
                BlockBottle.stopperStacks = [.. stopperStacks];
            }

            if (api.Side is EnumAppSide.Client && sealantStacks == null)
            {
                List<ItemStack> sealantStacks = [];
                foreach (CollectibleObject obj in api.World.Collectibles)
                {
                    if (obj.StorageFlags.HasFlag(EnumItemStorageFlags.Offhand) && obj.GetTags(new ItemStack(obj)).Overlaps(bottleSealantTag))
                    {
                        sealantStacks.Add(new ItemStack(obj));
                    }
                }
                BlockBottle.sealantStacks = [.. sealantStacks];
            }
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            if (api is not ICoreClientAPI capi) return;

            if (capi.ObjectCache.TryGetValue(MeshRefsCacheKey, out var obj))
            {
                foreach (var val in obj as Dictionary<int, MultiTextureMeshRef> ?? []) val.Value.Dispose();

                capi.ObjectCache.Remove(MeshRefsCacheKey);
            }
        }

        #region Rendering

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            Dictionary<int, MultiTextureMeshRef> meshrefs;
            if (capi.ObjectCache.TryGetValue(MeshRefsCacheKey, out var obj))
            {
                meshrefs = obj as Dictionary<int, MultiTextureMeshRef> ?? [];
            }
            else
            {
                capi.ObjectCache[MeshRefsCacheKey] = meshrefs = [];
            }

            string key = itemstack.Collectible.Code.ToShortString();

            ItemStack? contentStack = GetContent(itemstack);
            if (contentStack != null)
            {
                key += "-" + contentStack?.StackSize + "x" + contentStack?.Collectible.Code.ToShortString();
            }

            ItemStack? stopperStack = GetStopper(itemstack);
            if (stopperStack != null)
            {
                key += "-" + stopperStack.Collectible.Code;
            }

            int hashcode = key.GetHashCode();
            if (!meshrefs.TryGetValue(hashcode, out var meshRef))
            {
                meshrefs[hashcode] = meshRef = capi.Render.UploadMultiTextureMesh(GenMesh(capi, itemstack));
            }

            renderinfo.ModelRef = meshRef;
        }

        public MeshData? GenMesh(ICoreClientAPI? capi, ItemStack stack, bool isSideways = false, BlockPos? atBlockPos = null)
        {
            if (capi?.Assets.TryGet(EmptyShapeLoc(stack).CopyWithPathPrefixAndAppendixOnce("shapes/", ".json")) is not IAsset asset) return new MeshData();

            ItemStack? stopper = GetStopper(stack);
            CompositeTexture? stopperTexture = stopper?.ItemAttributes?["bottleStopperTexture"]?.AsObject<CompositeTexture>() ?? stopper?.Item.FirstTexture;
            ItemStack? sealant = GetSealant(stack);
            CompositeTexture? sealantTexture = sealant?.ItemAttributes?["bottleSealantTexture"]?.AsObject<CompositeTexture>() ?? sealant?.Item.FirstTexture;

            capi.Tesselator.TesselateShape("bottle", asset.ToObject<Shape>(), out var mesh, new BottleTextureSource(capi, stack, stopperTexture, sealantTexture, null), new Vec3f(Shape.rotateX, Shape.rotateY, Shape.rotateZ));

            if (GetContent(stack) is ItemStack contentStack && (IsClear || IsTopOpened))
            {
                if (GetContainableProps(contentStack) is not WaterTightContainableProps props)
                {
                    ACulinaryArtillery.logger?.Error($"Bottle content stack {contentStack.Item.Code} does not have waterTightContainerProps and will not render any liquid.");
                    return mesh;
                }

                float fullness = contentStack.StackSize / props.ItemsPerLitre;
                Shape? shape = capi.Assets.TryGet((props.IsOpaque ? ContentShapeLoc : LiquidContentShapeLoc).CopyWithPathPrefixAndAppendixOnce("shapes/", ".json"))?.ToObject<Shape>();
                if (shape == null) return mesh;
                shape = SliceFlattenedShape(shape.FlattenElementHierarchy(), fullness, isSideways);

                MeshData bottleMesh = mesh;
                capi.Tesselator.TesselateShape("bottle contents", shape, out mesh, new BottleTextureSource(capi, stack, stopperTexture, sealantTexture, props.Texture), new Vec3f(Shape.rotateX, Shape.rotateY, Shape.rotateZ));
                for (int i = 0; i < mesh.Flags.Length; i++) mesh.Flags[i] = mesh.Flags[i] & ~(1 << 12); // Remove water waving flag

                mesh.AddMeshData(bottleMesh);

                // Water flags
                if (atBlockPos != null)
                {
                    mesh.CustomInts = new CustomMeshDataPartInt(mesh.FlagsCount) { Count = mesh.FlagsCount };
                    mesh.CustomInts.Values.Fill(0x4000000); // light foam only
                    mesh.CustomFloats = new CustomMeshDataPartFloat(mesh.FlagsCount * 2) { Count = mesh.FlagsCount * 2 };
                }
            }
            return mesh;
        }

        // Works only if the shape hierarchy has been flattened, it must not have any element with children - Thanks for the code, Jayu!
        public Shape SliceFlattenedShape(Shape fullShape, float fullness, bool isSideways)
        {
            int axis = isSideways ? 2 : 1;
            var min = isSideways ? MinFillZ : MinFillY;
            var max = isSideways ? MaxFillZ : MaxFillY;

            var newMax = min + (max - min) * fullness;
            var newElements = new List<ShapeElement>();

            double elementMin, elementMax, adjustedFrom, adjustedTo;
            double originalHeight, newHeight, heightProportion;
            double vMin, vMax, vRange;
            foreach (var element in fullShape.Elements)
            {
                elementMin = Math.Min(element.From[axis], element.To[axis]);
                elementMax = Math.Max(element.From[axis], element.To[axis]);

                if (elementMax < min || elementMin > newMax) continue;

                var newElement = element.Clone();
                adjustedFrom = Math.Max(element.From[axis], 0);
                adjustedTo = Math.Min(element.To[axis], newMax);
                if (!(adjustedFrom <= adjustedTo)) continue;
                newElement.From[axis] = adjustedFrom;
                newElement.To[axis] = adjustedTo;

                // Calculate the proportion of the adjustment
                originalHeight = elementMax - elementMin;
                newHeight = adjustedTo - adjustedFrom;
                heightProportion = originalHeight > 0 ? newHeight / originalHeight : 0;

                for (var i = 0; i < 4; i++)
                {
                    var face = newElement.FacesResolved[i];
                    if (face != null)
                    {
                        vMin = face.Uv[1];
                        vMax = face.Uv[3];
                        vRange = vMax - vMin;

                        // Adjust the V values based on the height proportion
                        face.Uv[1] = (float)(vMin + vRange * (1 - heightProportion));
                        face.Uv[3] = (float)vMax;
                    }
                }
                if (isSideways)
                {
                    newElement.RotationOrigin = [8.0, 0.2, 8.0];
                    newElement.RotationY = 180;
                }
                newElements.Add(newElement);
            }

            var partialShape = fullShape.Clone();
            partialShape.Elements = [.. newElements];
            return partialShape;
        }

        public MeshData? GenMesh(ItemSlot slot, ITextureAtlasAPI targetAtlas, BlockPos? atBlockPos = null)
        {
            if (slot.Itemstack is not ItemStack stack) return new MeshData();

            bool isSideways = atBlockPos != null && GetBlockEntity<BlockEntityBottleRack>(atBlockPos) != null;
            return GenMesh(api as ICoreClientAPI, stack, isSideways, atBlockPos);
        }

        public string GetMeshCacheKey(ItemSlot slot)
        {
            if (slot.Itemstack is not ItemStack stack) return "";

            ItemStack? contentStack = GetContent(stack);
            string key = stack.Collectible.Code.ToShortString();

            if (contentStack != null)
            {
                key += "-" + contentStack?.StackSize + "x" + contentStack?.Collectible.Code.ToShortString();
            }

            ItemStack? stopperStack = GetStopper(stack);
            if (stopperStack != null)
            {
                key += "-" + stopperStack.Collectible.Code;
            }

            return key;
        }

        #endregion

        #region IAttachableToEntity

        protected IAttachableToEntity? attrAtta;
        public int RequiresBehindSlots { get; set; } = 0;

        public bool IsAttachable(Entity toEntity, ItemStack stack)
        {
            if (!HasTransparentStopper(stack)) return true;

            (api as ICoreClientAPI)?.TriggerIngameError(this, "notransparentstopper", Lang.Get("aculinaryartillery:mountfailure-transparentstopper"));
            return false;
        }

        public void CollectTextures(ItemStack stack, Shape shape, string texturePrefixCode, Dictionary<string, CompositeTexture> intoDict)
        {
            attrAtta?.CollectTextures(stack, shape, texturePrefixCode, intoDict);

            if (GetStopper(stack)?.Item is Item stopper)
            {
                shape.Textures["stopper"] = new AssetLocation(stopper.Attributes?["bottleStopperTexture"]?.AsObject<CompositeTexture>()?.Base ?? stopper.FirstTexture.Base);
            }

            if (GetSealant(stack)?.Item is Item sealant)
            {
                shape.Textures["wax"] = new AssetLocation(sealant.Attributes?["bottleSealantTexture"]?.AsObject<CompositeTexture>()?.Base ?? sealant.FirstTexture.Base);
            }
        }

        public CompositeShape? GetAttachedShape(ItemStack stack, string slotCode)
        {
            if (attrAtta?.GetAttachedShape(stack, slotCode) is not CompositeShape baseShape)
            {
                return new();
            }

            CompositeShape shape = baseShape.Clone();

            // This is currently broken. The stopper creates a transparent viewport through the entire mount shape.
            // Current solution is to simply disallow transparent stoppers on mounts.
            if (HasTransparentStopper(stack))
            {
                shape.Base = shape.Base.WithPathAppendixOnce("-transparentstopper");
            }

            return shape;
        }

        public string? GetTexturePrefixCode(ItemStack stack)
        {
            string? code = attrAtta?.GetTexturePrefixCode(stack);
            if (code != null)
            {
                code += stack.Collectible.Code;
            }

            if (code != null && GetStopper(stack)?.Item is Item stopper)
            {
                code += "-" + stopper.Code;
            }

            if (code != null && GetSealant(stack)?.Item is Item sealant)
            {
                code += "-" + sealant.Code;
            }

            return code;
        }

        public string? GetCategoryCode(ItemStack stack) => attrAtta?.GetCategoryCode(stack);

        public string[]? GetDisableElements(ItemStack stack) => attrAtta?.GetDisableElements(stack);
        public string[]? GetKeepElements(ItemStack stack) => attrAtta?.GetKeepElements(stack);

        #endregion

        public ItemStack? GetStopper(ItemStack bottleStack)
        {
            if (bottleStack.Collectible.LastCodePart() != "corked" && bottleStack.Collectible.LastCodePart() != "waxed") return null;

            if (GetContents(api.World, bottleStack) is ItemStack[] contentStacks)
            {
                return contentStacks.ElementAtOrDefault(1) ?? DefaultStopper;
            }

            return DefaultStopper;
        }

        public void SetStopper(ItemStack bottleStack, ItemStack? stopperStack)
        {
            List<ItemStack?> contentStacks = [.. GetContents(api.World, bottleStack)];
            while (contentStacks.Count() < 2) contentStacks.Add(null);
            contentStacks[1] = stopperStack;
            SetContents(bottleStack, [.. contentStacks]);
        }

        public ItemStack? GetSealant(ItemStack bottleStack)
        {
            if (bottleStack.Collectible.LastCodePart() != "waxed") return null;

            if (GetContents(api.World, bottleStack) is ItemStack[] contentStacks)
            {
                return contentStacks.ElementAtOrDefault(2) ?? DefaultSealant;
            }

            return DefaultSealant;
        }

        public void SetSealant(ItemStack bottleStack, ItemStack? sealantStack)
        {
            List<ItemStack?> contentStacks = [.. GetContents(api.World, bottleStack)];
            while (contentStacks.Count() < 3) contentStacks.Add(null);
            contentStacks[2] = sealantStack;
            SetContents(bottleStack, [.. contentStacks]);
        }

        public string GetContainedInfo(ItemSlot inSlot)
        {
            float litres = GetCurrentLitres(inSlot.Itemstack);
            ItemStack? contentStack = GetContent(inSlot.Itemstack);

            if (contentStack == null || litres <= 0) return Lang.GetWithFallback("contained-empty-container", "{0} (Empty)", inSlot.Itemstack.GetName());

            string incontainername = Lang.Get(contentStack.Collectible.Code.Domain + ":incontainer-" + contentStack.Class.ToString().ToLowerInvariant() + "-" + contentStack.Collectible.Code.Path);

            return Lang.Get("contained-liquidcontainer-compact", inSlot.Itemstack.GetName(), litres, incontainername, PerishableInfoCompactContainer(api, inSlot));
        }


        public string GetContainedName(ItemSlot inSlot, int quantity)
        {
            return inSlot.Itemstack?.GetName() ?? "";
        }

        public void PlayCorkingSound(ICoreServerAPI? sapi, ItemStack? bottle, Entity entity, bool isUncorking = false)
        {
            if (sapi == null || bottle == null) return;

            float fullness = 0;
            if (GetContent(bottle) is ItemStack contentStack
                && GetContainableProps(contentStack) is WaterTightContainableProps liquidProps)
            {
                fullness = contentStack.StackSize / liquidProps.ItemsPerLitre;
            }

            string suffix = fullness switch
            {
                0 => "empty",
                <= 0.5f => "partial",
                _ => "full"
            };
            AssetLocation sound = new($"aculinaryartillery:sounds/player/bottle/{(isUncorking ? "uncork" : "cork")}{suffix}*");
            sapi.World.PlaySoundAt(sound, entity);
        }

        public override int GetMergableQuantity(ItemStack sinkStack, ItemStack sourceStack, EnumMergePriority priority)
        {
            bool isStopping = sourceStack.Collectible.GetTags(sourceStack).Overlaps(bottleStopperTag) && Variant["type"] == "fired";
            bool isSealing = sourceStack.Collectible.GetTags(sourceStack).Overlaps(bottleSealantTag) && Variant["type"] == "corked";

            if (priority == EnumMergePriority.DirectMerge && (isStopping || isSealing))
            {
                return 1;
            }

            return base.GetMergableQuantity(sinkStack, sourceStack, priority);
        }

        public override void TryMergeStacks(ItemStackMergeOperation op)
        {
            ItemSlot sourceSlot = op.SourceSlot;
            ItemSlot sinkSlot = op.SinkSlot;

            bool isStopping = sourceSlot.Itemstack?.Collectible.GetTags(sourceSlot.Itemstack).Overlaps(bottleStopperTag) == true && Variant["type"] == "fired";
            bool isSealing = sourceSlot.Itemstack?.Collectible.GetTags(sourceSlot.Itemstack).Overlaps(bottleSealantTag) == true && Variant["type"] == "corked";

            if (isStopping && op.CurrentPriority == EnumMergePriority.DirectMerge && sinkSlot.Itemstack != null && sourceSlot.Itemstack != null)
            {
                ItemStack stopperedBottle = new(op.World.GetBlock(sinkSlot.Itemstack.Collectible.CodeWithVariant("type", "corked"))) { Attributes = sinkSlot.Itemstack.Attributes };
                ItemStack stopper = sourceSlot.Itemstack.Clone();
                SetStopper(stopperedBottle, stopper);

                if (sinkSlot.StackSize == 1)
                {
                    sinkSlot.Itemstack = stopperedBottle;
                }
                else
                {
                    sinkSlot.TakeOut(1);
                    if (!op.ActingPlayer.InventoryManager.TryGiveItemstack(stopperedBottle, true))
                    {
                        op.World.SpawnItemEntity(stopperedBottle, op.ActingPlayer.Entity.Pos.AsBlockPos);
                    }
                }
                op.MovedQuantity = 1;
                sourceSlot.TakeOut(1);
                sinkSlot.MarkDirty();

                PlayCorkingSound(api as ICoreServerAPI, sinkSlot.Itemstack, op.ActingPlayer.Entity);

                return;
            }

            if (isSealing && op.CurrentPriority == EnumMergePriority.DirectMerge && sinkSlot.Itemstack != null && sourceSlot.Itemstack != null)
            {
                ItemStack sealedBottle = new(op.World.GetBlock(sinkSlot.Itemstack.Collectible.CodeWithVariant("type", "waxed"))) { Attributes = sinkSlot.Itemstack.Attributes };
                ItemStack sealant = sourceSlot.Itemstack.Clone();
                SetSealant(sealedBottle, sealant);

                if (sinkSlot.StackSize == 1)
                {
                    sinkSlot.Itemstack = sealedBottle;
                }
                else
                {
                    sinkSlot.TakeOut(1);
                    if (!op.ActingPlayer.InventoryManager.TryGiveItemstack(sealedBottle, true))
                    {
                        op.World.SpawnItemEntity(sealedBottle, op.ActingPlayer.Entity.Pos.AsBlockPos);
                    }
                }
                op.MovedQuantity = 1;
                sourceSlot.TakeOut(1);
                sinkSlot.MarkDirty();
            }

            base.TryMergeStacks(op);
        }

        public override void OnCreatedByCrafting(ItemSlot[] allInputslots, ItemSlot outputSlot, IRecipeBase byRecipe)
        {
            if (byRecipe.Name?.FirstCodePart() == "uncork" && outputSlot.Itemstack != null)
            {
                SetStopper(outputSlot.Itemstack, null);
            }

            if (byRecipe.Name?.FirstCodePart() == "cork" && outputSlot.Itemstack != null)
            {
                ItemStack? stopper = null;
                foreach (ItemSlot slot in allInputslots)
                {
                    if (slot.Itemstack?.Collectible.GetTags(slot.Itemstack).Overlaps(bottleStopperTag) == true)
                    {
                        stopper = slot.Itemstack.Clone();
                        stopper.StackSize = 1;
                    }
                }

                if (stopper == null) return;

                SetStopper(outputSlot.Itemstack, stopper);
            }

            if (byRecipe.Name?.FirstCodePart() == "unseal" && outputSlot.Itemstack != null)
            {
                SetSealant(outputSlot.Itemstack, null);
            }

            if (byRecipe.Name?.FirstCodePart() == "seal" && outputSlot.Itemstack != null)
            {
                ItemStack? sealant = null;
                foreach (ItemSlot slot in allInputslots)
                {
                    if (slot.Itemstack?.Collectible.GetTags(slot.Itemstack).Overlaps(bottleSealantTag) == true)
                    {
                        sealant = slot.Itemstack.Clone();
                        sealant.StackSize = 1;
                    }
                }

                if (sealant == null) return;

                SetSealant(outputSlot.Itemstack, sealant);
            }

            base.OnCreatedByCrafting(allInputslots, outputSlot, byRecipe);
        }

        public override void OnConsumedByCrafting(ItemSlot[] allInputSlots, ItemSlot stackInSlot, IRecipeBase recipe, IRecipeIngredient fromIngredient, IPlayer byPlayer, int quantity)
        {
            // Stoppers are returned. Sealants are not.
            if (recipe.Name?.FirstCodePart() == "uncork" && stackInSlot.Itemstack != null)
            {
                ItemStack? stopper = GetStopper(stackInSlot.Itemstack);
                SetStopper(stackInSlot.Itemstack, null);

                if (!byPlayer.InventoryManager.TryGiveItemstack(stopper, true))
                {
                    byPlayer.Entity.World.SpawnItemEntity(stopper, byPlayer.Entity.Pos.AsBlockPos);
                }
            }

            if (recipe.Name?.FirstCodePart() == "cork" && stackInSlot.Itemstack != null)
            {
                ItemStack? stopper = null;
                foreach (ItemSlot slot in allInputSlots)
                {
                    if (slot.Itemstack?.Collectible.GetTags(slot.Itemstack).Overlaps(bottleStopperTag) == true)
                    {
                        stopper = slot.Itemstack.Clone();
                        stopper.StackSize = 1;
                    }
                }

                SetStopper(stackInSlot.Itemstack, stopper);
            }

            if (recipe.Name?.FirstCodePart() == "seal" && stackInSlot.Itemstack != null)
            {
                ItemStack? sealant = null;
                foreach (ItemSlot slot in allInputSlots)
                {
                    if (slot.Itemstack?.Collectible.GetTags(slot.Itemstack).Overlaps(bottleSealantTag) == true)
                    {
                        sealant = slot.Itemstack.Clone();
                        sealant.StackSize = 1;
                    }
                }

                SetSealant(stackInSlot.Itemstack, sealant);
            }

            base.OnConsumedByCrafting(allInputSlots, stackInSlot, recipe, fromIngredient, byPlayer, quantity);
        }

        #region HeldInteract

        public override void OnHeldInteractStart(ItemSlot itemslot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
        {
            if (entitySel != null) return;

            IPlayer? plr = (byEntity as EntityPlayer)?.Player;
            ItemSlot? offhandSlot = plr?.InventoryManager?.OffhandHotbarSlot;

            // Uncorking corked bottle
            if (plr != null && blockSel == null && Variant["type"] == "corked" && !byEntity.Controls.ShiftKey && itemslot.Itemstack != null)
            {
                if (offhandSlot != null && (offhandSlot.Empty || offhandSlot.Itemstack.Collectible == GetStopper(itemslot.Itemstack)?.Collectible))
                {
                    ItemStack? stopper = GetStopper(itemslot.Itemstack);
                    ItemStack unstopperedBottle = new(byEntity.World.GetBlock(CodeWithVariant("type", "fired"))) { Attributes = itemslot.Itemstack.Attributes };
                    SetStopper(unstopperedBottle, null);

                    if (itemslot.StackSize == 1)
                    {
                        itemslot.Itemstack = unstopperedBottle;
                    }
                    else
                    {
                        itemslot.TakeOut(1);
                        if (!plr.InventoryManager.TryGiveItemstack(unstopperedBottle, true))
                        {
                            byEntity.World.SpawnItemEntity(unstopperedBottle, byEntity.Pos.AsBlockPos);
                        }
                    }


                    if (new DummySlot(stopper).TryPutInto(byEntity.World, offhandSlot) <= 0)
                    {
                        byEntity.World.SpawnItemEntity(stopper, byEntity.Pos.AsBlockPos);
                    }


                    itemslot.MarkDirty();
                    offhandSlot.MarkDirty();
                    plr.InventoryManager.BroadcastHotbarSlot();

                    PlayCorkingSound(api as ICoreServerAPI, itemslot.Itemstack, byEntity, isUncorking: true);

                    handHandling = EnumHandHandling.PreventDefault;
                    return;
                }
                else (api as ICoreClientAPI)?.TriggerIngameError(this, "fulloffhandslot", Lang.Get("aculinaryartillery:bottle-fulloffhandslot"));
            }

            // Corking open bottle
            if (blockSel == null && byEntity.Controls.ShiftKey && plr != null && offhandSlot != null && itemslot.Itemstack != null
                && Variant["type"] == "fired"
                && offhandSlot.Itemstack?.Collectible.GetTags(offhandSlot.Itemstack).Overlaps(bottleStopperTag) == true)
            {
                ItemStack stopperedBottle = new(byEntity.World.GetBlock(CodeWithVariant("type", "corked"))) { Attributes = itemslot.Itemstack.Attributes };
                ItemStack stopper = offhandSlot.Itemstack.Clone();
                stopper.StackSize = 1;
                SetStopper(stopperedBottle, stopper);
                offhandSlot.TakeOut(1);

                if (itemslot.StackSize == 1)
                {
                    itemslot.Itemstack = stopperedBottle;
                }
                else
                {
                    itemslot.TakeOut(1);
                    if (!plr.InventoryManager.TryGiveItemstack(stopperedBottle, true))
                    {
                        byEntity.World.SpawnItemEntity(stopperedBottle, byEntity.Pos.AsBlockPos);
                    }
                }

                itemslot.MarkDirty();
                offhandSlot.MarkDirty();
                plr.InventoryManager.BroadcastHotbarSlot();

                PlayCorkingSound(api as ICoreServerAPI, stopperedBottle, byEntity);

                handHandling = EnumHandHandling.PreventDefault;
                return;
            }

            // Unsealing waxed bottle
            if (plr != null && blockSel == null && Variant["type"] == "waxed" && !byEntity.Controls.ShiftKey && itemslot.Itemstack != null)
            {
                ItemStack unsealedBottle = new(byEntity.World.GetBlock(CodeWithVariant("type", "corked"))) { Attributes = itemslot.Itemstack.Attributes };
                SetSealant(unsealedBottle, null);

                if (itemslot.StackSize == 1)
                {
                    itemslot.Itemstack = unsealedBottle;
                }
                else
                {
                    itemslot.TakeOut(1);
                    if (!plr.InventoryManager.TryGiveItemstack(unsealedBottle, true))
                    {
                        byEntity.World.SpawnItemEntity(unsealedBottle, byEntity.Pos.AsBlockPos);
                    }
                }

                itemslot.MarkDirty();
                plr.InventoryManager.BroadcastHotbarSlot();

                handHandling = EnumHandHandling.PreventDefault;
                return;
            }

            // Sealing corked bottle
            if (blockSel == null && byEntity.Controls.ShiftKey && plr != null && offhandSlot != null && itemslot.Itemstack != null
                && Variant["type"] == "corked"
                && offhandSlot.Itemstack?.Collectible.GetTags(offhandSlot.Itemstack).Overlaps(bottleSealantTag) == true)
            {
                ItemStack sealedBottle = new(byEntity.World.GetBlock(CodeWithVariant("type", "waxed"))) { Attributes = itemslot.Itemstack.Attributes };
                ItemStack sealant = offhandSlot.Itemstack.Clone();
                sealant.StackSize = 1;
                SetSealant(sealedBottle, sealant);
                offhandSlot.TakeOut(1);

                if (itemslot.StackSize == 1)
                {
                    itemslot.Itemstack = sealedBottle;
                }
                else
                {
                    itemslot.TakeOut(1);
                    if (!plr.InventoryManager.TryGiveItemstack(sealedBottle, true))
                    {
                        byEntity.World.SpawnItemEntity(sealedBottle, byEntity.Pos.AsBlockPos);
                    }
                }

                itemslot.MarkDirty();
                offhandSlot.MarkDirty();
                plr.InventoryManager.BroadcastHotbarSlot();

                handHandling = EnumHandHandling.PreventDefault;
                return;
            }

            base.OnHeldInteractStart(itemslot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);
        }

        protected override void tryEatBegin(ItemSlot slot, EntityAgent byEntity, ref EnumHandHandling handling, string eatSound = "eat", int eatSoundRepeats = 1)
        {
            if (GetNutritionProperties(byEntity.World, slot.Itemstack, byEntity) == null || Variant["type"] != "fired") return;

            base.tryEatBegin(slot, byEntity, ref handling, eatSound, eatSoundRepeats);
        }

        protected override bool tryEatStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, ItemStack? content = null)
        {
            var pos = byEntity.Pos.AheadCopy(0.4f).XYZ;
            pos.X += byEntity.LocalEyePos.X;
            pos.Y += byEntity.LocalEyePos.Y - 0.4f;
            pos.Z += byEntity.LocalEyePos.Z;

            if (secondsUsed > 0.5f && (int)(30 * secondsUsed) % 7 == 1)
            {
                byEntity.World.SpawnCubeParticles(pos, GetContent(slot.Itemstack), 0.3f, 4, 0.5f, (byEntity as EntityPlayer)?.Player);
            }

            if (byEntity.World is IClientWorldAccessor)
            {
                var tf = new ModelTransform();
                tf.EnsureDefaultValues();
                tf.Origin.Set(0f, 0, 0f);

                if (secondsUsed > 0.5f)
                {
                    tf.Translation.Y = Math.Min(0.02f, GameMath.Sin(20 * secondsUsed) / 10);
                }
                tf.Translation.X -= Math.Min(1f, secondsUsed * 4 * 1.57f);
                tf.Translation.Y -= Math.Min(0.05f, secondsUsed * 2);
                tf.Rotation.X += Math.Min(30f, secondsUsed * 350);
                tf.Rotation.Y += Math.Min(80f, secondsUsed * 350);
                return secondsUsed <= 1f;
            }

            return true;
        }

        #endregion

        public override float GetContainingTransitionModifierContained(IWorldAccessor world, ItemSlot inSlot, EnumTransitionType transType)
        {
            float mul = 1;

            if (transType != EnumTransitionType.Perish && transType != EnumTransitionType.Cure) return 1;

            string rateAttr = transType == EnumTransitionType.Perish ? "bottleStopperPerishRate" : "bottleStopperCureRate";
            if (inSlot.Itemstack != null && GetStopper(inSlot.Itemstack) is ItemStack stopperStack)
            {
                mul *= stopperStack.ItemAttributes?[rateAttr]?.AsFloat(1) ?? 1;
            }

            rateAttr = transType == EnumTransitionType.Perish ? "bottleSealantPerishRate" : "bottleSealantCureRate";
            if (inSlot.Itemstack != null && GetSealant(inSlot.Itemstack) is ItemStack sealantStack)
            {
                mul *= sealantStack.ItemAttributes?[rateAttr]?.AsFloat(1) ?? 1;
            }

            return mul;
        }

        public float SatMult => Attributes?["satMult"].AsFloat(1f) ?? 1f;

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

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            if (inSlot.Itemstack != null && GetContent(inSlot.Itemstack) is ItemStack content)
            {
                string newDescription = content.Collectible.Code.Domain + ":itemdesc-" + content.Collectible.Code.Path;
                string finalDescription = Lang.GetMatching(newDescription);

                var dummy = new DummySlot(content);

                if (finalDescription != newDescription)
                {
                    dsc.AppendLine();
                    dsc.Append(finalDescription);
                }

                EntityPlayer? entity = (world as IClientWorldAccessor)?.Player.Entity;
                float spoilState = AppendPerishableInfoText(dummy, new StringBuilder(), world);

                var nutriProps = ItemExpandedRawFood.GetExpandedContentNutritionProperties(world, dummy, content, entity);

                FoodNutritionProperties[]? addProps = GetPropsFromArray((content.Attributes["expandedSats"] as FloatArrayAttribute)?.value);

                if (nutriProps != null && addProps?.Length > 0)
                {
                    dsc.AppendLine();
                    dsc.AppendLine(Lang.Get("efrecipes:Extra Nutrients"));

                    foreach (FoodNutritionProperties props in addProps)
                    {
                        double liquidVolume = content.StackSize;
                        float satLossMul = GlobalConstants.FoodSpoilageSatLossMul(spoilState, content, entity);
                        float healthLossMul = GlobalConstants.FoodSpoilageHealthLossMul(spoilState, content, entity);

                        if (Math.Abs(props.Health * healthLossMul) > 0.001f)
                        {
                            dsc.AppendLine(Lang.Get("efrecipes:- {0} {2} sat, {1} hp", Math.Round(props.Satiety * satLossMul * (liquidVolume / 10), 1), Math.Round(props.Health * healthLossMul * (liquidVolume / 10), 1), ItemExpandedFood.GetLocalizedFoodCategory(props.FoodCategory)));
                        }
                        else
                        {
                            dsc.AppendLine(Lang.Get("efrecipes:- {0} {1} sat", Math.Round(props.Satiety * satLossMul * (liquidVolume / 10), 1), ItemExpandedFood.GetLocalizedFoodCategory(props.FoodCategory)));
                        }
                    }
                }
            }

            if (inSlot.Itemstack != null && GetStopper(inSlot.Itemstack) is ItemStack stopper)
            {
                float perishRate = stopper.ItemAttributes?["bottleStopperPerishRate"].AsFloat(1) ?? 1;
                float cureRate = stopper.ItemAttributes?["bottleStopperCureRate"].AsFloat(1) ?? 1;

                if (GetSealant(inSlot.Itemstack) is ItemStack sealant)
                {
                    perishRate *= sealant.ItemAttributes?["bottleSealantPerishRate"].AsFloat(1) ?? 1;
                    cureRate *= sealant.ItemAttributes?["bottleSealantCureRate"].AsFloat(1) ?? 1;
                }

                dsc.AppendLine();
                if (perishRate != 1) dsc.AppendLine(Lang.Get("aculinaryartillery:bottle-perish-rate-desc", Math.Round(perishRate, 2)));
                if (cureRate != 1) dsc.AppendLine(Lang.Get("aculinaryartillery:bottle-cure-rate-desc", Math.Round(cureRate, 2)));
            }
        }

        public override void OnGroundIdle(EntityItem entityItem)
        {
            base.OnGroundIdle(entityItem);

            if (!entityItem.Swimming || entityItem.World.Side != EnumAppSide.Server) return;

            var contents = GetContent(entityItem.Itemstack);
            if (contents?.Collectible.Code.Path == "rot")
            {
                entityItem.World.SpawnItemEntity(contents, entityItem.ServerPos.XYZ);
                SetContent(entityItem.Itemstack, null);
            }
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return
            [
                new()
                {
                    ActionLangCode = "heldhelp-empty",
                    HotKeyCode = "ctrl",
                    MouseButton = EnumMouseButton.Right,
                    ShouldApply = (wi, bs, es) => bs != null && GetCurrentLitres(inSlot.Itemstack) > 0,
                },
                new()
                {
                    ActionLangCode = "aculinaryartillery:heldhelp-drink",
                    MouseButton = EnumMouseButton.Right,
                    ShouldApply = (wi, bs, es) => (GetContent(inSlot.Itemstack)?.GetName() is not null and not "Water") && GetCurrentLitres(inSlot.Itemstack) > 0,
                },
                new()
                {
                    ActionLangCode = "heldhelp-fill",
                    MouseButton = EnumMouseButton.Right,
                    ShouldApply = (wi, bs, es) => bs != null && (api.World.BlockAccessor.GetBlock(bs.Position.AddCopy(bs.Face))?.Code.GetName().Contains("water-") == true) && GetCurrentLitres(inSlot.Itemstack) == 0,
                },
                new()
                {
                    ActionLangCode = "heldhelp-place",
                    HotKeyCode = "shift",
                    MouseButton = EnumMouseButton.Right,
                    ShouldApply = (wi, bs, es) => true
                },

                new()
                {
                    ActionLangCode = "aculinaryartillery:heldhelp-unstopper",
                    MouseButton = EnumMouseButton.Right,
                    ShouldApply = (wi, bs, es) => bs == null && es == null && Variant["type"] == "corked"
                },
                new()
                {
                    ActionLangCode = "aculinaryartillery:heldhelp-stopper",
                    HotKeyCode = "shift",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = stopperStacks,
                    GetMatchingStacks = (wi, bs, es) => bs == null && es == null && Variant["type"] == "fired" ? wi.Itemstacks : null
                },

                new()
                {
                    ActionLangCode = "aculinaryartillery:heldhelp-unseal",
                    MouseButton = EnumMouseButton.Right,
                    ShouldApply = (wi, bs, es) => bs == null && es == null && Variant["type"] == "waxed"
                },
                new()
                {
                    ActionLangCode = "aculinaryartillery:heldhelp-seal",
                    HotKeyCode = "shift",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = sealantStacks,
                    GetMatchingStacks = (wi, bs, es) => bs == null && es == null && Variant["type"] == "corked" ? wi.Itemstacks : null
                },
            ];
        }
    }

    public class BottleTextureSource : ITexPositionSource
    {
        private readonly ICoreClientAPI capi;

        // Used for loading dynamic textures
        private readonly Dictionary<string, TextureAtlasPosition?> texturePositions = [];

        // Stored as a default to avoid a double lookup
        private readonly TextureAtlasPosition blockTexPos;

        public BottleTextureSource(ICoreClientAPI capi, ItemStack bottleStack, CompositeTexture? stopperTexture, CompositeTexture? sealantTexture, CompositeTexture? contentTexture)
        {
            this.capi = capi;
            if (contentTexture != null) texturePositions["content"] = GetOrInsertTexture(capi.BlockTextureAtlas, "content", contentTexture);
            if (stopperTexture != null) texturePositions["stopper"] = GetOrInsertTexture(capi.BlockTextureAtlas, "stopper", stopperTexture);
            if (sealantTexture != null) texturePositions["wax"] = GetOrInsertTexture(capi.BlockTextureAtlas, "wax", sealantTexture);
            texturePositions["material"] = capi.BlockTextureAtlas.GetPosition(bottleStack.Block, "material");
            texturePositions["sides"] = capi.BlockTextureAtlas.GetPosition(bottleStack.Block, "sides", true);

            blockTexPos = capi.BlockTextureAtlas.GetPosition(bottleStack.Block, "material");
        }

        public TextureAtlasPosition GetOrInsertTexture(ITextureAtlasAPI atlas, string name, CompositeTexture texture)
        {
            int textureSubId = ObjectCacheUtil.GetOrCreate(capi, $"{name}texture-{texture}", () =>
            {
                capi.BlockTextureAtlas.GetOrInsertTexture(
                    texture.Base.CopyWithPathPrefixAndAppendixOnce("textures/", ".png"),
                    out var id,
                    out _,
                    new CreateTextureDelegate(() =>
                    {
                        var bmp = capi.Assets.TryGet(texture.Base.CopyWithPathPrefixAndAppendixOnce("textures/", ".png"))?.ToBitmap(capi);
                        if (bmp != null && texture.Alpha != 255) bmp.MulAlpha(texture.Alpha);
                        return bmp;
                    })
                );
                return id;
            });

            return atlas.Positions[textureSubId];
        }

        public TextureAtlasPosition this[string textureCode]
        {
            get => texturePositions.GetValueOrDefault(textureCode) ?? blockTexPos;
        }

        public Size2i AtlasSize => capi.BlockTextureAtlas.Size;
    }
}
