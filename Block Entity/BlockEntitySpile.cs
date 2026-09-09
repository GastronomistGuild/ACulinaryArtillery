using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ACulinaryArtillery
{
    public class SapProperties
    {
        public double dripChance = 1;
        public double dripTime = 12;
        public int[] seasons = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
        public string sap = "game:waterportion";
        public int dripCount = 1;
    }

    public class BlockEntitySpile : BlockEntity
    {
        public double sapDripTimer;

        /// <summary>
        /// Once the spile starts dripping, stores the sap item being produced.
        /// </summary>
        public Item? sap = null;

        /// <summary>
        /// The number of drips left to generate particles for.
        ///
        /// Reflects the amount of sap actually generated.
        /// </summary>
        private int todoDripCount = 0;

        static SimpleParticleProperties sapParticle;
        static BlockEntitySpile()
        {
            sapParticle = new SimpleParticleProperties()
            {
                MinVelocity = new Vec3f(-0.04f, 0, -0.04f),
                AddVelocity = new Vec3f(0.08f, 0, 0.08f),
                addLifeLength = 0f,
                LifeLength = 0.2f,
                MinQuantity = 1f,
                GravityEffect = 0.5f,
                SelfPropelled = true,
                MinSize = 0.1f,
                MaxSize = 0.2f
            };
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            RegisterGameTickListener(SapDrip, 5000);
            RegisterGameTickListener(dripParticleAndSound, 75);
            if (sapDripTimer == -1000) sapDripTimer = Api.World.Calendar.TotalHours;
        }

        public override void OnBlockPlaced(ItemStack? byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);

            sapDripTimer = Api.World.Calendar.TotalHours;
        }

        public void SapDrip(float dt)
        {
            BlockPos containerpos = posForward(0, -1, 0);
            if (Api.World.BlockAccessor.GetBlock(containerpos) is not BlockLiquidContainerBase container) return;
            if (Api.World.BlockAccessor.GetBlock(posForward(1, 0, 0))?.Attributes?["sapProperties"]?.AsObject<SapProperties>() is not SapProperties xylem) return;

            while (Api.World.Calendar.TotalHours - sapDripTimer >= xylem.dripTime)
            {
                // Add additional time to drip of up to 10% of the xylem's drip time.
                // Makes things a little more natural by not all ticking at the same time.
                sapDripTimer += xylem.dripTime - (Api.World.Rand.NextDouble() / 10 * xylem.dripTime);

                if (Api.World.Rand.NextDouble() > xylem.dripChance || !xylem.seasons.Contains(GetMonth(sapDripTimer))) return;

                if (Api.World.GetItem(xylem.sap) is not Item sap)
                {
                    Api.Logger.Error($"Spile at {Pos} tried to drip invalid sap {xylem.sap}");
                    return;
                }

                todoDripCount += Api.World.Rand.Next(1, 4);

                this.sap = sap;
            }


            if (todoDripCount == 0) return;

            ItemStack dripStack = new(Api.World.GetItem(xylem.sap));
            dripStack.StackSize = todoDripCount;
            container.TryPutLiquid(containerpos, dripStack, dripStack.StackSize);

            // Avoid showing a ton of particles when fast forwarding
            todoDripCount %= 4;
        }


        private void dripParticleAndSound(float dt)
        {
            if (todoDripCount == 0 || sap == null || Api is not ICoreClientAPI capi) return;
            if (capi.World.BlockAccessor.GetBlock(Pos) is not BlockSpile spile) return;

            AssetLocation sound = new($"aculinaryartillery:sounds/block/spile/drip*");
            Api.World.PlaySoundAt(sound, Pos.X, Pos.Y, Pos.Z, range: 5);

            sapParticle.Color = capi.ItemTextureAtlas.GetRandomColor(capi.ItemTextureAtlas.GetPosition(sap, sap.Code), Api.World.Rand.Next(TextureAtlasPosition.RndColorsLength));

            if (BlockFacing.FromCode(spile.LastCodePart()) is not BlockFacing face) return;

            Vec3d minPos = face.Plane.Startd.Add(-0.45, 0, -0.5);
            Vec3d maxPos = face.Plane.Endd.Add(-0.45, 0, -0.5);

            minPos.Mul(2 / 16f);
            maxPos.Mul(2 / 16f);

            minPos.Add(face.Normalf.X * 1.2f / 16f, 0, face.Normalf.Z * 1.2f / 16f);
            maxPos.Add(face.Normalf.X * 1.2f / 16f, 0, face.Normalf.Z * 1.2f / 16f);

            sapParticle.MinPos = minPos;
            sapParticle.AddPos = maxPos.Sub(minPos);
            sapParticle.MinPos.Add(Pos).Add(0.45, -0.1, 0.5);

            sapParticle.WithTerrainCollision = false;

            Api.World.SpawnParticles(sapParticle);
            todoDripCount--;
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            mesher.AddMeshData(GetOrCreateMesh());
            return false;
        }

        public MeshData? GetOrCreateMesh()
        {
            Dictionary<string, MeshData> meshes = ObjectCacheUtil.GetOrCreate(Api, "aculinaryartillery:blockspileMeshes", () => new Dictionary<string, MeshData>());

            if (Api.World.BlockAccessor.GetBlock(Pos) is not BlockSpile spile) return null;

            string key = Block.Code;
            if (sap != null) key += "-" + sap.Code;

            if (meshes.TryGetValue(key, out MeshData? mesh))
            {
                return mesh;
            }

            return meshes[key] = spile.GenMesh(Api as ICoreClientAPI, sap);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetDouble("timer", sapDripTimer);
            tree.SetString("sap", sap?.Code);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            sapDripTimer = tree.GetDouble("timer", -1000);
            sap = worldAccessForResolve.GetItem(tree.GetString("sap"));
        }

        public int GetMonth(double pastTime)
        {
            int pastDay = Api.World.Calendar.DayOfYear - (int)((Api.World.Calendar.TotalHours - pastTime) / Api.World.Calendar.HoursPerDay);
            if (pastDay < 0) pastDay += Api.World.Calendar.DaysPerYear;

            int month = (pastDay / Api.World.Calendar.DaysPerMonth);
            return (Api.World.Calendar.GetHemisphere(Pos) == EnumHemisphere.North ? month : (month + 6) % 12) + 1;
        }

        public BlockPos posForward(int offset, int height, int otheraxis)
        {
            return Block.Shape.rotateY switch
            {
                0 => Pos.AddCopy(otheraxis, height, -offset),
                90 => Pos.AddCopy(-offset, height, otheraxis),
                180 => Pos.AddCopy(otheraxis, height, offset),
                270 => Pos.AddCopy(offset, height, otheraxis),
                _ => Pos
            };
        }
    }
}
