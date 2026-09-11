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
    public class BlockBottleRack : Block, IContainedMeshSource
    {
        public TagSet plankWoodTag;
        public string Frame = "game:plank-oak";
        public string Interior = "game:plank-oak";

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            ReadOnlySpan<string> plankTag = ["plank-wood"];
            if (plankWoodTag.IsEmpty) api.CollectibleTagRegistry.TryCreateTagSet(out plankWoodTag, plankTag);
        }

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            var meshRefs = ObjectCacheUtil.GetOrCreate(capi, "aculinaryartillery:bottlerack-meshes", () => new Dictionary<string, MultiTextureMeshRef>());
            Frame = itemstack.Attributes.GetString("frame", "game:plank-oak");
            Interior = itemstack.Attributes.GetString("interior", "game:plank-oak");

            string key = Code + "-" + Frame + "-" + Interior;
            if (!meshRefs.TryGetValue(key, out var meshref))
            {
                var mesh = GenMesh(capi, itemstack);
                meshref = capi.Render.UploadMultiTextureMesh(mesh);
                meshRefs[key] = meshref;
            }

            renderinfo.ModelRef = meshref;
        }

        public MeshData? GenMesh(ItemSlot slot, ITextureAtlasAPI targetAtlas, BlockPos? forBlockPos = null)
        {
            if (slot.Empty) return null;
            return GenMesh(api as ICoreClientAPI, slot.Itemstack);
        }

        public MeshData GenMesh(ICoreClientAPI? capi, ItemStack stack)
        {
            if (capi == null) return new();

            CompositeTexture? frameTexture = null;
            CompositeTexture? interiorTexture = null;

            // STABLERACK
            string[] codeParts = stack.Collectible.Code.Path.Split("-");
            if (stack.Attributes.HasAttribute("frame") && stack.Attributes.HasAttribute("interior"))
            {
                frameTexture = capi.World.GetItem(stack.Attributes.GetString("frame", "game:plank-oak"))?.FirstTexture;
                interiorTexture = capi.World.GetItem(stack.Attributes.GetString("interior", "game:plank-oak"))?.FirstTexture;
            }
            else if (codeParts.Length > 2)
            {
                Dictionary<string, string[]> plankTypesByDomain = [];
                plankTypesByDomain["game"] = ["acacia", "baldcypress", "birch", "ebony", "kapok", "larch", "maple", "oak", "pine", "purpleheart", "redwood", "walnut", "aged", "veryaged"];
                plankTypesByDomain["wildcrafttree"] = ["douglasfir", "willow", "honeylocust", "bearnut", "poplar", "catalpa", "mahogany", "sal", "saxaul", "spruce", "sycamore", "elm", "beech", "eucalyptus", "cedar", "tuja", "redcedar", "yew", "kauri", "ginkgo", "dalbergia", "umnini", "banyan", "guajacum", "ghostgum", "ohia", "satinash", "bluemahoe", "jacaranda", "empresstree", "chlorociboria", "petrified", "fir", "tamanu", "spurgetree", "azobe", "leadwood", "linden", "horsechestnut", "tigerwood", "sapele", "ash", "mangrove", "charred"];

                foreach ((string domain, string[] plankTypes) in plankTypesByDomain)
                {
                    if (plankTypes.Contains(codeParts[1]))
                    {
                        frameTexture = capi.World.GetItem($"{domain}:plank-{codeParts[1]}")?.FirstTexture;
                    }

                    if (plankTypes.Contains(codeParts[2]))
                    {
                        interiorTexture = capi.World.GetItem($"{domain}:plank-{codeParts[2]}")?.FirstTexture;
                    }
                }
            }

            return GenMesh(capi, new BottleRackTextureSource(capi, frameTexture, interiorTexture));
        }

        public MeshData GenMesh(ICoreClientAPI? capi, BottleRackTextureSource textureSource)
        {
            AssetLocation shapeLoc = Shape.Base;
            if (capi?.Assets.TryGet(shapeLoc.CopyWithPathPrefixAndAppendixOnce("shapes/", ".json")) is not IAsset asset) return new();

            capi.Tesselator.TesselateShape("aculinaryartillery:" + Code.FirstCodePart(), asset.ToObject<Shape>(), out MeshData mesh, textureSource, new Vec3f(Shape.rotateX, Shape.rotateY, Shape.rotateZ));

            return mesh;
        }

        public string GetMeshCacheKey(ItemSlot slot)
        {
            if (slot.Itemstack is not ItemStack stack) return "unknown";
            return stack.Collectible.Code.ToShortString() + "-" + stack.Attributes.GetString("frame", "unknownframe") + "-" + stack.Attributes.GetString("interior", "unknowninterior");
        }

        public override bool DoPartialSelection(IWorldAccessor world, BlockPos pos)
        {
            return true;
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            return GetBlockEntity<BlockEntityBottleRack>(blockSel.Position)?.OnInteract(byPlayer, blockSel) ?? base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            ItemStack stack = base.OnPickBlock(world, pos);
            if (world.BlockAccessor.GetBlockEntity<BlockEntityBottleRack>(pos) is not BlockEntityBottleRack beRack) return stack;

            // STABLERACK; Don't need the if block anymore
            // stack.Attributes.SetString("frame", beRack.Frame);
            // stack.Attributes.SetString("interior", beRack.Frame);

            if (beRack.Frame != "" && beRack.Interior != "")
            {
                stack.Attributes.SetString("frame", beRack.Frame);
                stack.Attributes.SetString("interior", beRack.Interior);
            }

            return stack;
        }

        public override void OnCreatedByCrafting(ItemSlot[] allInputslots, ItemSlot outputSlot, IRecipeBase byRecipe)
        {
            if (byRecipe.Name?.FirstCodePart() == "bottlerack")
            {
                outputSlot.Itemstack?.Attributes.SetString("frame", allInputslots[0].Itemstack!.Collectible.Code);
                outputSlot.Itemstack?.Attributes.SetString("interior", allInputslots[1].Itemstack!.Collectible.Code);

                // This matching enforces the recipe shape to prevent the behavior
                // where all tagged ingredients accept any item with a matching tag
                // regardless of which ingredient it is without differentiating.

                AssetLocation[] woodtype1 = [
                    allInputslots[0].Itemstack!.Collectible.Code,
                    allInputslots[3].Itemstack!.Collectible.Code,
                    allInputslots[5].Itemstack!.Collectible.Code,
                    allInputslots[6].Itemstack!.Collectible.Code,
                    allInputslots[8].Itemstack!.Collectible.Code
                ];

                AssetLocation[] woodtype2 = [
                    allInputslots[1].Itemstack!.Collectible.Code,
                    allInputslots[4].Itemstack!.Collectible.Code,
                    allInputslots[7].Itemstack!.Collectible.Code
                ];

                if (woodtype1.Any(code => code != woodtype1[0]) || woodtype2.Any(code => code != woodtype2[0]))
                {
                    outputSlot.Itemstack = null;
                }
            }

            base.OnCreatedByCrafting(allInputslots, outputSlot, byRecipe);
        }

        public override string GetHeldItemName(ItemStack itemStack)
        {
            // STABLERACK
            if (!itemStack.Attributes.HasAttribute("frame") || !itemStack.Attributes.HasAttribute("interior"))
            {
                return Lang.Get($"aculinaryartillery:block-{itemStack.Collectible.FirstCodePart()}-legacy");
            }

            string woodName1 = Lang.Get("bottlerack-woodname-" + itemStack.Attributes["frame"]);
            string woodName2 = Lang.Get("bottlerack-woodname-" + itemStack.Attributes["interior"]).ToLowerInvariant();

            if (itemStack.Attributes["frame"] == itemStack.Attributes["interior"])
            {
                return Lang.Get("aculinaryartillery:block-" + itemStack.Collectible.FirstCodePart() + "-single", woodName1);
            }
            else
            {
                return Lang.Get("aculinaryartillery:block-" + itemStack.Collectible.FirstCodePart() + "-double", woodName1, woodName2);
            }
        }

        // STABLERACK
        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            ItemStack[] drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
            ItemStack? rack = null;
            int idx = 0;
            string[] codeParts = [];

            foreach ((int i, ItemStack drop) in drops.Index())
            {
                if (drop.Collectible is BlockBottleRack)
                {
                    rack = new ItemStack(world.GetBlock(new AssetLocation("aculinaryartillery:" + drop.Collectible.FirstCodePart() + "-north")));
                    if (world.BlockAccessor.GetBlockEntity<BlockEntityBottleRack>(pos) is not BlockEntityBottleRack be) return drops;
                    rack.Attributes.SetString("frame", be.Frame);
                    rack.Attributes.SetString("interior", be.Interior);
                    codeParts = drop.Collectible.Code.Path.ToString().Split("-");
                    idx = i;
                }
            }

            if (rack == null) return drops;

            if (codeParts.Length > 2)
            {
                Dictionary<string, string[]> plankTypesByDomain = [];
                plankTypesByDomain["game"] = ["acacia", "baldcypress", "birch", "ebony", "kapok", "larch", "maple", "oak", "pine", "purpleheart", "redwood", "walnut", "aged", "veryaged"];
                plankTypesByDomain["wildcrafttree"] = ["douglasfir", "willow", "honeylocust", "bearnut", "poplar", "catalpa", "mahogany", "sal", "saxaul", "spruce", "sycamore", "elm", "beech", "eucalyptus", "cedar", "tuja", "redcedar", "yew", "kauri", "ginkgo", "dalbergia", "umnini", "banyan", "guajacum", "ghostgum", "ohia", "satinash", "bluemahoe", "jacaranda", "empresstree", "chlorociboria", "petrified", "fir", "tamanu", "spurgetree", "azobe", "leadwood", "linden", "horsechestnut", "tigerwood", "sapele", "ash", "mangrove", "charred"];

                foreach ((string domain, string[] plankTypes) in plankTypesByDomain)
                {
                    if (plankTypes.Contains(codeParts[1]))
                    {
                        rack.Attributes.SetString("frame", $"{domain}:plank-{codeParts[1]}");
                    }

                    if (plankTypes.Contains(codeParts[2]))
                    {
                        rack.Attributes.SetString("interior", $"{domain}:plank-{codeParts[2]}");
                    }
                }
            }

            drops[idx] = rack;

            return drops;
        }
    }

    public class BottleRackTextureSource : ITexPositionSource
    {
        private readonly ICoreClientAPI capi;

        // Used for loading dynamic textures
        private readonly Dictionary<string, TextureAtlasPosition?> texturePositions = [];

        // Stored as a default to avoid a double lookup
        private readonly TextureAtlasPosition blockTexPos;

        public BottleRackTextureSource(ICoreClientAPI capi, CompositeTexture? frameTexture, CompositeTexture? interiorTexture)
        {
            this.capi = capi;
            if (frameTexture != null) texturePositions["frame"] = GetOrInsertTexture(capi.BlockTextureAtlas, "frame", frameTexture);
            if (interiorTexture != null) texturePositions["interior"] = GetOrInsertTexture(capi.BlockTextureAtlas, "interior", interiorTexture);

            if (capi.World.GetItem("game:plank-oak")?.FirstTexture is CompositeTexture plankTexture)
            {
                blockTexPos = GetOrInsertTexture(capi.BlockTextureAtlas, "fallback-plank-oak", plankTexture);
            }
            else
            {
                blockTexPos = capi.BlockTextureAtlas.UnknownTexturePosition;
            }
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
