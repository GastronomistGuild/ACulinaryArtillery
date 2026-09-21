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
    public class BlockEntityMeatHooks : BlockEntityDisplay, ITexPositionSource
    {
        protected InventoryGeneric inventory;
        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "meathooks";
        public override string AttributeTransformCode => "meatHookTransform";

        public string Wood = "";
        public string Metal = "";

        public MeshData? mesh = null;

        public BlockEntityMeatHooks()
        {
            inventory = new InventoryDisplayed(this, 4, "meathooks-0", null, null);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            RegisterGameTickListener(RotDrop, 3000);
            Inventory.OnAcquireTransitionSpeed += Inventory_OnAcquireTransitionSpeed;

            if (Api.World.BlockAccessor.GetBlock(Pos) is BlockMeatHooks rack)
            {
                rack.Wood = Wood;
                rack.Metal = Metal;
            }
        }

        public void GenMesh()
        {
            if (Block is not BlockMeatHooks rack || Api is not ICoreClientAPI capi) return;

            CompositeTexture? woodTexture = capi.World.GetItem(Wood)?.FirstTexture;
            CompositeTexture? metalTexture = capi.World.GetItem(Metal)?.FirstTexture;

            // STABLERACK
            string[] codeParts = rack.Code.Path.Split("-");
            if (woodTexture == null && metalTexture == null && codeParts.Length > 2)
            {
                Dictionary<string, string[]> plankTypesByDomain = [];
                plankTypesByDomain["game"] = ["acacia", "baldcypress", "birch", "ebony", "kapok", "larch", "maple", "oak", "pine", "purpleheart", "redwood", "walnut", "aged", "veryaged"];
                plankTypesByDomain["wildcrafttree"] = ["douglasfir", "willow", "honeylocust", "bearnut", "poplar", "catalpa", "mahogany", "sal", "saxaul", "spruce", "sycamore", "elm", "beech", "eucalyptus", "cedar", "tuja", "redcedar", "yew", "kauri", "ginkgo", "dalbergia", "umnini", "banyan", "guajacum", "ghostgum", "ohia", "satinash", "bluemahoe", "jacaranda", "empresstree", "chlorociboria", "petrified", "fir", "tamanu", "spurgetree", "azobe", "leadwood", "linden", "horsechestnut", "tigerwood", "sapele", "ash", "mangrove", "charred"];

                foreach ((string domain, string[] plankTypes) in plankTypesByDomain)
                {
                    if (plankTypes.Contains(codeParts[1]))
                    {
                        woodTexture = capi.World.GetItem($"{domain}:plank-{codeParts[1]}")?.FirstTexture
                            ?? capi.World.GetItem("game:plank-oak")?.FirstTexture;
                    }
                }

                string metalCode = $"aculinaryartillery:bighook-{codeParts[2]}";
                metalTexture = capi.World.GetItem(metalCode)?.FirstTexture
                    ?? capi.World.GetItem("aculinaryartillery:bighook-copper")?.FirstTexture;
            }

            DynamicTextureSource textureSource = new(capi, Block, "wood");
            if (woodTexture != null) textureSource.GetOrInsertTexture("wood", woodTexture);
            if (metalTexture != null) textureSource.GetOrInsertTexture("metal", metalTexture);

            mesh = rack.GenMesh(capi, textureSource);
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            GenMesh();
            mesher.AddMeshData(mesh);
            return base.OnTesselation(mesher, tessThreadTesselator);
        }

        protected override MeshData getOrCreateMesh(ItemSlot slot, int index)
        {
            MeshData mesh = getMesh(slot);
            if (mesh != null) return mesh;

            var stack = slot.Itemstack;
            if (stack == null) return new();

            CompositeShape? customShape = stack.ItemAttributes?["meatHookShape"].AsObject<CompositeShape>(null, stack.Collectible.Code.Domain);

            Dictionary<string, CompositeTexture> stackTextures = [];
            if (stack.Collectible is Block)
            {
                stackTextures.AddRange(stack.Block.Textures);
            }
            else
            {
                stackTextures.AddRange(stack.Item.Textures);
            }

            Dictionary<string, AssetLocation> stackTextureLocs = [];
            foreach ((string name, CompositeTexture texture) in stackTextures)
            {
                stackTextureLocs[name] = texture.Base;
            }

            if (customShape != null)
            {
                string customkey = $"meatHookShape-{stack.Collectible.Code}-{customShape?.ToString() ?? ""}";
                mesh = ObjectCacheUtil.GetOrCreate(capi, customkey, () =>
                    capi.TesselatorManager.CreateMesh(
                        "meathook item shape",
                        customShape,
                        (shape, name) =>
                        {
                            shape.Textures.AddRange(stackTextureLocs);
                            return new ShapeTextureSource(capi, shape, string.Format("For meathook item {0}", stack?.Collectible.Code));
                        },
                        null
                ));
            }
            else
            {
                IContainedMeshSource? meshSource = stack.Collectible?.GetCollectibleInterface<IContainedMeshSource>();

                if (meshSource != null)
                {
                    mesh = meshSource.GenMesh(slot, capi.BlockTextureAtlas, Pos);
                }
            }

            if (mesh == null)
            {
                mesh = getDefaultMesh(stack);
            }

            applyDefaultTranforms(stack, mesh);

            string key = getMeshCacheKey(slot);
            MeshCache[key] = mesh;

            return mesh;
        }

        Vec3d? dropPos;
        private void RotDrop(float dt)
        {
            dropPos ??= Pos.ToVec3d().Add(0.5, -1, 0.5);
            inventory.DropSlots(dropPos, [.. inventory.Where(slot => slot.Itemstack?.Collectible.FirstCodePart() == "rot").Select(inventory.GetSlotId)]);
        }

        public override void OnBlockPlaced(ItemStack? itemStack = null)
        {
            base.OnBlockPlaced(itemStack);

            Wood = itemStack?.Attributes.GetString("wood") ?? "game:plank-oak";
            Metal = itemStack?.Attributes.GetString("metal") ?? "aculinaryartillery:bighook-copper";

            if (Api.World.BlockAccessor.GetBlock(Pos) is BlockMeatHooks rack)
            {
                rack.Wood = Wood;
                rack.Metal = Metal;
            }

            GenMesh();
        }

        internal bool OnInteract(IPlayer byPlayer, BlockSelection blockSel)
        {
            ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (slot.Empty) return TryTake(byPlayer, blockSel);
            else if (slot.Itemstack.Collectible.Attributes?["meathookable"].AsBool() == true && TryPut(slot, blockSel))
            {
                if (slot.Itemstack?.Block?.Sounds?.Place != null)
                {
                    Api.World.PlaySoundAt(slot.Itemstack.Block.Sounds.Place, byPlayer.Entity, byPlayer);
                }
                else
                {
                    Api.World.PlaySoundAt("sounds/player/build", byPlayer.Entity, byPlayer, true, 16, 1f);
                }
                return true;
            }

            return false;
        }

        private float Inventory_OnAcquireTransitionSpeed(EnumTransitionType transType, ItemStack stack, float baseMul)
        {
            if (Api == null) return 1;
            if (transType == EnumTransitionType.Cure) return Block.Attributes["cureRate"].AsFloat(3);
            if (transType == EnumTransitionType.Dry) return Block.Attributes["dryRate"].AsFloat(3);
            return baseMul;
        }

        private bool TryPut(ItemSlot slot, BlockSelection blockSel)
        {
            int index = blockSel.SelectionBoxIndex;

            if (inventory[index].Empty && slot.TryPutInto(Api.World, inventory[index]) > 0)
            {
                updateMesh(index);
                GenMesh();
                MarkDirty(true);
                return true;
            }

            return false;
        }

        private bool TryTake(IPlayer byPlayer, BlockSelection blockSel)
        {
            int index = blockSel.SelectionBoxIndex;

            if (!inventory[index].Empty)
            {
                ItemStack stack = inventory[index].TakeOut(1);

                if (byPlayer.InventoryManager.TryGiveItemstack(stack))
                {
                    if (stack?.Block?.Sounds?.Place != null)
                    {
                        Api.World.PlaySoundAt(stack.Block.Sounds.Place, byPlayer.Entity, byPlayer);
                    }
                    else
                    {
                        Api.World.PlaySoundAt("sounds/player/build", byPlayer.Entity, byPlayer, true, 16, 1f);
                    }
                }

                if (stack.StackSize > 0)
                {
                    Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }

                updateMesh(index);
                MarkDirty(true);
                return true;
            }

            return false;
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
        {
            sb.AppendLine();

            if (forPlayer?.CurrentBlockSelection == null) return;

            int index = forPlayer.CurrentBlockSelection.SelectionBoxIndex;

            if (!inventory[index].Empty)
            {
                if (inventory[index].Itemstack.Collectible.TransitionableProps != null && inventory[index].Itemstack.Collectible.TransitionableProps.Length > 0)
                {
                    sb.AppendLine(PerishableInfoCompact(Api, inventory[index], 0));
                }
                else sb.AppendLine(inventory[index].Itemstack.GetName());
            }
        }

        protected override float[][] genTransformationMatrices()
        {
            float[][] tfMatrices = new float[4][];
            Cuboidf selectionBox;
            int rnd = 0;

            for (int index = 0; index < 4; index++)
            {
                selectionBox = Block.SelectionBoxes[index];

                if (inventory[index]?.Itemstack?.ItemAttributes?["randomizeInDisplayCase"].AsBool(true) != false)
                {
                    rnd = GameMath.MurmurHash3Mod(Pos.X, Pos.Y + index * 50, Pos.Z, 30) - 15;
                }

                tfMatrices[index] =
                    new Matrixf()
                    .Translate(0.5f, 0, 0.5f)
                    .Translate(selectionBox.MidX - 0.5f, selectionBox.MaxY, selectionBox.MidZ - 0.5f)
                    .RotateYDeg(getRotateOnHook(index) + rnd)
                    .Scale(0.75f, 0.75f, 0.75f)
                    .Translate(-0.5f, 0, -0.5f)
                    .Values
                ;

                rnd = 0;
            }

            return tfMatrices;
        }

        private float getRotateOnHook(int index)
        {
            return Block.Shape.rotateY switch
            {
                0 => index < 2 ? 180 : 0,
                90 => index % 2 == 0 ? 270 : 90,
                180 => index < 2 ? 180 : 360,
                270 => index % 2 == 0 ? 270 : 450,
                var rot => index < 2 ? rot + 180 : rot
            };
        }
        public string PerishableInfoCompact(ICoreAPI Api, ItemSlot contentSlot, float ripenRate, bool withStackName = true)
        {
            if (contentSlot.Empty) return "";

            StringBuilder dsc = new();

            if (withStackName) dsc.Append(contentSlot.Itemstack.GetName());

            TransitionState[]? transitionStates = contentSlot.Itemstack.Collectible.UpdateAndGetTransitionStates(Api.World, contentSlot);

            bool nowSpoiling = false;

            if (transitionStates != null)
            {
                bool appendLine = false;
                for (int i = 0; i < transitionStates.Length; i++)
                {
                    TransitionState state = transitionStates[i];

                    TransitionableProperties prop = state.Props;
                    float perishRate = contentSlot.Itemstack.Collectible.GetTransitionRateMul(Api.World, contentSlot, prop.Type);

                    if (perishRate <= 0) continue;

                    float transitionLevel = state.TransitionLevel;
                    float freshHoursLeft = state.FreshHoursLeft / perishRate;
                    double hoursPerday = Api.World.Calendar.HoursPerDay;
                    switch (prop.Type)
                    {
                        case EnumTransitionType.Perish:
                            appendLine = true;

                            if (transitionLevel > 0)
                            {
                                nowSpoiling = true;
                                dsc.Append("\n" + Lang.Get("itemstack-perishable-spoiling", (int)Math.Round(transitionLevel * 100)));
                            }
                            else
                            {
                                if (freshHoursLeft / hoursPerday >= Api.World.Calendar.DaysPerYear)
                                {
                                    dsc.Append("\n" + Lang.Get("itemstack-perishable-fresh-years", Math.Round(freshHoursLeft / hoursPerday / Api.World.Calendar.DaysPerYear, 1)));
                                }
                                else if (freshHoursLeft > hoursPerday)
                                {
                                    dsc.Append("\n" + Lang.Get("itemstack-perishable-fresh-days", Math.Round(freshHoursLeft / hoursPerday, 1)));
                                }
                                else
                                {
                                    dsc.Append("\n" + Lang.Get("itemstack-perishable-fresh-hours", Math.Round(freshHoursLeft, 1)));
                                }
                            }
                            break;
                        case EnumTransitionType.Cure:
                            if (nowSpoiling) break;

                            appendLine = true;

                            if (transitionLevel > 0)
                            {
                                int hoursLeft = (int)((state.TransitionHours - (state.TransitionedHours - state.FreshHours)) / Block.Attributes["cureRate"].AsFloat(3f));

                                dsc.Append("\n" + Lang.Get("itemstack-curable-cured", Math.Round(transitionLevel * 100)));

                                if (hoursLeft > hoursPerday) dsc.Append(", " + Lang.Get("{0:0.#} days left", hoursLeft / hoursPerday));
                                else dsc.Append(", " + Lang.Get("{0:0} hrs left", hoursLeft));
                            }
                            else
                            {
                                if (freshHoursLeft / hoursPerday >= Api.World.Calendar.DaysPerYear)
                                {
                                    dsc.Append("\n" + Lang.Get("will cure in ") + Lang.Get("{0} years", Math.Round(freshHoursLeft / hoursPerday / Api.World.Calendar.DaysPerYear, 1)));
                                }
                                else if (freshHoursLeft > hoursPerday)
                                {
                                    dsc.Append("\n" + Lang.Get("itemstack-curable-duration-days", Math.Round(freshHoursLeft / hoursPerday, 1)));
                                }
                                else
                                {
                                    dsc.Append("\n" + Lang.Get("itemstack-curable-duration-hours", Math.Round(freshHoursLeft, 1)));
                                }
                            }
                            break;
                        case EnumTransitionType.Dry:
                            if (nowSpoiling) break;

                            appendLine = true;

                            if (transitionLevel > 0)
                            {
                                int hoursLeft = (int)((state.TransitionHours - (state.TransitionedHours - state.FreshHours)) / Block.Attributes["dryRate"].AsFloat(6f));

                                dsc.Append("\n" + Lang.Get("itemstack-dryable-dried", Math.Round(transitionLevel * 100)));

                                if (hoursLeft > hoursPerday) dsc.Append(", " + Lang.Get("{0:0.#} days left", hoursLeft / hoursPerday));
                                else dsc.Append(", " + Lang.Get("{0:0} hrs left", hoursLeft));
                            }
                            else
                            {
                                if (freshHoursLeft / hoursPerday >= Api.World.Calendar.DaysPerYear)
                                {
                                    dsc.Append("\n" + Lang.Get("will dry in ") + Lang.Get("{0} years", Math.Round(freshHoursLeft / hoursPerday / Api.World.Calendar.DaysPerYear, 1)));
                                }
                                else if (freshHoursLeft > hoursPerday)
                                {
                                    dsc.Append("\n" + Lang.Get("itemstack-dryable-duration-days", Math.Round(freshHoursLeft / hoursPerday, 1)));
                                }
                                else
                                {
                                    dsc.Append("\n" + Lang.Get("itemstack-dryable-duration-hours", Math.Round(freshHoursLeft, 1)));
                                }
                            }
                            break;
                    }
                }

                if (appendLine) dsc.AppendLine();
            }

            return dsc.ToString();
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);

            Wood = tree.GetString("wood");
            Metal = tree.GetString("metal");

            GenMesh();

            RedrawAfterReceivingTreeAttributes(worldForResolving);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetString("wood", Wood);
            tree.SetString("metal", Metal);
        }
    }
}
