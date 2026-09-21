using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ACulinaryArtillery
{
    public class BlockMeatHooks : Block, IContainedMeshSource
    {
        public string Wood = "game:plank-oak";
        public string Metal = "aculinaryartillery:bighook-copper";

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            // Todo: Add interaction help
        }

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            var meshRefs = ObjectCacheUtil.GetOrCreate(capi, "aculinaryartillery:meathook-meshes", () => new Dictionary<string, MultiTextureMeshRef>());
            Wood = itemstack.Attributes.GetString("wood", "game:plank-oak") ?? "game:plank-oak";
            Metal = itemstack.Attributes.GetString("metal", "aculinaryartillery:bighook-copper") ?? "aculinaryartillery:bighook-copper";

            string key = Code + "-" + Wood + "-" + Metal;
            if (!meshRefs.TryGetValue(key, out var meshref))
            {
                capi.Logger.Debug($"generating mesh for key {key}");
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

            CompositeTexture? woodTexture = capi.World.GetItem(stack.Attributes.GetString("wood", "game:plank-oak") ?? "game:plank-oak")?.FirstTexture;
            CompositeTexture? metalTexture = capi.World.GetItem(stack.Attributes.GetString("metal", "aculinaryartillery:bighook-copper") ?? "aculinaryartillery:bighook-copper")?.FirstTexture;

            DynamicTextureSource textureSource = new(capi, this, "wood");
            if (woodTexture != null) textureSource.GetOrInsertTexture("wood", woodTexture);
            if (metalTexture != null) textureSource.GetOrInsertTexture("metal", metalTexture);

            return GenMesh(capi, textureSource);
        }

        public MeshData GenMesh(ICoreClientAPI? capi, DynamicTextureSource textureSource)
        {
            AssetLocation shapeLoc = Shape.Base;
            if (capi?.Assets.TryGet(shapeLoc.CopyWithPathPrefixAndAppendixOnce("shapes/", ".json")) is not IAsset asset) return new();

            capi.Tesselator.TesselateShape(Code, asset.ToObject<Shape>(), out MeshData mesh, textureSource, new Vec3f(Shape.rotateX, Shape.rotateY, Shape.rotateZ));

            return mesh;
        }

        public string GetMeshCacheKey(ItemSlot slot)
        {
            if (slot.Itemstack is not ItemStack stack) return "unknown";
            api.Logger.Debug("mesh cache key: " + stack.Collectible.Code.ToShortString() + "-" + stack.Attributes.GetString("wood", "game:plank-oak") + "-" + stack.Attributes.GetString("metal", "aculinaryartillery:bighook-copper"));
            return stack.Collectible.Code.ToShortString() + "-" + stack.Attributes.GetString("wood", "unknownwood") + "-" + stack.Attributes.GetString("metal", "unknownmetal");
        }

        public override bool DoPartialSelection(IWorldAccessor world, BlockPos pos)
        {
            return true;
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            return GetBlockEntity<BlockEntityMeatHooks>(blockSel.Position)?.OnInteract(byPlayer, blockSel) ??
                   base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            ItemStack stack = new ItemStack(world.GetBlock(new AssetLocation(Code.Domain + ":" + FirstCodePart() + "-east")));

            if (world.BlockAccessor.GetBlockEntity<BlockEntityMeatHooks>(pos) is not BlockEntityMeatHooks beRack) return stack;

            // STABLERACK; Don't need the if block anymore
            // stack.Attributes.SetString("wood", hooks.Wood);
            // stack.Attributes.SetString("metal", hooks.Metal);

            if (beRack.Wood != "" && beRack.Metal != "")
            {
                stack.Attributes.SetString("wood", beRack.Wood);
                stack.Attributes.SetString("metal", beRack.Metal);
            }

            return stack;
        }

        public override void OnCreatedByCrafting(ItemSlot[] allInputslots, ItemSlot outputSlot, IRecipeBase byRecipe)
        {
            bool matches = false;
            for (int i = 0; i < allInputslots.Length; i++)
            {
                if (i != 2 && i != 5 && i != 8
                    && allInputslots[i]?.Itemstack?.Collectible.Tags.Overlaps(BlockBottleRack.plankWoodTag) == true
                    && allInputslots[i + 1]?.Itemstack?.Collectible.Code == allInputslots[i]?.Itemstack?.Collectible.Code
                    && allInputslots[i + 3]?.Itemstack?.Collectible.FirstCodePart() == "bighook"
                    && allInputslots[i + 4]?.Itemstack?.Collectible.FirstCodePart() == "bighook")
                {
                    outputSlot.Itemstack?.Attributes.SetString("wood", allInputslots[i].Itemstack!.Collectible.Code);
                    outputSlot.Itemstack?.Attributes.SetString("metal", allInputslots[i + 3].Itemstack!.Collectible.Code);

                    matches = true;
                    break;
                }
            }

            if (!matches) outputSlot.Itemstack = null;

            base.OnCreatedByCrafting(allInputslots, outputSlot, byRecipe);
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
                if (drop.Collectible is BlockMeatHooks)
                {
                    rack = new ItemStack(world.GetBlock(new AssetLocation("aculinaryartillery:" + drop.Collectible.FirstCodePart() + "-east")));
                    rack.Attributes.SetString("wood", Wood);
                    rack.Attributes.SetString("metal", Metal);
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
                        rack.Attributes.SetString("wood", $"{domain}:plank-{codeParts[1]}");
                    }
                }

                rack.Attributes.SetString("metal", "aculinaryartillery:bighook-" + codeParts[2]);
            }

            drops[idx] = rack;

            return drops;
        }
    }
}
