using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ACulinaryArtillery
{
    public class BlockEntityBottleRack : BlockEntityDisplay, ITexPositionSource
    {
        InventoryGeneric inventory;
        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "bottlerack";
        public override string AttributeTransformCode => "onBottlerackTransform";
        private readonly int slotCount = 16;

        public string Frame = "";
        public string Interior = "";

        MeshData? mesh = null;

        public BlockEntityBottleRack()
        {
            inventory = new InventoryGeneric(slotCount, null, null);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            if (Api.World.BlockAccessor.GetBlock(Pos) is BlockBottleRack bottleRack)
            {
                bottleRack.Frame = Frame;
                bottleRack.Interior = Interior;
            }

            GenMesh();
        }

        public override void OnBlockPlaced(ItemStack itemStack = null!)
        {
            base.OnBlockPlaced(itemStack);

            Frame = itemStack?.Attributes.GetString("frame") ?? "game:plank-oak";
            Interior = itemStack?.Attributes.GetString("interior") ?? "game:plank-oak";

            if (Api.World.BlockAccessor.GetBlock(Pos) is BlockBottleRack bottleRack)
            {
                bottleRack.Frame = Frame;
                bottleRack.Interior = Interior;
            }

            GenMesh();
        }

        public void GenMesh()
        {
            if (Block is not BlockBottleRack bottleRack || Api is not ICoreClientAPI capi) return;

            CompositeTexture? frameTexture = capi.World.GetItem(Frame)?.FirstTexture;
            CompositeTexture? interiorTexture = capi.World.GetItem(Interior)?.FirstTexture;

            // STABLERACK
            string[] codeParts = bottleRack.Code.Path.Split("-");
            if (frameTexture == null && interiorTexture == null && codeParts.Length > 2)
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

            BottleRackTextureSource textureSource = new(capi, frameTexture, interiorTexture);

            mesh = bottleRack.GenMesh(capi, textureSource);
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            mesher.AddMeshData(mesh);
            return base.OnTesselation(mesher, tessThreadTesselator);
        }

        internal bool OnInteract(IPlayer byPlayer, BlockSelection blockSel)
        {
            var playerSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (playerSlot.Empty) return TryTake(byPlayer, blockSel);
            else
            {
                var colObj = playerSlot.Itemstack.Collectible;

                BlockBottle? bottle = colObj as BlockBottle;
                float fullness = bottle?.GetCurrentLitres(playerSlot.Itemstack) ?? 0;
                if (bottle?.IsTopOpened == true && fullness > 0.2f)
                {
                    (Api as ICoreClientAPI)?.TriggerIngameError(this, "bottletoofull", Lang.Get("aculinaryartillery:bottle-toofullforrack"));
                    return false;
                }

                if (colObj.Attributes?["bottlerackable"].AsBool() == true && TryPut(playerSlot, blockSel))
                {
                    if (Block?.Sounds?.Place != null)
                    {
                        Api.World.PlaySoundAt(Block.Sounds.Place, byPlayer.Entity, byPlayer);
                    }
                    else
                    {
                        Api.World.PlaySoundAt(new AssetLocation("game:sounds/player/build"), byPlayer.Entity, byPlayer, true, 16, 1f);
                    }
                    return true;
                }
            }
            return false;
        }

        private bool TryPut(ItemSlot slot, BlockSelection blockSel)
        {
            var index = blockSel.SelectionBoxIndex;

            if (inventory[index].Empty && slot.TryPutInto(Api.World, inventory[index]) > 0)
            {
                updateMesh(index);
                MarkDirty(true);
                return true;
            }

            return false;
        }

        private bool TryTake(IPlayer byPlayer, BlockSelection blockSel)
        {
            var index = blockSel.SelectionBoxIndex;

            if (!inventory[index].Empty)
            {
                var stack = inventory[index].TakeOut(1);

                if (byPlayer.InventoryManager.TryGiveItemstack(stack))
                {
                    if (stack.Block?.Sounds?.Place != null)
                    {
                        Api.World.PlaySoundAt(stack.Block.Sounds.Place, byPlayer.Entity, byPlayer);
                    }
                    else
                    {
                        Api.World.PlaySoundAt(new AssetLocation("game:sounds/player/build"), byPlayer.Entity, byPlayer, true, 16, 1f);
                    }
                }
                else Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));

                updateMesh(index);
                MarkDirty(true);
                return true;
            }

            return false;
        }

        protected override float[][] genTransformationMatrices()
        {
            float[][] tfMatrices = new float[slotCount][];
            bool corner = Block.FirstCodePart() == "bottlerackcorner";

            for (int slot = 0; slot < slotCount; slot++)
            {
                double col = slot % 4;
                double y = Math.Floor(slot / 4f) / 4f + 0.125f;

                (double x, double z, float rot) = (corner, col) switch
                {
                    (true, 1) => (col / 4 - 0.38, -0.22f, Block.Shape.rotateY - 45),
                    (true, 2) => (col / 4 - 0.37, -0.22f, Block.Shape.rotateY - 45),
                    (true, 3) => (col / 4 - 0.37375, -0.42f, Block.Shape.rotateY - 90),
                    (_, _) => (col / 4 - 0.37625, -0.42f, Block.Shape.rotateY)
                };


                tfMatrices[slot] =
                    new Matrixf()
                    .Translate(0.5f, 0, 0.5f)
                    .RotateYDeg(rot)
                    .Translate(x, y, z)
                    .RotateXDeg(90)
                    .Scale(0.99f, 0.99f, 0.99f)
                    .Translate(-0.5f, 0, -0.5f)
                    .Values
                ;
            }

            return tfMatrices;
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
        {
            sb.AppendLine(Lang.Get("Suitable spot for liquid storage."));
            sb.AppendLine();

            if (forPlayer?.CurrentBlockSelection == null) return;

            var index = forPlayer.CurrentBlockSelection.SelectionBoxIndex;
            if (!inventory[index].Empty)
            {
                var slot = inventory[index];
                sb.AppendLine(slot.Itemstack?.Collectible.GetCollectibleInterface<IContainedCustomName>()?.GetContainedInfo(slot) ?? slot.GetStackName() ?? Lang.Get("unknown"));
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);

            Frame = tree.GetString("frame");
            Interior = tree.GetString("interior");

            GenMesh();

            RedrawAfterReceivingTreeAttributes(worldForResolving);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetString("frame", Frame);
            tree.SetString("interior", Interior);
        }
    }
}
