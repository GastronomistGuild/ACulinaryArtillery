using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ACulinaryArtillery
{
    [DocumentAsJson]
    public class SapProperties
    {
        /// <summary>
        /// The chance (out of 1) for a drip to succeed on each tick.
        /// </summary>
        [DocumentAsJson("Optional")]
        public double dripChance = 1;

        /// <summary>
        /// The number of hours between each drip tick.
        /// </summary>
        [DocumentAsJson("Optional")]
        public double dripHours = 12;

        /// <summary>
        /// The liquid produced by this xylem.
        /// </summary>
        [DocumentAsJson("Recommended")]
        public string sap = "game:waterportion";

        /// <summary>
        /// The amount of liquid produced by this xylem per tick.
        /// </summary>
        [DocumentAsJson("Optional")]
        public float dripLitres = 0.01f;

        /// <summary>
        /// The amount of liquid produced by this xylem when stimulated by temperature shifts.
        ///
        /// Default is base litres * 2 so that there is always at least 1 extra item in the liquid stack.
        /// </summary>
        [DocumentAsJson("Optional", "dripLitres * 2")]
        public float boostedDripLitres = 0;

        /// <summary>
        /// The range of temperatures in which this xylem produces.
        /// </summary>
        [DocumentAsJson("Recommended")]
        public int[] temperatureRange = [-5, 40];

        public static SapProperties? ReadFrom(CollectibleObject obj)
        {
            if (obj.Attributes?["sapProperties"]?.AsObject<SapProperties>() is not SapProperties xylem) return null;

            if (xylem.boostedDripLitres == 0) xylem.boostedDripLitres = xylem.dripLitres * 2f;

            return xylem;
        }

        public static SapProperties? ReadFrom(ItemStack stack)
        {
            return ReadFrom(stack.Collectible);
        }
    }

    public class BlockEntitySpile : BlockEntity
    {
        public static HashSet<BlockPos> CachedSpiledTreeBlocks = [];
        public static Dictionary<BlockPos, Stack<BlockPos>> CachedTreesBySpilePos = [];
        public double timer;
        public RoomRegistry? roomreg = null;
        public double sapDripTimer;

        /// <summary>
        /// Once the spile starts dripping, stores the sap item being produced.
        /// </summary>
        public Item? sap = null;

        static readonly SimpleParticleProperties sapParticle;
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

        public BlockFacing Facing()
        {
            string[] parts = Block.Code.Path.Split('-');
            return BlockFacing.FromCode(parts[parts.Length - 1]);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            RegisterGameTickListener(SapDrip, 5000);
            if (timer == -1000) timer = Api.World.Calendar.TotalHours;

            if ((Block as BlockSpile)?.FindTree(Api.World.BlockAccessor, Pos.AddCopy(Facing())) is Stack<BlockPos> tree)
            {
                CachedSpiledTreeBlocks.AddRange(tree);
                CachedTreesBySpilePos.TryAdd(Pos, tree);
            }

            roomreg = api.ModLoader.GetModSystem<RoomRegistry>();
            if (Api.Side == EnumAppSide.Client) RegisterGameTickListener(dripParticleAndSound, 1000);
            if (sapDripTimer == -1000) sapDripTimer = Api.World.Calendar.TotalHours;
        }

        public override void OnBlockPlaced(ItemStack? byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);

            sapDripTimer = Api.World.Calendar.TotalHours;
        }

        protected float getGreenhouseTempBonus()
        {
            if (Api.World.BlockAccessor.GetRainMapHeightAt(Pos) > Pos.Y) // Fast pre-check
            {
                Room? room = roomreg?.GetRoomForPosition(Pos);
                int roomness = (room != null && room.SkylightCount > room.NonSkylightCount && room.ExitCount == 0) ? 1 : 0;
                if (roomness > 0) return 5;
            }
            return 0;
        }

        public enum EnumSpileClimateStatus
        {
            Inactive,
            Active,
            Boosted
        }

        public EnumSpileClimateStatus GetClimateStatus(SapProperties xylem, float baseDays)
        {
            // Can't read if region isn't loaded.
            if (Api.World.BlockAccessor.GetClimateAt(Pos, EnumGetClimateMode.WorldGenValues) is not ClimateCondition baseClimate) return EnumSpileClimateStatus.Inactive;

            // Check if we cross over a temperature range limit and back again using quarter-day increments.
            // If so, the spile is boosted for the day.
            // Otherwise, if the temps are in range, the spile is active for the day.
            List<float> tempsForToday = [];
            float greenhouseBonus = getGreenhouseTempBonus();
            for (int i = 0; i < 4; i++)
            {
                float checkDays = (int)baseDays + (0.25f * i);
                float temp = Api.World.BlockAccessor.GetClimateAt(Pos, baseClimate, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, checkDays).Temperature;
                temp += greenhouseBonus;
                tempsForToday.Add(temp);
            }

            bool lowerThanRange = false;
            bool higherThanRange = false;
            bool active = false;
            foreach (float temp in tempsForToday)
            {
                if (xylem.temperatureRange[0] < temp && temp < xylem.temperatureRange[1]) active = true;
                if (temp < xylem.temperatureRange[0]) lowerThanRange = true;
                if (xylem.temperatureRange[1] < temp) higherThanRange = true;
            }

            // Account for very small temperature ranges
            if (lowerThanRange && higherThanRange) active = true;

            if (active && (lowerThanRange || higherThanRange))
            {
                return EnumSpileClimateStatus.Boosted;
            }
            else if (active)
            {
                return EnumSpileClimateStatus.Active;
            }
            else
            {
                return EnumSpileClimateStatus.Inactive;
            }
        }

        public override void OnBlockRemoved()
        {
            if (CachedTreesBySpilePos.TryGetValue(Pos) is Stack<BlockPos> tree)
            {
                CachedTreesBySpilePos.Remove(Pos);
                foreach (BlockPos pos in tree)
                {
                    CachedSpiledTreeBlocks.Remove(pos);
                }
            }
        }

        public void SapDrip(float dt)
        {
            BlockPos containerPos = PosForward(0, -1, 0);
            if (Api.World.BlockAccessor.GetBlock(containerPos) is not BlockLiquidContainerBase container) return;
            if (SapProperties.ReadFrom(Api.World.BlockAccessor.GetBlock(PosForward(1, 0, 0))) is not SapProperties xylem) return;

            float totalOutput = 0;

            while (Api.World.Calendar.TotalHours - sapDripTimer >= xylem.dripHours)
            {
                // Add additional time to drip of up to 10% of the xylem's drip time.
                // Makes things a little more natural by not all ticking at the same time.
                sapDripTimer += xylem.dripHours + (Api.World.Rand.NextDouble() / 10 * xylem.dripHours);

                EnumSpileClimateStatus status = GetClimateStatus(xylem, (float)(sapDripTimer / Api.World.Calendar.HoursPerDay));
                bool active = status is EnumSpileClimateStatus.Active or EnumSpileClimateStatus.Boosted;
                bool boosted = status is EnumSpileClimateStatus.Boosted;

                if (Api.World.Rand.NextDouble() > xylem.dripChance || !active) continue;
                totalOutput += boosted ? xylem.boostedDripLitres : xylem.dripLitres;
            }

            if (Api.World.GetItem(xylem.sap) is not Item sap)
            {
                Api.Logger.Error($"Spile at {Pos} tried to drip invalid sap {xylem.sap}");
                return;
            }

            if (totalOutput == 0) return;

            dripParticleAndSound(dt);
            container.TryPutLiquid(containerPos, new(sap, 999999), totalOutput);
            this.sap = sap;

            MarkDirty(true);
        }


        private void dripParticleAndSound(float dt)
        {
            if (sap == null || Api is not ICoreClientAPI capi) return;

            // 5 seconds per drip on average, randomized for natural appearance.
            if (Api.World.Rand.Next(0, 6) != 0) return;
            if (capi.World.BlockAccessor.GetBlock(Pos) is not BlockSpile spile) return;

            // Only play the sound if a container is present.
            if (Api.World.BlockAccessor.GetBlock(PosForward(0, -1, 0)) is BlockLiquidContainerBase)
            {
                AssetLocation sound = new($"{Block.Code.Domain}:sounds/block/{Block.FirstCodePart()}/drip*");
                Api.World.PlaySoundAt(sound, Pos.X, Pos.Y, Pos.Z, range: 5);
            }

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
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            mesher.AddMeshData(GetOrCreateMesh(tessThreadTesselator));
            return true;
        }

        public MeshData? GetOrCreateMesh(ITesselatorAPI tessThreadTesselator)
        {
            Dictionary<string, MeshData> meshes = ObjectCacheUtil.GetOrCreate(Api, $"{Block.Code.Domain}:blockspileMeshes", () => new Dictionary<string, MeshData>());

            if (Api.World.BlockAccessor.GetBlock(Pos) is not BlockSpile spile) return null;

            string key = Block.Code;
            if (sap != null) key += "-" + sap.Code;

            if (meshes.TryGetValue(key, out MeshData? mesh))
            {
                return mesh;
            }

            return meshes[key] = spile.GenMesh(Api as ICoreClientAPI, tessThreadTesselator, sap);
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
            if (tree.GetString("sap") is string sap)
            {
                this.sap = worldAccessForResolve.GetItem(sap);
            }
        }

        public BlockPos PosForward(int offset, int height, int otheraxis)
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
