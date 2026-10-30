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
    public class BlockStoppableContainer : BlockLiquidContainerTopOpened, IContainedMeshSource
    {
        protected LiquidTopOpenContainerProps props = new();
        protected virtual string MeshRefsCacheKey => Code.ToShortString() + "meshRefs";
        public virtual ItemStack DefaultStopper => new(api.World.GetItem("aculinaryartillery:stopper-bark-cork"));
        public bool HasTransparentStopper(ItemStack stack) => GetStopper(stack)?.ItemAttributes?["isTransparent"].AsBool() == true;
        public virtual ItemStack DefaultSealant => new(api.World.GetItem("game:beeswax"));
        public string CorkedTypeVariant => Attributes["corkedTypeVariant"]?.AsString() ?? "type";

        public static ItemStack[] stopperStacks = null!;
        public static ItemStack[] sealantStacks = null!;

        protected virtual AssetLocation EmptyShapeLoc(ItemStack stack)
        {
            if (HasTransparentStopper(stack))
            {
                api.Logger.Debug($"{stack.ItemAttributes["transparentStopperShapeLoc"].AsString("missing-transparentStopperShapeLoc")}");
                return stack.ItemAttributes["transparentStopperShapeLoc"].AsString("missing-transparentStopperShapeLoc");
            }

            return props.EmptyShapeLoc ?? Shape.Base;
        }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            props = Attributes?["liquidContainerProps"]?.AsObject(props, Code.Domain) ?? props;
            drinkPortionSizeFromAttributes = Attributes?["drinkPortionSize"].AsFloat(0.25f) ?? 0.25f; // base game reads this as an integer

            if (api.Side is EnumAppSide.Client && stopperStacks == null)
            {
                List<ItemStack> stopperStacks = [];
                foreach (CollectibleObject obj in api.World.Collectibles)
                {
                    if (obj.StorageFlags.HasFlag(EnumItemStorageFlags.Offhand) && obj.Attributes?["isLiquidContainerStopper"]?.AsBool() == true)
                    {
                        stopperStacks.Add(new ItemStack(obj));
                    }
                }
                BlockStoppableContainer.stopperStacks = [.. stopperStacks];
            }

            if (api.Side is EnumAppSide.Client && sealantStacks == null)
            {
                List<ItemStack> sealantStacks = [];
                foreach (CollectibleObject obj in api.World.Collectibles)
                {
                    if (obj.StorageFlags.HasFlag(EnumItemStorageFlags.Offhand) && obj.Attributes?["isLiquidContainerSealant"]?.AsBool() == true)
                    {
                        sealantStacks.Add(new ItemStack(obj));
                    }
                }
                BlockStoppableContainer.sealantStacks = [.. sealantStacks];
            }
        }


        public ItemStack? GetStopper(ItemStack containerStack)
        {
            if (containerStack.Collectible.LastCodePart() != "corked" && containerStack.Collectible.LastCodePart() != "waxed") return null;

            if (GetContents(api.World, containerStack) is ItemStack[] contentStacks)
            {
                return contentStacks.ElementAtOrDefault(1) ?? DefaultStopper;
            }

            return DefaultStopper;
        }

        public void SetStopper(ItemStack containerStack, ItemStack? stopperStack)
        {
            List<ItemStack?> contentStacks = [.. GetContents(api.World, containerStack)];
            while (contentStacks.Count() < 2) contentStacks.Add(null);
            contentStacks[1] = stopperStack;
            SetContents(containerStack, [.. contentStacks]);
        }

        public ItemStack? GetSealant(ItemStack containerStack)
        {
            if (containerStack.Collectible.LastCodePart() != "waxed") return null;

            if (GetContents(api.World, containerStack) is ItemStack[] contentStacks)
            {
                return contentStacks.ElementAtOrDefault(2) ?? DefaultSealant;
            }

            return DefaultSealant;
        }

        public void SetSealant(ItemStack containerStack, ItemStack? sealantStack)
        {
            List<ItemStack?> contentStacks = [.. GetContents(api.World, containerStack)];
            while (contentStacks.Count() < 3) contentStacks.Add(null);
            contentStacks[2] = sealantStack;
            SetContents(containerStack, [.. contentStacks]);
        }

        public void PlayCorkingSound(ICoreServerAPI sapi, ItemStack containerStack, Entity entity, bool isUncorking = false)
        {
            float fullness = 0;
            if (GetContent(containerStack) is ItemStack contentStack
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
            AssetLocation sound = new($"{Code.Domain}:sounds/player/{FirstCodePart()}/{(isUncorking ? "uncork" : "cork")}{suffix}*");
            sapi.World.PlaySoundAt(sound, entity);
        }

        public override int GetMergableQuantity(ItemStack sinkStack, ItemStack sourceStack, EnumMergePriority priority)
        {
            bool isStopping = sourceStack?.ItemAttributes?["isLiquidContainerStopper"]?.AsBool() == true && Variant[CorkedTypeVariant] == "fired";
            bool isSealing = sourceStack?.ItemAttributes?["isLiquidContainerSealant"]?.AsBool() == true && Variant[CorkedTypeVariant] == "corked";

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

            bool isStopping = sourceSlot.Itemstack?.ItemAttributes?["isLiquidContainerStopper"]?.AsBool() == true && Variant[CorkedTypeVariant] == "fired";
            bool isSealing = sourceSlot.Itemstack?.ItemAttributes?["isLiquidContainerSealant"]?.AsBool() == true && Variant[CorkedTypeVariant] == "corked";

            if (isStopping && op.CurrentPriority == EnumMergePriority.DirectMerge && sinkSlot.Itemstack != null && sourceSlot.Itemstack != null)
            {
                ItemStack stopperedContainer = new(op.World.GetBlock(sinkSlot.Itemstack.Collectible.CodeWithVariant(CorkedTypeVariant, "corked"))) { Attributes = sinkSlot.Itemstack.Attributes.Clone() };
                ItemStack stopper = sourceSlot.Itemstack.Clone();
                SetStopper(stopperedContainer, stopper);

                if (sinkSlot.StackSize == 1)
                {
                    sinkSlot.Itemstack = stopperedContainer;
                }
                else
                {
                    sinkSlot.TakeOut(1);
                    if (!op.ActingPlayer.InventoryManager.TryGiveItemstack(stopperedContainer, true))
                    {
                        op.World.SpawnItemEntity(stopperedContainer, op.ActingPlayer.Entity.Pos.AsBlockPos);
                    }
                }
                op.MovedQuantity = 1;
                sourceSlot.TakeOut(1);
                sinkSlot.MarkDirty();

                if (api is ICoreServerAPI sapi) PlayCorkingSound(sapi, sinkSlot.Itemstack, op.ActingPlayer.Entity);

                return;
            }

            if (isSealing && op.CurrentPriority == EnumMergePriority.DirectMerge && sinkSlot.Itemstack != null && sourceSlot.Itemstack != null)
            {
                ItemStack sealedContainer = new(op.World.GetBlock(sinkSlot.Itemstack.Collectible.CodeWithVariant(CorkedTypeVariant, "waxed"))) { Attributes = sinkSlot.Itemstack.Attributes.Clone() };
                ItemStack sealant = sourceSlot.Itemstack.Clone();
                SetSealant(sealedContainer, sealant);

                if (sinkSlot.StackSize == 1)
                {
                    sinkSlot.Itemstack = sealedContainer;
                }
                else
                {
                    sinkSlot.TakeOut(1);
                    if (!op.ActingPlayer.InventoryManager.TryGiveItemstack(sealedContainer, true))
                    {
                        op.World.SpawnItemEntity(sealedContainer, op.ActingPlayer.Entity.Pos.AsBlockPos);
                    }
                }
                op.MovedQuantity = 1;
                sourceSlot.TakeOut(1);
                sinkSlot.MarkDirty();

                return;
            }

            if (!sourceSlot.Empty) base.TryMergeStacks(op);
        }

        #region HeldInteract

        // Fixes a vanilla bug where holding shift overrides these checks.
        protected override void tryEatBegin(ItemSlot slot, EntityAgent byEntity, ref EnumHandHandling handling, string eatSound = "eat", int eatSoundRepeats = 1)
        {
            if (GetNutritionProperties(byEntity.World, slot.Itemstack, byEntity) == null || !CanDrinkFrom) return;

            base.tryEatBegin(slot, byEntity, ref handling, eatSound, eatSoundRepeats);
        }

        public override void OnHeldInteractStart(ItemSlot itemslot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
        {
            if (entitySel != null) return;

            IPlayer? player = (byEntity as EntityPlayer)?.Player;
            ItemSlot? offhandSlot = player?.InventoryManager?.OffhandHotbarSlot;

            // Uncorking corked container
            if (player != null && blockSel == null && Variant[CorkedTypeVariant] == "corked" && !byEntity.Controls.ShiftKey && itemslot.Itemstack != null)
            {
                if (offhandSlot != null && (offhandSlot.Empty || offhandSlot.Itemstack.Collectible == GetStopper(itemslot.Itemstack)?.Collectible))
                {
                    ItemStack? stopper = GetStopper(itemslot.Itemstack);
                    ItemStack unstopperedContainer = new(byEntity.World.GetBlock(CodeWithVariant(CorkedTypeVariant, "fired"))) { Attributes = itemslot.Itemstack.Attributes.Clone() };
                    SetStopper(unstopperedContainer, null);

                    if (itemslot.StackSize == 1)
                    {
                        itemslot.Itemstack = unstopperedContainer;
                    }
                    else
                    {
                        itemslot.TakeOut(1);
                        if (!player.InventoryManager.TryGiveItemstack(unstopperedContainer, true))
                        {
                            byEntity.World.SpawnItemEntity(unstopperedContainer, byEntity.Pos.AsBlockPos);
                        }
                    }


                    if (new DummySlot(stopper).TryPutInto(byEntity.World, offhandSlot) <= 0)
                    {
                        byEntity.World.SpawnItemEntity(stopper, byEntity.Pos.AsBlockPos);
                    }


                    itemslot.MarkDirty();
                    offhandSlot.MarkDirty();
                    player.InventoryManager.BroadcastHotbarSlot();

                    if (api is ICoreServerAPI sapi && !itemslot.Empty) PlayCorkingSound(sapi, itemslot.Itemstack, byEntity, isUncorking: true);

                    handHandling = EnumHandHandling.PreventDefault;
                    return;
                }
                else (api as ICoreClientAPI)?.TriggerIngameError(this, "fulloffhandslot", Lang.Get("aculinaryartillery:container-fulloffhandslot"));
            }

            // Corking open container
            if (blockSel == null && byEntity.Controls.ShiftKey && player != null && offhandSlot != null && itemslot.Itemstack != null
                && Variant[CorkedTypeVariant] == "fired"
                && offhandSlot.Itemstack?.ItemAttributes?["isLiquidContainerStopper"]?.AsBool() == true)
            {
                ItemStack stopperedContainer = new(byEntity.World.GetBlock(CodeWithVariant(CorkedTypeVariant, "corked"))) { Attributes = itemslot.Itemstack.Attributes.Clone() };
                ItemStack stopper = offhandSlot.Itemstack.Clone();
                stopper.StackSize = 1;
                SetStopper(stopperedContainer, stopper);
                offhandSlot.TakeOut(1);

                if (itemslot.StackSize == 1)
                {
                    itemslot.Itemstack = stopperedContainer;
                }
                else
                {
                    itemslot.TakeOut(1);
                    if (!player.InventoryManager.TryGiveItemstack(stopperedContainer, true))
                    {
                        byEntity.World.SpawnItemEntity(stopperedContainer, byEntity.Pos.AsBlockPos);
                    }
                }

                itemslot.MarkDirty();
                offhandSlot.MarkDirty();
                player.InventoryManager.BroadcastHotbarSlot();

                if (api is ICoreServerAPI sapi && !itemslot.Empty) PlayCorkingSound(sapi, stopperedContainer, byEntity, isUncorking: false);

                handHandling = EnumHandHandling.PreventDefault;
                return;
            }

            // Unsealing waxed container
            if (player != null && blockSel == null && Variant[CorkedTypeVariant] == "waxed" && !byEntity.Controls.ShiftKey && itemslot.Itemstack != null)
            {
                ItemStack unsealedContainer = new(byEntity.World.GetBlock(CodeWithVariant(CorkedTypeVariant, "corked"))) { Attributes = itemslot.Itemstack.Attributes.Clone() };
                SetSealant(unsealedContainer, null);

                if (itemslot.StackSize == 1)
                {
                    itemslot.Itemstack = unsealedContainer;
                }
                else
                {
                    itemslot.TakeOut(1);
                    if (!player.InventoryManager.TryGiveItemstack(unsealedContainer, true))
                    {
                        byEntity.World.SpawnItemEntity(unsealedContainer, byEntity.Pos.AsBlockPos);
                    }
                }

                itemslot.MarkDirty();
                player.InventoryManager.BroadcastHotbarSlot();

                handHandling = EnumHandHandling.PreventDefault;
                return;
            }

            // Sealing corked container
            if (blockSel == null && byEntity.Controls.ShiftKey && player != null && offhandSlot != null && itemslot.Itemstack != null
                && Variant[CorkedTypeVariant] == "corked"
                && offhandSlot.Itemstack?.ItemAttributes?["isLiquidContainerSealant"]?.AsBool() == true)
            {
                ItemStack sealedContainer = new(byEntity.World.GetBlock(CodeWithVariant(CorkedTypeVariant, "waxed"))) { Attributes = itemslot.Itemstack.Attributes.Clone() };
                ItemStack sealant = offhandSlot.Itemstack.Clone();
                sealant.StackSize = 1;
                SetSealant(sealedContainer, sealant);
                offhandSlot.TakeOut(1);

                if (itemslot.StackSize == 1)
                {
                    itemslot.Itemstack = sealedContainer;
                }
                else
                {
                    itemslot.TakeOut(1);
                    if (!player.InventoryManager.TryGiveItemstack(sealedContainer, true))
                    {
                        byEntity.World.SpawnItemEntity(sealedContainer, byEntity.Pos.AsBlockPos);
                    }
                }

                itemslot.MarkDirty();
                offhandSlot.MarkDirty();
                player.InventoryManager.BroadcastHotbarSlot();

                handHandling = EnumHandHandling.PreventDefault;
                return;
            }

            base.OnHeldInteractStart(itemslot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);
        }

        #endregion

        public override float GetContainingTransitionModifierContained(IWorldAccessor world, ItemSlot inSlot, EnumTransitionType transType)
        {
            float mul = 1;

            if (transType != EnumTransitionType.Perish && transType != EnumTransitionType.Cure) return 1;

            string rateAttr = transType == EnumTransitionType.Perish ? "liquidContainerStopperPerishRate" : "liquidContainerStopperCureRate";
            if (inSlot.Itemstack != null && GetStopper(inSlot.Itemstack) is ItemStack stopperStack)
            {
                mul *= stopperStack.ItemAttributes?[rateAttr]?.AsFloat(1) ?? 1;
            }

            rateAttr = transType == EnumTransitionType.Perish ? "liquidContainerSealantPerishRate" : "liquidContainerSealantCureRate";
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
                float perishRate = stopper.ItemAttributes?["containerStopperPerishRate"].AsFloat(1) ?? 1;
                float cureRate = stopper.ItemAttributes?["containerStopperCureRate"].AsFloat(1) ?? 1;

                if (GetSealant(inSlot.Itemstack) is ItemStack sealant)
                {
                    perishRate *= sealant.ItemAttributes?["containerSealantPerishRate"].AsFloat(1) ?? 1;
                    cureRate *= sealant.ItemAttributes?["containerSealantCureRate"].AsFloat(1) ?? 1;
                }

                dsc.AppendLine();
                if (perishRate != 1) dsc.AppendLine(Lang.Get("aculinaryartillery:container-perish-rate-desc", Math.Round(perishRate, 2)));
                if (cureRate != 1) dsc.AppendLine(Lang.Get("aculinaryartillery:container-cure-rate-desc", Math.Round(cureRate, 2)));
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
                meshrefs[hashcode] = meshRef = capi.Render.UploadMultiTextureMesh(GenMesh(capi, itemstack, new DynamicTextureSource(capi, this)));
            }

            renderinfo.ModelRef = meshRef;
        }

        public virtual MeshData? GenMesh(ICoreClientAPI? capi, ItemStack stack, DynamicTextureSource textureSource, BlockPos? atBlockPos = null)
        {
            if (capi?.Assets.TryGet(EmptyShapeLoc(stack).CopyWithPathPrefixAndAppendixOnce("shapes/", ".json")) is not IAsset asset) return new MeshData();

            ItemStack? stopper = GetStopper(stack);
            if (stopper != null) textureSource.GetOrInsertTexture("stopper", stopper.ItemAttributes?["liquidContainerStopperTexture"]?.AsObject<CompositeTexture>() ?? stopper.Item.FirstTexture);
            ItemStack? sealant = GetSealant(stack);
            if (sealant != null) textureSource.GetOrInsertTexture("wax", sealant.ItemAttributes?["liquidContainerSealantTexture"]?.AsObject<CompositeTexture>() ?? sealant.Item.FirstTexture);

            capi.Tesselator.TesselateShape(Code, asset.ToObject<Shape>(), out var mesh, textureSource, new Vec3f(Shape.rotateX, Shape.rotateY, Shape.rotateZ));

            return mesh;
        }

        public new MeshData? GenMesh(ItemSlot slot, ITextureAtlasAPI targetAtlas, BlockPos? atBlockPos = null)
        {
            if (slot.Itemstack is not ItemStack stack || api is not ICoreClientAPI capi) return new MeshData();

            return GenMesh(api as ICoreClientAPI, stack, new DynamicTextureSource(capi, this), atBlockPos);
        }

        public new string GetMeshCacheKey(ItemSlot slot)
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
                    ShouldApply = (wi, bs, es) => inSlot.Itemstack != null && (GetContent(inSlot.Itemstack)?.GetName() is not null and not "Water") && GetCurrentLitres(inSlot.Itemstack) > 0,
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
                    ShouldApply = (wi, bs, es) => bs == null && es == null && Variant[CorkedTypeVariant] == "corked"
                },
                new()
                {
                    ActionLangCode = "aculinaryartillery:heldhelp-stopper",
                    HotKeyCode = "shift",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = stopperStacks,
                    GetMatchingStacks = (wi, bs, es) => bs == null && es == null && Variant[CorkedTypeVariant] == "fired" ? wi.Itemstacks : null
                },
                new()
                {
                    ActionLangCode = "aculinaryartillery:heldhelp-unseal",
                    MouseButton = EnumMouseButton.Right,
                    ShouldApply = (wi, bs, es) => bs == null && es == null && Variant[CorkedTypeVariant] == "waxed"
                },
                new()
                {
                    ActionLangCode = "aculinaryartillery:heldhelp-seal",
                    HotKeyCode = "shift",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = sealantStacks,
                    GetMatchingStacks = (wi, bs, es) => bs == null && es == null && Variant[CorkedTypeVariant] == "corked" ? wi.Itemstacks : null
                },
            ];
        }
    }
}
