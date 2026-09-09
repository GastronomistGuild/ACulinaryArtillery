using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ACulinaryArtillery
{
    public class BlockSpile : Block
    {
        public virtual AssetLocation EmptyShapeLoc => "aculinaryartillery:shapes/block/spile";
        public virtual AssetLocation DripShapeLoc => "aculinaryartillery:shapes/block/spiledrip";

        protected MeshData? mesh = null;

        // Using CanPlaceBlock causes the error message to be overriden by BlockBehaviorHorizontalAttachable
        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            Block? attachingTo = world.BlockAccessor.GetBlock(blockSel.Position.AddCopy(blockSel.Face, -1));
            if (blockSel.Face.IsHorizontal && attachingTo?.Attributes?["sapProperties"]?.AsObject<SapProperties>() == null)
            {
                failureCode = "notspileable";
                return false;
            }

            if (blockSel.Face.IsHorizontal && HasSpile(world.BlockAccessor, blockSel.Position.Copy(), blockSel.Face))
            {
                failureCode = "alreadyhasspile";
                return false;
            }

            return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
        }

        bool HasSpile(IBlockAccessor blockAccess, BlockPos pos, BlockFacing face)
        {
            pos.Add(face, -2); // check the opposite side of the log
            if (blockAccess.GetBlock(pos) is BlockSpile) return true;

            pos.Add(face, 1); // move back into the log
            face = face.GetCW(); // turn 90° clockwise
            pos.Add(face, -1); // move to the next side
            if (blockAccess.GetBlock(pos) is BlockSpile) return true;

            pos.Add(face, 2); // and finally move back through the log to the third side
            if (blockAccess.GetBlock(pos) is BlockSpile) return true;

            return false;
        }

        public MeshData GenMesh(ICoreClientAPI? capi, Item? sap = null)
        {
            AssetLocation shapeLoc = sap != null ? DripShapeLoc : EmptyShapeLoc;
            if (capi?.Assets.TryGet(shapeLoc.CopyWithPathPrefixAndAppendixOnce("shapes/", ".json")) is not IAsset asset) return new MeshData();

            CompositeTexture? sapTexture = sap?.FirstTexture;

            capi.Tesselator.TesselateShape("aculinaryartillery:spile", asset.ToObject<Shape>(), out MeshData mesh, new SpileTextureSource(capi, this, sapTexture), new Vec3f(Shape.rotateX, Shape.rotateY, Shape.rotateZ));

            return mesh;
        }
    }

    public class SpileTextureSource : ITexPositionSource
    {
        private readonly ICoreClientAPI capi;

        // Used for loading dynamic textures
        private readonly Dictionary<string, TextureAtlasPosition?> texturePositions = [];

        // Stored as a default to avoid a double lookup
        private readonly TextureAtlasPosition blockTexPos;

        public SpileTextureSource(ICoreClientAPI capi, Block spile, CompositeTexture? sapTexture)
        {
            this.capi = capi;
            if (sapTexture != null) texturePositions["sap"] = GetOrInsertTexture(capi.BlockTextureAtlas, "sap", sapTexture);
            texturePositions["material"] = capi.BlockTextureAtlas.GetPosition(spile, "material");

            blockTexPos = capi.BlockTextureAtlas.GetPosition(spile, "material");
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
