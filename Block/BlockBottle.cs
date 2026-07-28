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
    public class BlockBottle : BlockStoppableContainer, IContainedMeshSource, IContainedCustomName, IAttachableToEntity
    {
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

        public override byte[]? GetLightHsv(IBlockAccessor blockAccessor, BlockPos pos, ItemStack? stack = null)
        {
            return GetContent(stack)?.Item?.LightHsv ?? base.GetLightHsv(blockAccessor, pos, stack);
        }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            attrAtta = IAttachableToEntity.FromAttributes(this);
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

        public override MeshData? GenMesh(ICoreClientAPI? capi, ItemStack stack, DynamicTextureSource textureSource, BlockPos? atBlockPos = null)
        {
            if (base.GenMesh(capi, stack, textureSource) is not MeshData mesh) return new();

            if (GetContent(stack) is ItemStack contentStack && (IsClear || IsTopOpened))
            {
                if (GetContainableProps(contentStack) is not WaterTightContainableProps props)
                {
                    ACulinaryArtillery.logger?.Error($"Bottle content stack {contentStack.Item.Code} does not have waterTightContainerProps and will not render any liquid.");
                    return mesh;
                }

                float fullness = contentStack.StackSize / props.ItemsPerLitre;
                Shape? shape = capi?.Assets.TryGet((props.IsOpaque ? ContentShapeLoc : LiquidContentShapeLoc).CopyWithPathPrefixAndAppendixOnce("shapes/", ".json"))?.ToObject<Shape>();
                if (shape == null) return mesh;
                bool isSideways = atBlockPos != null && GetBlockEntity<BlockEntityBottleRack>(atBlockPos) != null;
                shape = SliceFlattenedShape(shape.FlattenElementHierarchy(), fullness, isSideways);

                MeshData bottleMesh = mesh;
                if (props.Texture != null) textureSource.GetOrInsertTexture("content", props.Texture);

                capi?.Tesselator.TesselateShape("bottle contents", shape, out mesh, textureSource, new Vec3f(Shape.rotateX, Shape.rotateY, Shape.rotateZ));
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

        public string GetContainedInfo(ItemSlot inSlot)
        {
            float litres = GetCurrentLitres(inSlot.Itemstack);
            ItemStack? contentStack = GetContent(inSlot.Itemstack);

            if (contentStack == null || litres <= 0) return Lang.GetWithFallback("contained-empty-container", "{0} (Empty)", inSlot.Itemstack.GetName());

            string incontainername = Lang.Get(contentStack.Collectible.Code.Domain + ":incontainer-" + contentStack.Class.ToString().ToLowerInvariant() + "-" + contentStack.Collectible.Code.Path);

            return Lang.Get("contained-liquidcontainer-compact", inSlot.Itemstack.GetName(), litres, incontainername, PerishableInfoCompactContainer(api, inSlot));
        }

        #region HeldInteract

        protected override bool tryEatStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, ItemStack? content = null)
        {
            var pos = byEntity.Pos.AheadCopy(0.4f).XYZ;
            pos.X += byEntity.LocalEyePos.X;
            pos.Y += byEntity.LocalEyePos.Y - 0.4f;
            pos.Z += byEntity.LocalEyePos.Z;

            if (secondsUsed > 0.5f && (int)(30 * secondsUsed) % 7 == 1 && !slot.Empty && GetContent(slot.Itemstack) is ItemStack contentStack)
            {
                byEntity.World.SpawnCubeParticles(pos, contentStack, 0.3f, 4, 0.5f, (byEntity as EntityPlayer)?.Player);
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
    }
}
