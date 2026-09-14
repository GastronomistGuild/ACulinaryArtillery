using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace ACulinaryArtillery
{
    public class BlockEntityMixingBowl : BlockEntityOpenableContainer
    {
        ILoadedSound? ambientSound;

        internal InventoryMixingBowl inventory;

        // For how long the current ore has been mixing
        public float inputMixTime;
        public float prevInputMixTime;
        public int CapacityLitres { get; set; }
        public int StackCapcity { get; set; }

        //For automation
        public bool invLocked;
        public ItemStack?[] lockedInv = new ItemStack[6];


        GuiDialogBlockEntityMixingBowl? clientDialog;
        MixingBowlTopRenderer? renderer;
        bool automated;
        BEBehaviorMPConsumer? mpc;


        // Server side only
        Dictionary<string, long> playersMixing = [];
        // Client and serverside
        int quantityPlayersMixing;

        int nowOutputFace;

        #region Getters

        public float MixSpeed
        {
            get
            {
                if (quantityPlayersMixing > 0) return 1f;

                if (automated && mpc?.Network != null) return mpc.TrueSpeed;

                return 0;
            }
        }


        MeshData? mixingBowlBaseMesh
        {
            get
            {
                Api.ObjectCache.TryGetValue(Block.FirstCodePart() + "-basemesh-" + Block.Variant["color"] + "-" + Block.Variant["material"], out object? value);
                return value as MeshData;
            }
            set => Api.ObjectCache[Block.FirstCodePart() + "-basemesh-" + Block.Variant["color"] + "-" + Block.Variant["material"]] = value;
        }

        MeshData? mixingBowlTopMesh
        {
            get
            {
                Api.ObjectCache.TryGetValue(Block.FirstCodePart() + "-topmesh-" + Block.Variant["color"] + "-" + Block.Variant["material"], out object? value);
                return value as MeshData;
            }
            set => Api.ObjectCache[Block.FirstCodePart() + "-topmesh-" + Block.Variant["color"] + "-" + Block.Variant["material"]] = value;
        }

        #endregion

        #region Config

        public virtual float MaxMixingTime => 4;
        public override string InventoryClassName => Block.FirstCodePart();
        public virtual string DialogTitle => Lang.Get($"{Block.Code.Domain}:{Block.FirstCodePart()}-dialogtitle");
        public override InventoryBase Inventory => inventory;

        #endregion


        public BlockEntityMixingBowl()
        {
            inventory = new InventoryMixingBowl(null, null, this);
            inventory.SlotModified += OnSlotModifid;
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            CapacityLitres = Block.Attributes?["capacityLitres"]?.AsInt(CapacityLitres) ?? CapacityLitres;
            StackCapcity = Block.Attributes?["maxSlotStackSize"]?.AsInt(24) ?? StackCapcity;
            inventory.LateInitialize(Block.FirstCodePart() + "-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z, api);

            RegisterGameTickListener(Every100ms, 100);
            RegisterGameTickListener(Every500ms, 500);



            if (api is ICoreClientAPI capi)
            {
                ambientSound ??= capi.World.LoadSound(new SoundParams()
                {
                    Location = new AssetLocation($"{Block.Code.Domain}:sounds/block/mixing.ogg"),
                    ShouldLoop = true,
                    Position = Pos.ToVec3f().Add(0.5f, 0.25f, 0.5f),
                    DisposeOnFinish = false,
                    Volume = 0.75f
                });

                renderer = new(capi, Pos, GenMesh("top") ?? new()) { mechPowerPart = mpc };
                if (automated)
                {
                    renderer.ShouldRender = true;
                    renderer.ShouldRotateAutomated = true;
                }

                capi.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque, Block.FirstCodePart());

                mixingBowlBaseMesh ??= GenMesh("base");
                mixingBowlTopMesh ??= GenMesh("top");
            }
        }

        public override void CreateBehaviors(Block block, IWorldAccessor worldForResolve)
        {
            base.CreateBehaviors(block, worldForResolve);

            mpc = GetBehavior<BEBehaviorMPConsumer>();
            if (mpc != null)
            {
                mpc.OnConnected = () =>
                {
                    automated = true;
                    quantityPlayersMixing = 0;
                    if (renderer != null)
                    {
                        renderer.ShouldRender = true;
                        renderer.ShouldRotateAutomated = true;
                    }
                };

                mpc.OnDisconnected = () =>
                {
                    automated = false;
                    if (renderer != null)
                    {
                        renderer.ShouldRender = false;
                        renderer.ShouldRotateAutomated = false;
                    }
                };
            }
        }

        private void Every100ms(float dt)
        {
            float mixSpeed = MixSpeed;

            if (Api.Side == EnumAppSide.Client)
            {
                if (ambientSound != null && mpc != null && automated)
                {
                    ambientSound.SetPitch((0.5f + mpc.TrueSpeed) * 0.9f);
                    ambientSound.SetVolume(Math.Min(1f, mpc.TrueSpeed * 3f));
                }

                return; // Only tick on the server and merely sync to client
            }

            // Use up fuel
            if (CanMix && mixSpeed > 0)
            {
                inputMixTime += dt * mixSpeed;

                if (inputMixTime >= MaxMixingTime)
                {
                    mixInput();
                    inputMixTime = 0;
                }

                MarkDirty();
            }
        }

        private void mixInput()
        {
            CookingRecipe? mrecipe = GetMatchingMixingRecipe(Api.World, IngredStacks);
            DoughRecipe? drecipe = GetMatchingDoughRecipe(Api.World, IngredSlots);
            if (mrecipe == null && drecipe == null) return;

            ItemStack? mixedStack = null;
            int servings = 0;
            // Clone, don't alias: IngredStacks hands back the live inventory stacks, and the meal path
            // below mutates them. We may still bail out before consuming anything, so that work has to
            // happen on copies for now.
            ItemStack[] stacks = [.. IngredStacks.Select(stack => stack.Clone())];
            if (mrecipe != null)
            {
                string? mealBlockCode = InputStack.ItemAttributes?["mealBlockCode"].AsString();
                if (mealBlockCode == null) return;

                Block mealBlock = Api.World.GetBlock(new AssetLocation(mealBlockCode));
                mixedStack = new ItemStack(mealBlock);
                servings = mrecipe.GetQuantityServings(stacks);

                for (int i = 0; i < stacks.Length; i++)
                {
                    CookingRecipeIngredient? ingred = mrecipe.GetIngrendientFor(stacks[i]);
                    ItemStack? cookedStack = ingred?.GetMatchingStack(stacks[i])?.CookedStack?.ResolvedItemstack.Clone();
                    if (cookedStack != null) stacks[i] = cookedStack;
                }

                // Carry over and set perishable properties
                TransitionableProperties? cookedPerishProps = mrecipe.PerishableProps?.Clone();
                cookedPerishProps?.TransitionedStack.Resolve(Api.World, "cooking container perished stack");

                if (cookedPerishProps != null) CollectibleObject.CarryOverFreshness(Api, IngredSlots, stacks, cookedPerishProps);

                for (int i = 0; i < stacks.Length; i++)
                {
                    stacks[i].StackSize /= servings; // This makes sure that there's only one serving worth of items in the pot, which is needed for rot
                }

                ((BlockCookedContainer)mealBlock).SetContents(mrecipe.Code, servings, mixedStack, stacks);
            }
            else if (drecipe != null) mixedStack = drecipe.TryGetOutput(IngredSlots);

            if (mixedStack == null) return;

            // Work out where the result goes BEFORE consuming anything. Everything above only built
            // the output stack; the ingredients are still untouched at this point, so bailing out here
            // costs nothing but the mixing cycle. This mirrors the vanilla quern: if the output slot is
            // full and the side we'd eject onto is blocked, the cycle is wasted rather than the food.
            if (OutputSlot.Itemstack == null) OutputSlot.Itemstack = mixedStack;
            else
            {
                int mergableQuantity = OutputSlot.Itemstack.Collectible.GetMergableQuantity(OutputSlot.Itemstack, mixedStack, EnumMergePriority.AutoMerge);

                if (mergableQuantity > 0) OutputSlot.Itemstack.StackSize += mixedStack.StackSize;
                else
                {
                    BlockFacing face = BlockFacing.HORIZONTALS[nowOutputFace];
                    nowOutputFace = (nowOutputFace + 1) % 4;

                    Block block = Api.World.BlockAccessor.GetBlock(Pos.AddCopy(face));
                    if (block.Replaceable < 6000) return; // Blocked side: keep the ingredients, waste the cycle
                    Api.World.SpawnItemEntity(mixedStack, Pos.ToVec3d().Add(0.5 + face.Normalf.X * 0.7, 0.75, 0.5 + face.Normalf.Z * 0.7), new Vec3d(face.Normalf.X * 0.02f, 0, face.Normalf.Z * 0.02f));
                }
            }

            // The output is committed, so it's safe to consume the ingredients
            if (mrecipe != null)
            {
                inventory[0].TakeOut(1);
                inventory[0].MarkDirty();
                for (var i = 0; i < IngredSlots.Length; i++)
                {
                    //the recipe must be valid at this point, so can't we just take out everything? Like so
                    if (IngredSlots[i].Itemstack != null)
                    {
                        IngredSlots[i].TakeOut(IngredSlots[i].Itemstack.StackSize);
                        IngredSlots[i].MarkDirty();
                    }
                }
            }
            else drecipe!.ConsumeIngredients(IngredSlots, mixedStack);

            OutputSlot.MarkDirty();
        }

        // Sync to client every 500ms
        private void Every500ms(float dt)
        {
            if (Api.Side == EnumAppSide.Server && (MixSpeed > 0 || prevInputMixTime != inputMixTime))
            {
                if (inventory[0].Itemstack?.Collectible.GrindingProps != null) MarkDirty(); //don't spam update packets when empty, as inputMixTime is irrelevant when empty
            }

            prevInputMixTime = inputMixTime;

            foreach (var val in playersMixing)
            {
                long ellapsedMs = Api.World.ElapsedMilliseconds;
                if (ellapsedMs - val.Value > 1000)
                {
                    playersMixing.Remove(val.Key);
                    break;
                }
            }
        }

        public void SetPlayerMixing(IPlayer player, bool playerMixing)
        {
            if (!automated)
            {
                if (playerMixing) playersMixing[player.PlayerUID] = Api.World.ElapsedMilliseconds;
                else playersMixing.Remove(player.PlayerUID);

                quantityPlayersMixing = playersMixing.Count;
            }

            updateMixingState();
        }

        bool beforeMixing;
        void updateMixingState()
        {
            if (Api?.World == null) return;

            bool nowMixing = quantityPlayersMixing > 0 || (automated && mpc?.TrueSpeed > 0f);

            if (nowMixing != beforeMixing)
            {
                if (renderer != null) renderer.ShouldRotateManual = quantityPlayersMixing > 0;

                Api.World.BlockAccessor.MarkBlockDirty(Pos, OnRetesselated);

                if (nowMixing) ambientSound?.Start();
                else ambientSound?.Stop();

                if (Api.Side == EnumAppSide.Server) MarkDirty();
            }

            beforeMixing = nowMixing;
        }

        private void OnSlotModifid(int slotid)
        {
            clientDialog?.Update(inputMixTime, MaxMixingTime, GetOutputText());

            if (slotid != 1) // Anything that isn't the output slot
            {
                inputMixTime = 0.0f; // Reset the progress to 0 if any input slot changes
                MarkDirty();

                if (clientDialog?.IsOpened() == true) clientDialog.SingleComposer.ReCompose();
            }
        }

        private void OnRetesselated()
        {
            if (renderer == null) return; // Maybe already disposed

            renderer.ShouldRender = quantityPlayersMixing > 0 || automated;
        }

        internal MeshData? GenMesh(string type = "base")
        {
            Block block = Api.World.BlockAccessor.GetBlock(Pos);
            if (block.BlockId == 0 || Api is not ICoreClientAPI capi) return null;

            if (Api.Assets.TryGet($"{Block.Code.Domain}:shapes/block/{Block.FirstCodePart()}/{type}.json") is not IAsset asset) return new();

            capi.Tesselator.TesselateShape(block, asset.ToObject<Shape>(), out var mesh);

            return mesh;
        }

        public bool CanMix => GetMatchingMixingRecipe(Api.World, IngredStacks) != null || GetMatchingDoughRecipe(Api.World, IngredSlots) != null;

        #region Events
        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel.SelectionBoxIndex == 1) return false;

            if (Api is ICoreServerAPI sapi)
            {
                sapi.Network.SendBlockEntityPacket(
                    (IServerPlayer)byPlayer,
                    Pos,
                    (int)EnumBlockStovePacket.OpenGUI
                );

                byPlayer.InventoryManager.OpenInventory(inventory);
                MarkDirty();
            }

            return true;
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);

            invLocked = tree.GetBool("invLocked");
            ITreeAttribute locker = tree.GetTreeAttribute("lockedInv");
            if (locker != null)
            {
                for (int i = 0; i < 6; i++)
                {
                    lockedInv[i] = locker.GetItemstack($"{i}");
                    lockedInv[i]?.ResolveBlockOrItem(worldForResolving);
                }
            }

            Inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));

            if (Api != null) Inventory.AfterBlocksLoaded(Api.World);

            inputMixTime = tree.GetFloat("inputMixTime");
            nowOutputFace = tree.GetInt("nowOutputFace");

            if (worldForResolving.Side == EnumAppSide.Client)
            {
                List<int> clientIds = [.. (tree["clientIsMixing"] as IntArrayAttribute)?.value ?? []];

                quantityPlayersMixing = clientIds.Count;

                foreach (var uid in playersMixing.Keys)
                {
                    IPlayer plr = worldForResolving.PlayerByUid(uid);

                    if (!clientIds.Contains(plr.ClientId)) playersMixing.Remove(uid);
                    else clientIds.Remove(plr.ClientId);
                }

                for (int i = 0; i < clientIds.Count; i++)
                {
                    IPlayer? plr = worldForResolving.AllPlayers.FirstOrDefault(p => p.ClientId == clientIds[i]);
                    if (plr != null) playersMixing.Add(plr.PlayerUID, worldForResolving.ElapsedMilliseconds);
                }

                updateMixingState();
            }


            if (Api?.Side == EnumAppSide.Client) clientDialog?.Update(inputMixTime, MaxMixingTime, GetOutputText());
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetBool("invLocked", invLocked);
            ITreeAttribute locker = tree.GetOrAddTreeAttribute("lockedInv");
            for (int i = 0; i < 6; i++) locker.SetItemstack($"{i}", lockedInv[i]);

            ITreeAttribute invtree = new TreeAttribute();
            Inventory.ToTreeAttributes(invtree);
            tree["inventory"] = invtree;

            tree.SetFloat("inputMixTime", inputMixTime);
            tree.SetInt("nowOutputFace", nowOutputFace);
            tree["clientIsMixing"] = new IntArrayAttribute([.. playersMixing.Select(val => Api.World.PlayerByUid(val.Key)).Where(plr => plr != null).Select(plr => plr.ClientId)]);
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();

            if (ambientSound != null)
            {
                ambientSound.Stop();
                ambientSound.Dispose();
            }

            renderer?.Dispose();
            renderer = null;
        }

        ~BlockEntityMixingBowl()
        {
            ambientSound?.Dispose();
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (packetid < 1000)
            {
                Inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data);

                // Tell server to save this chunk to disk again
                Api.World.BlockAccessor.GetChunkAtBlockPos(Pos).MarkModified();

                return;
            }

            if (packetid == (int)EnumBlockStovePacket.CloseGUI)
            {
                player.InventoryManager?.CloseInventory(Inventory);
            }
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            if (packetid == (int)EnumBlockStovePacket.OpenGUI && Api is ICoreClientAPI capi && clientDialog?.IsOpened() != true)
            {
                clientDialog = new GuiDialogBlockEntityMixingBowl(DialogTitle, Inventory, Pos, capi);
                clientDialog.TryOpen();
                clientDialog.OnClosed += () => clientDialog = null;
                clientDialog.Update(inputMixTime, MaxMixingTime, GetOutputText());
            }

            if (packetid == (int)EnumBlockEntityPacketId.Close)
            {
                IClientWorldAccessor clientWorld = (IClientWorldAccessor)Api.World;
                clientWorld.Player.InventoryManager.CloseInventory(Inventory);
            }
        }

        #endregion

        #region Helper getters
        public BlockCookingContainer? Pot => inventory[0].Itemstack?.Collectible as BlockCookingContainer;
        public ItemSlot OutputSlot => inventory[1];

        public ItemStack InputStack
        {
            get => inventory[0].Itemstack;
            set
            {
                inventory[0].Itemstack = value;
                inventory[0].MarkDirty();
            }
        }

        public ItemSlot[] IngredSlots => [inventory[2], inventory[3], inventory[4], inventory[5], inventory[6], inventory[7]];
        public ItemStack[] IngredStacks => [.. IngredSlots.Select(slot => slot.Itemstack).Where(stack => stack != null)];

        public ItemStack OutputStack
        {
            get => inventory[1].Itemstack;
            set
            {
                inventory[1].Itemstack = value;
                inventory[1].MarkDirty();
            }
        }

        #endregion


        public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
        {
            foreach (var slot in Inventory)
            {
                if (slot.Itemstack == null) continue;

                if (slot.Itemstack.Class == EnumItemClass.Item) itemIdMapping[slot.Itemstack.Item.Id] = slot.Itemstack.Item.Code;
                else blockIdMapping[slot.Itemstack.Block.BlockId] = slot.Itemstack.Block.Code;
            }
        }

        public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
        {
            foreach (var slot in Inventory)
            {
                if (slot.Itemstack == null) continue;
                if (!slot.Itemstack.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve)) slot.Itemstack = null;
            }
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
        {
            if (Block == null || mixingBowlTopMesh == null || renderer == null) return false;

            mesher.AddMeshData(mixingBowlBaseMesh);
            if (quantityPlayersMixing == 0 && !automated)
            {
                mesher.AddMeshData(
                    mixingBowlTopMesh.Clone()
                    .Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0, renderer.AngleRad, 0)
                    .Translate(0 / 16f, 11 / 16f, 0 / 16f)
                );
            }

            return true;
        }

        public CookingRecipe? GetMatchingMixingRecipe(IWorldAccessor world, ItemStack[] stacks)
        {
            if (Pot == null) return null;

            return Api.GetMixingRecipes()?.FirstOrDefault(rec => rec.Matches(stacks) && rec.GetQuantityServings(stacks) <= Pot.MaxServingSize);
        }

        public DoughRecipe? GetMatchingDoughRecipe(IWorldAccessor world, ItemSlot[] slots)
        {
            if (Pot != null) return null;

            return Api.GetKneadingRecipes()?.FirstOrDefault(rec => rec.Matches(world, slots));
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();

            renderer?.Dispose();
        }

        public string GetOutputText()
        {
            CookingRecipe? mrecipe = GetMatchingMixingRecipe(Api.World, IngredStacks);
            DoughRecipe? drecipe = GetMatchingDoughRecipe(Api.World, IngredSlots);
            string locked = invLocked ? Lang.Get("aculinaryartillery:(Locked) ") : "";

            if (mrecipe != null)
            {
                double quantity = mrecipe.GetQuantityServings(IngredStacks);

                return locked + Lang.Get("mealcreation-make" + (quantity == 1 ? "singular" : "plural"), (int)quantity, mrecipe.GetOutputName(Api.World, IngredStacks).ToLowerInvariant());
            }
            else if (drecipe != null) return locked + drecipe.GetOutputName();

            return locked;
        }

        public void ToggleLock(IPlayer? player = null)
        {
            Api.World.PlaySoundAt(new AssetLocation("aculinaryartillery:sounds/lock.ogg"), Pos.X, Pos.Y, Pos.Z, player);
            if (invLocked)
            {
                invLocked = false;
                return;
            }

            invLocked = true;

            for (int i = 0; i < 6; i++) lockedInv[i] = inventory[i + 2].Itemstack?.Clone();
        }
    }
}
